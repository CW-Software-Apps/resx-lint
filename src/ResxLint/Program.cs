using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

// ─────────────────────────────────────────────────────────────────────────────
// resx-lint — dotnet tool
//
// Códigos:
//   TRANS001 — chave em {maui:Translate X} (XAML) ausente no .resx base  → FATAL
//   TRANS002 — chave duplicada em algum .resx                             → AUTO-FIX
//   TRANS003 — chave no .resx base sem propriedade no Designer.cs         → AUTO-FIX
//   TRANS004 — chave em AppResources.Key (C#) ausente no .resx base       → FATAL
//   TRANS005 — chave em idioma sem equivalente no .resx base              → AUTO-FIX
//   TRANS006 — chave no .resx base sem tradução em algum idioma           → AVISO
//   TRANS007 — valor vazio ou placeholder no .resx base                   → AVISO
//   TRANS008 — valor idêntico ao base em todos os idiomas                 → INFO
// ─────────────────────────────────────────────────────────────────────────────

var cliArgs = new CliArgs(args: Environment.GetCommandLineArgs()[1..]);

if (cliArgs.Help)
{
    PrintHelp();
    return 0;
}

if (!cliArgs.Validate(out var paramError))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"ERRO: {paramError}");
    Console.ResetColor();
    PrintHelp();
    return 2;
}

var runner = new LintRunner(cliArgs);
return runner.Run();

static void PrintHelp()
{
    Console.WriteLine("""
        resx-lint — Validador de chaves de localização .resx

        USO:
          resx-lint --project-dir <dir> --resx-file <path> [opções]

        OPÇÕES:
          --project-dir <dir>     Raiz do projeto (onde ficam os .xaml e .cs)
          --resx-file <path>      Caminho do .resx base (ex: Resources/AppResources.resx)
          --what-if               Mostra o que faria sem modificar arquivos
          --fail-on-warnings      TRANS006/TRANS007 também interrompem o build
          --quiet                 Suprime mensagens OK e INFO
          --help                  Exibe esta ajuda

        EXIT CODES:
          0  Tudo OK
          1  Correções automáticas aplicadas (reinicie o build)
          2  Parâmetros inválidos
          3  Erros fatais (TRANS001, TRANS004)

        EXEMPLO (.csproj):
          <Target Name="ValidateTranslations" BeforeTargets="Build">
            <Exec Command="resx-lint --project-dir &quot;$(ProjectDir)&quot; --resx-file &quot;$(ProjectDir)Resources\AppResources.resx&quot;" />
          </Target>
        """);
}

// ─────────────────────────────────────────────────────────────────────────────

class CliArgs
{
    public string ProjectDir { get; } = "";
    public string ResxFile { get; } = "";
    public bool WhatIf { get; }
    public bool FailOnWarnings { get; }
    public bool Quiet { get; }
    public bool Help { get; }

    public CliArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--project-dir" when i + 1 < args.Length:
                    ProjectDir = args[++i];
                    break;
                case "--resx-file" when i + 1 < args.Length:
                    ResxFile = args[++i];
                    break;
                case "--what-if":
                    WhatIf = true;
                    break;
                case "--fail-on-warnings":
                    FailOnWarnings = true;
                    break;
                case "--quiet":
                    Quiet = true;
                    break;
                case "--help":
                case "-h":
                    Help = true;
                    break;
            }
        }
    }

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(ProjectDir) || !Directory.Exists(ProjectDir))
        {
            error = $"--project-dir inválido ou não encontrado: '{ProjectDir}'";
            return false;
        }
        if (string.IsNullOrWhiteSpace(ResxFile) || !File.Exists(ResxFile))
        {
            error = $"--resx-file inválido ou não encontrado: '{ResxFile}'";
            return false;
        }
        error = "";
        return true;
    }
}

// ─────────────────────────────────────────────────────────────────────────────

class LintRunner(CliArgs args)
{
    readonly string _projectDir = Path.GetFullPath(args.ProjectDir);
    readonly string _resxFile   = Path.GetFullPath(args.ResxFile);

    readonly List<string> _fatalErrors = [];
    readonly List<string> _warnings    = [];
    bool _fixesApplied = false;
    int  _statsFixed   = 0;

    static readonly Regex PlaceholderRx = new(@"^\[TRADUZIR.*?\]$|^TODO$|^FIXME$|^#N/A$|^$", RegexOptions.Compiled);

    public int Run()
    {
        if (args.WhatIf)
            WriteColor("\n[MODO WHATIF] Nenhum arquivo será modificado.\n", ConsoleColor.Magenta);

        Step1_Duplicates();
        var (baseData, resxSet) = Step2_LoadBase();
        Step3_LanguageFiles(baseData, resxSet);
        Step4_XamlReferences(resxSet);
        Step5_CSharpReferences(resxSet);
        Step6_DesignerCs(resxSet);

        PrintSummary(resxSet.Count);

        if (_fatalErrors.Count > 0 || (args.FailOnWarnings && _warnings.Count > 0))
        {
            WriteErr($"Build cancelado: {_fatalErrors.Count} erro(s) fatal(is). Corrija as chaves ausentes e rebuilde.");
            return 3;
        }

        if (_fixesApplied && !args.WhatIf)
        {
            WriteWarn("Correções automáticas aplicadas. Reinicie o build para validar novamente.");
            return 1;
        }

        WriteOk($"resx-lint concluído sem erros. {resxSet.Count} chaves OK.");
        return 0;
    }

    // ── 1. Duplicatas ─────────────────────────────────────────────────────────

    void Step1_Duplicates()
    {
        WriteHeader("1/6 Duplicatas em .resx");

        var resxFiles = EnumerateFiles(_projectDir, "*.resx");
        foreach (var rf in resxFiles)
        {
            var content = File.ReadAllText(rf, Encoding.UTF8);
            var rel     = Rel(rf);

            var blockRx = new Regex(@"(?s)<data name=""([^""]+)""[^>]*>\s*<value>(.*?)</value>");
            var seen    = new HashSet<string>(StringComparer.Ordinal);
            var dupes   = new List<string>();

            foreach (Match m in blockRx.Matches(content))
            {
                var key = m.Groups[1].Value;
                if (!seen.Add(key)) dupes.Add(key);
            }

            if (dupes.Count == 0)
            {
                WriteOk($"{rel} — sem duplicatas");
                continue;
            }

            var uniqueDupes = dupes.Distinct().ToList();
            foreach (var key in uniqueDupes)
            {
                var count = blockRx.Matches(content).Count(m => m.Groups[1].Value == key);
                WriteMsBuildWarn(rel, 0, "TRANS002", $"Chave duplicada '{key}' — aparece {count} vezes. Ocorrências extras serão removidas.");
            }

            if (!args.WhatIf)
            {
                var seenOnReplace = new HashSet<string>(StringComparer.Ordinal);
                var newContent = blockRx.Replace(content, m =>
                {
                    var k = m.Groups[1].Value;
                    return seenOnReplace.Add(k) ? m.Value : string.Empty;
                });
                File.WriteAllText(rf, newContent, Encoding.UTF8);
                foreach (var key in uniqueDupes)
                    WriteFix($"TRANS002: duplicatas de '{key}' removidas em {rel}");
                _fixesApplied = true;
                _statsFixed += uniqueDupes.Count;
            }
            else
            {
                foreach (var key in uniqueDupes)
                    WriteInfo($"TRANS002 (WhatIf): removeria duplicatas de '{key}' em {rel}");
            }
        }
    }

    // ── 2. Carrega base ───────────────────────────────────────────────────────

    (Dictionary<string, string> baseData, HashSet<string> resxSet) Step2_LoadBase()
    {
        WriteHeader("2/6 Carregando .resx base");

        var baseData = LoadResxData(_resxFile);
        var resxSet  = new HashSet<string>(baseData.Keys, StringComparer.Ordinal);
        var rel      = Rel(_resxFile);

        WriteOk($"{rel} — {resxSet.Count} chaves carregadas");

        foreach (var kvp in baseData)
        {
            if (PlaceholderRx.IsMatch(kvp.Value))
            {
                var desc = kvp.Value == "" ? "valor vazio" : $"placeholder: '{kvp.Value}'";
                WriteMsBuildWarn(rel, 0, "TRANS007", $"Chave '{kvp.Key}' com {desc} no .resx base — tradução pendente.");
                _warnings.Add($"TRANS007: '{kvp.Key}'");
            }
        }

        if (_warnings.Count == 0)
            WriteOk("Nenhum placeholder/vazio encontrado no .resx base");

        return (baseData, resxSet);
    }

    // ── 3. Arquivos de idioma ─────────────────────────────────────────────────

    void Step3_LanguageFiles(Dictionary<string, string> baseData, HashSet<string> resxSet)
    {
        WriteHeader("3/6 Verificando arquivos de idioma");

        var resxDir      = Path.GetDirectoryName(_resxFile)!;
        var resxBaseName = Path.GetFileNameWithoutExtension(_resxFile);
        var langFiles    = Directory.GetFiles(resxDir, $"{resxBaseName}.*.resx");

        if (langFiles.Length == 0)
        {
            WriteWarn($"Nenhum arquivo de idioma encontrado ({resxBaseName}.*.resx)");
            return;
        }

        WriteInfo($"Idiomas encontrados: {string.Join(", ", langFiles.Select(Path.GetFileName))}");

        foreach (var lf in langFiles)
        {
            var langData = LoadResxData(lf);
            var langRel  = Rel(lf);
            var lang     = Path.GetExtension(Path.GetFileNameWithoutExtension(lf)).TrimStart('.');

            WriteInfo($"{langRel} — {langData.Count} chaves");

            // TRANS005: chave no idioma mas não na base → adiciona na base
            var onlyInLang = langData.Keys.Where(k => !resxSet.Contains(k)).ToList();
            if (onlyInLang.Count > 0)
            {
                var baseContent = File.ReadAllText(_resxFile, Encoding.UTF8);
                foreach (var key in onlyInLang)
                {
                    var langValue = langData[key];
                    WriteMsBuildWarn(langRel, 0, "TRANS005", $"Chave '{key}' existe em [{lang}] mas não no .resx base. Será adicionada.");
                    if (!args.WhatIf)
                    {
                        var insertion = $"""  <data name="{key}" xml:space="preserve">{Environment.NewLine}    <value>[TRADUZIR: {langValue}]</value>{Environment.NewLine}  </data>{Environment.NewLine}""";
                        baseContent = baseContent.Replace("</root>", $"{insertion}</root>");
                        resxSet.Add(key);
                        baseData[key] = $"[TRADUZIR: {langValue}]";
                        WriteFix($"TRANS005: '{key}' adicionado ao .resx base");
                        _statsFixed++;
                    }
                    else
                    {
                        WriteInfo($"TRANS005 (WhatIf): adicionaria '{key}' ao .resx base (valor: '{langValue}')");
                    }
                }
                if (!args.WhatIf)
                {
                    File.WriteAllText(_resxFile, baseContent, Encoding.UTF8);
                    _fixesApplied = true;
                }
            }

            // TRANS006: chave na base mas não no idioma → AVISO
            var missingInLang = resxSet.Where(k => !langData.ContainsKey(k)).ToList();
            if (missingInLang.Count > 0)
            {
                var preview = string.Join("', '", missingInLang.Take(5));
                var extra   = missingInLang.Count > 5 ? $" (e mais {missingInLang.Count - 5})" : "";
                WriteMsBuildWarn(langRel, 0, "TRANS006", $"{missingInLang.Count} chave(s) sem tradução em [{lang}]: '{preview}'{extra}");
                foreach (var k in missingInLang)
                    _warnings.Add($"TRANS006: '{k}' ausente em {lang}");
            }
            else
            {
                WriteOk($"{lang} — todas as {resxSet.Count} chaves traduzidas ✓");
            }

            // TRANS008: valor idêntico ao base (informativo)
            var identical = langData.Keys
                .Where(k => resxSet.Contains(k)
                    && langData[k] == baseData.GetValueOrDefault(k)
                    && !PlaceholderRx.IsMatch(langData[k])
                    && langData[k].Length > 3)
                .ToList();

            if (identical.Count > 0)
            {
                var preview = string.Join("', '", identical.Take(3));
                WriteInfo($"TRANS008: {identical.Count} chave(s) em [{lang}] com valor idêntico ao base (pode ser intencional): '{preview}'...");
            }
        }
    }

    // ── 4. XAML: {maui:Translate Key} ────────────────────────────────────────

    void Step4_XamlReferences(HashSet<string> resxSet)
    {
        WriteHeader("4/6 Validando uso em XAML ({maui:Translate})");

        var xamlFiles  = EnumerateFiles(_projectDir, "*.xaml");
        var xamlRx     = new Regex(@"\{(?:maui|localize):Translate\s+([\w\.]+)\}", RegexOptions.Compiled);
        int xamlChecked = 0;

        foreach (var file in xamlFiles)
        {
            var content = File.ReadAllText(file, Encoding.UTF8);
            var rel     = Rel(file);

            foreach (Match m in xamlRx.Matches(content))
            {
                var key  = m.Groups[1].Value;
                var line = GetLineNumber(content, m.Index);
                xamlChecked++;

                if (!resxSet.Contains(key))
                {
                    var similar = FindSimilarKeys(key, resxSet);
                    var hint    = similar.Length > 0 ? $" Chaves similares: '{string.Join("', '", similar)}'." : "";
                    WriteMsBuildError(rel, line, "TRANS001", $"Chave de tradução '{key}' não existe no AppResources.resx.{hint}");
                    _fatalErrors.Add($"TRANS001 — XAML {rel}({line}): '{key}' não encontrada.{hint}");
                }
            }
        }

        if (xamlChecked == 0)
            WriteInfo("Nenhuma referência {maui:Translate} encontrada nos XAML.");
        else if (!_fatalErrors.Any(e => e.StartsWith("TRANS001")))
            WriteOk($"{xamlChecked} referência(s) XAML validadas — todas OK");
    }

    // ── 5. C#: AppResources.Key ───────────────────────────────────────────────

    void Step5_CSharpReferences(HashSet<string> resxSet)
    {
        WriteHeader("5/6 Validando uso em C# (AppResources.Key)");

        var csFiles  = EnumerateFiles(_projectDir, "*.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));
        var csRx     = new Regex(@"\bAppResources\.([A-Z][A-Za-z0-9_]+)\b", RegexOptions.Compiled);
        var infraKeys = new HashSet<string>(["ResourceManager", "Culture", "resourceCulture", "resourceMan"], StringComparer.Ordinal);
        int csChecked = 0;

        foreach (var file in csFiles)
        {
            var content = File.ReadAllText(file, Encoding.UTF8);
            if (!content.Contains("AppResources")) continue;

            var rel = Rel(file);

            foreach (Match m in csRx.Matches(content))
            {
                var key = m.Groups[1].Value;
                if (infraKeys.Contains(key)) continue;

                var line = GetLineNumber(content, m.Index);
                csChecked++;

                if (!resxSet.Contains(key))
                {
                    var similar = FindSimilarKeys(key, resxSet);
                    var hint    = similar.Length > 0 ? $" Chaves similares: '{string.Join("', '", similar)}'." : "";
                    WriteMsBuildError(rel, line, "TRANS004", $"Propriedade 'AppResources.{key}' não existe no .resx base.{hint}");
                    _fatalErrors.Add($"TRANS004 — C# {rel}({line}): 'AppResources.{key}' sem chave no .resx base.{hint}");
                }
            }
        }

        if (csChecked == 0)
            WriteInfo("Nenhuma referência AppResources.Key encontrada nos arquivos .cs.");
        else if (!_fatalErrors.Any(e => e.StartsWith("TRANS004")))
            WriteOk($"{csChecked} referência(s) C# validadas — todas OK");
    }

    // ── 6. Designer.cs ────────────────────────────────────────────────────────

    void Step6_DesignerCs(HashSet<string> resxSet)
    {
        WriteHeader("6/6 Verificando Designer.cs");

        var designerFile = Path.ChangeExtension(_resxFile, "Designer.cs");
        if (!File.Exists(designerFile))
        {
            WriteInfo($"Designer.cs não encontrado — pulando verificação.");
            return;
        }

        var designerContent = File.ReadAllText(designerFile, Encoding.UTF8);
        var designerRel     = Rel(designerFile);
        var designerKeys    = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(designerContent, @"ResourceManager\.GetString\(""([^""]+)"""))
            designerKeys.Add(m.Groups[1].Value);

        var missing = resxSet.Where(k => !designerKeys.Contains(k)).ToList();
        if (missing.Count == 0)
        {
            WriteOk($"Designer.cs — todas as {designerKeys.Count} propriedades presentes ✓");
            return;
        }

        WriteWarn($"TRANS003: {missing.Count} chave(s) sem propriedade no Designer.cs");
        foreach (var key in missing)
            WriteMsBuildWarn(designerRel, 0, "TRANS003", $"Chave '{key}' sem propriedade no Designer.cs. Será adicionada automaticamente.");

        if (args.WhatIf)
        {
            WriteInfo($"TRANS003 (WhatIf): adicionaria {missing.Count} propriedade(s) ao Designer.cs");
            return;
        }

        var sb = new StringBuilder();
        foreach (var key in missing)
        {
            var propName = Regex.Replace(key, @"[^\w]", "_");
            WriteFix($"TRANS003: adicionando propriedade '{propName}' ao Designer.cs");
            sb.AppendLine($"        public static string {propName} {{");
            sb.AppendLine("            get {");
            sb.AppendLine($"                return ResourceManager.GetString(\"{key}\", resourceCulture);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
            _statsFixed++;
        }

        var insertPos = designerContent.LastIndexOf("    }");
        if (insertPos >= 0)
        {
            var newContent = designerContent[..insertPos] + sb + designerContent[insertPos..];
            File.WriteAllText(designerFile, newContent, Encoding.UTF8);
            _fixesApplied = true;
        }
        else
        {
            WriteErr("TRANS003: Não foi possível encontrar posição de inserção no Designer.cs — corrija manualmente.");
        }
    }

    // ── Resumo ─────────────────────────────────────────────────────────────────

    void PrintSummary(int totalKeys)
    {
        var resxDir      = Path.GetDirectoryName(_resxFile)!;
        var resxBaseName = Path.GetFileNameWithoutExtension(_resxFile);
        var langFiles    = Directory.GetFiles(resxDir, $"{resxBaseName}.*.resx");

        Console.WriteLine();
        Console.WriteLine(new string('─', 60));
        WriteColor(" RESUMO — resx-lint", ConsoleColor.White);
        Console.WriteLine(new string('─', 60));
        WriteColor($"  .resx base         : {Rel(_resxFile)}", ConsoleColor.Gray);
        WriteColor($"  Chaves no base     : {totalKeys}", ConsoleColor.Gray);
        WriteColor($"  Idiomas            : {langFiles.Length}  ({string.Join(", ", langFiles.Select(Path.GetFileName))})", ConsoleColor.Gray);
        WriteColor($"  Correções aplicadas: {_statsFixed}", _statsFixed > 0 ? ConsoleColor.Cyan : ConsoleColor.Gray);
        WriteColor($"  Avisos (TRANS006+) : {_warnings.Count}", _warnings.Count > 0 ? ConsoleColor.Yellow : ConsoleColor.Gray);
        WriteColor($"  Erros fatais       : {_fatalErrors.Count}", _fatalErrors.Count > 0 ? ConsoleColor.Red : ConsoleColor.Green);

        if (_fatalErrors.Count > 0)
        {
            Console.WriteLine();
            WriteColor("  ERROS FATAIS:", ConsoleColor.Red);
            foreach (var e in _fatalErrors)
                WriteColor($"    • {e}", ConsoleColor.Red);
        }

        Console.WriteLine(new string('─', 60));
        Console.WriteLine();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static Dictionary<string, string> LoadResxData(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var doc  = XDocument.Load(path);
        foreach (var d in doc.Root!.Elements("data"))
        {
            var name = d.Attribute("name")?.Value;
            if (name is null || Regex.IsMatch(name, @"^(Name|Color|Bitmap|Icon)\d+$")) continue;
            if (!dict.ContainsKey(name))
                dict[name] = d.Element("value")?.Value ?? "";
        }
        return dict;
    }

    static IEnumerable<string> EnumerateFiles(string root, string pattern)
        => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
                    .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                             && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

    static int GetLineNumber(string content, int charIndex)
        => content[..Math.Min(charIndex, content.Length)].Count(c => c == '\n') + 1;

    static string[] FindSimilarKeys(string key, IEnumerable<string> keys, int max = 3)
    {
        var keyLower = key.ToLowerInvariant();
        return keys
            .Select(k =>
            {
                var kl = k.ToLowerInvariant();
                int prefix = 0;
                int minLen = Math.Min(keyLower.Length, kl.Length);
                while (prefix < minLen && keyLower[prefix] == kl[prefix]) prefix++;
                return (Key: k, Score: prefix);
            })
            .Where(x => x.Score > 3)
            .OrderByDescending(x => x.Score)
            .Take(max)
            .Select(x => x.Key)
            .ToArray();
    }

    string Rel(string path) => path.Replace(_projectDir, "").TrimStart(Path.DirectorySeparatorChar, '/');

    // Output helpers
    void WriteOk(string msg)   { if (!args.Quiet) WriteColor($"  ✓ {msg}", ConsoleColor.Green); }
    void WriteInfo(string msg) { if (!args.Quiet) WriteColor($"  ℹ {msg}", ConsoleColor.Cyan); }
    void WriteWarn(string msg) => WriteColor($"  ⚠ {msg}", ConsoleColor.Yellow);
    void WriteErr(string msg)  => WriteColor($"  ✖ {msg}", ConsoleColor.Red);
    void WriteFix(string msg)  => WriteColor($"  ✔ [AUTO-FIX] {msg}", ConsoleColor.DarkCyan);
    void WriteHeader(string msg) { Console.WriteLine(); WriteColor($"═══ {msg} ═══", ConsoleColor.White); }

    static void WriteColor(string msg, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(msg);
        Console.ResetColor();
    }

    static void WriteMsBuildError(string file, int line, string code, string msg)
    {
        var loc = line > 0 ? $"{file}({line})" : file;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"{loc} : error {code} : {msg}");
        Console.ResetColor();
    }

    static void WriteMsBuildWarn(string file, int line, string code, string msg)
    {
        var loc = line > 0 ? $"{file}({line})" : file;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"{loc} : warning {code} : {msg}");
        Console.ResetColor();
    }
}
