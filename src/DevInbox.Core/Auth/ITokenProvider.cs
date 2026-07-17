namespace DevInbox.Core.Auth;

public enum AuthFailureReason
{
    None,
    GhNotInstalled,
    GhNotLoggedIn,
    NoStoredToken,
}

public sealed record TokenResult(string? Token, string Source, AuthFailureReason Failure)
{
    public bool Success => Token is not null;

    public static TokenResult Ok(string token, string source) => new(token, source, AuthFailureReason.None);

    public static TokenResult Fail(string source, AuthFailureReason reason) => new(null, source, reason);
}

public interface ITokenProvider
{
    string Name { get; }

    Task<TokenResult> GetTokenAsync(CancellationToken cancellationToken);
}
