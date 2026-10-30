using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using DesktopSticker.Wallpaper;

namespace DesktopSticker.Imaging;

/// <summary>
/// Technischer Kern (Kapitel 10): Setzt das gesicherte Originalhintergrundbild (oder die Volltonfarbe)
/// unter Berücksichtigung des ursprünglichen Windows-Anzeigemodus mit dem skalierten und abgerundeten Sticker zusammen.
/// </summary>
public static class WallpaperComposer
{
    /// <summary>
    /// Erzeugt die Hintergrundfläche exakt so, wie Windows das Original-Hintergrundbild dargestellt hat (Schritt 1-4).
    /// </summary>
    public static Bitmap RenderBackgroundSurface(
        Bitmap? originalWallpaper,
         bool isSolidColorOnly,
        Color backgroundColor,
        WallpaperPositionMode positionMode,
        int targetWidth,
        int targetHeight)
    {
        if (targetWidth <= 0 || targetHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetWidth), "Zielabmessungen müssen größer als 0 sein.");
        }

        var canvas = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
        try
        {
            canvas.SetResolution(96.0f, 96.0f);

            using Graphics g = Graphics.FromImage(canvas);
            g.Clear(Color.FromArgb(255, backgroundColor.R, backgroundColor.G, backgroundColor.B));

            if (isSolidColorOnly || originalWallpaper is null)
            {
                return canvas;
            }

            g.CompositingMode = CompositingMode.SourceOver;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int imgW = originalWallpaper.Width;
            int imgH = originalWallpaper.Height;
            if (imgW <= 0 || imgH <= 0)
            {
                return canvas;
            }

            using var wrapAttr = new ImageAttributes();
            wrapAttr.SetWrapMode(WrapMode.TileFlipXY);

            switch (positionMode)
            {
                case WallpaperPositionMode.Fill:
                case WallpaperPositionMode.Span:
                {
                    // Ausfüllen: Proportional vergrößern/verkleinern und mittig beschneiden
                    double scale = Math.Max(targetWidth / (double)imgW, targetHeight / (double)imgH);
                    float scaledW = (float)(imgW * scale);
                    float scaledH = (float)(imgH * scale);
                    float offsetX = (targetWidth - scaledW) * 0.5f;
                    float offsetY = (targetHeight - scaledH) * 0.5f;

                    DrawImageHighQuality(g, originalWallpaper, offsetX, offsetY, scaledW, scaledH, wrapAttr);
                    break;
                }

                case WallpaperPositionMode.Fit:
                {
                    // Anpassen: Vollständig und proportional einpassen, Ränder erhalten die Hintergrundfarbe
                    double scale = Math.Min(targetWidth / (double)imgW, targetHeight / (double)imgH);
                    float scaledW = (float)(imgW * scale);
                    float scaledH = (float)(imgH * scale);
                    float offsetX = (targetWidth - scaledW) * 0.5f;
                    float offsetY = (targetHeight - scaledH) * 0.5f;

                    DrawImageHighQuality(g, originalWallpaper, offsetX, offsetY, scaledW, scaledH, wrapAttr);
                    break;
                }

                case WallpaperPositionMode.Stretch:
                {
                    // Strecken: Ohne Beibehaltung des Seitenverhältnisses auf die gesamte Bildschirmfläche anpassen
                    DrawImageHighQuality(g, originalWallpaper, 0f, 0f, targetWidth, targetHeight, wrapAttr);
                    break;
                }

                case WallpaperPositionMode.Center:
                {
                    // Zentrieren: Ursprüngliche Pixelgröße mittig darstellen; überstehende Bereiche abschneiden
                    float offsetX = (float)Math.Round((targetWidth - imgW) * 0.5);
                    float offsetY = (float)Math.Round((targetHeight - imgH) * 0.5);

                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    DrawImageHighQuality(g, originalWallpaper, offsetX, offsetY, imgW, imgH, wrapAttr);
                    break;
                }

                case WallpaperPositionMode.Tile:
                {
                    // Kacheln: Über nativen TextureBrush kacheln (verhindert Millionen Einzelaufrufe bei kleinen Kacheln)
                    using var textureBrush = new TextureBrush(originalWallpaper, WrapMode.Tile);
                    g.FillRectangle(textureBrush, 0, 0, targetWidth, targetHeight);
                    break;
                }
            }

            return canvas;
        }
        catch
        {
            canvas.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Führt alle 10 Schritte der Wallpaper-Komposition aus (Kapitel 10) und gibt ein vollständig
    /// deckendes 24-Bit-Bitmap in der exakten Zielauflösung zurück.
    /// </summary>
    public static Bitmap ComposeWallpaper(
        Bitmap? originalWallpaper,
        bool isSolidColorOnly,
        Color backgroundColor,
        WallpaperPositionMode originalPositionMode,
        Bitmap stickerBitmap,
        int screenWidth,
        int screenHeight,
        double relativeWidth,
        double relativeX,
        double relativeY,
        double cornerRadiusPercent)
    {
        ArgumentNullException.ThrowIfNull(stickerBitmap);

        // Schritte 1 bis 4: Hintergrundfläche in exakter Monitorauflösung vorbereiten
        using Bitmap bgCanvas = RenderBackgroundSurface(
            originalWallpaper,
            isSolidColorOnly,
            backgroundColor,
            originalPositionMode,
            screenWidth,
            screenHeight);

        // Schritte 5, 6 und 8: Stickergröße und Pixelkoordinaten berechnen
        StickerPlacementResult placement = StickerGeometry.CalculatePlacement(
            stickerBitmap.Width,
            stickerBitmap.Height,
            screenWidth,
            screenHeight,
            relativeWidth,
            relativeX,
            relativeY);

        // Schritt 7: Sticker einmalig skalieren und Eckenradius-Transparenzmaske berechnen
        using Bitmap processedSticker = RoundedCornerProcessor.CreateScaledAndRoundedSticker(
            stickerBitmap,
            placement.Width,
            placement.Height,
            cornerRadiusPercent);

        // Schritt 9: Sticker mit korrekter Alphatransparenz auf die Hintergrundfläche zeichnen
        using (Graphics g = Graphics.FromImage(bgCanvas))
        {
            g.CompositingMode = CompositingMode.SourceOver;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            g.DrawImage(
                processedSticker,
                new Rectangle(placement.X, placement.Y, placement.Width, placement.Height),
                0,
                0,
                processedSticker.Width,
                processedSticker.Height,
                GraphicsUnit.Pixel);
        }

        // Schritt 10: Als vollständig deckendes 24-Bit-RGB-Bitmap bereitstellen (für verlustfreie BMP-Speicherung)
        var opaqueOutput = new Bitmap(screenWidth, screenHeight, PixelFormat.Format24bppRgb);
        try
        {
            opaqueOutput.SetResolution(96.0f, 96.0f);
            using (Graphics gOut = Graphics.FromImage(opaqueOutput))
            {
                gOut.CompositingMode = CompositingMode.SourceCopy;
                gOut.DrawImageUnscaled(bgCanvas, 0, 0);
            }

            return opaqueOutput;
        }
        catch
        {
            opaqueOutput.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Speichert ein zusammengesetztes Wallpaper verlustfrei als BMP-Datei (über eine temporäre Datei).
    /// </summary>
    public static void SaveComposedWallpaperAsBmp(Bitmap composedBitmap, string targetFilePath, string tempDir)
    {
        ArgumentNullException.ThrowIfNull(composedBitmap);
        if (string.IsNullOrWhiteSpace(targetFilePath))
        {
            throw new ArgumentException("Zielpfad darf nicht leer sein.", nameof(targetFilePath));
        }

        Directory.CreateDirectory(tempDir);
        string? targetDir = Path.GetDirectoryName(targetFilePath);
        if (!string.IsNullOrWhiteSpace(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        string tempFile = Path.Combine(tempDir, $"composed_{Guid.NewGuid():N}.bmp.tmp");
        try
        {
            composedBitmap.Save(tempFile, ImageFormat.Bmp);

            if (!WindowsWallpaperService.CanReadImageFile(tempFile))
            {
                throw new IOException("Das erzeugte BMP-Hintergrundbild konnte nach dem Speichern nicht verifiziert werden.");
            }

            File.Move(tempFile, targetFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch
                {
                    // Ignorieren
                }
            }
        }
    }

    private static void DrawImageHighQuality(
        Graphics g,
        Bitmap source,
        float destX,
        float destY,
        float destW,
        float destH,
        ImageAttributes wrapAttr)
    {
        PointF[] destPoints =
        {
            new(destX, destY),
            new(destX + destW, destY),
            new(destX, destY + destH)
        };

        g.DrawImage(
            source,
            destPoints,
            new RectangleF(0, 0, source.Width, source.Height),
            GraphicsUnit.Pixel,
            wrapAttr);
    }
}
