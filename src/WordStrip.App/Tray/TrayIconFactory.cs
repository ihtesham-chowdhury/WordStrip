using System.Drawing;
using System.Drawing.Drawing2D;

namespace WordStrip.App.Tray;

/// <summary>Draws a simple tray icon at runtime rather than shipping an .ico asset — one less binary asset to manage for a single-glyph icon.</summary>
internal static class TrayIconFactory
{
    public static Icon CreateIcon()
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var backgroundBrush = new SolidBrush(Color.FromArgb(255, 59, 130, 246)); // accent blue, matches the bar's selection highlight
            g.FillEllipse(backgroundBrush, 1, 1, size - 2, size - 2);

            using var font = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            const string text = "W";
            var textSize = g.MeasureString(text, font);
            g.DrawString(text, font, textBrush, (size - textSize.Width) / 2, (size - textSize.Height) / 2 - 1);
        }

        // Icon.FromHandle wraps the HICON without taking ownership of freeing it; for a single tray icon
        // that lives for the whole app session this is an acceptable one-time handle, not worth the extra
        // DestroyIcon P/Invoke for a personal-use MVP.
        return Icon.FromHandle(bitmap.GetHicon());
    }
}
