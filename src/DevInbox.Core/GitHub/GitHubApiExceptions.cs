namespace DevInbox.Core.GitHub;

/// <summary>Token rejeitado pelo GitHub (HTTP 401) — dispara a reexecução da cadeia de auth.</summary>
public sealed class GitHubAuthException : Exception
{
    public GitHubAuthException(string message)
        : base(message)
    {
    }
}

public sealed class GitHubApiException : Exception
{
    public GitHubApiException(string message)
        : base(message)
    {
    }
}
