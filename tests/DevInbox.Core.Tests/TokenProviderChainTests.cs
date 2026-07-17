using DevInbox.Core.Auth;
using Xunit;

namespace DevInbox.Core.Tests;

public class TokenProviderChainTests
{
    [Fact]
    public async Task FallsBackToSecondProviderWhenFirstFails()
    {
        var gh = new FakeProvider("gh", TokenResult.Fail("gh", AuthFailureReason.GhNotInstalled));
        var pat = new FakeProvider("pat", TokenResult.Ok("token-123", "pat"));
        var chain = new TokenProviderChain([gh, pat]);

        var token = await chain.GetTokenAsync(CancellationToken.None);

        Assert.Equal("token-123", token);
        Assert.Equal("pat", chain.CurrentSource);
        Assert.Contains(chain.LastFailures, failure => failure.Failure == AuthFailureReason.GhNotInstalled);
    }

    [Fact]
    public async Task CachesTokenAcrossCalls()
    {
        var gh = new FakeProvider("gh", TokenResult.Ok("token-123", "gh"));
        var chain = new TokenProviderChain([gh]);

        await chain.GetTokenAsync(CancellationToken.None);
        await chain.GetTokenAsync(CancellationToken.None);

        Assert.Equal(1, gh.Calls);
    }

    [Fact]
    public async Task InvalidateForcesProvidersToRunAgain()
    {
        var gh = new FakeProvider("gh", TokenResult.Ok("token-123", "gh"));
        var chain = new TokenProviderChain([gh]);

        await chain.GetTokenAsync(CancellationToken.None);
        chain.Invalidate();
        await chain.GetTokenAsync(CancellationToken.None);

        Assert.Equal(2, gh.Calls);
    }

    [Fact]
    public async Task AllProvidersFailing_ReturnsNullWithFailures()
    {
        var gh = new FakeProvider("gh", TokenResult.Fail("gh", AuthFailureReason.GhNotLoggedIn));
        var pat = new FakeProvider("pat", TokenResult.Fail("pat", AuthFailureReason.NoStoredToken));
        var chain = new TokenProviderChain([gh, pat]);

        var token = await chain.GetTokenAsync(CancellationToken.None);

        Assert.Null(token);
        Assert.Null(chain.CurrentSource);
        Assert.Equal(2, chain.LastFailures.Count);
    }

    private sealed class FakeProvider : ITokenProvider
    {
        private readonly TokenResult _result;

        public FakeProvider(string name, TokenResult result)
        {
            Name = name;
            _result = result;
        }

        public string Name { get; }

        public int Calls { get; private set; }

        public Task<TokenResult> GetTokenAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }
}
