using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Alerts;

/// <summary>Runs the <see cref="AlertEvaluator"/> on a fixed interval for the lifetime of the server.</summary>
internal sealed class AlertEvaluationService(
    IServiceScopeFactory scopes,
    IOptions<AlertOptions> options,
    TimeProvider time,
    ILogger<AlertEvaluationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.BackgroundEvaluation)
        {
            logger.LogInformation("Background alert evaluation is switched off");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.EvaluationIntervalSeconds), time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<AlertEvaluator>().EvaluateAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Alert evaluation failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
