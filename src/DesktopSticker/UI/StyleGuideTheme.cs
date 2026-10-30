using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace DesktopSticker.UI;

/// <summary>
/// Farbpalette und Zeichenhilfen gemäß dem Product UI Styleguide:
/// - Hellblau:   #0098FE
/// - Mittelblau: #0074FC
/// - Dunkelblau: #0042E9
/// - Pur Weiß:   #FFFFFF
/// </summary>
public static class StyleGuidePalette
{
    public static readonly Color LightBlue = Color.FromArgb(0, 152, 254);   // #0098FE
    public static readonly Color MediumBlue = Color.FromArgb(0, 116, 252);  // #0074FC
    public static readonly Color DarkBlue = Color.FromArgb(0, 66, 233);     // #0042E9
    public static readonly Color PureWhite = Color.FromArgb(255, 255, 255); // #FFFFFF

    // Abgeleitete helle Oberflächen- und Schattentöne (Weiß + Blau-Abstufungen)
    public static readonly Color AppBackground = Color.FromArgb(240, 245, 253);
    public static readonly Color SurfaceSoft = Color.FromArgb(247, 250, 255);
    public static readonly Color BorderSoft = Color.FromArgb(208, 224, 248);
    public static readonly Color BevelLipLight = Color.FromArgb(184, 206, 240);
    public static readonly Color DisabledSurface = Color.FromArgb(228, 235, 246);
    public static readonly Color DisabledBevel = Color.FromArgb(198, 210, 228);
    public static readonly Color DisabledText = Color.FromArgb(140, 158, 186);

    public static readonly Color TextPrimary = Color.FromArgb(0, 44, 148);
    public static readonly Color TextSecondary = Color.FromArgb(72, 108, 172);

    public static GraphicsPath CreateRoundedRectPath(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return path;
        }

        float maxRadius = Math.Min(rect.Width, rect.Height) * 0.5f;
        float r = Math.Clamp(radius, 0f, maxRadius);
        if (r <= 0.5f)
        {
            path.AddRectangle(rect);
            return path;
        }

        float d = r * 2f;
        path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
        path.CloseFigure();
        return path;
    }
}

public enum TactileButtonStyle
{
    PrimaryBlue,
    SecondaryWhite,
    AccentOutline
}

/// <summary>
/// Taktiler 3D-Pill-Button im Stil des Product UI Styleguide (Default, Hover mit Leuchtring, Pressed, Disabled).
/// </summary>
public sealed class TactilePillButton : Button
{
    private bool _isHovered;
    private bool _isPressed;
    private TactileButtonStyle _buttonStyle = TactileButtonStyle.PrimaryBlue;
    private int _cornerRadius = -1; // -1 = volle Pillenform

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TactileButtonStyle ButtonStyle
    {
        get => _buttonStyle;
        set
        {
            _buttonStyle = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = value;
            Invalidate();
        }
    }

    public TactilePillButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _isHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _isHovered = false;
        _isPressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        if (mevent.Button == MouseButtons.Left)
        {
            _isPressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        if (_isPressed)
        {
            _isPressed = false;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        Graphics g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        Color parentBg = Parent?.BackColor ?? StyleGuidePalette.PureWhite;
        if (parentBg.A < 255)
        {
            parentBg = StyleGuidePalette.PureWhite;
        }
        g.Clear(parentBg);

        float w = ClientSize.Width;
        float h = ClientSize.Height;
        if (w <= 6 || h <= 6)
        {
            return;
        }

        float pad = 4f;
        float pressOffset = (_isPressed && Enabled) ? 1.5f : 0f;
        float bevelDepth = (_isPressed && Enabled) ? 1.5f : 3.5f;

        var fullBox = new RectangleF(pad, pad + pressOffset, w - pad * 2f, h - pad * 2f - 2f);
        var capBox = new RectangleF(fullBox.X, fullBox.Y, fullBox.Width, Math.Max(4f, fullBox.Height - bevelDepth));
        var bevelBox = new RectangleF(fullBox.X, fullBox.Y + bevelDepth, fullBox.Width, capBox.Height);

        float radius = _cornerRadius > 0 ? _cornerRadius : capBox.Height * 0.5f;

        // 1. Weicher Schlagschatten + Hover-Focus-Ring (#0098FE) wie im Styleguide
        if (Enabled && _isHovered)
        {
            for (int i = 3; i >= 1; i--)
            {
                var glowRect = RectangleF.Inflate(fullBox, i * 1.2f, i * 1.2f);
                using GraphicsPath glowPath = StyleGuidePalette.CreateRoundedRectPath(glowRect, radius + i);
                using var glowPen = new Pen(Color.FromArgb(42 * (4 - i), StyleGuidePalette.LightBlue), 2f);
                g.DrawPath(glowPen, glowPath);
            }
        }
        else if (Enabled)
        {
            var shadowRect = new RectangleF(bevelBox.X + 1f, bevelBox.Y + 2f, bevelBox.Width - 2f, bevelBox.Height);
            using GraphicsPath shadowPath = StyleGuidePalette.CreateRoundedRectPath(shadowRect, radius);
            using var shadowBrush = new SolidBrush(Color.FromArgb(26, StyleGuidePalette.DarkBlue));
            g.FillPath(shadowBrush, shadowPath);
        }

        // 2. 3D-Unterkante (Tactile Lip)
        Color bevelColor;
        if (!Enabled)
        {
            bevelColor = StyleGuidePalette.DisabledBevel;
        }
        else if (_buttonStyle == TactileButtonStyle.PrimaryBlue)
        {
            bevelColor = StyleGuidePalette.DarkBlue; // #0042E9
        }
        else
        {
            bevelColor = _isHovered ? StyleGuidePalette.MediumBlue : StyleGuidePalette.BevelLipLight;
        }

        using (GraphicsPath bevelPath = StyleGuidePalette.CreateRoundedRectPath(bevelBox, radius))
        using (var bevelBrush = new SolidBrush(bevelColor))
        {
            g.FillPath(bevelBrush, bevelPath);
        }

        // 3. Oberfläche (Cap)
        using (GraphicsPath capPath = StyleGuidePalette.CreateRoundedRectPath(capBox, radius))
        {
            if (!Enabled)
            {
                using var disabledBrush = new SolidBrush(StyleGuidePalette.DisabledSurface);
                g.FillPath(disabledBrush, capPath);
            }
            else if (_buttonStyle == TactileButtonStyle.PrimaryBlue)
            {
                Color topCol = _isHovered
                    ? StyleGuidePalette.LightBlue
                    : (_isPressed ? StyleGuidePalette.MediumBlue : StyleGuidePalette.LightBlue);
                Color botCol = _isHovered
                    ? StyleGuidePalette.MediumBlue
                    : (_isPressed ? StyleGuidePalette.DarkBlue : StyleGuidePalette.MediumBlue);

                using var grad = new LinearGradientBrush(capBox, topCol, botCol, LinearGradientMode.Vertical);
                g.FillPath(grad, capPath);

                // Glanzkante oben in Pur Weiß
                var innerTop = new RectangleF(capBox.X + 1f, capBox.Y + 1f, capBox.Width - 2f, capBox.Height - 2f);
                using GraphicsPath innerPath = StyleGuidePalette.CreateRoundedRectPath(innerTop, Math.Max(1f, radius - 1f));
                using var hiPen = new Pen(Color.FromArgb(_isHovered ? 170 : 110, StyleGuidePalette.PureWhite), 1.3f);
                g.DrawPath(hiPen, innerPath);
            }
            else
            {
                Color topCol = StyleGuidePalette.PureWhite;
                Color botCol = _isHovered
                    ? Color.FromArgb(236, 246, 255)
                    : Color.FromArgb(245, 249, 255);

                using var grad = new LinearGradientBrush(capBox, topCol, botCol, LinearGradientMode.Vertical);
                g.FillPath(grad, capPath);

                Color borderCol = _isHovered
                    ? StyleGuidePalette.LightBlue
                    : (_buttonStyle == TactileButtonStyle.AccentOutline ? StyleGuidePalette.MediumBlue : StyleGuidePalette.BorderSoft);
                float borderWidth = (_isHovered || _buttonStyle == TactileButtonStyle.AccentOutline) ? 1.6f : 1.1f;

                using var borderPen = new Pen(borderCol, borderWidth);
                g.DrawPath(borderPen, capPath);
            }
        }

        // 4. Beschriftung
        Color textColor;
        if (!Enabled)
        {
            textColor = StyleGuidePalette.DisabledText;
        }
        else if (_buttonStyle == TactileButtonStyle.PrimaryBlue)
        {
            textColor = StyleGuidePalette.PureWhite;
        }
        else
        {
            textColor = _isHovered ? StyleGuidePalette.MediumBlue : StyleGuidePalette.DarkBlue;
        }

        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };
        using var textBrush = new SolidBrush(textColor);
        g.DrawString(Text, Font, textBrush, capBox, sf);
    }
}

/// <summary>
/// Taktiler 3D-Schieberegler im Stil des Product UI Styleguide (Inputs / Tabs-Balken mit #0098FE -> #0074FC Füllung und 3D-Knopf).
/// </summary>
public sealed class TactileSlider : Control
{
    private int _minimum = 0;
    private int _maximum = 100;
    private int _value = 20;
    private int _smallChange = 1;
    private int _largeChange = 10;
    private bool _isHovered;
    private bool _isDragging;

    public event EventHandler? Scroll;
    public event EventHandler? ValueChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            if (_maximum < _minimum)
            {
                _maximum = _minimum;
            }
            Value = Math.Clamp(_value, _minimum, _maximum);
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(_minimum, value);
            Value = Math.Clamp(_value, _minimum, _maximum);
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Clamp(value, _minimum, _maximum);
            if (_value != clamped)
            {
                _value = clamped;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SmallChange
    {
        get => _smallChange;
        set => _smallChange = Math.Max(1, value);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LargeChange
    {
        get => _largeChange;
        set => _largeChange = Math.Max(1, value);
    }

    public TactileSlider()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);

        Height = 34;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _isHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _isHovered = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || e.Button != MouseButtons.Left)
        {
            return;
        }

        Focus();
        _isDragging = true;
        Capture = true;
        UpdateValueFromMouseX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_isDragging && Enabled)
        {
            UpdateValueFromMouseX(e.X);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && _isDragging)
        {
            _isDragging = false;
            Capture = false;
            Invalidate();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!Enabled)
        {
            return;
        }

        int deltaSteps = e.Delta > 0 ? _smallChange : -_smallChange;
        SetValueFromUserInteraction(_value + deltaSteps);
    }

    private void UpdateValueFromMouseX(int mouseX)
    {
        float padX = 14f;
        float trackW = Math.Max(1f, ClientSize.Width - padX * 2f);
        float ratio = Math.Clamp((mouseX - padX) / trackW, 0f, 1f);
        int range = Math.Max(1, _maximum - _minimum);
        int newValue = _minimum + (int)Math.Round(ratio * range);
        SetValueFromUserInteraction(newValue);
    }

    private void SetValueFromUserInteraction(int newValue)
    {
        int clamped = Math.Clamp(newValue, _minimum, _maximum);
        if (_value != clamped)
        {
            _value = clamped;
            Invalidate();
            Scroll?.Invoke(this, EventArgs.Empty);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color parentBg = Parent?.BackColor ?? StyleGuidePalette.PureWhite;
        g.Clear(parentBg);

        float w = ClientSize.Width;
        float h = ClientSize.Height;
        if (w <= 28 || h <= 10)
        {
            return;
        }

        float padX = 14f;
        float trackH = 12f;
        float trackY = (h - trackH) * 0.5f;
        float trackW = w - padX * 2f;

        // 1. Äußere taktile Pillen-Mulde (wie "Inputs" im Styleguide)
        var outerWell = new RectangleF(padX - 3f, trackY - 3f, trackW + 6f, trackH + 6f);
        using (GraphicsPath wellPath = StyleGuidePalette.CreateRoundedRectPath(outerWell, outerWell.Height * 0.5f))
        {
            using var wellBrush = new SolidBrush(StyleGuidePalette.SurfaceSoft);
            g.FillPath(wellBrush, wellPath);

            Color wellBorder = (_isHovered || _isDragging) && Enabled
                ? StyleGuidePalette.LightBlue
                : StyleGuidePalette.BorderSoft;
            using var wellPen = new Pen(wellBorder, (_isHovered || _isDragging) && Enabled ? 1.5f : 1f);
            g.DrawPath(wellPen, wellPath);
        }

        // 2. Innere Spur
        var trackRect = new RectangleF(padX, trackY, trackW, trackH);
        using (GraphicsPath trackPath = StyleGuidePalette.CreateRoundedRectPath(trackRect, trackH * 0.5f))
        using (var trackBrush = new SolidBrush(Color.FromArgb(226, 236, 252)))
        {
            g.FillPath(trackBrush, trackPath);
        }

        // 3. Aktiver Fortschrittsbalken (#0098FE -> #0074FC)
        int range = Math.Max(1, _maximum - _minimum);
        float ratio = Math.Clamp((_value - _minimum) / (float)range, 0f, 1f);
        float thumbX = padX + ratio * trackW;

        float fillW = Math.Max(trackH, thumbX - padX + trackH * 0.5f);
        var fillRect = new RectangleF(padX, trackY, Math.Min(trackW, fillW), trackH);
        using (GraphicsPath fillPath = StyleGuidePalette.CreateRoundedRectPath(fillRect, trackH * 0.5f))
        {
            if (Enabled)
            {
                using var fillGrad = new LinearGradientBrush(
                    fillRect,
                    StyleGuidePalette.LightBlue,
                    StyleGuidePalette.MediumBlue,
                    LinearGradientMode.Horizontal);
                g.FillPath(fillGrad, fillPath);
            }
            else
            {
                using var disFill = new SolidBrush(StyleGuidePalette.DisabledBevel);
                g.FillPath(disFill, fillPath);
            }
        }

        // 4. Taktiler 3D-Schiebeknopf (Thumb)
        float thumbR = 10.5f;
        var thumbCenter = new PointF(thumbX, h * 0.5f);

        if (Enabled && (_isHovered || _isDragging))
        {
            var glowRect = new RectangleF(thumbCenter.X - thumbR - 3.5f, thumbCenter.Y - thumbR - 3.5f, (thumbR + 3.5f) * 2f, (thumbR + 3.5f) * 2f);
            using var glowBrush = new SolidBrush(Color.FromArgb(65, StyleGuidePalette.LightBlue));
            g.FillEllipse(glowBrush, glowRect);
        }

        // 3D-Unterkante des Knopfs (#0042E9)
        var thumbBevelRect = new RectangleF(thumbCenter.X - thumbR, thumbCenter.Y - thumbR + 2f, thumbR * 2f, thumbR * 2f);
        using (var bevelBrush = new SolidBrush(Enabled ? StyleGuidePalette.DarkBlue : StyleGuidePalette.DisabledBevel))
        {
            g.FillEllipse(bevelBrush, thumbBevelRect);
        }

        // Weiße Oberfläche des Knopfs (#FFFFFF) mit blauem Kernpunkt (#0074FC)
        var thumbCapRect = new RectangleF(thumbCenter.X - thumbR, thumbCenter.Y - thumbR - 0.5f, thumbR * 2f, thumbR * 2f);
        using (var capBrush = new SolidBrush(StyleGuidePalette.PureWhite))
        using (var ringPen = new Pen(Enabled ? StyleGuidePalette.MediumBlue : StyleGuidePalette.DisabledBevel, 2.2f))
        {
            g.FillEllipse(capBrush, thumbCapRect);
            g.DrawEllipse(ringPen, thumbCapRect);
        }

        float dotR = 3.6f;
        var dotRect = new RectangleF(thumbCenter.X - dotR, thumbCenter.Y - dotR - 0.5f, dotR * 2f, dotR * 2f);
        using (var dotBrush = new SolidBrush(Enabled ? StyleGuidePalette.LightBlue : StyleGuidePalette.DisabledText))
        {
            g.FillEllipse(dotBrush, dotRect);
        }
    }
}

/// <summary>
/// Taktiler Auswahl-Chip im Stil der "Chips (✓ Selected)" aus dem Product UI Styleguide.
/// </summary>
public sealed class TactileCheckChip : CheckBox
{
    private bool _isHovered;

    public TactileCheckChip()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        AutoSize = false;
        Height = 36;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs eventargs)
    {
        base.OnMouseEnter(eventargs);
        _isHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs eventargs)
    {
        base.OnMouseLeave(eventargs);
        _isHovered = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        Graphics g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        Color parentBg = Parent?.BackColor ?? StyleGuidePalette.PureWhite;
        g.Clear(parentBg);

        float w = ClientSize.Width;
        float h = ClientSize.Height;
        if (w <= 10 || h <= 10)
        {
            return;
        }

        var box = new RectangleF(3f, 3f, w - 6f, h - 8f);
        var bevelBox = new RectangleF(3f, 5.5f, w - 6f, h - 8f);
        float radius = box.Height * 0.5f;

        // 3D-Unterkante
        Color bevelCol = Checked ? StyleGuidePalette.DarkBlue : StyleGuidePalette.BevelLipLight;
        using (GraphicsPath bevelPath = StyleGuidePalette.CreateRoundedRectPath(bevelBox, radius))
        using (var bevelBrush = new SolidBrush(bevelCol))
        {
            g.FillPath(bevelBrush, bevelPath);
        }

        // Chip-Körper
        using (GraphicsPath boxPath = StyleGuidePalette.CreateRoundedRectPath(box, radius))
        {
            Color bgTop = Checked ? Color.FromArgb(232, 244, 255) : StyleGuidePalette.PureWhite;
            Color bgBot = Checked ? Color.FromArgb(215, 236, 255) : StyleGuidePalette.SurfaceSoft;
            using var grad = new LinearGradientBrush(box, bgTop, bgBot, LinearGradientMode.Vertical);
            g.FillPath(grad, boxPath);

            Color borderCol = (_isHovered || Checked) ? StyleGuidePalette.LightBlue : StyleGuidePalette.BorderSoft;
            using var pen = new Pen(borderCol, (_isHovered || Checked) ? 1.6f : 1.1f);
            g.DrawPath(pen, boxPath);
        }

        // Häkchen-Kreis links
        float circleSize = 18f;
        var circleRect = new RectangleF(box.X + 9f, box.Y + (box.Height - circleSize) * 0.5f, circleSize, circleSize);
        if (Checked)
        {
            using var circleBrush = new LinearGradientBrush(
                circleRect,
                StyleGuidePalette.LightBlue,
                StyleGuidePalette.MediumBlue,
                LinearGradientMode.Vertical);
            g.FillEllipse(circleBrush, circleRect);

            using var checkPen = new Pen(StyleGuidePalette.PureWhite, 2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            g.DrawLines(checkPen, new[]
            {
                new PointF(circleRect.X + 4.5f, circleRect.Y + 9.5f),
                new PointF(circleRect.X + 8.0f, circleRect.Y + 13.0f),
                new PointF(circleRect.X + 13.8f, circleRect.Y + 5.8f)
            });
        }
        else
        {
            using var emptyBrush = new SolidBrush(StyleGuidePalette.PureWhite);
            using var emptyPen = new Pen(StyleGuidePalette.BorderSoft, 1.5f);
            g.FillEllipse(emptyBrush, circleRect);
            g.DrawEllipse(emptyPen, circleRect);
        }

        var textRect = new RectangleF(circleRect.Right + 8f, box.Y, box.Right - circleRect.Right - 14f, box.Height);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        using var textBrush = new SolidBrush(Checked ? StyleGuidePalette.DarkBlue : StyleGuidePalette.TextPrimary);
        g.DrawString(Text, Font, textBrush, textRect, sf);
    }
}

/// <summary>
/// Erhabenes Card-Panel im Stil der "Cards / Elevation Level 1-2" aus dem Product UI Styleguide.
/// </summary>
public sealed class TactileCardPanel : Panel
{
    private int _cornerRadius = 18;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = Math.Max(4, value);
            Invalidate();
        }
    }

    public TactileCardPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = StyleGuidePalette.PureWhite;
        Padding = new Padding(14);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color parentBg = Parent?.BackColor ?? StyleGuidePalette.AppBackground;
        g.Clear(parentBg);

        float w = ClientSize.Width;
        float h = ClientSize.Height;
        if (w <= 10 || h <= 10)
        {
            return;
        }

        var bevelRect = new RectangleF(2f, 5f, w - 4f, h - 7f);
        var cardRect = new RectangleF(2f, 2f, w - 4f, h - 7f);

        using (GraphicsPath bevelPath = StyleGuidePalette.CreateRoundedRectPath(bevelRect, _cornerRadius))
        using (var bevelBrush = new SolidBrush(StyleGuidePalette.BevelLipLight))
        {
            g.FillPath(bevelBrush, bevelPath);
        }

        using (GraphicsPath cardPath = StyleGuidePalette.CreateRoundedRectPath(cardRect, _cornerRadius))
        using (var cardBrush = new SolidBrush(StyleGuidePalette.PureWhite))
        using (var borderPen = new Pen(StyleGuidePalette.BorderSoft, 1.2f))
        {
            g.FillPath(cardBrush, cardPath);
            g.DrawPath(borderPen, cardPath);
        }
    }
}
