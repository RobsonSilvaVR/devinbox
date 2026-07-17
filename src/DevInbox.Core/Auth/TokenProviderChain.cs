namespace DevInbox.Core.Auth;

public sealed class TokenProviderChain(IReadOnlyList<ITokenProvider> providers)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;

    public string? CurrentSource { get; private set; }

    public IReadOnlyList<TokenResult> LastFailures { get; private set; } = [];

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null)
                return _token;

            var failures = new List<TokenResult>();
            foreach (var provider in providers)
            {
                var result = await provider.GetTokenAsync(cancellationToken);
                if (result.Success)
                {
                    _token = result.Token;
                    CurrentSource = result.Source;
                    LastFailures = failures;
                    return _token;
                }

                failures.Add(result);
            }

            CurrentSource = null;
            LastFailures = failures;
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        _token = null;
        CurrentSource = null;
    }
}
