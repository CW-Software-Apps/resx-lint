using System.Text.RegularExpressions;
using System.Xml.Linq;
using ResxLint.Models;

namespace ResxLint.Services;

/// <summary>
/// Implements <c>resx-lint remove-key</c> — the counterpart to <c>add-key</c> for the cases
/// TRANS009 flags but can't auto-fix on its own (both case-variants unreferenced, or both
/// referenced and needing a human decision): explicitly deleting one exact key from the base
/// .resx, every sibling language .resx, and its Designer.cs property, in one call.
///
/// Case-insensitive only as a *lookup convenience* (so "remove-key ok" still finds "Ok" if
/// that's the only match) — the actual deletion always targets one exact, resolved key name.
/// It never guesses between two coexisting case-variants; if more than one case-insensitive
/// match exists, it refuses and lists them so the caller states the exact one to remove.
/// </summary>
static class RemoveKeyService
{
    public static RemoveKeyResult Run(RemoveKeyRequest req)
    {
        var baseData = LoadResxDataOrdinal(req.ResxFile);

        var exactMatch = baseData.Keys.FirstOrDefault(k => k == req.Key);
        string target;
        string? matchNote = null;

        if (exactMatch != null)
        {
            target = exactMatch;
        }
        else
        {
            var caseMatches = baseData.Keys.Where(k => string.Equals(k, req.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (caseMatches.Count == 0)
                return RemoveKeyResult.Fail($"Key '{req.Key}' not found in {Path.GetFileName(req.ResxFile)} (checked case-insensitively too).");
            if (caseMatches.Count > 1)
                return RemoveKeyResult.Fail(
                    $"'{req.Key}' matches more than one key case-insensitively: {string.Join(", ", caseMatches.Select(k => $"'{k}'"))}. " +
                    "Pass the exact casing of the one you want to remove.");

            target = caseMatches[0];
            matchNote = $"No exact match for '{req.Key}' — removing the case-insensitive match '{target}' instead.";
        }

        if (!string.IsNullOrWhiteSpace(req.ProjectDir) && !req.Force)
        {
            var usedIn = FindUsages(req.ProjectDir!, target);
            if (usedIn.Count > 0)
            {
                return RemoveKeyResult.Fail(
                    $"Refusing to remove '{target}': still referenced in {usedIn.Count} place(s) " +
                    $"({string.Join(", ", usedIn.Take(3))}{(usedIn.Count > 3 ? ", ..." : "")}). " +
                    "Update those references first, or pass --force to remove it anyway.");
            }
        }

        var resxDir = Path.GetDirectoryName(Path.GetFullPath(req.ResxFile))!;
        var resxBaseName = Path.GetFileNameWithoutExtension(req.ResxFile);
        var allResx = new[] { req.ResxFile }.Concat(Directory.GetFiles(resxDir, $"{resxBaseName}.*.resx")).ToList();

        var touched = new List<string>();
        foreach (var rf in allResx)
        {
            var removed = req.WhatIf ? DataElementExists(rf, target) : RemoveDataElement(rf, target);
            if (removed) touched.Add(Path.GetFileName(rf));
        }

        var designerFile = Path.ChangeExtension(req.ResxFile, "Designer.cs");
        var designerTouched = req.WhatIf
            ? DesignerHasProperty(designerFile, target)
            : RemoveDesignerProperty(designerFile, target);
        if (designerTouched) touched.Add(Path.GetFileName(designerFile));

        return RemoveKeyResult.Ok(target, touched, matchNote);
    }

    static List<string> FindUsages(string projectDir, string key)
    {
        var usedIn = new List<string>();
        var xamlRx = new Regex($@"\{{(?:maui|localize):Translate\s+{Regex.Escape(key)}\}}");
        var csRx = new Regex($@"\bAppResources\.{Regex.Escape(key)}\b");

        foreach (var f in DirectoryScan.EnumerateFilesPruned(projectDir, "*.xaml"))
        {
            if (xamlRx.IsMatch(File.ReadAllText(f))) usedIn.Add(Path.GetFileName(f));
        }
        foreach (var f in DirectoryScan.EnumerateFilesPruned(projectDir, "*.cs")
                     .Where(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)))
        {
            if (csRx.IsMatch(File.ReadAllText(f))) usedIn.Add(Path.GetFileName(f));
        }
        return usedIn;
    }

    static Dictionary<string, string> LoadResxDataOrdinal(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return dict;
        var doc = XDocument.Load(path);
        foreach (var d in doc.Root!.Elements("data"))
        {
            var name = d.Attribute("name")?.Value;
            if (name is null) continue;
            dict[name] = d.Element("value")?.Value ?? "";
        }
        return dict;
    }

    static bool DataElementExists(string resxPath, string key)
    {
        if (!File.Exists(resxPath)) return false;
        var doc = XDocument.Load(resxPath);
        return doc.Root!.Elements("data").Any(d => d.Attribute("name")?.Value == key);
    }

    /// <summary>Returns true if a &lt;data&gt; element was actually removed.</summary>
    static bool RemoveDataElement(string resxPath, string key)
    {
        if (!File.Exists(resxPath)) return false;
        var doc = XDocument.Load(resxPath);
        var el = doc.Root!.Elements("data").FirstOrDefault(d => d.Attribute("name")?.Value == key);
        if (el == null) return false;
        el.Remove();
        doc.Save(resxPath);
        return true;
    }

    static bool DesignerHasProperty(string designerFile, string key)
    {
        if (!File.Exists(designerFile)) return false;
        var content = File.ReadAllText(designerFile);
        return Regex.IsMatch(content, $@"ResourceManager\.GetString\(""{Regex.Escape(key)}""");
    }

    /// <summary>Returns true if a Designer.cs property was actually removed.</summary>
    static bool RemoveDesignerProperty(string designerFile, string key)
    {
        if (!File.Exists(designerFile)) return false;

        var content = File.ReadAllText(designerFile);
        var propRx = new Regex(
            $@"[ \t]*public static string {Regex.Escape(key)} \{{\r?\n\s*get \{{\r?\n\s*return ResourceManager\.GetString\(""{Regex.Escape(key)}"", resourceCulture\);\r?\n\s*\}}\r?\n\s*\}}\r?\n\r?\n?",
            RegexOptions.Multiline);
        var newContent = propRx.Replace(content, "");
        if (newContent == content) return false;
        File.WriteAllText(designerFile, newContent);
        return true;
    }
}
