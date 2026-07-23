using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ResxLint.Models;

namespace ResxLint.Services;

class LintService
{
    static readonly Regex PlaceholderRx = new(@"^\[TRANSLATE.*?\]$|^\[TRADUZIR.*?\]$|^TODO$|^FIXME$|^#N/A$|^$", RegexOptions.Compiled);

    readonly string _projectDir;
    readonly string _resxFile;
    readonly bool _whatIf;
    readonly bool _failOnWarnings;
    readonly List<LintIssue> _issues = [];
    int _statsFixed;

    public string SessionId { get; } = Guid.NewGuid().ToString("N")[..12];
    public IReadOnlyList<LintIssue> Issues => _issues;

    public delegate void ProgressDelegate(int step, int totalSteps, string message, string severity);
    public event ProgressDelegate? OnProgress;

    void Emit(int step, int total, string msg, string sev = "info") =>
        OnProgress?.Invoke(step, total, msg, sev);

    public LintService(LintRequest request)
    {
        _projectDir = Path.GetFullPath(request.ProjectDir);
        _resxFile = Path.GetFullPath(request.ResxFile);
        _whatIf = request.WhatIf;
        _failOnWarnings = request.FailOnWarnings;
    }

    public LintResult Run()
    {
        Emit(1, 6, "Checking for duplicate keys...");
        Step1_Duplicates();
        Emit(2, 6, $"Loading base .resx...");
        var (baseData, resxSet) = Step2_LoadBase();
        Emit(3, 6, "Checking language files...");
        Step3_LanguageFiles(baseData, resxSet);
        Emit(4, 6, "Validating XAML references...");
        Step4_XamlReferences(resxSet);
        Emit(5, 6, "Validating C# references...");
        Step5_CSharpReferences(resxSet);
        Emit(6, 6, "Checking Designer.cs...");
        Step6_DesignerCs(resxSet);

        var resxDir = Path.GetDirectoryName(_resxFile)!;
        var resxBaseName = Path.GetFileNameWithoutExtension(_resxFile);
        var langFiles = Directory.GetFiles(resxDir, $"{resxBaseName}.*.resx");

        var summary = new LintSummary(
            FatalErrors: _issues.Count(i => i.Severity == "fatal"),
            Warnings: _issues.Count(i => i.Severity == "warning"),
            Infos: _issues.Count(i => i.Severity == "info"),
            AutoFixesApplied: _statsFixed,
            Placeholders: _issues.Count(i => i.Code == "TRANS007"),
            MissingTranslations: _issues.Count(i => i.Code == "TRANS006"),
            IdenticalValues: _issues.Count(i => i.Code == "TRANS008")
        );

        return new LintResult(
            SessionId: SessionId,
            TotalKeys: resxSet.Count,
            LanguageCount: langFiles.Length,
            BaseResx: Rel(_resxFile),
            Languages: langFiles.Select(f => Path.GetExtension(Path.GetFileNameWithoutExtension(f)).TrimStart('.')).ToArray(),
            Issues: [.. _issues],
            Summary: summary
        );
    }

    void Step1_Duplicates()
    {
        var resxFiles = EnumerateFiles(_projectDir, "*.resx");
        foreach (var rf in resxFiles)
        {
            var content = File.ReadAllText(rf, Encoding.UTF8);
            var rel = Rel(rf);
            var blockRx = new Regex(@"(?s)<data name=""([^""]+)""[^>]*>\s*<value>(.*?)</value>");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var dupes = new List<string>();

            foreach (Match m in blockRx.Matches(content))
            {
                var key = m.Groups[1].Value;
                if (!seen.Add(key)) dupes.Add(key);
            }

            if (dupes.Count == 0) continue;

            var uniqueDupes = dupes.Distinct().ToList();
            foreach (var key in uniqueDupes)
            {
                var count = blockRx.Matches(content).Count(m => m.Groups[1].Value == key);
                _issues.Add(new LintIssue("TRANS002", "warning", rel, 0, key,
                    $"Duplicate key '{key}' — found {count} times. Extra occurrences will be removed.",
                    CanAutoFix: true,
                    FixDescription: $"Remove duplicate entries for '{key}' in {rel}"));
            }

            if (!_whatIf)
            {
                var seenOnReplace = new HashSet<string>(StringComparer.Ordinal);
                var newContent = blockRx.Replace(content, m =>
                {
                    var k = m.Groups[1].Value;
                    return seenOnReplace.Add(k) ? m.Value : string.Empty;
                });
                File.WriteAllText(rf, newContent, Encoding.UTF8);
                _statsFixed += uniqueDupes.Count;
            }
        }
    }

    (Dictionary<string, string> baseData, HashSet<string> resxSet) Step2_LoadBase()
    {
        var baseData = LoadResxData(_resxFile);
        var resxSet = new HashSet<string>(baseData.Keys, StringComparer.Ordinal);
        var rel = Rel(_resxFile);

        foreach (var kvp in baseData)
        {
            if (PlaceholderRx.IsMatch(kvp.Value))
            {
                var desc = kvp.Value == "" ? "empty value" : $"placeholder: '{kvp.Value}'";
                _issues.Add(new LintIssue("TRANS007", "warning", rel, 0, kvp.Key,
                    $"Key '{kvp.Key}' has {desc} in base .resx — translation pending."));
            }
        }

        return (baseData, resxSet);
    }

    void Step3_LanguageFiles(Dictionary<string, string> baseData, HashSet<string> resxSet)
    {
        var resxDir = Path.GetDirectoryName(_resxFile)!;
        var resxBaseName = Path.GetFileNameWithoutExtension(_resxFile);
        var langFiles = Directory.GetFiles(resxDir, $"{resxBaseName}.*.resx");

        foreach (var lf in langFiles)
        {
            var langData = LoadResxData(lf);
            var langRel = Rel(lf);
            var lang = Path.GetExtension(Path.GetFileNameWithoutExtension(lf)).TrimStart('.');

            var onlyInLang = langData.Keys.Where(k => !resxSet.Contains(k)).ToList();
            if (onlyInLang.Count > 0)
            {
                var baseContent = File.ReadAllText(_resxFile, Encoding.UTF8);
                foreach (var key in onlyInLang)
                {
                    var langValue = langData[key];
                    _issues.Add(new LintIssue("TRANS005", "warning", langRel, 0, key,
                        $"Key '{key}' exists in [{lang}] but not in base .resx. Will be added.",
                        CanAutoFix: true,
                        FixDescription: $"Add '{key}' to base .resx with value '{langValue}'"));

                    if (!_whatIf)
                    {
                        var insertion = $"""  <data name="{key}" xml:space="preserve">{Environment.NewLine}    <value>[TRANSLATE: {langValue}]</value>{Environment.NewLine}  </data>{Environment.NewLine}""";
                        baseContent = baseContent.Replace("</root>", $"{insertion}</root>");
                        resxSet.Add(key);
                        baseData[key] = $"[TRANSLATE: {langValue}]";
                        _statsFixed++;
                    }
                }
                if (!_whatIf)
                {
                    File.WriteAllText(_resxFile, baseContent, Encoding.UTF8);
                }
            }

            var missingInLang = resxSet.Where(k => !langData.ContainsKey(k)).ToList();
            if (missingInLang.Count > 0)
            {
                foreach (var k in missingInLang)
                {
                    _issues.Add(new LintIssue("TRANS006", "warning", langRel, 0, k,
                        $"Key '{k}' missing translation in [{lang}].",
                        CanAutoFix: false));
                }
            }

            var identical = langData.Keys
                .Where(k => resxSet.Contains(k)
                    && langData[k] == baseData.GetValueOrDefault(k)
                    && !PlaceholderRx.IsMatch(langData[k])
                    && langData[k].Length > 3)
                .ToList();

            if (identical.Count > 0)
            {
                foreach (var k in identical)
                {
                    _issues.Add(new LintIssue("TRANS008", "warning", langRel, 0, k,
                        $"Key '{k}' in [{lang}] has the same value as base language (may be untranslated)."));
                }
            }
        }
    }

    void Step4_XamlReferences(HashSet<string> resxSet)
    {
        var xamlFiles = EnumerateFiles(_projectDir, "*.xaml");
        var xamlRx = new Regex(@"\{(?:maui|localize):Translate\s+([\w\.]+)\}", RegexOptions.Compiled);

        foreach (var file in xamlFiles)
        {
            var content = File.ReadAllText(file, Encoding.UTF8);
            var rel = Rel(file);

            foreach (Match m in xamlRx.Matches(content))
            {
                var key = m.Groups[1].Value;
                var line = GetLineNumber(content, m.Index);

                if (!resxSet.Contains(key))
                {
                    var similar = FindSimilarKeys(key, resxSet);
                    _issues.Add(new LintIssue("TRANS001", "fatal", rel, line, key,
                        $"Translation key '{key}' not found in base .resx.",
                        SimilarKeys: similar.Length > 0 ? similar : null));
                }
            }
        }
    }

    void Step5_CSharpReferences(HashSet<string> resxSet)
    {
        var csFiles = EnumerateFiles(_projectDir, "*.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));
        var csRx = new Regex(@"\bAppResources\.([A-Z][A-Za-z0-9_]+)\b", RegexOptions.Compiled);
        var infraKeys = new HashSet<string>(["ResourceManager", "Culture", "resourceCulture", "resourceMan"], StringComparer.Ordinal);

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

                if (!resxSet.Contains(key))
                {
                    var similar = FindSimilarKeys(key, resxSet);
                    _issues.Add(new LintIssue("TRANS004", "fatal", rel, line, key,
                        $"Property 'AppResources.{key}' not found in base .resx.",
                        SimilarKeys: similar.Length > 0 ? similar : null));
                }
            }
        }
    }

    void Step6_DesignerCs(HashSet<string> resxSet)
    {
        var designerFile = Path.ChangeExtension(_resxFile, "Designer.cs");
        if (!File.Exists(designerFile)) return;

        var designerContent = File.ReadAllText(designerFile, Encoding.UTF8);
        var designerRel = Rel(designerFile);
        var designerKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(designerContent, @"ResourceManager\.GetString\(""([^""]+)"""))
            designerKeys.Add(m.Groups[1].Value);

        var missing = resxSet.Where(k => !designerKeys.Contains(k)).ToList();
        if (missing.Count == 0) return;

        foreach (var key in missing)
        {
            _issues.Add(new LintIssue("TRANS003", "warning", designerRel, 0, key,
                $"Key '{key}' has no property in Designer.cs. Will be added automatically.",
                CanAutoFix: true,
                FixDescription: $"Add property '{Regex.Replace(key, @"[^\w]", "_")}' to Designer.cs"));
        }

        if (_whatIf) return;

        var sb = new StringBuilder();
        foreach (var key in missing)
        {
            var propName = Regex.Replace(key, @"[^\w]", "_");
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
            designerContent = designerContent[..insertPos] + sb + designerContent[insertPos..];
            File.WriteAllText(designerFile, designerContent, Encoding.UTF8);
        }
    }

    public static ResxTranslationData LoadTranslationData(string resxFile)
    {
        var resxDir = Path.GetDirectoryName(Path.GetFullPath(resxFile))!;
        var resxBaseName = Path.GetFileNameWithoutExtension(resxFile);
        var baseData = LoadResxData(resxFile);
        var langFiles = Directory.GetFiles(resxDir, $"{resxBaseName}.*.resx");
        var languages = new List<ResxLanguageInfo>();
        var langDataMap = new Dictionary<string, Dictionary<string, string>>();

        foreach (var lf in langFiles)
        {
            var code = Path.GetExtension(Path.GetFileNameWithoutExtension(lf)).TrimStart('.');
            var data = LoadResxData(lf);
            langDataMap[code] = data;

            var missing = baseData.Keys.Count(k => !data.ContainsKey(k));
            var placeholders = data.Values.Count(v => PlaceholderRx.IsMatch(v));
            var identical = data.Count(kvp =>
                baseData.ContainsKey(kvp.Key) &&
                kvp.Value == baseData[kvp.Key] &&
                !PlaceholderRx.IsMatch(kvp.Value) &&
                kvp.Value.Length > 3);

            languages.Add(new ResxLanguageInfo(code, Path.GetFileName(lf),
                data.Count, missing, placeholders, identical));
        }

        var keys = new List<ResxKeyEntry>();
        foreach (var kvp in baseData)
        {
            var translations = new Dictionary<string, ResxTranslationValue>
            {
                ["base"] = new(kvp.Value, "base")
            };

            foreach (var (code, data) in langDataMap)
            {
                if (data.TryGetValue(kvp.Key, out var val))
                {
                    var status = PlaceholderRx.IsMatch(val) ? "placeholder"
                        : val == kvp.Value && val.Length > 3 ? "identical"
                        : "ok";
                    translations[code] = new(val, status);
                }
                else
                {
                    translations[code] = new("", "missing");
                }
            }

            keys.Add(new ResxKeyEntry(kvp.Key, kvp.Value, translations));
        }

        return new ResxTranslationData(
            Path.GetFileName(resxFile),
            [.. languages],
            [.. keys]
        );
    }

    public static void SaveTranslation(SaveTranslationRequest req)
    {
        var resxDir = Path.GetDirectoryName(Path.GetFullPath(req.ResxFile))!;
        var resxBaseName = Path.GetFileNameWithoutExtension(req.ResxFile);

        string targetFile;
        if (req.Language == "base")
            targetFile = req.ResxFile;
        else
            targetFile = Path.Combine(resxDir, $"{resxBaseName}.{req.Language}.resx");

        var doc = XDocument.Load(targetFile);
        var root = doc.Root!;

        foreach (var (key, value) in req.Updates)
        {
            var dataEl = root.Elements("data")
                .FirstOrDefault(d => d.Attribute("name")?.Value == key);

            if (dataEl != null)
            {
                var valueEl = dataEl.Element("value");
                if (valueEl != null)
                    valueEl.Value = EscapeXmlValue(value);
                else
                    dataEl.Add(new XElement("value", EscapeXmlValue(value)));
            }
            else
            {
                root.Add(new XElement("data",
                    new XAttribute("name", key),
                    new XAttribute(XNamespace.Xml + "space", "preserve"),
                    new XElement("value", EscapeXmlValue(value))));
            }
        }

        doc.Save(targetFile);
    }

    static string EscapeXmlValue(string value) => value;

    static Dictionary<string, string> LoadResxData(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var doc = XDocument.Load(path);
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
        => DirectoryScan.EnumerateFilesPruned(root, pattern);

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
}
