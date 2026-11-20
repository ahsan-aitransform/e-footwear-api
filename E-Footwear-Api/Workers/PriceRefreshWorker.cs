using EFootwear.Application.Catalog;

namespace EFootwear.Api.Workers;

/// <summary>
/// Rebuilds promo.effective_prices in the background: every few seconds for companies whose prices changed, and once a minute for
/// companies whose promotions just started or ended, or depend on stock level. A failed pass is logged and retried on the next tick.
/// </summary>
public sealed class PriceRefreshWorker(PriceRefresher refresher, ILogger<PriceRefreshWorker> log, IConfiguration cfg) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!cfg.GetValue("Workers:PriceRefresh", true)) return;
        var tick = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                try { await refresher.SweepAsync(checkAll: ++tick % 12 == 0, stop); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
                catch (Exception e) { log.LogError(e, "Price refresh pass failed; will retry."); }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }
}
