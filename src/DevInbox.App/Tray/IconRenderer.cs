using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using DevInbox.App.Services;

namespace DevInbox.App.Tray;

/// <summary>Ícone GDI cujo handle nativo precisa ser destruído depois que a bandeja troca de imagem.</summary>
public sealed class TrayIconImage : IDisposable
{
    private readonly nint _handle;

    public TrayIconImage(nint handle)
    {
        _handle = handle;
        Icon = Icon.FromHandle(handle);
    }

    public Icon Icon { get; }

    public void Dispose()
    {
        Icon.Dispose();
        DestroyIcon(_handle);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);
}

public sealed class IconRenderer
{
    private const int Size = 32;

    public TrayIconImage Render(int unreadCount, TrayStatus status)
    {
        using var bitmap = new Bitmap(Size, Size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using (var background = new SolidBrush(Color.FromArgb(0x18, 0x1C, 0x22)))
            using (var path = RoundedRectangle(new Rectangle(0, 0, Size - 1, Size - 1), 7))
            {
                graphics.FillPath(background, path);
            }

            DrawInboxGlyph(graphics, Size);

            if (status != TrayStatus.Ok)
            {
                using var dot = new SolidBrush(
                    status == TrayStatus.AuthRequired ? Color.Gold : Color.OrangeRed);
                graphics.FillEllipse(dot, 1, Size - 13, 12, 12);
            }

            if (unreadCount > 0)
            {
                var label = unreadCount > 9 ? "9+" : unreadCount.ToString();
                // Anel branco separa o badge da seta coral do logo.
                graphics.FillEllipse(Brushes.White, Size - 20, -1, 20, 20);
                graphics.FillEllipse(Brushes.Red, Size - 18.5f, 0.5f, 17, 17);
                DrawCenteredText(graphics, label, label.Length > 1 ? 9f : 11f, Brushes.White,
                    new PointF(Size - 10, 9f));
            }
        }

        return new TrayIconImage(bitmap.GetHicon());
    }

    /// <summary>Glifo monocromático (bandeja + seta brancas) — mesmo desenho do app.ico (icogen).</summary>
    private static void DrawInboxGlyph(Graphics graphics, float s)
    {
        PointF[] tray =
        [
            new(0.17f * s, 0.50f * s), new(0.35f * s, 0.50f * s), new(0.41f * s, 0.62f * s),
            new(0.59f * s, 0.62f * s), new(0.65f * s, 0.50f * s), new(0.83f * s, 0.50f * s),
            new(0.83f * s, 0.84f * s), new(0.17f * s, 0.84f * s),
        ];
        graphics.FillPolygon(Brushes.White, tray);

        graphics.FillRectangle(Brushes.White, 0.445f * s, 0.10f * s, 0.11f * s, 0.20f * s);
        PointF[] arrowHead =
        [
            new(0.33f * s, 0.28f * s), new(0.67f * s, 0.28f * s), new(0.50f * s, 0.46f * s),
        ];
        graphics.FillPolygon(Brushes.White, arrowHead);
    }

    private static void DrawCenteredText(
        Graphics graphics, string text, float fontSize, Brush brush, PointF center)
    {
        using var font = new Font("Segoe UI", fontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
        var measured = graphics.MeasureString(text, font);
        graphics.DrawString(text, font, brush,
            center.X - measured.Width / 2f, center.Y - measured.Height / 2f);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
