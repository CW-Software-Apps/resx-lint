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
                return new UpdateInfo { CurrentVersion = CurrentVersion, Error = "No versions found" };

            var current = new Version(CurrentVersion.TrimStart('v'));
            var latestV = new Version(latest.TrimStart('v'));

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

    public static async Task<UpdateInfo> InstallAsync()
    {
        try
        {
            var info = await CheckAsync();
            if (!info.IsUpdateAvailable)
                return new UpdateInfo { CurrentVersion = CurrentVersion, Error = "No update available" };

            var psi = new ProcessStartInfo("dotnet", "tool update --global ResxLint")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return new UpdateInfo { CurrentVersion = CurrentVersion, Error = "Failed to start update process" };

            var output = await proc.StandardOutput.ReadToEndAsync();
            var error = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0)
                return new UpdateInfo { CurrentVersion = CurrentVersion, Error = error.Trim() };

            var newVersion = output.Split(' ').LastOrDefault()?.Trim() ?? info.LatestVersion ?? "";

            return new UpdateInfo
            {
                CurrentVersion = CurrentVersion,
                LatestVersion = newVersion,
                IsUpdateAvailable = false,
                DownloadUrl = info.DownloadUrl
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
