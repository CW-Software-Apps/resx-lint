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

    public static async Task<InstallResult> InstallAsync()
    {
        try
        {
            var info = await CheckAsync();
            var currentPid = Environment.ProcessId;

            var scriptPath = Path.Combine(Path.GetTempPath(), $"update_resxlint_{Guid.NewGuid():N}.bat");
            var scriptContent = $@"@echo off
timeout /t 2 /nobreak > nul
taskkill /F /PID {currentPid} > nul 2>&1
dotnet tool update --global ResxLint
start resx-lint
(goto) 2>nul & del ""%~f0""
";

            await File.WriteAllTextAsync(scriptPath, scriptContent);

            var psi = new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(psi);

            return new InstallResult
            {
                Success = true,
                CurrentVersion = CurrentVersion,
                TargetVersion = info.LatestVersion ?? "",
                CommandOutput = $"[Auto-Kill Updater Initiated]\nTarget Process PID: {currentPid}\n\n1. Stopping current resx-lint process (releasing file lock)...\n2. Executing 'dotnet tool update --global ResxLint'...\n3. Automatically restarting resx-lint in 3 seconds!"
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
