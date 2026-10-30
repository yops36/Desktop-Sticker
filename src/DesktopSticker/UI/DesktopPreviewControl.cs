using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using DesktopSticker.Configuration;
using DesktopSticker.Imaging;
using DesktopSticker.Localization;
using DesktopSticker.Wallpaper;

namespace DesktopSticker.UI;

/// <summary>
/// Bereich A – Interaktive Desktop-Vorschau (Kapitel 8, 9, 10, 13).
/// Unterstützt sowohl ein einzelnes Bild als auch mehrere gestapelte Sticker-Bilder gleichzeitig (inkl. animierter GIFs):
/// - Alle hinzugefügten Bilder werden maßstabsgetreu dargestellt.
/// - Ein Klick auf ein beliebiges Bild in der Vorschau wählt dieses Bild im linken vertikalen Tab aus
///   und erlaubt das direkte Verschieben mit gedrückter linker Maustaste.
/// </summary>
public sealed class DesktopPreviewControl : Control
{
    private sealed class PreviewLayerCache : IDisposable
    {
        public required StickerItemConfig Config { get; set; }
        public required Bitmap SourceBitmap { get; set; }
        public IReadOnlyList<Bitmap> SourceFrames { get; set; } = Array.Empty<Bitmap>();
        public IReadOnlyList<int> FrameDelaysMs { get; set; } = Array.Empty<int>();
        public Bitmap?[] CachedRoundedFrames { get; set; } = Array.Empty<Bitmap?>();
        public int CurrentFrameIndex { get; set; }
        public long LastFrameTickMs { get; set; }
        public Bitmap? CachedRoundedPreview { get; set; }
        public Size CachedSize { get; set; }
        public double CachedRadiusPercent { get; set; } = -1.0;

        public void InvalidateRoundedFrames()
        {
            foreach (Bitmap? bmp in CachedRoundedFrames)
            {
                bmp?.Dispose();
            }
            CachedRoundedFrames = new Bitmap?[Math.Max(1, SourceFrames.Count)];
            CachedRoundedPreview = null;
            CachedSize = Size.Empty;
            CachedRadiusPercent = -1.0;
        }

        public void Dispose()
        {
            InvalidateRoundedFrames();
        }
    }

    private Bitmap? _originalWallpaper;
    private bool _isSolidColorOnly;
    private Color _backgroundColor = Color.Black;
    private WallpaperPositionMode _positionMode = WallpaperPositionMode.Fill;
    private int _screenWidth = 1920;
    private int _screenHeight = 1080;

    // Einzel-Sticker-Felder (Abwärtskompatibilität & synchron zum ausgewählten Sticker)
    private Bitmap? _stickerBitmap;
    private double _relativeWidth = 0.20;
    private double _relativeX = 0.40;
    private double _relativeY = 0.35;
    private double _cornerRadiusPercent;

    private Bitmap? _cachedBackgroundPreview;
    private Size _cachedBackgroundSize;

    private Bitmap? _cachedStickerPreview;
    private Size _cachedStickerSize;
    private double _cachedStickerRadiusPercent = -1.0;

    // Multi-Sticker-Ebenen & GIF-Animationstimer
    private readonly List<PreviewLayerCache> _multiLayers = new();
    private readonly System.Windows.Forms.Timer _animationTimer;
    private string _selectedStickerId = string.Empty;
    private bool _useMultiLayerMode;

    private bool _isDragging;
    private Point _dragStartMouse;
    private double _dragStartRelX;
    private double _dragStartRelY;

    public event EventHandler? StickerPositionChanged;
    public event EventHandler<string>? StickerSelectedInPreview;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double RelativeWidth => _relativeWidth;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double RelativeX => _relativeX;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double RelativeY => _relativeY;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double CornerRadiusPercent => _cornerRadiusPercent;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string SelectedStickerId => _selectedStickerId;

    public DesktopPreviewControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = StyleGuidePalette.SurfaceSoft;
        TabStop = false;

        _animationTimer = new System.Windows.Forms.Timer
        {
            Interval = 30
        };
        _animationTimer.Tick += OnAnimationTimerTick;
    }

    private void OnAnimationTimerTick(object? sender, EventArgs e)
    {
        if (!_useMultiLayerMode || _multiLayers.Count == 0)
        {
            _animationTimer.Stop();
            return;
        }

        long now = Environment.TickCount64;
        bool anyAdvanced = false;

        foreach (PreviewLayerCache layer in _multiLayers)
        {
            int frameCount = layer.SourceFrames.Count;
            if (frameCount <= 1)
            {
                continue;
            }

            int delayMs = layer.CurrentFrameIndex < layer.FrameDelaysMs.Count
                ? layer.FrameDelaysMs[layer.CurrentFrameIndex]
                : 100;

            if (now - layer.LastFrameTickMs >= delayMs)
            {
                layer.CurrentFrameIndex = (layer.CurrentFrameIndex + 1) % frameCount;
                layer.LastFrameTickMs = now;
                anyAdvanced = true;
            }
        }

        if (anyAdvanced)
        {
            Invalidate();
        }
    }

    public void SetDesktopBackground(
        Bitmap? originalWallpaper,
        bool isSolidColorOnly,
        Color backgroundColor,
        WallpaperPositionMode positionMode,
        int screenWidth,
        int screenHeight)
    {
        _originalWallpaper = originalWallpaper;
        _isSolidColorOnly = isSolidColorOnly;
        _backgroundColor = backgroundColor;
        _positionMode = positionMode;
        _screenWidth = Math.Max(1, screenWidth);
        _screenHeight = Math.Max(1, screenHeight);

        InvalidateBackgroundCache();
        ClampCurrentStickerPlacement();
        Invalidate();
    }

    public void SetStickerImage(Bitmap? stickerBitmap)
    {
        _useMultiLayerMode = false;
        _animationTimer.Stop();
        ClearMultiLayers();
        _stickerBitmap = stickerBitmap;
        InvalidateStickerCache();
        ClampCurrentStickerPlacement();
        Invalidate();
    }

    public void SetStickerLayers(IReadOnlyList<StickerTabEntry> entries, string? selectedStickerId)
    {
        _useMultiLayerMode = true;
        ClearMultiLayers();

        long now = Environment.TickCount64;
        bool hasAnimatedLayer = false;

        foreach (StickerTabEntry entry in entries)
        {
            Bitmap? primaryBmp = entry.LoadedImage?.Bitmap ?? entry.Bitmap;
            if (primaryBmp is null)
            {
                continue;
            }

            IReadOnlyList<Bitmap> frames = entry.LoadedImage?.Frames is { Count: > 0 }
                ? entry.LoadedImage.Frames
                : new[] { primaryBmp };

            IReadOnlyList<int> delays = entry.LoadedImage?.FrameDelaysMs is { Count: > 0 }
                ? entry.LoadedImage.FrameDelaysMs
                : new[] { 100 };

            if (frames.Count > 1)
            {
                hasAnimatedLayer = true;
            }

            _multiLayers.Add(new PreviewLayerCache
            {
                Config = entry.Config,
                SourceBitmap = primaryBmp,
                SourceFrames = frames,
                FrameDelaysMs = delays,
                CachedRoundedFrames = new Bitmap?[frames.Count],
                CurrentFrameIndex = 0,
                LastFrameTickMs = now
            });
        }

        _selectedStickerId = selectedStickerId ?? (_multiLayers.FirstOrDefault()?.Config.Id ?? string.Empty);
        SyncSingleFieldsFromSelectedMultiLayer();

        if (hasAnimatedLayer)
        {
            _animationTimer.Start();
        }
        else
        {
            _animationTimer.Stop();
        }

        Invalidate();
    }

    public void UpdateStickerParameters(
        double relativeWidth,
        double relativeX,
        double relativeY,
        double cornerRadiusPercent)
    {
        bool radiusOrSizeChanged =
            Math.Abs(_relativeWidth - relativeWidth) > 0.00001 ||
            Math.Abs(_cornerRadiusPercent - cornerRadiusPercent) > 0.001;

        _relativeWidth = relativeWidth;
        _relativeX = relativeX;
        _relativeY = relativeY;
        _cornerRadiusPercent = Math.Clamp(cornerRadiusPercent, 0.0, 100.0);

        if (radiusOrSizeChanged)
        {
            InvalidateStickerCache();
        }

        ClampCurrentStickerPlacement();

        if (_useMultiLayerMode)
        {
            PreviewLayerCache? activeLayer = GetSelectedLayer();
            if (activeLayer is not null)
            {
                if (Math.Abs(activeLayer.Config.RelativeWidth - _relativeWidth) > 0.00001 ||
                    Math.Abs(activeLayer.Config.CornerRadiusPercent - _cornerRadiusPercent) > 0.001)
                {
                    activeLayer.InvalidateRoundedFrames();
                }

                activeLayer.Config.RelativeWidth = _relativeWidth;
                activeLayer.Config.RelativeX = _relativeX;
                activeLayer.Config.RelativeY = _relativeY;
                activeLayer.Config.CornerRadiusPercent = _cornerRadiusPercent;
            }
        }

        Invalidate();
    }

    private PreviewLayerCache? GetSelectedLayer()
    {
        if (_multiLayers.Count == 0)
        {
            return null;
        }

        return _multiLayers.FirstOrDefault(l => string.Equals(l.Config.Id, _selectedStickerId, StringComparison.OrdinalIgnoreCase))
               ?? _multiLayers[0];
    }

    private void SyncSingleFieldsFromSelectedMultiLayer()
    {
        PreviewLayerCache? active = GetSelectedLayer();
        if (active is null)
        {
            _stickerBitmap = null;
            InvalidateStickerCache();
            return;
        }

        _selectedStickerId = active.Config.Id;
        _stickerBitmap = active.SourceBitmap;
        _relativeWidth = active.Config.RelativeWidth;
        _relativeX = active.Config.RelativeX;
        _relativeY = active.Config.RelativeY;
        _cornerRadiusPercent = active.Config.CornerRadiusPercent;
        ClampCurrentStickerPlacement();
        active.Config.RelativeWidth = _relativeWidth;
        active.Config.RelativeX = _relativeX;
        active.Config.RelativeY = _relativeY;
    }

    private void ClampCurrentStickerPlacement()
    {
        if (_stickerBitmap is null)
        {
            return;
        }

        StickerPlacementResult placement = StickerGeometry.CalculatePlacement(
            _stickerBitmap.Width,
            _stickerBitmap.Height,
            _screenWidth,
            _screenHeight,
            _relativeWidth,
            _relativeX,
            _relativeY);

        _relativeWidth = placement.RelativeWidth;
        _relativeX = placement.RelativeX;
        _relativeY = placement.RelativeY;
    }

    private Rectangle GetMonitorViewportRectangle()
    {
        const int padding = 14;
        int availW = Math.Max(10, ClientSize.Width - (padding * 2));
        int availH = Math.Max(10, ClientSize.Height - (padding * 2));

        double scale = Math.Min(availW / (double)_screenWidth, availH / (double)_screenHeight);
        int viewW = Math.Max(1, (int)Math.Round(_screenWidth * scale));
        int viewH = Math.Max(1, (int)Math.Round(_screenHeight * scale));

        int viewX = (ClientSize.Width - viewW) / 2;
        int viewY = (ClientSize.Height - viewH) / 2;

        return new Rectangle(viewX, viewY, viewW, viewH);
    }

    private static Rectangle GetLayerPreviewRectangle(Rectangle viewport, Bitmap bitmap, double relWidth, double relX, double relY)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return Rectangle.Empty;
        }

        StickerPlacementResult placement = StickerGeometry.CalculatePlacement(
            bitmap.Width,
            bitmap.Height,
            viewport.Width,
            viewport.Height,
            relWidth,
            relX,
            relY);

        return new Rectangle(
            viewport.X + placement.X,
            viewport.Y + placement.Y,
            placement.Width,
            placement.Height);
    }

    private Rectangle GetStickerPreviewRectangle(Rectangle viewport)
    {
        if (_stickerBitmap is null || viewport.Width <= 0 || viewport.Height <= 0)
        {
            return Rectangle.Empty;
        }

        return GetLayerPreviewRectangle(viewport, _stickerBitmap, _relativeWidth, _relativeX, _relativeY);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        InvalidateBackgroundCache();
        InvalidateStickerCache();
        foreach (PreviewLayerCache layer in _multiLayers)
        {
            layer.InvalidateRoundedFrames();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        Rectangle viewport = GetMonitorViewportRectangle();
        if (viewport.Width <= 2 || viewport.Height <= 2)
        {
            return;
        }

        // 1. Hintergrund in exakter Monitor-Proportion zeichnen
        EnsureBackgroundCache(viewport.Size);
        if (_cachedBackgroundPreview is not null)
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(_cachedBackgroundPreview, viewport.Location);
        }

        // 2. Sticker-Ebenen darüber zeichnen
        if (_useMultiLayerMode)
        {
            if (_multiLayers.Count > 0)
            {
                Rectangle selectedRect = Rectangle.Empty;
                foreach (PreviewLayerCache layer in _multiLayers)
                {
                    Rectangle layerRect = GetLayerPreviewRectangle(
                        viewport,
                        layer.SourceBitmap,
                        layer.Config.RelativeWidth,
                        layer.Config.RelativeX,
                        layer.Config.RelativeY);

                    if (layerRect.Width <= 0 || layerRect.Height <= 0)
                    {
                        continue;
                    }

                    EnsureLayerCache(layer, layerRect.Size);
                    if (layer.CachedRoundedPreview is not null)
                    {
                        g.CompositingMode = CompositingMode.SourceOver;
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(layer.CachedRoundedPreview, layerRect.Location);
                    }

                    if (string.Equals(layer.Config.Id, _selectedStickerId, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedRect = layerRect;
                    }
                }

                // Auswahlrahmen um das aktuell ausgewählte Bild (#0098FE + #FFFFFF)
                if (!selectedRect.IsEmpty)
                {
                    g.CompositingMode = CompositingMode.SourceOver;
                    using var glowPen = new Pen(Color.FromArgb(190, StyleGuidePalette.LightBlue), 2f);
                    using var innerWhitePen = new Pen(Color.FromArgb(200, StyleGuidePalette.PureWhite), 1f) { DashStyle = DashStyle.Dash };
                    g.DrawRectangle(glowPen, selectedRect.X - 1, selectedRect.Y - 1, selectedRect.Width + 2, selectedRect.Height + 2);
                    g.DrawRectangle(innerWhitePen, selectedRect);
                }
            }
            else
            {
                DrawEmptyPreviewOverlay(g, viewport);
            }
        }
        else if (_stickerBitmap is not null)
        {
            Rectangle stickerRect = GetStickerPreviewRectangle(viewport);
            if (stickerRect.Width > 0 && stickerRect.Height > 0)
            {
                EnsureStickerCache(stickerRect.Size);
                if (_cachedStickerPreview is not null)
                {
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(_cachedStickerPreview, stickerRect.Location);
                }
            }
        }
        else
        {
            DrawEmptyPreviewOverlay(g, viewport);
        }

        // 3. Taktiler Rahmen um die Monitorfläche in der Vorschau
        g.CompositingMode = CompositingMode.SourceOver;
        using var borderPen = new Pen(StyleGuidePalette.LightBlue, 1.5f);
        g.DrawRectangle(borderPen, viewport.X - 1, viewport.Y - 1, viewport.Width + 1, viewport.Height + 1);
    }

    private void DrawEmptyPreviewOverlay(Graphics g, Rectangle viewport)
    {
        g.CompositingMode = CompositingMode.SourceOver;
        using var overlayBrush = new SolidBrush(Color.FromArgb(150, 0, 35, 95));
        g.FillRectangle(overlayBrush, viewport);

        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        using var textBrush = new SolidBrush(StyleGuidePalette.PureWhite);
        g.DrawString(
            Localizer.T("Preview.NoImageSelected"),
            Font,
            textBrush,
            viewport,
            sf);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        Rectangle viewport = GetMonitorViewportRectangle();

        if (_useMultiLayerMode)
        {
            // Von oben nach unten im Stapel prüfen, welches Bild angeklickt wurde
            for (int i = _multiLayers.Count - 1; i >= 0; i--)
            {
                PreviewLayerCache layer = _multiLayers[i];
                Rectangle rect = GetLayerPreviewRectangle(
                    viewport,
                    layer.SourceBitmap,
                    layer.Config.RelativeWidth,
                    layer.Config.RelativeX,
                    layer.Config.RelativeY);

                if (rect.Contains(e.Location))
                {
                    bool changedSelection = !string.Equals(_selectedStickerId, layer.Config.Id, StringComparison.OrdinalIgnoreCase);
                    _selectedStickerId = layer.Config.Id;
                    SyncSingleFieldsFromSelectedMultiLayer();

                    if (changedSelection)
                    {
                        StickerSelectedInPreview?.Invoke(this, _selectedStickerId);
                    }

                    _isDragging = true;
                    _dragStartMouse = e.Location;
                    _dragStartRelX = _relativeX;
                    _dragStartRelY = _relativeY;
                    Capture = true;
                    Cursor = Cursors.SizeAll;
                    Invalidate();
                    return;
                }
            }
            return;
        }

        if (_stickerBitmap is null)
        {
            return;
        }

        Rectangle stickerRect = GetStickerPreviewRectangle(viewport);
        if (stickerRect.Contains(e.Location))
        {
            _isDragging = true;
            _dragStartMouse = e.Location;
            _dragStartRelX = _relativeX;
            _dragStartRelY = _relativeY;
            Capture = true;
            Cursor = Cursors.SizeAll;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_stickerBitmap is null)
        {
            Cursor = Cursors.Default;
            return;
        }

        Rectangle viewport = GetMonitorViewportRectangle();
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        if (_isDragging)
        {
            double deltaRelX = (e.X - _dragStartMouse.X) / (double)viewport.Width;
            double deltaRelY = (e.Y - _dragStartMouse.Y) / (double)viewport.Height;

            StickerPlacementResult clamped = StickerGeometry.CalculatePlacement(
                _stickerBitmap.Width,
                _stickerBitmap.Height,
                _screenWidth,
                _screenHeight,
                _relativeWidth,
                _dragStartRelX + deltaRelX,
                _dragStartRelY + deltaRelY);

            if (Math.Abs(_relativeX - clamped.RelativeX) > 0.00001 ||
                Math.Abs(_relativeY - clamped.RelativeY) > 0.00001)
            {
                _relativeX = clamped.RelativeX;
                _relativeY = clamped.RelativeY;

                if (_useMultiLayerMode)
                {
                    PreviewLayerCache? active = GetSelectedLayer();
                    if (active is not null)
                    {
                        active.Config.RelativeX = _relativeX;
                        active.Config.RelativeY = _relativeY;
                    }
                }

                Invalidate();
                StickerPositionChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (_useMultiLayerMode)
        {
            bool overAny = false;
            for (int i = _multiLayers.Count - 1; i >= 0; i--)
            {
                PreviewLayerCache layer = _multiLayers[i];
                Rectangle rect = GetLayerPreviewRectangle(
                    viewport,
                    layer.SourceBitmap,
                    layer.Config.RelativeWidth,
                    layer.Config.RelativeX,
                    layer.Config.RelativeY);
                if (rect.Contains(e.Location))
                {
                    overAny = true;
                    break;
                }
            }
            Cursor = overAny ? Cursors.SizeAll : Cursors.Default;
        }
        else
        {
            Rectangle stickerRect = GetStickerPreviewRectangle(viewport);
            Cursor = stickerRect.Contains(e.Location) ? Cursors.SizeAll : Cursors.Default;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == MouseButtons.Left && _isDragging)
        {
            _isDragging = false;
            Capture = false;

            Rectangle viewport = GetMonitorViewportRectangle();
            Rectangle stickerRect = GetStickerPreviewRectangle(viewport);
            Cursor = stickerRect.Contains(e.Location) ? Cursors.SizeAll : Cursors.Default;
        }
    }

    private void EnsureBackgroundCache(Size targetSize)
    {
        if (_cachedBackgroundPreview is not null && _cachedBackgroundSize == targetSize)
        {
            return;
        }

        InvalidateBackgroundCache();

        Bitmap? scaledOrigForPreview = null;
        try
        {
            if (!_isSolidColorOnly && _originalWallpaper is not null &&
                (_positionMode == WallpaperPositionMode.Center || _positionMode == WallpaperPositionMode.Tile))
            {
                double scaleX = targetSize.Width / (double)_screenWidth;
                double scaleY = targetSize.Height / (double)_screenHeight;
                int scaledImgW = Math.Max(1, (int)Math.Round(_originalWallpaper.Width * scaleX));
                int scaledImgH = Math.Max(1, (int)Math.Round(_originalWallpaper.Height * scaleY));

                scaledOrigForPreview = new Bitmap(scaledImgW, scaledImgH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using Graphics g = Graphics.FromImage(scaledOrigForPreview);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(_originalWallpaper, new Rectangle(0, 0, scaledImgW, scaledImgH));
            }

            _cachedBackgroundPreview = WallpaperComposer.RenderBackgroundSurface(
                scaledOrigForPreview ?? _originalWallpaper,
                _isSolidColorOnly,
                _backgroundColor,
                _positionMode,
                targetSize.Width,
                targetSize.Height);

            _cachedBackgroundSize = targetSize;
        }
        finally
        {
            scaledOrigForPreview?.Dispose();
        }
    }

    private static void EnsureLayerCache(PreviewLayerCache layer, Size targetStickerSize)
    {
        if (layer.CachedSize != targetStickerSize ||
            Math.Abs(layer.CachedRadiusPercent - layer.Config.CornerRadiusPercent) >= 0.001 ||
            layer.CachedRoundedFrames.Length == 0)
        {
            layer.InvalidateRoundedFrames();
            layer.CachedSize = targetStickerSize;
            layer.CachedRadiusPercent = layer.Config.CornerRadiusPercent;
        }

        int frameCount = Math.Max(1, layer.SourceFrames.Count);
        int idx = Math.Clamp(layer.CurrentFrameIndex, 0, frameCount - 1);

        Bitmap? frameBmp = layer.CachedRoundedFrames[idx];
        if (frameBmp is null)
        {
            Bitmap srcFrame = idx < layer.SourceFrames.Count ? layer.SourceFrames[idx] : layer.SourceBitmap;
            frameBmp = RoundedCornerProcessor.CreateScaledAndRoundedSticker(
                srcFrame,
                targetStickerSize.Width,
                targetStickerSize.Height,
                layer.Config.CornerRadiusPercent);
            layer.CachedRoundedFrames[idx] = frameBmp;
        }

        layer.CachedRoundedPreview = frameBmp;
    }

    private void EnsureStickerCache(Size targetStickerSize)
    {
        if (_stickerBitmap is null)
        {
            InvalidateStickerCache();
            return;
        }

        if (_cachedStickerPreview is not null &&
            _cachedStickerSize == targetStickerSize &&
            Math.Abs(_cachedStickerRadiusPercent - _cornerRadiusPercent) < 0.001)
        {
            return;
        }

        InvalidateStickerCache();
        _cachedStickerPreview = RoundedCornerProcessor.CreateScaledAndRoundedSticker(
            _stickerBitmap,
            targetStickerSize.Width,
            targetStickerSize.Height,
            _cornerRadiusPercent);
        _cachedStickerSize = targetStickerSize;
        _cachedStickerRadiusPercent = _cornerRadiusPercent;
    }

    private void ClearMultiLayers()
    {
        foreach (PreviewLayerCache layer in _multiLayers)
        {
            layer.Dispose();
        }
        _multiLayers.Clear();
    }

    private void InvalidateBackgroundCache()
    {
        _cachedBackgroundPreview?.Dispose();
        _cachedBackgroundPreview = null;
        _cachedBackgroundSize = Size.Empty;
    }

    private void InvalidateStickerCache()
    {
        _cachedStickerPreview?.Dispose();
        _cachedStickerPreview = null;
        _cachedStickerSize = Size.Empty;
        _cachedStickerRadiusPercent = -1.0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animationTimer.Stop();
            _animationTimer.Dispose();
            InvalidateBackgroundCache();
            InvalidateStickerCache();
            ClearMultiLayers();
        }

        base.Dispose(disposing);
    }
}
