using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// ─────────────────────────────────────────────────────────────────────────────
// resx-lint — dotnet tool
//
// Diagnostic codes:
//   TRANS001 — key used in {maui:Translate X} (XAML) not found in base .resx  → FATAL
//   TRANS002 — duplicate key in any .resx                                       → AUTO-FIX
//   TRANS003 — key in base .resx without property in Designer.cs               → AUTO-FIX
//   TRANS004 — key used as AppResources.Key (C#) not found in base .resx       → FATAL
//   TRANS005 — key in language file not found in base .resx                    → AUTO-FIX
//   TRANS006 — key in base .resx missing translation in some language          → WARNING
//   TRANS007 — empty or placeholder value in base .resx                        → WARNING
//   TRANS008 — value identical to base language in translated file             → INFO
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
    Console.Error.WriteLine($"ERROR: {paramError}");
    Console.ResetColor();
    PrintHelp();
    return 2;
}

var runner = new LintRunner(cliArgs);
return runner.Run();

static void PrintHelp()
{
    Console.WriteLine("""
        resx-lint — .resx localization key validator

        USAGE:
          resx-lint --project-dir <dir> --resx-file <path> [options]

        OPTIONS:
          --project-dir <dir>     Project root directory (where .xaml and .cs files live)
          --resx-file <path>      Path to the base .resx file (e.g. Resources/AppResources.resx)
          --what-if               Preview changes without writing any files
          --fail-on-warnings      Treat TRANS006/TRANS007 as fatal errors
          --quiet                 Suppress OK and INFO messages
          --help                  Show this help

        EXIT CODES:
          0  All OK
          1  Auto-fixes applied — restart the build
          2  Invalid parameters
          3  Fatal errors (TRANS001, TRANS004)

        EXAMPLE (.csproj):
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
            error = $"--project-dir is invalid or not found: '{ProjectDir}'";
            return false;
        }
        if (string.IsNullOrWhiteSpace(ResxFile) || !File.Exists(ResxFile))
        {
            error = $"--resx-file is invalid or not found: '{ResxFile}'";
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

    static readonly Regex PlaceholderRx = new(@"^\[TRANSLATE.*?\]$|^\[TRADUZIR.*?\]$|^TODO$|^FIXME$|^#N/A$|^$", RegexOptions.Compiled);

    public int Run()
    {
        if (args.WhatIf)
            WriteColor("\n[WHAT-IF MODE] No files will be modified.\n", ConsoleColor.Magenta);

        Step1_Duplicates();
        var (baseData, resxSet) = Step2_LoadBase();
        Step3_LanguageFiles(baseData, resxSet);
        Step4_XamlReferences(resxSet);
        Step5_CSharpReferences(resxSet);
        Step6_DesignerCs(resxSet);

        PrintSummary(resxSet.Count);

        if (_fatalErrors.Count > 0 || (args.FailOnWarnings && _warnings.Count > 0))
        {
            WriteErr($"Build cancelled: {_fatalErrors.Count} fatal error(s). Fix the missing keys and rebuild.");
            return 3;
        }

        if (_fixesApplied && !args.WhatIf)
        {
            WriteWarn("Auto-fixes were applied. Restart the build to re-validate.");
            return 1;
        }

        WriteOk($"resx-lint completed with no errors. {resxSet.Count} keys OK.");
        return 0;
    }

    // ── 1. Duplicates ─────────────────────────────────────────────────────────

    void Step1_Duplicates()
    {
        WriteHeader("1/6 Duplicate keys in .resx files");

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
                WriteOk($"{rel} — no duplicates");
                continue;
            }

            var uniqueDupes = dupes.Distinct().ToList();
            foreach (var key in uniqueDupes)
            {
                var count = blockRx.Matches(content).Count(m => m.Groups[1].Value == key);
                WriteMsBuildWarn(rel, 0, "TRANS002", $"Duplicate key '{key}' — found {count} times. Extra occurrences will be removed.");
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
                    WriteFix($"TRANS002: duplicate entries for '{key}' removed in {rel}");
                _fixesApplied = true;
                _statsFixed += uniqueDupes.Count;
            }
            else
            {
                foreach (var key in uniqueDupes)
                    WriteInfo($"TRANS002 (what-if): would remove duplicates of '{key}' in {rel}");
            }
        }
    }

    // ── 2. Load base .resx ────────────────────────────────────────────────────

    (Dictionary<string, string> baseData, HashSet<string> resxSet) Step2_LoadBase()
    {
        WriteHeader("2/6 Loading base .resx");

        var baseData = LoadResxData(_resxFile);
        var resxSet  = new HashSet<string>(baseData.Keys, StringComparer.Ordinal);
        var rel      = Rel(_resxFile);

        WriteOk($"{rel} — {resxSet.Count} keys loaded");

        foreach (var kvp in baseData)
        {
            if (PlaceholderRx.IsMatch(kvp.Value))
            {
                var desc = kvp.Value == "" ? "empty value" : $"placeholder: '{kvp.Value}'";
                WriteMsBuildWarn(rel, 0, "TRANS007", $"Key '{kvp.Key}' has {desc} in base .resx — translation pending.");
                _warnings.Add($"TRANS007: '{kvp.Key}'");
            }
        }

        if (_warnings.Count == 0)
            WriteOk("No placeholders or empty values found in base .resx");

        return (baseData, resxSet);
    }

    // ── 3. Language files ─────────────────────────────────────────────────────

    void Step3_LanguageFiles(Dictionary<string, string> baseData, HashSet<string> resxSet)
    {
        WriteHeader("3/6 Checking language files");

        var resxDir      = Path.GetDirectoryName(_resxFile)!;
        var resxBaseName = Path.GetFileNameWithoutExtension(_resxFile);
        var langFiles    = Directory.GetFiles(resxDir, $"{resxBaseName}.*.resx");

        if (langFiles.Length == 0)
        {
            WriteWarn($"No language files found ({resxBaseName}.*.resx)");
            return;
        }

        WriteInfo($"Languages found: {string.Join(", ", langFiles.Select(Path.GetFileName))}");

        foreach (var lf in langFiles)
        {
            var langData = LoadResxData(lf);
            var langRel  = Rel(lf);
            var lang     = Path.GetExtension(Path.GetFileNameWithoutExtension(lf)).TrimStart('.');

            WriteInfo($"{langRel} — {langData.Count} keys");

            // TRANS005: key in language file but not in base → add to base
            var onlyInLang = langData.Keys.Where(k => !resxSet.Contains(k)).ToList();
            if (onlyInLang.Count > 0)
            {
                var baseContent = File.ReadAllText(_resxFile, Encoding.UTF8);
                foreach (var key in onlyInLang)
                {
                    var langValue = langData[key];
                    WriteMsBuildWarn(langRel, 0, "TRANS005", $"Key '{key}' exists in [{lang}] but not in base .resx. Will be added.");
                    if (!args.WhatIf)
                    {
                        var insertion = $"""  <data name="{key}" xml:space="preserve">{Environment.NewLine}    <value>[TRANSLATE: {langValue}]</value>{Environment.NewLine}  </data>{Environment.NewLine}""";
                        baseContent = baseContent.Replace("</root>", $"{insertion}</root>");
                        resxSet.Add(key);
                        baseData[key] = $"[TRANSLATE: {langValue}]";
                        WriteFix($"TRANS005: '{key}' added to base .resx");
                        _statsFixed++;
                    }
                    else
                    {
                        WriteInfo($"TRANS005 (what-if): would add '{key}' to base .resx (value: '{langValue}')");
                    }
                }
                if (!args.WhatIf)
                {
                    File.WriteAllText(_resxFile, baseContent, Encoding.UTF8);
                    _fixesApplied = true;
                }
            }

            // TRANS006: key in base but not in language file → WARNING
            var missingInLang = resxSet.Where(k => !langData.ContainsKey(k)).ToList();
            if (missingInLang.Count > 0)
            {
                var preview = string.Join("', '", missingInLang.Take(5));
                var extra   = missingInLang.Count > 5 ? $" (and {missingInLang.Count - 5} more)" : "";
                WriteMsBuildWarn(langRel, 0, "TRANS006", $"{missingInLang.Count} key(s) missing translation in [{lang}]: '{preview}'{extra}");
                foreach (var k in missingInLang)
                    _warnings.Add($"TRANS006: '{k}' missing in {lang}");
            }
            else
            {
                WriteOk($"{lang} — all {resxSet.Count} keys translated ✓");
            }

            // TRANS008: value identical to base (informational)
            var identical = langData.Keys
                .Where(k => resxSet.Contains(k)
                    && langData[k] == baseData.GetValueOrDefault(k)
                    && !PlaceholderRx.IsMatch(langData[k])
                    && langData[k].Length > 3)
                .ToList();

            if (identical.Count > 0)
            {
                var preview = string.Join("', '", identical.Take(3));
                WriteInfo($"TRANS008: {identical.Count} key(s) in [{lang}] have the same value as the base (may be intentional): '{preview}'...");
            }
        }
    }

    // ── 4. XAML: {maui:Translate Key} ────────────────────────────────────────

    void Step4_XamlReferences(HashSet<string> resxSet)
    {
        WriteHeader("4/6 Validating XAML usage ({maui:Translate})");

        var xamlFiles   = EnumerateFiles(_projectDir, "*.xaml");
        var xamlRx      = new Regex(@"\{(?:maui|localize):Translate\s+([\w\.]+)\}", RegexOptions.Compiled);
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
                    var hint    = similar.Length > 0 ? $" Similar keys: '{string.Join("', '", similar)}'." : "";
                    WriteMsBuildError(rel, line, "TRANS001", $"Translation key '{key}' not found in AppResources.resx.{hint}");
                    _fatalErrors.Add($"TRANS001 — XAML {rel}({line}): '{key}' not found.{hint}");
                }
            }
        }

        if (xamlChecked == 0)
            WriteInfo("No {maui:Translate} references found in XAML files.");
        else if (!_fatalErrors.Any(e => e.StartsWith("TRANS001")))
            WriteOk($"{xamlChecked} XAML reference(s) validated — all OK");
    }

    // ── 5. C#: AppResources.Key ───────────────────────────────────────────────

    void Step5_CSharpReferences(HashSet<string> resxSet)
    {
        WriteHeader("5/6 Validating C# usage (AppResources.Key)");

        var csFiles   = EnumerateFiles(_projectDir, "*.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));
        var csRx      = new Regex(@"\bAppResources\.([A-Z][A-Za-z0-9_]+)\b", RegexOptions.Compiled);
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
                    var hint    = similar.Length > 0 ? $" Similar keys: '{string.Join("', '", similar)}'." : "";
                    WriteMsBuildError(rel, line, "TRANS004", $"Property 'AppResources.{key}' not found in base .resx.{hint}");
                    _fatalErrors.Add($"TRANS004 — C# {rel}({line}): 'AppResources.{key}' has no matching key in base .resx.{hint}");
                }
            }
        }

        if (csChecked == 0)
            WriteInfo("No AppResources.Key references found in .cs files.");
        else if (!_fatalErrors.Any(e => e.StartsWith("TRANS004")))
            WriteOk($"{csChecked} C# reference(s) validated — all OK");
    }

    // ── 6. Designer.cs ────────────────────────────────────────────────────────

    void Step6_DesignerCs(HashSet<string> resxSet)
    {
        WriteHeader("6/6 Checking Designer.cs");

        var designerFile = Path.ChangeExtension(_resxFile, "Designer.cs");
        if (!File.Exists(designerFile))
        {
            WriteInfo("Designer.cs not found — skipping.");
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
            WriteOk($"Designer.cs — all {designerKeys.Count} properties present ✓");
            return;
        }

        WriteWarn($"TRANS003: {missing.Count} key(s) in base .resx have no property in Designer.cs");
        foreach (var key in missing)
            WriteMsBuildWarn(designerRel, 0, "TRANS003", $"Key '{key}' has no property in Designer.cs. Will be added automatically.");

        if (args.WhatIf)
        {
            WriteInfo($"TRANS003 (what-if): would add {missing.Count} property/properties to Designer.cs");
            return;
        }

        var sb = new StringBuilder();
        foreach (var key in missing)
        {
            var propName = Regex.Replace(key, @"[^\w]", "_");
            WriteFix($"TRANS003: adding property '{propName}' to Designer.cs");
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
            WriteErr("TRANS003: Could not find insertion point in Designer.cs — fix manually.");
        }
    }

    // ── Summary ───────────────────────────────────────────────────────────────

    void PrintSummary(int totalKeys)
    {
        var resxDir      = Path.GetDirectoryName(_resxFile)!;
        var resxBaseName = Path.GetFileNameWithoutExtension(_resxFile);
        var langFiles    = Directory.GetFiles(resxDir, $"{resxBaseName}.*.resx");

        Console.WriteLine();
        Console.WriteLine(new string('─', 60));
        WriteColor(" SUMMARY — resx-lint", ConsoleColor.White);
        Console.WriteLine(new string('─', 60));
        WriteColor($"  Base .resx         : {Rel(_resxFile)}", ConsoleColor.Gray);
        WriteColor($"  Keys in base       : {totalKeys}", ConsoleColor.Gray);
        WriteColor($"  Languages          : {langFiles.Length}  ({string.Join(", ", langFiles.Select(Path.GetFileName))})", ConsoleColor.Gray);
        WriteColor($"  Auto-fixes applied : {_statsFixed}", _statsFixed > 0 ? ConsoleColor.Cyan : ConsoleColor.Gray);
        WriteColor($"  Warnings           : {_warnings.Count}", _warnings.Count > 0 ? ConsoleColor.Yellow : ConsoleColor.Gray);
        WriteColor($"  Fatal errors       : {_fatalErrors.Count}", _fatalErrors.Count > 0 ? ConsoleColor.Red : ConsoleColor.Green);

        if (_fatalErrors.Count > 0)
        {
            Console.WriteLine();
            WriteColor("  FATAL ERRORS:", ConsoleColor.Red);
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

    void WriteOk(string msg)     { if (!args.Quiet) WriteColor($"  ✓ {msg}", ConsoleColor.Green); }
    void WriteInfo(string msg)   { if (!args.Quiet) WriteColor($"  ℹ {msg}", ConsoleColor.Cyan); }
    void WriteWarn(string msg)   => WriteColor($"  ⚠ {msg}", ConsoleColor.Yellow);
    void WriteErr(string msg)    => WriteColor($"  ✖ {msg}", ConsoleColor.Red);
    void WriteFix(string msg)    => WriteColor($"  ✔ [AUTO-FIX] {msg}", ConsoleColor.DarkCyan);
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
