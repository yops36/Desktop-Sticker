using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using DesktopSticker.Configuration;
using DesktopSticker.Imaging;
using DesktopSticker.Wallpaper;
using Microsoft.Win32;

namespace DesktopSticker.Overlay;

/// <summary>
/// Rahmenloses, durchklickbares (Click-Through) Desktop-Overlay-Widget mit echter Per-Pixel-Alphatransparenz
/// und Unterstützung für animierte GIFs.
/// Liegt dauerhaft auf der Desktop-Ebene (vor Wallpaper Engine / dem Desktop-Hintergrund, aber hinter allen
/// normalen Programmfenstern) und stört weder Wallpaper Engine noch Fenstermanager wie Seelen UI.
/// </summary>
public sealed class StickerOverlayForm : Form
{
    private sealed class ActiveOverlayLayer : IDisposable
    {
        public required LoadedStickerImage Source { get; init; }
        public required Rectangle Bounds { get; init; }
        public required double CornerRadiusPercent { get; init; }
        public required Bitmap?[] CachedFrames { get; init; }
        public int CurrentFrameIndex { get; set; }
        public long LastFrameTickMs { get; set; }

        public Bitmap GetOrCreateCurrentFrame()
        {
            int count = Math.Max(1, Source.Frames.Count);
            int idx = Math.Clamp(CurrentFrameIndex, 0, count - 1);
            Bitmap? cached = CachedFrames[idx];
            if (cached is null)
            {
                cached = RoundedCornerProcessor.CreateScaledAndRoundedSticker(
                    Source.Frames[idx],
                    Bounds.Width,
                    Bounds.Height,
                    CornerRadiusPercent);
                CachedFrames[idx] = cached;
            }
            return cached;
        }

        public void Dispose()
        {
            foreach (Bitmap? bmp in CachedFrames)
            {
                bmp?.Dispose();
            }
            Source.Dispose();
        }
    }

    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_WINDOWPOSCHANGED = 0x0047;
    private const int WM_DISPLAYCHANGE = 0x007E;
    private const int MA_NOACTIVATE = 3;
    private const int HTTRANSPARENT = -1;

    private const uint GW_HWNDPREV = 3;
    private const uint GW_HWNDNEXT = 2;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOSENDCHANGING = 0x0400;

    private const int SW_SHOWNOACTIVATE = 4;

    private const uint ULW_ALPHA = 0x00000002;
    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;

    private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
    private const int DWMWA_EXCLUDED_FROM_PEEK = 12;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DONOTROUND = 1;

    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private static readonly IntPtr HWND_BOTTOM = new(1);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;

        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;

        public NativeSize(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref NativePoint pptDst,
        ref NativeSize psize,
        IntPtr hdcSrc,
        ref NativePoint pptSrc,
        uint crKey,
        ref BLENDFUNCTION pblend,
        uint dwFlags);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    private readonly SettingsManager _settingsManager;
    private readonly EventWaitHandle _reloadEvent;
    private readonly EventWaitHandle _shutdownEvent;
    private readonly EventWaitHandle _readyEvent;
    private readonly System.Windows.Forms.Timer _zOrderTimer;
    private readonly System.Windows.Forms.Timer _animationTimer;

    private readonly List<ActiveOverlayLayer> _activeLayers = new();
    private Bitmap? _compositeCanvas;
    private Rectangle _unionRect = Rectangle.Empty;
    private int _targetScreenX;
    private int _targetScreenY;

    private RegisteredWaitHandle? _reloadWaitRegistration;
    private RegisteredWaitHandle? _shutdownWaitRegistration;
    private bool _inZOrderUpdate;

    public StickerOverlayForm(
        SettingsManager settingsManager,
        EventWaitHandle reloadEvent,
        EventWaitHandle shutdownEvent,
        EventWaitHandle readyEvent)
    {
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _reloadEvent = reloadEvent ?? throw new ArgumentNullException(nameof(reloadEvent));
        _shutdownEvent = shutdownEvent ?? throw new ArgumentNullException(nameof(shutdownEvent));
        _readyEvent = readyEvent ?? throw new ArgumentNullException(nameof(readyEvent));

        Text = "DesktopStickerOverlayHost";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(1, 1);
        Location = new Point(-32000, -32000);

        _zOrderTimer = new System.Windows.Forms.Timer
        {
            Interval = 500
        };
        _zOrderTimer.Tick += (_, _) => AnchorToDesktopLayer();

        _animationTimer = new System.Windows.Forms.Timer
        {
            Interval = 25
        };
        _animationTimer.Tick += OnAnimationTimerTick;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.Style = WS_POPUP;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDwmDesktopWidgetAttributes();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _reloadWaitRegistration = ThreadPool.RegisterWaitForSingleObject(
            _reloadEvent,
            (_, _) => SafeInvokeOnUi(ReloadAndRenderSticker),
            null,
            -1,
            executeOnlyOnce: false);

        _shutdownWaitRegistration = ThreadPool.RegisterWaitForSingleObject(
            _shutdownEvent,
            (_, _) => SafeInvokeOnUi(Close),
            null,
            -1,
            executeOnlyOnce: true);

        ReloadAndRenderSticker();
        _zOrderTimer.Start();
    }

    private void ApplyDwmDesktopWidgetAttributes()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        try
        {
            int excludeFromPeek = 1;
            _ = DwmSetWindowAttribute(Handle, DWMWA_EXCLUDED_FROM_PEEK, ref excludeFromPeek, sizeof(int));

            int disableTransitions = 1;
            _ = DwmSetWindowAttribute(Handle, DWMWA_TRANSITIONS_FORCEDISABLED, ref disableTransitions, sizeof(int));

            int cornerPref = DWMWCP_DONOTROUND;
            _ = DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));
        }
        catch
        {
            // Ältere Windows-Versionen ohne bestimmte DWM-Attribute ignorieren
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        SafeInvokeOnUi(ReloadAndRenderSticker);
    }

    private void SafeInvokeOnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
        catch
        {
            // Ignorieren beim Herunterfahren
        }
    }

    private void OnAnimationTimerTick(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated || _activeLayers.Count == 0)
        {
            _animationTimer.Stop();
            return;
        }

        long now = Environment.TickCount64;
        bool anyAdvanced = false;

        foreach (ActiveOverlayLayer layer in _activeLayers)
        {
            int frameCount = layer.Source.Frames.Count;
            if (frameCount <= 1)
            {
                continue;
            }

            int delayMs = layer.CurrentFrameIndex < layer.Source.FrameDelaysMs.Count
                ? layer.Source.FrameDelaysMs[layer.CurrentFrameIndex]
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
            RenderCurrentOverlayFrame();
        }
    }

    public void ReloadAndRenderSticker()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        _animationTimer.Stop();
        ClearActiveLayers();

        SettingsLoadResult loadResult = _settingsManager.Load();
        AppSettings settings = loadResult.Settings;

        List<StickerItemConfig> validStickers = settings.GetEffectiveStickers()
            .Where(s => !string.IsNullOrWhiteSpace(s.StickerFilePath) && File.Exists(s.StickerFilePath))
            .ToList();

        if (!settings.IsStickerActive || validStickers.Count == 0)
        {
            _readyEvent.Set();
            Close();
            return;
        }

        try
        {
            var (screenWidth, screenHeight) = NativeDisplayMethods.GetPrimaryPhysicalResolution();
            Rectangle primaryBounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            if (screenWidth <= 0 || screenHeight <= 0)
            {
                screenWidth = Math.Max(1, primaryBounds.Width);
                screenHeight = Math.Max(1, primaryBounds.Height);
            }

            Rectangle unionRect = Rectangle.Empty;
            long now = Environment.TickCount64;

            foreach (StickerItemConfig item in validStickers)
            {
                LoadedStickerImage? loadedSticker = null;
                try
                {
                    loadedSticker = StickerImageLoader.LoadFromLocalCopy(item.StickerFilePath);

                    StickerPlacementResult placement = StickerGeometry.CalculatePlacement(
                        loadedSticker.Width,
                        loadedSticker.Height,
                        screenWidth,
                        screenHeight,
                        item.RelativeWidth,
                        item.RelativeX,
                        item.RelativeY);

                    var rect = new Rectangle(placement.X, placement.Y, placement.Width, placement.Height);
                    _activeLayers.Add(new ActiveOverlayLayer
                    {
                        Source = loadedSticker,
                        Bounds = rect,
                        CornerRadiusPercent = item.CornerRadiusPercent,
                        CachedFrames = new Bitmap?[Math.Max(1, loadedSticker.Frames.Count)],
                        CurrentFrameIndex = 0,
                        LastFrameTickMs = now
                    });
                    unionRect = unionRect.IsEmpty ? rect : Rectangle.Union(unionRect, rect);
                }
                catch
                {
                    loadedSticker?.Dispose();
                    // Einzelne defekte Bilddatei überspringen, damit andere Sticker weiterhin angezeigt werden
                }
            }

            if (_activeLayers.Count == 0 || unionRect.Width <= 0 || unionRect.Height <= 0)
            {
                ClearActiveLayers();
                _readyEvent.Set();
                Close();
                return;
            }

            _unionRect = unionRect;
            _targetScreenX = primaryBounds.Left + unionRect.X;
            _targetScreenY = primaryBounds.Top + unionRect.Y;

            RenderCurrentOverlayFrame();

            ShowWindow(Handle, SW_SHOWNOACTIVATE);
            AnchorToDesktopLayer();
            _readyEvent.Set();

            bool hasAnimatedGif = _activeLayers.Any(l => l.Source.IsAnimatedGif);
            if (hasAnimatedGif)
            {
                _animationTimer.Start();
            }
            else
            {
                // Bei rein statischen Bildern (PNG/JPG/statisches GIF) wird nach dem einmaligen Zeichnen
                // der gesamte Bildspeicher sofort wieder freigegeben (0 % CPU-Last im Hintergrund).
                ClearActiveLayers();
            }
        }
        catch
        {
            ClearActiveLayers();
            _readyEvent.Set();
        }
    }

    private void RenderCurrentOverlayFrame()
    {
        if (_activeLayers.Count == 0 || _unionRect.Width <= 0 || _unionRect.Height <= 0)
        {
            return;
        }

        try
        {
            if (_activeLayers.Count == 1)
            {
                Bitmap frameBmp = _activeLayers[0].GetOrCreateCurrentFrame();
                ApplyLayeredBitmap(frameBmp, _targetScreenX, _targetScreenY, _unionRect.Width, _unionRect.Height);
            }
            else
            {
                if (_compositeCanvas is null ||
                    _compositeCanvas.Width != _unionRect.Width ||
                    _compositeCanvas.Height != _unionRect.Height)
                {
                    _compositeCanvas?.Dispose();
                    _compositeCanvas = new Bitmap(_unionRect.Width, _unionRect.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                }

                using (Graphics g = Graphics.FromImage(_compositeCanvas))
                {
                    g.Clear(Color.Transparent);
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

                    foreach (ActiveOverlayLayer layer in _activeLayers)
                    {
                        Bitmap frameBmp = layer.GetOrCreateCurrentFrame();
                        g.DrawImage(
                            frameBmp,
                            layer.Bounds.X - _unionRect.X,
                            layer.Bounds.Y - _unionRect.Y,
                            layer.Bounds.Width,
                            layer.Bounds.Height);
                    }
                }

                ApplyLayeredBitmap(_compositeCanvas, _targetScreenX, _targetScreenY, _unionRect.Width, _unionRect.Height);
            }
        }
        catch
        {
            // Einzelnen Darstellungsfehler ignorieren
        }
    }

    private void ClearActiveLayers()
    {
        foreach (ActiveOverlayLayer layer in _activeLayers)
        {
            layer.Dispose();
        }
        _activeLayers.Clear();

        _compositeCanvas?.Dispose();
        _compositeCanvas = null;
    }

    private void ApplyLayeredBitmap(Bitmap bitmap, int screenX, int screenY, int width, int height)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;

        try
        {
            hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            oldBitmap = SelectObject(memDc, hBitmap);

            var topPos = new NativePoint(screenX, screenY);
            var size = new NativeSize(width, height);
            var pointSource = new NativePoint(0, 0);
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA
            };

            _ = UpdateLayeredWindow(
                Handle,
                screenDc,
                ref topPos,
                ref size,
                memDc,
                ref pointSource,
                0,
                ref blend,
                ULW_ALPHA);
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero)
            {
                _ = SelectObject(memDc, oldBitmap);
            }

            if (hBitmap != IntPtr.Zero)
            {
                _ = DeleteObject(hBitmap);
            }

            if (memDc != IntPtr.Zero)
            {
                _ = DeleteDC(memDc);
            }

            if (screenDc != IntPtr.Zero)
            {
                _ = ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    /// <summary>
    /// Hält das Sticker-Overlay zuverlässig unmittelbar vor der Desktop-/Wallpaper-Engine-Schicht (Progman / WorkerW)
    /// und hinter sämtlichen normalen Programmfenstern – ohne Flackern oder Z-Order-Kämpfe mit Vollbild- oder überlappenden Fenstern.
    /// </summary>
    private void AnchorToDesktopLayer()
    {
        if (_inZOrderUpdate || !IsHandleCreated || IsDisposed)
        {
            return;
        }

        _inZOrderUpdate = true;
        try
        {
            // GW_HWNDNEXT liefert das Fenster direkt unter uns in der Z-Order (Richtung Desktop-Hintergrund).
            IntPtr nextUnderneath = GetWindow(Handle, GW_HWNDNEXT);

            // GW_HWNDPREV liefert das Fenster direkt über uns in der Z-Order (Richtung Vordergrund).
            IntPtr prevAbove = GetWindow(Handle, GW_HWNDPREV);

            // Wenn das Fenster unter uns bereits der Desktop (Progman / WorkerW) oder das Ende der Z-Order-Kette ist,
            // UND über uns kein Desktop-Fenster liegt (z. B. nach Win+D / Explorer-Umschaltung),
            // befinden wir uns bereits exakt an der richtigen Stelle: hinter allen normalen Apps und vor dem Desktop.
            bool isDirectlyAboveDesktop = (nextUnderneath == IntPtr.Zero || IsDesktopWindow(nextUnderneath)) &&
                                          (prevAbove == IntPtr.Zero || !IsDesktopWindow(prevAbove));

            if (isDirectlyAboveDesktop)
            {
                return;
            }

            // Nur falls nötig: Sanft an den Boden der normalen Fensterschicht setzen.
            // WICHTIG: KEIN SWP_SHOWWINDOW übergeben, da dies DWM-Neuzeichnung und Flackern erzwingt!
            _ = SetWindowPos(
                Handle,
                HWND_BOTTOM,
                0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOSENDCHANGING);
        }
        finally
        {
            _inZOrderUpdate = false;
        }
    }

    private static bool IsDesktopWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        var sb = new StringBuilder(64);
        int len = GetClassNameW(hWnd, sb, sb.Capacity);
        if (len <= 0)
        {
            return false;
        }

        string className = sb.ToString();
        return string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_MOUSEACTIVATE:
                m.Result = new IntPtr(MA_NOACTIVATE);
                return;

            case WM_NCHITTEST:
                m.Result = new IntPtr(HTTRANSPARENT);
                return;

            case WM_DISPLAYCHANGE:
                base.WndProc(ref m);
                ReloadAndRenderSticker();
                return;

            case WM_WINDOWPOSCHANGED:
                base.WndProc(ref m);
                if (!_inZOrderUpdate)
                {
                    AnchorToDesktopLayer();
                }
                return;
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animationTimer.Stop();
            _animationTimer.Dispose();
            _zOrderTimer.Stop();
            _zOrderTimer.Dispose();
            ClearActiveLayers();
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

            _reloadWaitRegistration?.Unregister(null);
            _reloadWaitRegistration = null;

            _shutdownWaitRegistration?.Unregister(null);
            _shutdownWaitRegistration = null;
        }

        base.Dispose(disposing);
    }
}
