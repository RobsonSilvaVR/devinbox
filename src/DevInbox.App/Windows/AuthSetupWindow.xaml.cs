using System.Net.Http;
using System.Windows;
using System.Windows.Navigation;
using System.Windows.Shapes;
using DevInbox.App.Hosting;
using DevInbox.App.Services;
using DevInbox.Core.Auth;
using DevInbox.Core.GitHub;

namespace DevInbox.App.Windows;

public partial class AuthSetupWindow : Window
{
    private readonly GitHubGraphQlClient _graphQl;
    private readonly PatTokenStore _patStore;
    private readonly TokenProviderChain _authChain;
    private readonly PollingBackgroundService _polling;

    public AuthSetupWindow(
        GitHubGraphQlClient graphQl,
        PatTokenStore patStore,
        TokenProviderChain authChain,
        PollingBackgroundService polling)
    {
        _graphQl = graphQl;
        _patStore = patStore;
        _authChain = authChain;
        _polling = polling;
        InitializeComponent();
        WindowStyler.ApplyChrome(this);
        RefreshGitHubStatus();
    }

    private void RefreshGitHubStatus()
    {
        var source = _authChain.CurrentSource;
        var connected = source is not null;

        StatusDot.SetResourceReference(Shape.FillProperty, connected ? "Brush.Success" : "Brush.Danger");
        StatusText.Text = connected ? $"Conectado · {source}" : "Desconectado";
        RemoveTokenButton.Visibility = _patStore.HasToken ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowFeedback(string message)
    {
        FeedbackText.Text = message;
        FeedbackText.Visibility = Visibility.Visible;
    }

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        BrowserLauncher.Open(e.Uri.ToString());
        e.Handled = true;
    }

    private void OnToggleToken(object sender, RoutedEventArgs e)
    {
        var opening = TokenPanel.Visibility != Visibility.Visible;
        TokenPanel.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
        if (opening)
            TokenBox.Focus();
    }

    private void OnUseGhCli(object sender, RoutedEventArgs e)
    {
        _authChain.Invalidate();
        _polling.TriggerNow();
        ShowFeedback("Verificando o GitHub CLI… acompanhe o ícone na bandeja.");
    }

    private void OnRemoveToken(object sender, RoutedEventArgs e)
    {
        _patStore.Delete();
        _authChain.Invalidate();
        _polling.TriggerNow();
        RefreshGitHubStatus();
        ShowFeedback("Token removido desta máquina.");
    }

    private async void OnValidateAndSave(object sender, RoutedEventArgs e)
    {
        var token = TokenBox.Password.Trim();
        if (token.Length == 0)
        {
            ShowFeedback("Cole o token antes de validar.");
            return;
        }

        ValidateButton.IsEnabled = false;
        ShowFeedback("Validando token no GitHub…");
        try
        {
            var login = await _graphQl.GetViewerLoginAsync(token, CancellationToken.None);
            _patStore.Save(token);
            _authChain.Invalidate();
            _polling.TriggerNow();
            TokenBox.Clear();
            RefreshGitHubStatus();
            ShowFeedback($"Token válido para @{login}. Monitoramento iniciado — pode fechar esta janela.");
        }
        catch (GitHubAuthException)
        {
            ShowFeedback("Token inválido ou sem as permissões necessárias (repo e notifications).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or GitHubApiException)
        {
            ShowFeedback("Não foi possível contatar o GitHub. Verifique sua conexão e tente de novo.");
        }
        finally
        {
            ValidateButton.IsEnabled = true;
        }
    }
}
