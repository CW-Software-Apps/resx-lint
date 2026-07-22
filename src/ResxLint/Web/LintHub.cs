using Microsoft.AspNetCore.SignalR;
using ResxLint.Models;
using ResxLint.Services;

namespace ResxLint.Web;

class LintHub : Hub
{
    public async Task RunLint(LintRequest req)
    {
        var connectionId = Context.ConnectionId;

        if (!Directory.Exists(req.ProjectDir))
        {
            await Clients.Caller.SendAsync("LintError", "Project directory not found");
            return;
        }
        if (!File.Exists(req.ResxFile))
        {
            await Clients.Caller.SendAsync("LintError", "Resx file not found");
            return;
        }

        var service = new LintService(req);

        service.OnProgress += (step, total, message, severity) =>
        {
            Clients.Caller.SendAsync("LintProgress", new
            {
                sessionId = service.SessionId,
                step,
                totalSteps = total,
                message,
                severity
            });
        };

        var result = service.Run();
        await Clients.Caller.SendAsync("LintComplete", result);
    }
}
