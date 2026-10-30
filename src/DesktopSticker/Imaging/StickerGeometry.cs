using System.Drawing;

namespace DesktopSticker.Imaging;

/// <summary>
/// Berechnete Geometrie des Stickers in sowohl relativen Monitoranteilen als auch exakten Zielpixeln (Kapitel 7 & 8).
/// </summary>
public readonly record struct StickerPlacementResult(
    double RelativeWidth,
    double MaxAllowedRelativeWidth,
    double RelativeX,
    double RelativeY,
    double MaxRelativeX,
    double MaxRelativeY,
    int X,
    int Y,
    int Width,
    int Height)
{
    public Rectangle Bounds => new(X, Y, Width, Height);
}

/// <summary>
/// Kapselt alle Berechnungen für die proportionale Stickergröße (Phase 5) und die bildschirmbegrenzte Positionierung (Phase 6).
/// </summary>
public static class StickerGeometry
{
    public const double MinRelativeWidth = 0.02; // 2 % der Bildschirmbreite
    public const double AbsoluteMaxRelativeWidth = 1.00; // 100 % der Bildschirmbreite

    /// <summary>
    /// Berechnet die maximal zulässige relative Breite (0.02..1.00), damit auch hochformatige Bilder
    /// niemals höher als die sichtbare Bildschirmhöhe werden.
    /// </summary>
    public static double GetMaxAllowedRelativeWidth(
        int stickerOriginalWidth,
        int stickerOriginalHeight,
        int targetSurfaceWidth,
        int targetSurfaceHeight)
    {
        if (stickerOriginalWidth <= 0 || stickerOriginalHeight <= 0 ||
            targetSurfaceWidth <= 0 || targetSurfaceHeight <= 0)
        {
            return AbsoluteMaxRelativeWidth;
        }

        double maxByHeight = (targetSurfaceHeight * (double)stickerOriginalWidth) /
                             (stickerOriginalHeight * (double)targetSurfaceWidth);

        return Math.Clamp(Math.Min(AbsoluteMaxRelativeWidth, maxByHeight), MinRelativeWidth, AbsoluteMaxRelativeWidth);
    }

    /// <summary>
    /// Berechnet die exakte proportionale Größe und innerhalb des Bildschirms begrenzte Position des Stickers (Kapitel 7 & 8).
    /// </summary>
    public static StickerPlacementResult CalculatePlacement(
        int stickerOriginalWidth,
        int stickerOriginalHeight,
        int targetSurfaceWidth,
        int targetSurfaceHeight,
        double requestedRelativeWidth,
        double requestedRelativeX,
        double requestedRelativeY)
    {
        if (stickerOriginalWidth <= 0 || stickerOriginalHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stickerOriginalWidth), "Stickerabmessungen müssen größer als 0 sein.");
        }

        if (targetSurfaceWidth <= 0 || targetSurfaceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetSurfaceWidth), "Zieloberfläche muss größer als 0 sein.");
        }

        if (double.IsNaN(requestedRelativeWidth) || double.IsInfinity(requestedRelativeWidth))
        {
            requestedRelativeWidth = 0.20;
        }

        if (double.IsNaN(requestedRelativeX) || double.IsInfinity(requestedRelativeX))
        {
            requestedRelativeX = 0.40;
        }

        if (double.IsNaN(requestedRelativeY) || double.IsInfinity(requestedRelativeY))
        {
            requestedRelativeY = 0.35;
        }

        double maxRelWidth = GetMaxAllowedRelativeWidth(
            stickerOriginalWidth,
            stickerOriginalHeight,
            targetSurfaceWidth,
            targetSurfaceHeight);

        double clampedRelWidth = Math.Clamp(requestedRelativeWidth, MinRelativeWidth, maxRelWidth);

        double aspectRatio = stickerOriginalHeight / (double)stickerOriginalWidth;

        int widthPx = Math.Clamp((int)Math.Round(clampedRelWidth * targetSurfaceWidth), 1, targetSurfaceWidth);
        int heightPx = Math.Max(1, (int)Math.Round(widthPx * aspectRatio));

        if (heightPx > targetSurfaceHeight)
        {
            heightPx = targetSurfaceHeight;
            widthPx = Math.Clamp((int)Math.Round(heightPx / aspectRatio), 1, targetSurfaceWidth);
            clampedRelWidth = widthPx / (double)targetSurfaceWidth;
        }

        double maxRelX = Math.Max(0.0, (targetSurfaceWidth - widthPx) / (double)targetSurfaceWidth);
        double maxRelY = Math.Max(0.0, (targetSurfaceHeight - heightPx) / (double)targetSurfaceHeight);

        double clampedRelX = Math.Clamp(requestedRelativeX, 0.0, maxRelX);
        double clampedRelY = Math.Clamp(requestedRelativeY, 0.0, maxRelY);

        int xPx = Math.Clamp((int)Math.Round(clampedRelX * targetSurfaceWidth), 0, Math.Max(0, targetSurfaceWidth - widthPx));
        int yPx = Math.Clamp((int)Math.Round(clampedRelY * targetSurfaceHeight), 0, Math.Max(0, targetSurfaceHeight - heightPx));

        return new StickerPlacementResult(
            RelativeWidth: clampedRelWidth,
            MaxAllowedRelativeWidth: maxRelWidth,
            RelativeX: clampedRelX,
            RelativeY: clampedRelY,
            MaxRelativeX: maxRelX,
            MaxRelativeY: maxRelY,
            X: xPx,
            Y: yPx,
            Width: widthPx,
            Height: heightPx);
    }
}
