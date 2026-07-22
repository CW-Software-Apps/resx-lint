using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using ResxLint.Models;
using ResxLint.Services;

namespace ResxLint.Web;

class WebStartup
{
    public static void Start(int port, bool noOpen)
    {
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

        api.MapGet("/project/resx-files", (string dir) =>
        {
            if (!Directory.Exists(dir))
                return Results.BadRequest(new { error = $"Directory not found: {dir}" });

            var resxFiles = Directory.GetFiles(dir, "*.resx", SearchOption.AllDirectories)
                .Where(f => !f.Contains("obj") && !f.Contains("bin"))
                .Select(f =>
                {
                    var baseName = Path.GetFileNameWithoutExtension(f);
                    var dirPath = Path.GetDirectoryName(f)!;
                    var langFiles = Directory.GetFiles(dirPath, $"{baseName}.*.resx")
                        .Where(lf => lf != f)
                        .Select(lf => Path.GetFileName(lf)!)
                        .ToArray();

                    return new ProjectResxInfo(f, baseName, langFiles);
                })
                .ToArray();

            return Results.Ok(resxFiles);
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

        api.MapGet("/lint/auto-fix", (string projectDir, string resxFile) =>
        {
            if (!Directory.Exists(projectDir))
                return Results.BadRequest(new { error = $"Project directory not found: {projectDir}" });
            if (!File.Exists(resxFile))
                return Results.BadRequest(new { error = $"Resx file not found: {resxFile}" });

            try
            {
                var whatIfService = new LintService(new LintRequest(projectDir, resxFile, WhatIf: true));
                var whatIfResult = whatIfService.Run();

                var fixService = new LintService(new LintRequest(projectDir, resxFile, WhatIf: false));
                var fixResult = fixService.Run();

                return Results.Ok(new
                {
                    preview = whatIfResult.Issues.Where(i => i.CanAutoFix).Select(i => new
                    {
                        i.Code, i.Key, i.File, i.FixDescription
                    }),
                    result = fixResult
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
            return result.Error != null
                ? Results.BadRequest(result)
                : Results.Ok(result);
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
}
