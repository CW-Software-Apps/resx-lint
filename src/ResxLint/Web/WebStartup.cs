using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using ResxLint.Models;
using ResxLint.Services;

namespace ResxLint.Web;

class WebStartup
{
    public static void Start(int preferredPort, bool noOpen)
    {
        var port = FindAvailablePort(preferredPort);
        if (port != preferredPort)
            Console.WriteLine($"Port {preferredPort} in use — using port {port} instead.");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
        });

        builder.WebHost.UseKestrel(options =>
        {
            options.ListenLocalhost(port);
        });

        builder.Services.AddCors();
        builder.Services.AddSignalR();

        var app = builder.Build();

        app.UseCors(c => c.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
        app.UseDefaultFiles();
        app.UseStaticFiles();

        var api = app.MapGroup("/api");

        api.MapPost("/lint/run", async (LintRequest req, IHubContext<LintHub> hub) =>
        {
            if (!Directory.Exists(req.ProjectDir))
                return Results.BadRequest(new { error = $"Project directory not found: {req.ProjectDir}" });
            if (!File.Exists(req.ResxFile))
                return Results.BadRequest(new { error = $"Resx file not found: {req.ResxFile}" });

            var service = new LintService(req);

            if (!string.IsNullOrEmpty(req.ConnectionId))
            {
                service.OnProgress += (step, total, msg, sev) =>
                {
                    hub.Clients.Client(req.ConnectionId).SendAsync("LintProgress", new
                    {
                        sessionId = service.SessionId,
                        step,
                        totalSteps = total,
                        message = msg,
                        severity = sev
                    });
                };
            }

            var result = service.Run();

            if (!string.IsNullOrEmpty(req.ConnectionId))
                await hub.Clients.Client(req.ConnectionId).SendAsync("LintComplete", result);

            return Results.Ok(result);
        });

        api.MapGet("/project/resx-files", (string? dir) =>
        {
            if (string.IsNullOrWhiteSpace(dir))
                return Results.BadRequest(new { error = "Directory path is required" });

            var inputDirs = dir.Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var results = new List<ProjectResxInfo>();

            foreach (var rawDir in inputDirs)
            {
                var inputDir = Path.GetFullPath(rawDir);
                if (!Directory.Exists(inputDir)) continue;

                var allResx = Directory.GetFiles(inputDir, "*.resx", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                !f.Contains("/obj/") && !f.Contains("/bin/"))
                    .ToList();

                var groups = allResx.GroupBy(f =>
                {
                    var dirPath = Path.GetDirectoryName(f)!;
                    var fileName = Path.GetFileNameWithoutExtension(f);
                    var parts = fileName.Split('.');
                    var baseName = parts.Length > 1 && IsCultureCode(parts.Last())
                        ? string.Join('.', parts.Take(parts.Length - 1))
                        : fileName;
                    return Path.Combine(dirPath, baseName);
                });

                foreach (var group in groups)
                {
                    var items = group.ToList();
                    var dirPath = Path.GetDirectoryName(items.First())!;
                    var baseName = Path.GetFileName(group.Key);

                    var baseFile = items.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(baseName, StringComparison.OrdinalIgnoreCase))
                                   ?? items.First();

                    var langFiles = items.Where(f => !f.Equals(baseFile, StringComparison.OrdinalIgnoreCase)).ToArray();

                    var languages = new List<string> { "Default" };
                    foreach (var lf in langFiles)
                    {
                        var lfName = Path.GetFileNameWithoutExtension(lf);
                        if (lfName.StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase))
                        {
                            var langCode = lfName[(baseName.Length + 1)..];
                            languages.Add(langCode);
                        }
                        else
                        {
                            languages.Add(lfName);
                        }
                    }

                    var projName = FindProjectName(dirPath, inputDir);
                    var relFolder = Path.GetRelativePath(inputDir, dirPath);

                    results.Add(new ProjectResxInfo(
                        ProjectName: projName,
                        RelativeFolder: relFolder == "." ? "" : relFolder,
                        ResxFile: baseFile,
                        BaseName: baseName,
                        Languages: [.. languages],
                        LanguageFiles: langFiles.Select(Path.GetFileName).ToArray()!
                    ));
                }
            }

            return Results.Ok(results);
        });

        api.MapGet("/translate/resx-data", (string resxFile) =>
        {
            if (!File.Exists(resxFile))
                return Results.BadRequest(new { error = $"File not found: {resxFile}" });

            try
            {
                var data = LintService.LoadTranslationData(resxFile);
                return Results.Ok(data);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        api.MapPost("/translate/save", (SaveTranslationRequest req) =>
        {
            try
            {
                LintService.SaveTranslation(req);
                return Results.Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        api.MapPost("/translate/ai", async (AiTranslateRequest req) =>
        {
            var result = await TranslationService.TranslateAsync(req);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        api.MapGet("/translate/providers", () =>
            Results.Ok(TranslationService.SupportedProviders));

        api.MapGet("/lint/auto-fix/preview", (string projectDir, string resxFile) =>
        {
            if (!Directory.Exists(projectDir))
                return Results.BadRequest(new { error = $"Project directory not found: {projectDir}" });
            if (!File.Exists(resxFile))
                return Results.BadRequest(new { error = $"Resx file not found: {resxFile}" });

            try
            {
                // WhatIf: true — read-only, never touches disk.
                var whatIfService = new LintService(new LintRequest(projectDir, resxFile, WhatIf: true));
                var whatIfResult = whatIfService.Run();

                return Results.Ok(new
                {
                    preview = whatIfResult.Issues.Where(i => i.CanAutoFix).Select(i => new
                    {
                        i.Code, i.Key, i.File, i.FixDescription
                    })
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        api.MapPost("/lint/auto-fix/apply", (string projectDir, string resxFile) =>
        {
            if (!Directory.Exists(projectDir))
                return Results.BadRequest(new { error = $"Project directory not found: {projectDir}" });
            if (!File.Exists(resxFile))
                return Results.BadRequest(new { error = $"Resx file not found: {resxFile}" });

            try
            {
                // WhatIf: false — actually writes the fixes to disk. Caller must have confirmed already.
                var fixService = new LintService(new LintRequest(projectDir, resxFile, WhatIf: false));
                var fixResult = fixService.Run();

                return Results.Ok(new { result = fixResult });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        api.MapPost("/project/browse", async (string? initialDir) =>
        {
            try
            {
                var path = await NativeDialogService.PickFolderAsync(initialDir);
                return Results.Ok(new { path });
            }
            catch (FolderPickerUnavailableException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        api.MapGet("/project/discover", (string? root) =>
        {
            if (string.IsNullOrWhiteSpace(root))
                return Results.BadRequest(new { error = "Root directory path is required" });

            root = Path.GetFullPath(root);
            if (!Directory.Exists(root))
                return Results.BadRequest(new { error = $"Directory not found: {root}" });

            try
            {
                var projects = new List<object>();

                var csprojFiles = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                !f.Contains("/obj/") && !f.Contains("/bin/"));

                foreach (var csproj in csprojFiles)
                {
                    var projDir = Path.GetDirectoryName(csproj)!;
                    var projName = Path.GetFileNameWithoutExtension(csproj);
                    var relPath = Path.GetRelativePath(root, projDir);

                    var resxFiles = Directory.GetFiles(projDir, "*.resx", SearchOption.AllDirectories)
                        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                                    !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                    !f.Contains("/obj/") && !f.Contains("/bin/"))
                        .Count();

                    projects.Add(new
                    {
                        ProjectName = projName,
                        ProjectDir = projDir,
                        RelativePath = relPath == "." ? "" : relPath,
                        ResxCount = resxFiles
                    });
                }

                var slnFiles = Directory.GetFiles(root, "*.sln", SearchOption.TopDirectoryOnly);
                var solutions = slnFiles.Select(s => new
                {
                    SolutionName = Path.GetFileNameWithoutExtension(s),
                    SolutionFile = s
                }).ToList();

                return Results.Ok(new
                {
                    projects,
                    solutions,
                    totalProjects = projects.Count
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        api.MapGet("/update/check", async () =>
        {
            var info = await UpdateService.CheckAsync();
            return Results.Ok(info);
        });

        api.MapPost("/update/install", async () =>
        {
            var result = await UpdateService.InstallAsync();
            return Results.Ok(result);
        });

        app.MapHub<LintHub>("/hubs/lint");

        if (!noOpen)
        {
            try
            {
                var url = $"http://localhost:{port}";
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch { }
        }

        Console.WriteLine($"resx-lint Web UI running at http://localhost:{port}");
        app.Run();
    }

    static int FindAvailablePort(int start)
    {
        for (int p = start; p < start + 100; p++)
        {
            try
            {
                using var sock = new System.Net.Sockets.Socket(
                    System.Net.Sockets.AddressFamily.InterNetwork,
                    System.Net.Sockets.SocketType.Stream,
                    System.Net.Sockets.ProtocolType.Tcp);
                sock.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, p));
                sock.Close();
                return p;
            }
            catch { }
        }
        return start;
    }

    static string FindProjectName(string dirPath, string rootDir)
    {
        try
        {
            var current = new DirectoryInfo(dirPath);
            var root = new DirectoryInfo(rootDir);
            while (current != null)
            {
                var csproj = current.GetFiles("*.csproj").FirstOrDefault();
                if (csproj != null)
                    return Path.GetFileNameWithoutExtension(csproj.Name);

                if (current.FullName.Equals(root.FullName, StringComparison.OrdinalIgnoreCase) || current.Parent == null)
                    break;

                current = current.Parent;
            }
        }
        catch { }
        return Path.GetFileName(dirPath);
    }

    static bool IsCultureCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        try
        {
            var ci = System.Globalization.CultureInfo.GetCultureInfo(code);
            return ci != null;
        }
        catch
        {
            return false;
        }
    }
}
