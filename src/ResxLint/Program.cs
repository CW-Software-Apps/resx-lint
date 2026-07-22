using ResxLint.Models;
using ResxLint.Services;
using ResxLint.Web;

var cmdArgs = Environment.GetCommandLineArgs()[1..];

if (cmdArgs.Length > 0 && cmdArgs[0] is "--serve" or "-s")
{
    var port = 5123;
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
        Console.WriteLine($"Update available! Download: {info.DownloadUrl}");
        Console.ResetColor();
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

if (cmdArgs.Length == 0)
{
    Console.Title = $"resx-lint v{UpdateService.CurrentVersion}";
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write($"resx-lint v{UpdateService.CurrentVersion} — Press ");
    Console.ForegroundColor = ConsoleColor.White;
    Console.Write("W");
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write(" to open Web UI, or wait 3s for CLI check...");
    Console.ResetColor();

    var start = Environment.TickCount;
    var keyPressed = false;
    try
    {
        while (Environment.TickCount - start < 3000)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (key.KeyChar is 'w' or 'W')
                {
                    keyPressed = true;
                    break;
                }
            }
            Thread.Sleep(50);
        }
    }
    catch { }

    Console.WriteLine();

    if (keyPressed)
    {
        WebStartup.Start(5123, false);
        return 0;
    }

    // Auto-detect: find first .resx in current directory
    var cwd = Directory.GetCurrentDirectory();
    var foundResx = Directory.GetFiles(cwd, "*.resx", SearchOption.AllDirectories)
        .FirstOrDefault(f => !f.Contains("obj") && !f.Contains("bin"));

    if (foundResx != null)
    {
        Console.WriteLine($"Auto-detected: {foundResx.Replace(cwd, "").TrimStart('\\', '/')}");
        cmdArgs = ["--project-dir", cwd, "--resx-file", foundResx];
    }
    else
    {
        Console.WriteLine("No .resx files found in current directory — opening Web UI...");
        WebStartup.Start(5123, false);
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
          resx-lint                          Interactive: press W for Web UI, or waits 3s for CLI check
          resx-lint --project-dir <dir> --resx-file <path> [options]
          resx-lint --serve [--port <port>] [--no-open]

        OPTIONS:
          --project-dir <dir>     Project root directory (where .xaml and .cs files live)
          --resx-file <path>      Path to the base .resx file (e.g. Resources/AppResources.resx)
          --what-if               Preview changes without writing any files
          --fail-on-warnings      Treat TRANS006/TRANS007 as fatal errors
          --quiet                 Suppress OK and INFO messages
          --serve, -s             Start web UI dashboard
          --port <port>           Web UI port (default: 5123)
          --no-open               Don't open browser automatically
          --check-update, -u      Check for updates on NuGet
          --help, -h              Show this help

        EXIT CODES:
          0  All OK
          1  Auto-fixes applied — restart the build
          2  Invalid parameters
          3  Fatal errors (TRANS001, TRANS004)

        EXAMPLES:
          resx-lint                          Interactive mode (press W for web, or auto-CLI)
          resx-lint --project-dir . --resx-file Resources\AppResources.resx
          resx-lint --serve
          resx-lint --check-update
        """);
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
