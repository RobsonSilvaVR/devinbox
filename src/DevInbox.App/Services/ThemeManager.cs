using System.Windows;

namespace DevInbox.App.Services;

public sealed record ThemeInfo(string Id, string DisplayName, bool IsDark)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// Troca a paleta de cores da aplicação em runtime. Os estilos (Themes/Styles.xaml) referenciam
/// os brushes via DynamicResource, então substituir o dicionário de paleta reflete ao vivo em todas
/// as janelas abertas. A barra de título (chrome) é reaplicada conforme o tema seja claro ou escuro.
/// </summary>
public sealed class ThemeManager
{
    public static IReadOnlyList<ThemeInfo> Themes { get; } =
    [
        new("GitHub", "GitHub (padrão)", IsDark: true),
        new("Darcula", "Darcula", IsDark: true),
        new("VSCode", "VS Code", IsDark: true),
        new("Solarized", "Solarized Light", IsDark: false),
        new("Light", "Claro", IsDark: false),
    ];

    public static ThemeInfo Resolve(string? id) =>
        Themes.FirstOrDefault(t => t.Id == id) ?? Themes[0];

    public string CurrentThemeId { get; private set; } = Themes[0].Id;

    public void Apply(string themeId)
    {
        var info = Resolve(themeId);
        if (Application.Current is not { } app)
            return;

        var newPalette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/Palettes/{info.Id}.xaml", UriKind.Absolute),
        };

        var dictionaries = app.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(d =>
            d.Source is { } source &&
            source.OriginalString.Contains("/Palettes/", StringComparison.OrdinalIgnoreCase));

        if (current is not null)
            dictionaries[dictionaries.IndexOf(current)] = newPalette;
        else
            dictionaries.Insert(0, newPalette);

        CurrentThemeId = info.Id;
        WindowStyler.IsDarkChrome = info.IsDark;
        foreach (Window window in app.Windows)
            WindowStyler.RefreshChrome(window, info.IsDark);
    }
}
