using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace DesktopSticker.Imaging;

/// <summary>
/// Berechnet die Skalierung und die kantengeglättete Eckenradius-Transparenzmaske für Stickerbilder (Kapitel 7 & 9).
/// </summary>
public static class RoundedCornerProcessor
{
    /// <summary>
    /// Berechnet den tatsächlichen Eckenradius in Pixeln anhand des eingestellten Prozentwerts (0..100)
    /// und begrenzt ihn auf maximal die Hälfte der kürzeren Bildkante.
    /// </summary>
    public static double CalculateRadiusInPixels(int width, int height, double cornerRadiusPercent)
    {
        if (width <= 1 || height <= 1 || cornerRadiusPercent <= 0.0)
        {
            return 0.0;
        }

        double clampedPercent = Math.Clamp(cornerRadiusPercent, 0.0, 100.0);
        double maxRadius = Math.Min(width, height) * 0.5;
        return Math.Clamp((clampedPercent / 100.0) * maxRadius, 0.0, maxRadius);
    }

    /// <summary>
    /// Skaliert das Quell-Stickerbild einmalig in hoher Qualität auf die Zielgröße und wendet
    /// anschließend den gewünschten Eckenradius unter vollständigem Erhalt vorhandener PNG-Transparenz an.
    /// </summary>
    public static Bitmap CreateScaledAndRoundedSticker(
        Bitmap sourceSticker,
        int targetWidth,
        int targetHeight,
        double cornerRadiusPercent)
    {
        ArgumentNullException.ThrowIfNull(sourceSticker);

        int width = Math.Max(1, targetWidth);
        int height = Math.Max(1, targetHeight);

        var scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            scaled.SetResolution(96.0f, 96.0f);

            using (Graphics g = Graphics.FromImage(scaled))
            {
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using var wrapAttr = new ImageAttributes();
                wrapAttr.SetWrapMode(WrapMode.TileFlipXY);

                g.DrawImage(
                    sourceSticker,
                    new Rectangle(0, 0, width, height),
                    0,
                    0,
                    sourceSticker.Width,
                    sourceSticker.Height,
                    GraphicsUnit.Pixel,
                    wrapAttr);
            }

            ApplyRoundedCornersInPlace(scaled, cornerRadiusPercent);
            return scaled;
        }
        catch
        {
            scaled.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Wendet eine kantengeglättete (4x4 Subpixel-Supersampling) Eckenradius-Transparenzmaske direkt
    /// auf ein 32bpp-ARGB-Bitmap an. Vorhandene transparente Bereiche bleiben vollständig erhalten.
    /// </summary>
    public static unsafe void ApplyRoundedCornersInPlace(Bitmap bitmap, double cornerRadiusPercent)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        int width = bitmap.Width;
        int height = bitmap.Height;
        double radius = CalculateRadiusInPixels(width, height, cornerRadiusPercent);

        if (radius < 0.5)
        {
            return;
        }

        int cornerLimit = (int)Math.Ceiling(radius);
        var rect = new Rectangle(0, 0, width, height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

        try
        {
            byte* scan0 = (byte*)data.Scan0;
            int stride = data.Stride;

            double rMinus = Math.Max(0.0, radius - 0.85);
            double rPlus = radius + 0.85;
            double rMinusSq = rMinus * rMinus;
            double rPlusSq = rPlus * rPlus;
            double rSq = radius * radius;

            for (int y = 0; y < height; y++)
            {
                double cy;
                if (y < cornerLimit)
                {
                    cy = radius;
                }
                else if (y >= height - cornerLimit)
                {
                    cy = height - radius;
                }
                else
                {
                    // Zeile liegt vertikal zwischen den Ecken -> keine Änderung nötig
                    continue;
                }

                byte* row = scan0 + (y * stride);

                for (int x = 0; x < width; x++)
                {
                    double cx;
                    if (x < cornerLimit)
                    {
                        cx = radius;
                    }
                    else if (x >= width - cornerLimit)
                    {
                        cx = width - radius;
                    }
                    else
                    {
                        // Zwischen linker und rechter Ecke überspringen
                        x = Math.Max(x, width - cornerLimit - 1);
                        continue;
                    }

                    double pxCenter = x + 0.5;
                    double pyCenter = y + 0.5;

                    // Prüfen, ob das Pixel im äußeren Quadranten der jeweiligen Ecke liegt
                    bool inOuterCorner =
                        (x < cornerLimit && pxCenter < cx || x >= width - cornerLimit && pxCenter > cx) &&
                        (y < cornerLimit && pyCenter < cy || y >= height - cornerLimit && pyCenter > cy);

                    if (!inOuterCorner)
                    {
                        continue;
                    }

                    byte* pixel = row + (x * 4);
                    byte origAlpha = pixel[3];
                    if (origAlpha == 0)
                    {
                        // Bereits vollständig transparentes PNG-Pixel bleibt transparent
                        continue;
                    }

                    double dx = pxCenter - cx;
                    double dy = pyCenter - cy;
                    double distSq = (dx * dx) + (dy * dy);

                    if (distSq <= rMinusSq)
                    {
                        // Vollständig innerhalb des abgerundeten Bereichs
                        continue;
                    }

                    if (distSq >= rPlusSq)
                    {
                        // Vollständig außerhalb der abgerundeten Ecke -> transparent setzen
                        pixel[0] = 0;
                        pixel[1] = 0;
                        pixel[2] = 0;
                        pixel[3] = 0;
                        continue;
                    }

                    // Grenzbereich der Kante: 4x4 Subpixel-Supersampling für saubere Kantenglättung
                    int insideSamples = 0;
                    for (int sy = 0; sy < 4; sy++)
                    {
                        double subY = y + ((sy + 0.5) * 0.25) - cy;
                        double subYSq = subY * subY;
                        for (int sx = 0; sx < 4; sx++)
                        {
                            double subX = x + ((sx + 0.5) * 0.25) - cx;
                            if ((subX * subX) + subYSq <= rSq)
                            {
                                insideSamples++;
                            }
                        }
                    }

                    if (insideSamples == 0)
                    {
                        pixel[0] = 0;
                        pixel[1] = 0;
                        pixel[2] = 0;
                        pixel[3] = 0;
                    }
                    else if (insideSamples < 16)
                    {
                        byte newAlpha = (byte)((origAlpha * insideSamples + 8) / 16);
                        pixel[3] = newAlpha;
                        if (newAlpha == 0)
                        {
                            pixel[0] = 0;
                            pixel[1] = 0;
                            pixel[2] = 0;
                        }
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
