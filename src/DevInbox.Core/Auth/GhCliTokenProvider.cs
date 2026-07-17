using System.ComponentModel;
using System.Diagnostics;

namespace DevInbox.Core.Auth;

public sealed class GhCliTokenProvider : ITokenProvider
{
    private static readonly string FallbackGhPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI", "gh.exe");

    public string Name => "GitHub CLI";

    public async Task<TokenResult> GetTokenAsync(CancellationToken cancellationToken)
    {
        var result = await RunGhAsync("gh", cancellationToken);
        if (result is null && File.Exists(FallbackGhPath))
            result = await RunGhAsync(FallbackGhPath, cancellationToken);

        if (result is null)
            return TokenResult.Fail(Name, AuthFailureReason.GhNotInstalled);

        var token = result.Value.Stdout.Trim();
        return result.Value.ExitCode == 0 && token.Length > 0
            ? TokenResult.Ok(token, Name)
            : TokenResult.Fail(Name, AuthFailureReason.GhNotLoggedIn);
    }

    private static async Task<(int ExitCode, string Stdout)?> RunGhAsync(string ghPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(ghPath, "auth token")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            var stdout = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            _ = await process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            return (process.ExitCode, stdout);
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
