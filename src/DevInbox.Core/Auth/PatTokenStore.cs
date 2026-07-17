using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace DevInbox.Core.Auth;

[SupportedOSPlatform("windows")]
public sealed class PatTokenStore : ITokenProvider
{
    private readonly string _filePath;

    public PatTokenStore(string baseDirectory)
    {
        Directory.CreateDirectory(baseDirectory);
        _filePath = Path.Combine(baseDirectory, "token.bin");
    }

    public string Name => "Token de acesso pessoal";

    public bool HasToken => File.Exists(_filePath);

    public Task<TokenResult> GetTokenAsync(CancellationToken cancellationToken)
    {
        var token = TryLoad();
        return Task.FromResult(token is null
            ? TokenResult.Fail(Name, AuthFailureReason.NoStoredToken)
            : TokenResult.Ok(token, Name));
    }

    public void Save(string token)
    {
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(token), optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_filePath, encrypted);
    }

    public void Delete()
    {
        if (HasToken)
            File.Delete(_filePath);
    }

    private string? TryLoad()
    {
        if (!File.Exists(_filePath))
            return null;

        try
        {
            var decrypted = ProtectedData.Unprotect(
                File.ReadAllBytes(_filePath), optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (CryptographicException)
        {
            File.Delete(_filePath);
            return null;
        }
    }
}
