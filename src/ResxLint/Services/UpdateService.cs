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

            var psi = new ProcessStartInfo("dotnet", "tool update --global ResxLint")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return new InstallResult { Success = false, CurrentVersion = CurrentVersion, Error = "Não foi possível iniciar o processo dotnet tool." };

            var output = await proc.StandardOutput.ReadToEndAsync();
            var error = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var fullOutput = string.IsNullOrWhiteSpace(error) ? output : $"{output}\n{error}";

            if (proc.ExitCode != 0)
            {
                var errMessage = $"Command failed with exit code {proc.ExitCode}";
                if (fullOutput.Contains("Access to the path", StringComparison.OrdinalIgnoreCase) ||
                    fullOutput.Contains("denied", StringComparison.OrdinalIgnoreCase))
                {
                    errMessage = "File lock detected: resx-lint is currently running. Close the running terminal process (Ctrl+C) and run 'dotnet tool update --global ResxLint'.";
                }

                return new InstallResult
                {
                    Success = false,
                    CurrentVersion = CurrentVersion,
                    TargetVersion = info.LatestVersion ?? "",
                    CommandOutput = fullOutput.Trim(),
                    Error = errMessage
                };
            }

            return new InstallResult
            {
                Success = true,
                CurrentVersion = CurrentVersion,
                TargetVersion = info.LatestVersion ?? "",
                CommandOutput = fullOutput.Trim()
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
