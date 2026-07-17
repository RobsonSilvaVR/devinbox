using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DevInbox.App.Services;

public static class WindowStyler
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>Se a barra de título deve ser escura — definido pelo <see cref="ThemeManager"/> conforme o tema.</summary>
    public static bool IsDarkChrome { get; set; } = true;

    /// <summary>Aplica o chrome (barra de título) combinando com o tema atual assim que a janela ganha handle.</summary>
    public static void ApplyChrome(Window window)
    {
        window.SourceInitialized += (_, _) => SetImmersiveDark(window, IsDarkChrome);
    }

    /// <summary>Reaplica o chrome numa janela já aberta (usado ao trocar de tema em runtime).</summary>
    public static void RefreshChrome(Window window, bool dark) => SetImmersiveDark(window, dark);

    private static void SetImmersiveDark(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
            return;

        var enabled = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
