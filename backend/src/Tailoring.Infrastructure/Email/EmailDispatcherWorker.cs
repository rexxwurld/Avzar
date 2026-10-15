using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tailoring.Infrastructure.Email;

public sealed class EmailDispatcherWorker(IServiceScopeFactory scopes, IOptions<EmailOptions> options, ILogger<EmailDispatcherWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.DispatcherEnabled) { log.LogInformation("Email dispatcher disabled on this host."); return; }
        if (o.Provider.Equals("log", StringComparison.OrdinalIgnoreCase))
            log.LogWarning("Email:Provider is 'log': messages are written to the log, NOT delivered. Development only.");

        while (!stoppingToken.IsCancellationRequested)
        {
            int handled = 0;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                handled = await scope.ServiceProvider.GetRequiredService<EmailDispatchService>().DispatchOnceAsync(null, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Email dispatch cycle failed; will retry."); }

            // A full batch means more may be waiting: go again immediately.
            if (handled < o.BatchSize)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(o.PollSeconds), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
