namespace Budditec.Overlay.Core.Chat;

internal static class ReconnectLoop
{
    /// <summary>
    /// Runs one connection after another until cancelled, waiting 1 s doubling to 30 s between failures.
    /// A connection that lasted over a minute resets the wait.
    /// </summary>
    public static async Task RunAsync(ChatPlatform platform, Func<CancellationToken, Task> runOnce, Action<ChatEvent> raise, CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            var connectedAt = DateTime.UtcNow;
            try
            {
                await runOnce(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                raise(new ChatConnectionChanged(platform, false, ex.Message));
            }
            if (ct.IsCancellationRequested) break;

            attempt = DateTime.UtcNow - connectedAt > TimeSpan.FromMinutes(1) ? 0 : attempt + 1;
            var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(attempt, 5))));
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }
}
