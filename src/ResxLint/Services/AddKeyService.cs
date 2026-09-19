using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ResxLint.Models;

namespace ResxLint.Services;

/// <summary>
/// Implements <c>resx-lint add-key</c> — the safe, scriptable way to add (or update) one
/// translation key across the base .resx and every sibling language .resx, plus its
/// Designer.cs property, in one atomic call.
///
/// This exists specifically because an AI coding agent editing .resx files by hand keeps
/// reproducing the same two failure modes: (1) adding a key that already exists under a
/// different casing (compiles fine, silently renders "[MISSING: Key]" at runtime — see
/// TRANS009), and (2) adding the key to the base .resx but forgetting one of the language
/// files or the Designer.cs property, which either breaks the build (TRANS003/006) or ships
/// silently untranslated. <c>add-key</c> makes both mistakes structurally impossible: it
/// refuses outright on any case-insensitive collision, and always touches every resx file +
/// Designer.cs in the same call.
/// </summary>
static class AddKeyService
{
    public static AddKeyResult Run(AddKeyRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Key) || !Regex.IsMatch(req.Key, @"^[A-Za-z_][A-Za-z0-9_.]*$"))
            return AddKeyResult.Fail($"Invalid key '{req.Key}'. Keys must start with a letter or underscore and contain only letters, digits, '_', or '.'.");

        if (req.Value is null)
            return AddKeyResult.Fail("--value is required (the base/default-culture translation text).");

        var baseData = LoadResxDataOrdinal(req.ResxFile);

        var exactMatch = baseData.Keys.FirstOrDefault(k => k == req.Key);
        var caseMatch = baseData.Keys.FirstOrDefault(k => string.Equals(k, req.Key, StringComparison.OrdinalIgnoreCase) && k != req.Key);

        if (caseMatch != null)
        {
            return AddKeyResult.Fail(
                $"Refusing to add '{req.Key}': a case-variant key already exists — '{caseMatch}' = \"{baseData[caseMatch]}\". " +
                $"This exact scenario ships a silent \"[MISSING: {req.Key}]\" at runtime (see TRANS009). " +
                $"Use '{{maui:Translate {caseMatch}}}' / 'AppResources.{caseMatch}' with that exact casing instead of adding a new key.");
        }

        if (exactMatch != null && !req.Update)
        {
            return AddKeyResult.Fail(
                $"Key '{req.Key}' already exists with value \"{baseData[exactMatch]}\". " +
                $"Pass --update to change its value, or reuse the existing key as-is.");
        }

        var resxDir = Path.GetDirectoryName(Path.GetFullPath(req.ResxFile))!;
        var resxBaseName = Path.GetFileNameWithoutExtension(req.ResxFile);
        var langFiles = Directory.GetFiles(resxDir, $"{resxBaseName}.*.resx");

        var touched = new List<string>();

        if (exactMatch != null)
        {
            if (!req.WhatIf) SetDataValue(req.ResxFile, req.Key, req.Value);
            touched.Add(Path.GetFileName(req.ResxFile));
        }
        else
        {
            if (!req.WhatIf) InsertDataElement(req.ResxFile, req.Key, req.Value);
            touched.Add(Path.GetFileName(req.ResxFile));
        }

        var langResults = new List<string>();
        foreach (var lf in langFiles)
        {
            var lang = Path.GetExtension(Path.GetFileNameWithoutExtension(lf)).TrimStart('.');
            var langData = LoadResxDataOrdinal(lf);
            var alreadyThere = langData.ContainsKey(req.Key);

            if (req.Translations.TryGetValue(lang, out var explicitValue))
            {
                if (!req.WhatIf)
                {
                    if (alreadyThere) SetDataValue(lf, req.Key, explicitValue);
                    else InsertDataElement(lf, req.Key, explicitValue);
                }
                langResults.Add($"{lang}: \"{explicitValue}\"");
            }
            else if (!alreadyThere)
            {
                var placeholder = $"[TRANSLATE: {req.Value}]";
                if (!req.WhatIf) InsertDataElement(lf, req.Key, placeholder);
                langResults.Add($"{lang}: {placeholder} (needs translation)");
            }
            else if (req.Update)
            {
                // --update with no explicit value for this language: leave its existing
                // translation alone rather than overwriting a real translation with a guess.
                langResults.Add($"{lang}: unchanged (already translated, no --lang value given)");
            }
            else
            {
                langResults.Add($"{lang}: unchanged (already present)");
            }

            touched.Add(Path.GetFileName(lf));
        }

        foreach (var unknownLang in req.Translations.Keys.Where(l => !langFiles.Any(lf => lf.EndsWith($".{l}.resx", StringComparison.OrdinalIgnoreCase))))
        {
            langResults.Add($"{unknownLang}: WARNING — no '{resxBaseName}.{unknownLang}.resx' file found, value was not written anywhere");
        }

        var designerTouched = req.WhatIf
            ? !DesignerHasProperty(req.ResxFile, req.Key)
            : EnsureDesignerProperty(req.ResxFile, req.Key);
        if (designerTouched) touched.Add(Path.GetFileName(Path.ChangeExtension(req.ResxFile, "Designer.cs")));

        return AddKeyResult.Ok(req.Key, req.Value, touched, langResults, exactMatch != null);
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

    static void InsertDataElement(string resxPath, string key, string value)
    {
        var content = File.ReadAllText(resxPath, Encoding.UTF8);
        var insertion = $"""  <data name="{key}" xml:space="preserve">{Environment.NewLine}    <value>{EscapeXml(value)}</value>{Environment.NewLine}  </data>{Environment.NewLine}""";
        var newContent = content.Contains("</root>")
            ? content.Replace("</root>", $"{insertion}</root>")
            : content + insertion; // malformed file without a closing root — append defensively rather than throw
        File.WriteAllText(resxPath, newContent, Encoding.UTF8);
    }

    static void SetDataValue(string resxPath, string key, string value)
    {
        var doc = XDocument.Load(resxPath);
        var el = doc.Root!.Elements("data").First(d => d.Attribute("name")?.Value == key);
        var valueEl = el.Element("value");
        if (valueEl != null) valueEl.Value = value;
        else el.Add(new XElement("value", value));
        doc.Save(resxPath);
    }

    static string EscapeXml(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    static bool DesignerHasProperty(string resxFile, string key)
    {
        var designerFile = Path.ChangeExtension(resxFile, "Designer.cs");
        if (!File.Exists(designerFile)) return true; // nothing to add to — don't claim we'd touch it
        var content = File.ReadAllText(designerFile, Encoding.UTF8);
        return Regex.IsMatch(content, $@"ResourceManager\.GetString\(""{Regex.Escape(key)}""");
    }

    /// <summary>Returns true if a Designer.cs property was appended (i.e. it didn't already exist).</summary>
    static bool EnsureDesignerProperty(string resxFile, string key)
    {
        if (DesignerHasProperty(resxFile, key)) return false;
        var designerFile = Path.ChangeExtension(resxFile, "Designer.cs");
        var content = File.ReadAllText(designerFile, Encoding.UTF8);

        var propName = Regex.Replace(key, @"[^\w]", "_");
        var sb = new StringBuilder();
        sb.AppendLine($"        public static string {propName} {{");
        sb.AppendLine("            get {");
        sb.AppendLine($"                return ResourceManager.GetString(\"{key}\", resourceCulture);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();

        var insertPos = content.LastIndexOf("    }");
        if (insertPos < 0) return false;

        content = content[..insertPos] + sb + content[insertPos..];
        File.WriteAllText(designerFile, content, Encoding.UTF8);
        return true;
    }
}
