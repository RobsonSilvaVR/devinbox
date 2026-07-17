using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using DevInbox.App.Services;

namespace DevInbox.App.Windows;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        WindowStyler.ApplyChrome(this);

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "" : $"Versão {version.ToString(3)}";
    }

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        BrowserLauncher.Open(e.Uri.ToString());
        e.Handled = true;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
