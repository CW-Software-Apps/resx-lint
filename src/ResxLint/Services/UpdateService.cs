using System.Diagnostics;
using System.Text.Json;

namespace ResxLint.Services;

class UpdateInfo
{
    public string CurrentVersion { get; init; } = "";
    public string? LatestVersion { get; init; }
    public string? DownloadUrl { get; init; }
    public bool IsUpdateAvailable { get; init; }
    public string? Error { get; init; }
}

class InstallResult
{
    public bool Success { get; init; }
    public string CurrentVersion { get; init; } = "";
    public string TargetVersion { get; init; } = "";
    public string LatestVersion => TargetVersion;
    public string CommandOutput { get; init; } = "";
    public string? Error { get; init; }
}

class UpdateService
{
    const string NuGetApiUrl = "https://api.nuget.org/v3-flatcontainer/resxlint/index.json";
    const string PackageUrl = "https://www.nuget.org/packages/ResxLint";

    public static string CurrentVersion =>
        typeof(UpdateService).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            is [System.Reflection.AssemblyInformationalVersionAttribute attr]
            ? attr.InformationalVersion.Split('+')[0]
            : "0.0.0";

    public static async Task<UpdateInfo> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var json = await http.GetStringAsync(NuGetApiUrl);
            var doc = JsonDocument.Parse(json);
            var versions = doc.RootElement.GetProperty("versions");
            var latest = versions.EnumerateArray()
                .Select(v => v.GetString()!)
                .OrderByDescending(v => v, new VersionComparer())
                .FirstOrDefault();

            if (latest == null)
                return new UpdateInfo { CurrentVersion = CurrentVersion, Error = "Nenhuma versão encontrada no NuGet" };

            var current = TryParseVersion(CurrentVersion);
            var latestV = TryParseVersion(latest);

            return new UpdateInfo
            {
                CurrentVersion = CurrentVersion,
                LatestVersion = latest,
                DownloadUrl = $"{PackageUrl}/{latest}",
                IsUpdateAvailable = latestV > current
            };
        }
        catch (Exception ex)
        {
            return new UpdateInfo
            {
                CurrentVersion = CurrentVersion,
                Error = ex.Message
            };
        }
    }

    /// <param name="restartArgs">
    /// Args to relaunch resx-lint with after the update, e.g. "--serve --port 7950" so a
    /// Web UI-triggered update comes back up as the Web UI instead of dropping into the bare
    /// CLI prompt (which is what "resx-lint" with no args does). Null/empty for plain CLI restart.
    /// </param>
    public static async Task<InstallResult> InstallAsync(string? restartArgs = null)
    {
        try
        {
            var info = await CheckAsync();
            var currentPid = Environment.ProcessId;
            var restartCommand = string.IsNullOrWhiteSpace(restartArgs) ? "resx-lint" : $"resx-lint {restartArgs}";

            // The update/restart happens in a visible console window on purpose — a previous
            // version piped everything to `nul` and hid the window, so any failure (dotnet
            // update erroring, "resx-lint" not resolving on PATH, etc.) was completely silent:
            // the window just vanished and nothing came back up, with zero diagnostic trail.
            var scriptPath = Path.Combine(Path.GetTempPath(), $"update_resxlint_{Guid.NewGuid():N}.bat");
            var scriptContent = $@"@echo off
title resx-lint updater
echo ============================================
echo   resx-lint auto-update
echo ============================================
echo.
echo Waiting for resx-lint to close (PID {currentPid})...
timeout /t 2 /nobreak > nul
taskkill /F /PID {currentPid} > nul 2>&1
echo.
echo Updating to the latest version...
echo.
dotnet tool update --global ResxLint
if %errorlevel% neq 0 (
    echo.
    echo ============================================
    echo   Update command failed ^(exit code %errorlevel%^).
    echo   resx-lint will still try to restart with whatever version is installed.
    echo ============================================
    timeout /t 5
)
echo.
echo Restarting: {restartCommand}
start """" {restartCommand}
if %errorlevel% neq 0 (
    echo.
    echo Could not launch 'resx-lint' automatically — it may not be on PATH.
    echo Open a new terminal and run 'resx-lint' manually.
    echo.
    pause
    goto :cleanup
)
timeout /t 1 /nobreak > nul
:cleanup
(goto) 2>nul & del ""%~f0""
";

            await File.WriteAllTextAsync(scriptPath, scriptContent);

            var psi = new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };

            Process.Start(psi);

            return new InstallResult
            {
                Success = true,
                CurrentVersion = CurrentVersion,
                TargetVersion = info.LatestVersion ?? "",
                CommandOutput = $"[Auto-Kill Updater Initiated]\nTarget Process PID: {currentPid}\n\n1. Stopping current resx-lint process (releasing file lock)...\n2. Executing 'dotnet tool update --global ResxLint'...\n3. Restarting resx-lint automatically.\n\nA console window will open showing progress — if the restart fails for any reason, it stays open with the error instead of vanishing."
            };
        }
        catch (Exception ex)
        {
            return new InstallResult
            {
                Success = false,
                CurrentVersion = CurrentVersion,
                Error = ex.Message
            };
        }
    }

    static Version TryParseVersion(string v)
    {
        var clean = v.TrimStart('v').Split('-')[0];
        return Version.TryParse(clean, out var parsed) ? parsed : new Version(0, 0, 0);
    }
}

class VersionComparer : IComparer<string>
{
    public int Compare(string? a, string? b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return -1;
        if (b == null) return 1;

        var va = new Version(a.TrimStart('v'));
        var vb = new Version(b.TrimStart('v'));
        return va.CompareTo(vb);
    }
}
