using DevInbox.App.Services;
using DevInbox.Core.Auth;
using DevInbox.Core.GitHub;
using DevInbox.Core.Polling;
using DevInbox.Core.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevInbox.App.Hosting;

public sealed class PollingBackgroundService(
    PollingEngine engine,
    TokenProviderChain auth,
    SettingsStore settings,
    AppStateService state,
    ILogger<PollingBackgroundService> logger) : BackgroundService
{
    private volatile TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Acorda o loop imediatamente (ex.: token recém-configurado ou settings salvas).</summary>
    public void TriggerNow() => _wake.TrySetResult();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consecutiveFailures = 0;
        var authRetried = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                var outcome = await engine.PollOnceAsync(stoppingToken);
                consecutiveFailures = 0;
                authRetried = false;
                state.SetOk();

                delay = TimeSpan.FromSeconds(Math.Max(15, settings.Current.PollIntervalSeconds));
                if (outcome.RateLimit is { Remaining: < 500 })
                {
                    delay *= 2;
                    logger.LogWarning(
                        "Rate limit baixo (restam {Remaining} pontos); intervalo dobrado até o reset.",
                        outcome.RateLimit.Remaining);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (AuthUnavailableException ex)
            {
                logger.LogWarning("Sem autenticação disponível: {Reasons}.",
                    string.Join(", ", ex.Failures.Select(f => $"{f.Source}={f.Failure}")));
                state.SetAuthRequired(ex.Failures);
                delay = TimeSpan.FromMinutes(5);
            }
            catch (GitHubAuthException ex)
            {
                auth.Invalidate();
                if (!authRetried)
                {
                    // O token do gh CLI pode ter expirado; a cadeia busca um novo na próxima volta.
                    authRetried = true;
                    delay = TimeSpan.FromSeconds(2);
                }
                else
                {
                    logger.LogWarning(ex, "Token rejeitado mesmo após renovar a cadeia de auth.");
                    state.SetAuthRequired(auth.LastFailures);
                    delay = TimeSpan.FromMinutes(5);
                }
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                delay = TimeSpan.FromSeconds(Math.Min(
                    settings.Current.PollIntervalSeconds * Math.Pow(2, consecutiveFailures), 900));
                logger.LogWarning(ex,
                    "Falha no poll ({Count} seguidas); nova tentativa em {Delay}.", consecutiveFailures, delay);
                state.SetError(ex.Message);
            }

            var wake = _wake;
            try
            {
                await Task.WhenAny(Task.Delay(delay, stoppingToken), wake.Task);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (wake.Task.IsCompleted)
                _wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
