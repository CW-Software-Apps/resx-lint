using System.ComponentModel;
using System.Diagnostics;

namespace ResxLint.Services;

/// <summary>Thrown when no native folder-picker is available on the current OS/desktop.</summary>
class FolderPickerUnavailableException(string message) : Exception(message);

class NativeDialogService
{
    public static Task<string?> PickFolderAsync(string? initialDir)
    {
        if (OperatingSystem.IsWindows()) return PickFolderWindows(initialDir);
        if (OperatingSystem.IsMacOS()) return PickFolderMac(initialDir);
        if (OperatingSystem.IsLinux()) return PickFolderLinux(initialDir);

        throw new FolderPickerUnavailableException(
            "Native folder picker is not supported on this OS. Type the path manually or use 'Find Projects'.");
    }

    static async Task<string?> PickFolderWindows(string? initialDir)
    {
        var initialDirArg = !string.IsNullOrWhiteSpace(initialDir) && Directory.Exists(initialDir)
            ? $"$f.SelectedPath = '{initialDir.Replace("'", "''")}'"
            : "";

        var script = """
            Add-Type -AssemblyName System.Windows.Forms
            $f = New-Object System.Windows.Forms.FolderBrowserDialog
            $f.Description = 'Select a project folder'
            $f.ShowNewFolderButton = $false
            __INITIAL_DIR__
            $result = $f.ShowDialog()
            if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
                Write-Output $f.SelectedPath
            }
            """.Replace("__INITIAL_DIR__", initialDirArg);

        var (output, _) = await RunAsync("powershell.exe", ["-NoProfile", "-Sta", "-Command", script]);
        return NullIfEmpty(output);
    }

    static async Task<string?> PickFolderMac(string? initialDir)
    {
        var defaultLocation = !string.IsNullOrWhiteSpace(initialDir) && Directory.Exists(initialDir)
            ? $" default location (POSIX file \"{initialDir.Replace("\"", "\\\"")}\")"
            : "";

        var appleScript = $"POSIX path of (choose folder with prompt \"Select a project folder\"{defaultLocation})";

        try
        {
            var (output, exitCode) = await RunAsync("osascript", ["-e", appleScript]);
            if (exitCode != 0) return null; // user clicked Cancel
            return NullIfEmpty(output.TrimEnd('/'));
        }
        catch (Win32Exception)
        {
            throw new FolderPickerUnavailableException(
                "'osascript' was not found. Type the path manually or use 'Find Projects'.");
        }
    }

    static async Task<string?> PickFolderLinux(string? initialDir)
    {
        var startDir = !string.IsNullOrWhiteSpace(initialDir) && Directory.Exists(initialDir) ? initialDir : "";

        foreach (var (exe, args) in new (string, string[])[]
        {
            ("zenity", ["--file-selection", "--directory", "--title=Select a project folder", .. startDir.Length > 0 ? new[] { $"--filename={startDir}/" } : []]),
            ("kdialog", ["--title", "Select a project folder", "--getexistingdirectory", startDir.Length > 0 ? startDir : "."]),
        })
        {
            try
            {
                var (output, exitCode) = await RunAsync(exe, args);
                if (exitCode != 0) return null; // user clicked Cancel
                return NullIfEmpty(output);
            }
            catch (Win32Exception)
            {
                // Tool not installed — fall through and try the next one.
            }
        }

        throw new FolderPickerUnavailableException(
            "No folder-picker tool found. Install 'zenity' (GNOME) or 'kdialog' (KDE), or type the path manually.");
    }

    static async Task<(string Output, int ExitCode)> RunAsync(string fileName, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
        var output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return (output.Trim(), proc.ExitCode);
    }

    static string? NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;
}
