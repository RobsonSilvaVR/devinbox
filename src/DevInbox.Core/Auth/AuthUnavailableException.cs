namespace DevInbox.Core.Auth;

public sealed class AuthUnavailableException : Exception
{
    public AuthUnavailableException(IReadOnlyList<TokenResult> failures)
        : base("Nenhuma fonte de autenticação disponível (GitHub CLI ou PAT).")
    {
        Failures = failures;
    }

    public IReadOnlyList<TokenResult> Failures { get; }
}
