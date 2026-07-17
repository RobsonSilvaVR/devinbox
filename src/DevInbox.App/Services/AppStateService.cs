using DevInbox.Core.Auth;

namespace DevInbox.App.Services;

public enum TrayStatus
{
    Ok,
    AuthRequired,
    Error,
}

public sealed class AppStateService
{
    private readonly Lock _lock = new();

    public event Action? Changed;

    public TrayStatus Status { get; private set; }

    public string? Detail { get; private set; }

    public DateTimeOffset? LastSync { get; private set; }

    public IReadOnlyList<TokenResult> AuthFailures { get; private set; } = [];

    public void SetOk()
    {
        lock (_lock)
        {
            Status = TrayStatus.Ok;
            Detail = null;
            LastSync = DateTimeOffset.Now;
            AuthFailures = [];
        }

        Changed?.Invoke();
    }

    public void SetAuthRequired(IReadOnlyList<TokenResult> failures)
    {
        lock (_lock)
        {
            Status = TrayStatus.AuthRequired;
            Detail = null;
            AuthFailures = failures;
        }

        Changed?.Invoke();
    }

    public void SetError(string detail)
    {
        lock (_lock)
        {
            Status = TrayStatus.Error;
            Detail = detail;
        }

        Changed?.Invoke();
    }
}
