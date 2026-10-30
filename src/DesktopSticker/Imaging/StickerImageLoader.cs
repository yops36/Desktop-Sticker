using System.Drawing;
using System.Drawing.Imaging;
using DesktopSticker.Configuration;

namespace DesktopSticker.Imaging;

public sealed class LoadedStickerImage : IDisposable
{
    public Bitmap Bitmap { get; }
    public string LocalFilePath { get; }
    public bool IsGif { get; }
    public IReadOnlyList<Bitmap> Frames { get; }
    public IReadOnlyList<int> FrameDelaysMs { get; }
    public int FrameCount => Frames.Count;
    public bool IsAnimatedGif => IsGif && Frames.Count > 1;
    public int Width => Bitmap.Width;
    public int Height => Bitmap.Height;
    public double AspectRatio => Height / (double)Width;

    /// <summary>
    /// Geschätzter unkomprimierter Arbeitsspeicherbedarf aller Einzelbilder in Bytes (Breite × Höhe × 4 × FrameCount).
    /// </summary>
    public long EstimatedUncompressedRamBytes => (long)Width * Height * 4L * Math.Max(1, Frames.Count);

    public LoadedStickerImage(
        Bitmap bitmap,
        string localFilePath,
        bool isGif = false,
        IReadOnlyList<Bitmap>? frames = null,
        IReadOnlyList<int>? frameDelaysMs = null)
    {
        Bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        LocalFilePath = localFilePath ?? throw new ArgumentNullException(nameof(localFilePath));
        IsGif = isGif;

        if (frames is { Count: > 0 })
        {
            Frames = frames;
        }
        else
        {
            Frames = new[] { bitmap };
        }

        if (frameDelaysMs is { Count: > 0 } && frameDelaysMs.Count == Frames.Count)
        {
            FrameDelaysMs = frameDelaysMs;
        }
        else
        {
            var delays = new int[Frames.Count];
            Array.Fill(delays, 100);
            FrameDelaysMs = delays;
        }
    }

    public void Dispose()
    {
        foreach (Bitmap frame in Frames)
        {
            if (!ReferenceEquals(frame, Bitmap))
            {
                frame.Dispose();
            }
        }
        Bitmap.Dispose();
    }
}

/// <summary>
/// Verantwortlich für das Prüfen, Laden, EXIF-Orientierungskorrigieren, GIF-Einzelbild-Dekodieren
/// und lokale Kopieren von Stickerbildern (Kapitel 6 & 14).
/// </summary>
public static class StickerImageLoader
{
    public const long MaxFileSizeBytes = 50L * 1024L * 1024L; // 50 MB
    public const int MaxImageDimension = 8192;
    public const long MaxTotalPixels = 36_000_000L; // z. B. 6000 x 6000
    public const int MaxGifFrames = 500;

    private const int ExifOrientationTagId = 0x0112;
    private const int ExifFrameDelayTagId = 0x5100;

    public static IReadOnlyCollection<string> SupportedExtensions => AllowedExtensions;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".gif"
    };

    /// <summary>
    /// Prüft und lädt eine vom Benutzer ausgewählte Bilddatei (PNG, JPG, JPEG oder GIF),
    /// berücksichtigt die EXIF-Orientierung bzw. GIF-Einzelbilder und legt eine unabhängige Kopie
    /// im Anwendungsdatenordner an.
    /// </summary>
    public static LoadedStickerImage ImportFromUserFile(string sourceFilePath, AppStoragePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ValidateSourceFileBasic(sourceFilePath);

        paths.EnsureDirectoriesAndWriteAccess();

        var (isGif, frames, frameDelaysMs) = LoadFramesFromFile(sourceFilePath, enforceStrictFormat: true);

        try
        {
            if (isGif)
            {
                // Bei GIF-Dateien die geprüfte Originaldatei als .gif sichern, damit alle Animationsframes
                // und Frame-Verzögerungen unverändert erhalten bleiben.
                string localGifName = $"sticker_{Guid.NewGuid():N}.gif";
                string tempGifPath = Path.Combine(paths.TempDir, localGifName + ".tmp");
                string finalGifPath = Path.Combine(paths.StickersDir, localGifName);

                try
                {
                    File.Copy(sourceFilePath, tempGifPath, overwrite: true);
                    File.Move(tempGifPath, finalGifPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(tempGifPath))
                    {
                        try
                        {
                            File.Delete(tempGifPath);
                        }
                        catch
                        {
                            // Ignorieren
                        }
                    }
                }

                return new LoadedStickerImage(frames[0], finalGifPath, isGif: true, frames, frameDelaysMs);
            }
            else
            {
                Bitmap normalizedBitmap = frames[0];

                // Als verlustfreie PNG-Kopie (32bpp ARGB) im Stickers-Ordner sichern, damit Transparenz und
                // bereits angewandte EXIF-Drehung dauerhaft und unabhängig von der Quelldatei erhalten bleiben.
                string localFileName = $"sticker_{Guid.NewGuid():N}.png";
                string tempPath = Path.Combine(paths.TempDir, localFileName + ".tmp");
                string finalLocalPath = Path.Combine(paths.StickersDir, localFileName);

                try
                {
                    normalizedBitmap.Save(tempPath, ImageFormat.Png);
                    File.Move(tempPath, finalLocalPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        try
                        {
                            File.Delete(tempPath);
                        }
                        catch
                        {
                            // Ignorieren
                        }
                    }
                }

                return new LoadedStickerImage(normalizedBitmap, finalLocalPath, isGif: false, frames, frameDelaysMs);
            }
        }
        catch
        {
            foreach (Bitmap bmp in frames)
            {
                bmp.Dispose();
            }
            throw;
        }
    }

    /// <summary>
    /// Lädt ein bereits im lokalen Anwendungsdatenordner gespeichertes Stickerbild (inkl. GIF-Animation).
    /// </summary>
    public static LoadedStickerImage LoadFromLocalCopy(string localFilePath)
    {
        if (string.IsNullOrWhiteSpace(localFilePath) || !File.Exists(localFilePath))
        {
            throw new FileNotFoundException("Die gespeicherte Sticker-Bilddatei wurde nicht gefunden.", localFilePath);
        }

        var (isGif, frames, frameDelaysMs) = LoadFramesFromFile(localFilePath, enforceStrictFormat: false);
        return new LoadedStickerImage(frames[0], localFilePath, isGif, frames, frameDelaysMs);
    }

    /// <summary>
    /// Lädt ein beliebiges Hintergrund- oder Stickerbild sicher in den Arbeitsspeicher (32bpp ARGB),
    /// ohne die Datei auf der Festplatte gesperrt zu halten.
    /// </summary>
    public static Bitmap LoadAndNormalizeBitmap(string filePath, bool enforceStrictFormat = false)
    {
        var (_, frames, _) = LoadFramesFromFile(filePath, enforceStrictFormat, firstFrameOnly: true);
        return frames[0];
    }

    private static (bool IsGif, List<Bitmap> Frames, int[] FrameDelaysMs) LoadFramesFromFile(
        string filePath,
        bool enforceStrictFormat,
        bool firstFrameOnly = false)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("Die angegebene Bilddatei existiert nicht.", filePath);
        }

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length <= 0)
        {
            throw new InvalidDataException("Die ausgewählte Bilddatei ist leer.");
        }

        long maxAllowedBytes = enforceStrictFormat ? MaxFileSizeBytes : 256L * 1024L * 1024L;
        if (fileInfo.Length > maxAllowedBytes)
        {
            throw new InvalidDataException(
                $"Die ausgewählte Bilddatei ist zu groß ({fileInfo.Length / (1024 * 1024)} MB). Maximal erlaubt sind {maxAllowedBytes / (1024 * 1024)} MB.");
        }

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var rawImage = Image.FromStream(fs, useEmbeddedColorManagement: true, validateImageData: true);

            bool isPng = rawImage.RawFormat.Equals(ImageFormat.Png);
            bool isJpeg = rawImage.RawFormat.Equals(ImageFormat.Jpeg);
            bool isGif = rawImage.RawFormat.Equals(ImageFormat.Gif);

            if (enforceStrictFormat && !isPng && !isJpeg && !isGif)
            {
                throw new InvalidDataException(
                    "Das Bildformat wird nicht unterstützt. Bitte wählen Sie ein gültiges PNG-, JPG-, JPEG- oder GIF-Bild aus.");
            }

            if (rawImage.Width < 1 || rawImage.Height < 1)
            {
                throw new InvalidDataException("Das ausgewählte Bild hat ungültige Abmessungen.");
            }

            int maxDim = enforceStrictFormat ? MaxImageDimension : 16384;
            long maxPixels = enforceStrictFormat ? MaxTotalPixels : 134_217_728L;

            if (rawImage.Width > maxDim || rawImage.Height > maxDim ||
                (long)rawImage.Width * rawImage.Height > maxPixels)
            {
                throw new InvalidDataException(
                    $"Die Bildauflösung ({rawImage.Width} × {rawImage.Height} Pixel) überschreitet den zulässigen Grenzwert von {maxDim} × {maxDim} Pixeln.");
            }

            if (isGif && !firstFrameOnly)
            {
                int frameCount = 1;
                bool hasTimeDimension = rawImage.FrameDimensionsList.Any(g => g == FrameDimension.Time.Guid);
                if (hasTimeDimension)
                {
                    try
                    {
                        frameCount = Math.Max(1, rawImage.GetFrameCount(FrameDimension.Time));
                    }
                    catch
                    {
                        frameCount = 1;
                    }
                }

                frameCount = Math.Min(frameCount, MaxGifFrames);
                int[] delaysMs = ExtractGifFrameDelaysMs(rawImage, frameCount);
                var frames = new List<Bitmap>(frameCount);

                try
                {
                    for (int i = 0; i < frameCount; i++)
                    {
                        if (hasTimeDimension && frameCount > 1)
                        {
                            rawImage.SelectActiveFrame(FrameDimension.Time, i);
                        }

                        var frameBitmap = new Bitmap(rawImage.Width, rawImage.Height, PixelFormat.Format32bppArgb);
                        frames.Add(frameBitmap);
                        frameBitmap.SetResolution(96.0f, 96.0f);

                        using (Graphics g = Graphics.FromImage(frameBitmap))
                        {
                            g.Clear(Color.Transparent);
                            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                            g.DrawImage(
                                rawImage,
                                new Rectangle(0, 0, frameBitmap.Width, frameBitmap.Height),
                                0, 0, rawImage.Width, rawImage.Height,
                                GraphicsUnit.Pixel);
                        }
                    }

                    return (true, frames, delaysMs);
                }
                catch
                {
                    foreach (Bitmap b in frames)
                    {
                        b.Dispose();
                    }
                    throw;
                }
            }

            // Statisches Bild (PNG, JPG oder Einzelbild): EXIF-Orientierung auswerten und anwenden (Anforderung 6.4)
            if (!isGif)
            {
                ApplyExifOrientationIfPresent(rawImage);
            }

            var cleanBitmap = new Bitmap(rawImage.Width, rawImage.Height, PixelFormat.Format32bppArgb);
            try
            {
                cleanBitmap.SetResolution(96.0f, 96.0f);

                using (Graphics g = Graphics.FromImage(cleanBitmap))
                {
                    g.Clear(Color.Transparent);
                    g.CompositingMode = isGif
                        ? System.Drawing.Drawing2D.CompositingMode.SourceOver
                        : System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.DrawImage(
                        rawImage,
                        new Rectangle(0, 0, cleanBitmap.Width, cleanBitmap.Height),
                        0, 0, rawImage.Width, rawImage.Height,
                        GraphicsUnit.Pixel);
                }

                return (isGif, new List<Bitmap>(1) { cleanBitmap }, new[] { 100 });
            }
            catch
            {
                cleanBitmap.Dispose();
                throw;
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (OutOfMemoryException ex)
        {
            // GDI+ wirft bei beschädigten Bilddateien häufig OutOfMemoryException
            throw new InvalidDataException(
                "Die ausgewählte Datei ist beschädigt oder kein gültiges PNG-, JPG-, JPEG- oder GIF-Bild.", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"Das Bild konnte nicht gelesen werden: {ex.Message}", ex);
        }
    }

    private static int[] ExtractGifFrameDelaysMs(Image image, int frameCount)
    {
        var delays = new int[frameCount];
        Array.Fill(delays, 100); // Standard 100 ms (10 FPS), falls keine Angaben im GIF hinterlegt sind

        try
        {
            if (image.PropertyIdList.Contains(ExifFrameDelayTagId))
            {
                PropertyItem? prop = image.GetPropertyItem(ExifFrameDelayTagId);
                if (prop?.Value is not null && prop.Value.Length >= 4)
                {
                    int availableDelays = prop.Value.Length / 4;
                    for (int i = 0; i < frameCount; i++)
                    {
                        int srcIdx = Math.Min(i, availableDelays - 1);
                        int delayHundredths = BitConverter.ToInt32(prop.Value, srcIdx * 4);
                        int ms = delayHundredths * 10;

                        // Standard-Viewer-Konvention: 0ms oder <=10ms wird als 100ms interpretiert,
                        // sehr kleine Werte auf mindestens 20ms (max. 50 FPS) begrenzen.
                        if (ms <= 10)
                        {
                            ms = 100;
                        }
                        else if (ms < 20)
                        {
                            ms = 20;
                        }
                        else if (ms > 60_000)
                        {
                            ms = 60_000;
                        }

                        delays[i] = ms;
                    }
                }
            }
        }
        catch
        {
            // Standardwert 100 ms beibehalten
        }

        return delays;
    }

    /// <summary>
    /// Entfernt nicht mehr benötigte alte Stickerkopien im Stickers-Ordner erst NACH dem erfolgreichen Speichern (Kapitel 14).
    /// </summary>
    public static void CleanupUnusedStickerCopies(AppStoragePaths paths, string? activeStickerFilePath)
    {
        string[] keep = string.IsNullOrWhiteSpace(activeStickerFilePath)
            ? Array.Empty<string>()
            : new[] { activeStickerFilePath };
        CleanupUnusedStickerCopies(paths, keep);
    }

    /// <summary>
    /// Entfernt alle Stickerkopien im Stickers-Ordner, die nicht in <paramref name="activeStickerFilePaths"/> enthalten sind.
    /// </summary>
    public static void CleanupUnusedStickerCopies(AppStoragePaths paths, IEnumerable<string?> activeStickerFilePaths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(activeStickerFilePaths);

        List<string> keepList = activeStickerFilePaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToList();

        try
        {
            if (!Directory.Exists(paths.StickersDir))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(paths.StickersDir))
            {
                if (keepList.Any(k => Backup.BackupManager.PathsEqual(file, k)))
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Ignorieren, falls Datei noch verwendet wird
                }
            }
        }
        catch
        {
            // Ignorieren
        }
    }

    private static void ValidateSourceFileBasic(string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath))
        {
            throw new ArgumentException("Es wurde keine Bilddatei ausgewählt.", nameof(sourceFilePath));
        }

        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("Die ausgewählte Bilddatei wurde nicht gefunden.", sourceFilePath);
        }

        string ext = Path.GetExtension(sourceFilePath);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedExtensions.Contains(ext))
        {
            throw new InvalidDataException(
                "Nicht unterstützte Dateiendung. Es werden ausschließlich PNG-, JPG-, JPEG- und GIF-Bilder unterstützt.");
        }
    }

    private static void ApplyExifOrientationIfPresent(Image image)
    {
        try
        {
            if (!image.PropertyIdList.Contains(ExifOrientationTagId))
            {
                return;
            }

            PropertyItem? prop = image.GetPropertyItem(ExifOrientationTagId);
            if (prop?.Value is null || prop.Value.Length < 2)
            {
                return;
            }

            ushort orientation = BitConverter.ToUInt16(prop.Value, 0);
            RotateFlipType rotateFlip = orientation switch
            {
                2 => RotateFlipType.RotateNoneFlipX,
                3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.Rotate180FlipX,
                5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone,
                7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone
            };

            if (rotateFlip != RotateFlipType.RotateNoneFlipNone)
            {
                image.RotateFlip(rotateFlip);
            }

            image.RemovePropertyItem(ExifOrientationTagId);
        }
        catch
        {
            // Ignorieren, falls EXIF-Tag fehlerhaft ist
        }
    }
}
