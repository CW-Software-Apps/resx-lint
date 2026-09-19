using ResxLint.Models;
using ResxLint.Services;
using ResxLint.Web;

var cmdArgs = Environment.GetCommandLineArgs()[1..];
var port = 7950;

try
{

if (cmdArgs.Length > 0 && cmdArgs[0] is "--serve" or "-s")
{
    var noOpen = false;
    for (int i = 1; i < cmdArgs.Length; i++)
    {
        switch (cmdArgs[i].ToLowerInvariant())
        {
            case "--port" when i + 1 < cmdArgs.Length:
                int.TryParse(cmdArgs[++i], out port);
                break;
            case "--no-open":
                noOpen = true;
                break;
        }
    }
    WebStartup.Start(port, noOpen);
    return 0;
}

if (cmdArgs.Length > 0 && cmdArgs[0] is "--auto-update")
{
    Console.WriteLine("Checking for updates...");
    var check = UpdateService.CheckAsync().GetAwaiter().GetResult();
    if (!check.IsUpdateAvailable)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"resx-lint {check.CurrentVersion} is up to date.");
        Console.ResetColor();
        return 0;
    }
    Console.WriteLine($"Updating from {check.CurrentVersion} to {check.LatestVersion}...");
    var installResult = UpdateService.InstallAsync().GetAwaiter().GetResult();
    if (installResult.Error != null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Update failed: {installResult.Error}");
        Console.ResetColor();
        return 1;
    }
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"Updated to {installResult.LatestVersion}. Restart to use the new version.");
    Console.ResetColor();
    return 0;
}

if (cmdArgs.Length > 0 && cmdArgs[0] is "--check-update" or "-u")
{
    var info = UpdateService.CheckAsync().GetAwaiter().GetResult();
    if (info.Error != null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Update check failed: {info.Error}");
        Console.ResetColor();
        return 1;
    }
    Console.WriteLine($"Current version: {info.CurrentVersion}");
    Console.WriteLine($"Latest version:  {info.LatestVersion}");
    if (info.IsUpdateAvailable)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Update available: {info.CurrentVersion} → {info.LatestVersion}");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("To update, run:");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  dotnet tool update --global ResxLint");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("Or, if you started resx-lint via the web UI, click the");
        Console.WriteLine("version badge in the top bar and choose 'Install Update'.");
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("You're up to date.");
        Console.ResetColor();
    }
    return 0;
}

if (cmdArgs.Length > 0 && (cmdArgs[0] is "--help" or "-h"))
{
    PrintHelp();
    return 0;
}

if (cmdArgs.Length > 0 && cmdArgs[0] == "add-key")
{
    return RunAddKey(cmdArgs[1..]);
}

if (cmdArgs.Length > 0 && cmdArgs[0] == "remove-key")
{
    return RunRemoveKey(cmdArgs[1..]);
}

if (cmdArgs.Length == 0)
{
    Console.Title = $"resx-lint v{UpdateService.CurrentVersion}";
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"resx-lint v{UpdateService.CurrentVersion}");
    Console.ResetColor();
    Console.Write("  [");
    Console.ForegroundColor = ConsoleColor.White;
    Console.Write("W");
    Console.ResetColor();
    Console.Write("] Web UI    [");
    Console.ForegroundColor = ConsoleColor.White;
    Console.Write("L");
    Console.ResetColor();
    Console.Write("] Lint here now    [");
    Console.ForegroundColor = ConsoleColor.White;
    Console.Write("H");
    Console.ResetColor();
    Console.Write("] Help    [");
    Console.ForegroundColor = ConsoleColor.White;
    Console.Write("Q");
    Console.ResetColor();
    Console.WriteLine("] Quit");
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write("  Auto-detecting a .resx in this folder in 3s... ");
    Console.ResetColor();

    var start = Environment.TickCount;
    var pressedKey = '\0';
    try
    {
        while (Environment.TickCount - start < 3000)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (key.KeyChar is 'w' or 'W' or 'l' or 'L' or 'h' or 'H' or 'q' or 'Q')
                {
                    pressedKey = char.ToUpperInvariant(key.KeyChar);
                    break;
                }
            }
            Thread.Sleep(50);
        }
    }
    catch
    {
        // Console not available (e.g. launched from script) — proceed directly to auto-detect
    }

    try { Console.WriteLine(); } catch { }

    switch (pressedKey)
    {
        case 'W':
            WebStartup.Start(port, false);
            return 0;
        case 'Q':
            return 0;
        case 'H':
            PrintHelp();
            return 0;
        // 'L' or timeout: fall through to auto-detect below.
    }

    // Auto-detect: find the first .resx under the current directory (pruning bin/obj/node_modules/.git
    // and skipping anything we can't read — Windows profile folders are full of restricted junctions).
    var cwd = Directory.GetCurrentDirectory();
    List<string> allResx;
    try
    {
        allResx = DirectoryScan.EnumerateFilesPruned(cwd, "*.resx");
    }
    catch
    {
        allResx = [];
    }
    var foundResx = allResx.FirstOrDefault();

    if (foundResx != null)
    {
        // Never lint the whole cwd tree blindly — walk up from the .resx to the nearest
        // .csproj (the actual project root). This is what crashed before: cwd was the user's
        // home directory, so the lint step recursively scanned the entire profile.
        var projectDir = ResolveProjectDir(foundResx, cwd);
        try { Console.WriteLine($"Auto-detected: {foundResx.Replace(cwd, "").TrimStart('\\', '/')}"); } catch { }
        try { Console.WriteLine($"Project dir:   {projectDir}"); } catch { }
        cmdArgs = ["--project-dir", projectDir, "--resx-file", foundResx];
    }
    else
    {
        try { Console.WriteLine("No .resx files found in current directory — opening Web UI..."); } catch { }
        try
        {
        WebStartup.Start(port, false);
        }
        catch (Exception webEx)
        {
            try { Console.Error.WriteLine($"ERROR: Failed to start Web UI: {webEx.Message}"); } catch { }
            return 3;
        }
        return 0;
    }
}

var cliArgs = new CliArgs(cmdArgs);

if (!cliArgs.Validate(out var paramError))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"ERROR: {paramError}");
    Console.ResetColor();
    PrintHelp();
    return 2;
}

var request = new LintRequest(cliArgs.ProjectDir, cliArgs.ResxFile, cliArgs.WhatIf, cliArgs.FailOnWarnings);
var service = new LintService(request);
var result = service.Run();

PrintResult(result, cliArgs.Quiet);

if (result.Summary.FatalErrors > 0 || (cliArgs.FailOnWarnings && result.Summary.Warnings > 0))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"Build cancelled: {result.Summary.FatalErrors} fatal error(s). Fix the missing keys and rebuild.");
    Console.ResetColor();
    return 3;
}

if (result.Summary.AutoFixesApplied > 0 && !cliArgs.WhatIf)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("Auto-fixes were applied. Restart the build to re-validate.");
    Console.ResetColor();
    return 1;
}

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"resx-lint completed with no errors. {result.TotalKeys} keys OK.");
Console.ResetColor();
return 0;

}
catch (Exception ex)
{
    // Never let an unhandled exception dump a raw stack trace on a regular user — print
    // something actionable and exit cleanly instead of crashing the console.
    try
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"resx-lint crashed: {ex.Message}");
        Console.ResetColor();
        Console.Error.WriteLine("This has been caught so your terminal isn't left in a broken state.");
        Console.Error.WriteLine("If this keeps happening, run 'resx-lint --help' or report it at:");
        Console.Error.WriteLine("  https://github.com/CW-Software-Apps/resx-lint/issues");
    }
    catch { }
    return 3;
}

static string ResolveProjectDir(string resxFile, string fallbackCwd)
{
    // Walk up from the .resx file looking for the nearest .csproj — that's the real project
    // root. Falling back to a blindly-passed cwd (e.g. the user's home directory) is how a
    // lint run ends up recursively scanning the entire profile.
    try
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(resxFile))!);
        var root = new DirectoryInfo(fallbackCwd);
        while (dir != null)
        {
            if (dir.GetFiles("*.csproj").Length > 0) return dir.FullName;
            if (dir.FullName.Equals(root.FullName, StringComparison.OrdinalIgnoreCase)) break;
            dir = dir.Parent;
        }
    }
    catch { }
    return Path.GetDirectoryName(Path.GetFullPath(resxFile))!;
}

static void PrintResult(LintResult result, bool quiet)
{
    foreach (var issue in result.Issues)
    {
        if (quiet && issue.Severity == "info") continue;

        var color = issue.Severity switch
        {
            "fatal" => ConsoleColor.Red,
            "warning" => ConsoleColor.Yellow,
            _ => ConsoleColor.Cyan
        };
        var symbol = issue.Severity switch
        {
            "fatal" => "✖",
            "warning" => "⚠",
            _ => "ℹ"
        };

        Console.ForegroundColor = color;
        var loc = issue.Line > 0 ? $"{issue.File}({issue.Line})" : issue.File;
        var severity = issue.Severity == "fatal" ? "error" : issue.Severity;
        Console.WriteLine($"{loc} : {severity} {issue.Code} : {issue.Message}");
        if (issue.SimilarKeys is { Length: > 0 })
            Console.WriteLine($"  Similar keys: {string.Join(", ", issue.SimilarKeys)}");
        Console.ResetColor();
    }

    Console.WriteLine();
    Console.WriteLine(new string('─', 60));
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine(" SUMMARY — resx-lint");
    Console.ResetColor();
    Console.WriteLine(new string('─', 60));
    Console.ForegroundColor = ConsoleColor.Gray;
    Console.WriteLine($"  Base .resx         : {result.BaseResx}");
    Console.WriteLine($"  Keys in base       : {result.TotalKeys}");
    Console.WriteLine($"  Languages          : {result.LanguageCount}  ({string.Join(", ", result.Languages)})");
    Console.ResetColor();
    Console.ForegroundColor = result.Summary.AutoFixesApplied > 0 ? ConsoleColor.Cyan : ConsoleColor.Gray;
    Console.WriteLine($"  Auto-fixes applied : {result.Summary.AutoFixesApplied}");
    Console.ResetColor();
    Console.ForegroundColor = result.Summary.Placeholders > 0 ? ConsoleColor.Yellow : ConsoleColor.Gray;
    Console.WriteLine($"  Warnings           : {result.Summary.Warnings}");
    Console.ResetColor();
    Console.ForegroundColor = result.Summary.FatalErrors > 0 ? ConsoleColor.Red : ConsoleColor.Green;
    Console.WriteLine($"  Fatal errors       : {result.Summary.FatalErrors}");
    Console.ResetColor();
    Console.WriteLine(new string('─', 60));
    Console.WriteLine();
}

static void PrintHelp()
{
    Console.WriteLine("""
        resx-lint — .resx localization key validator

        USAGE:
          resx-lint                          Interactive: [W]eb UI, [L]int here, [H]elp, [Q]uit — auto-detects after 3s
          resx-lint --project-dir <dir> --resx-file <path> [options]
          resx-lint add-key <Key> --resx-file <path> --value "<text>" [options]
          resx-lint remove-key <Key> --resx-file <path> [options]
          resx-lint --serve [--port <port>] [--no-open]

        OPTIONS:
          --project-dir <dir>     Project root directory (where .xaml and .cs files live)
          --resx-file <path>      Path to the base .resx file (e.g. Resources/AppResources.resx)
          --what-if               Preview changes without writing any files
          --fail-on-warnings      Treat TRANS006/TRANS007 as fatal errors
          --quiet                 Suppress OK and INFO messages
          --serve, -s             Start web UI dashboard
          --port <port>           Web UI port (default: 7950)
          --no-open               Don't open browser automatically
          --check-update, -u      Check for updates on NuGet
          --help, -h              Show this help

        ADD-KEY — the safe way for an AI agent (or anyone) to add one translation key.
        Refuses outright if a case-variant of the key already exists (the #1 way AI
        sessions silently break localization — see TRANS009), and always writes the base
        .resx + every sibling language .resx + the Designer.cs property in one call, so a
        forgotten language file or missing property can't happen. See README.md → "For AI
        agents" for the full guide and worked examples.

          resx-lint add-key <Key> --resx-file <path> --value "<base text>" [options]

          --lang <code>=<text>     Translation for one language file (repeatable),
                                   e.g. --lang en-US="Service Type" --lang es-ES="Tipo de Servicio"
                                   Any sibling language file not covered gets a
                                   "[TRANSLATE: <base text>]" placeholder instead of being
                                   left out — never silently skipped.
          --update                 Allow changing the value of a key that already exists
                                   under the exact same casing (still refuses on a
                                   case-variant collision — --update does not bypass that).
          --what-if                Preview: report what would change, write nothing.

        REMOVE-KEY — deletes one exact key from the base .resx, every sibling language
        .resx, and its Designer.cs property, in one call. Use it to resolve a TRANS009
        finding (two case-variant keys) once you've decided which one to keep — never
        guesses between two coexisting variants itself.

          resx-lint remove-key <Key> --resx-file <path> [options]

          --project-dir <dir>      If given, refuses to remove a key still referenced in
                                   any .xaml/.cs under this directory (safety check).
                                   Omit it to skip the check (e.g. resx-only workspaces).
          --force                  Remove even if --project-dir found live references.
          --what-if                Preview: report what would be removed, write nothing.

          Case-insensitive as a lookup convenience only: 'remove-key ok' resolves to 'Ok'
          if that is the only case-insensitive match. If more than one case-variant exists
          (e.g. both 'OK' and 'Ok'), it refuses and lists them — pass the exact one to remove.

        EXIT CODES:
          0  All OK
          1  Auto-fixes applied — restart the build
          2  Invalid parameters, or add-key/remove-key refused (collision, not found, in use)
          3  Fatal errors (TRANS001, TRANS004, or an unresolved TRANS009 case-collision)

        EXAMPLES:
          resx-lint                          Interactive mode (press W for web, or auto-CLI)
          resx-lint --project-dir . --resx-file Resources\AppResources.resx
          resx-lint add-key TipoDeServico --resx-file Resources\AppResources.resx --value "Tipo de Serviço" --lang en-US="Service Type" --lang es-ES="Tipo de Servicio"
          resx-lint remove-key TipoDeServiCO --resx-file Resources\AppResources.resx --project-dir .
          resx-lint --serve
          resx-lint --check-update
        """);
}

static int RunAddKey(string[] args)
{
    string? key = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : null;
    string resxFile = "";
    string? value = null;
    var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    bool update = false, whatIf = false;

    var start = key != null ? 1 : 0;
    for (int i = start; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--key" when i + 1 < args.Length:
                key = args[++i];
                break;
            case "--resx-file" when i + 1 < args.Length:
                resxFile = args[++i];
                break;
            case "--value" when i + 1 < args.Length:
                value = args[++i];
                break;
            case "--lang" when i + 1 < args.Length:
                var pair = args[++i];
                var eq = pair.IndexOf('=');
                if (eq <= 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"ERROR: --lang expects 'code=value', got '{pair}'");
                    Console.ResetColor();
                    return 2;
                }
                translations[pair[..eq]] = pair[(eq + 1)..];
                break;
            case "--update":
                update = true;
                break;
            case "--what-if":
                whatIf = true;
                break;
        }
    }

    if (string.IsNullOrWhiteSpace(key))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine("ERROR: add-key requires a key name (positional or --key <Name>).");
        Console.ResetColor();
        return 2;
    }
    if (string.IsNullOrWhiteSpace(resxFile) || !File.Exists(resxFile))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"ERROR: --resx-file is invalid or not found: '{resxFile}'");
        Console.ResetColor();
        return 2;
    }

    var result = AddKeyService.Run(new AddKeyRequest(
        Path.GetFullPath(resxFile), key!, value, translations, update, whatIf));

    if (!result.Success)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"ERROR: {result.Error}");
        Console.ResetColor();
        return 2;
    }

    Console.ForegroundColor = whatIf ? ConsoleColor.Cyan : ConsoleColor.Green;
    Console.WriteLine(whatIf
        ? $"[what-if] Would {(result.WasUpdate ? "update" : "add")} key '{result.Key}' = \"{result.Value}\""
        : $"{(result.WasUpdate ? "Updated" : "Added")} key '{result.Key}' = \"{result.Value}\"");
    Console.ResetColor();

    if (result.LanguageResults is { Count: > 0 })
    {
        Console.WriteLine("Languages:");
        foreach (var lr in result.LanguageResults)
            Console.WriteLine($"  {lr}");
    }
    if (result.FilesTouched is { Count: > 0 })
        Console.WriteLine($"Files {(whatIf ? "that would be touched" : "touched")}: {string.Join(", ", result.FilesTouched)}");

    Console.WriteLine();
    Console.WriteLine("Use it as:");
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine($"  XAML: {{maui:Translate {result.Key}}}");
    Console.WriteLine($"  C#:   AppResources.{result.Key}");
    Console.ResetColor();

    return 0;
}

static int RunRemoveKey(string[] args)
{
    string? key = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : null;
    string resxFile = "";
    string? projectDir = null;
    bool force = false, whatIf = false;

    var start = key != null ? 1 : 0;
    for (int i = start; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--key" when i + 1 < args.Length:
                key = args[++i];
                break;
            case "--resx-file" when i + 1 < args.Length:
                resxFile = args[++i];
                break;
            case "--project-dir" when i + 1 < args.Length:
                projectDir = args[++i];
                break;
            case "--force":
                force = true;
                break;
            case "--what-if":
                whatIf = true;
                break;
        }
    }

    if (string.IsNullOrWhiteSpace(key))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine("ERROR: remove-key requires a key name (positional or --key <Name>).");
        Console.ResetColor();
        return 2;
    }
    if (string.IsNullOrWhiteSpace(resxFile) || !File.Exists(resxFile))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"ERROR: --resx-file is invalid or not found: '{resxFile}'");
        Console.ResetColor();
        return 2;
    }

    var result = RemoveKeyService.Run(new RemoveKeyRequest(
        Path.GetFullPath(resxFile), key!, projectDir, force, whatIf));

    if (!result.Success)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"ERROR: {result.Error}");
        Console.ResetColor();
        return 2;
    }

    if (result.MatchNote != null)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(result.MatchNote);
        Console.ResetColor();
    }

    Console.ForegroundColor = whatIf ? ConsoleColor.Cyan : ConsoleColor.Green;
    Console.WriteLine(whatIf
        ? $"[what-if] Would remove key '{result.Key}'"
        : $"Removed key '{result.Key}'");
    Console.ResetColor();

    if (result.FilesTouched is { Count: > 0 })
        Console.WriteLine($"Files {(whatIf ? "that would be touched" : "touched")}: {string.Join(", ", result.FilesTouched)}");

    return 0;
}

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
