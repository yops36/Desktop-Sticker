using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using DesktopSticker.Configuration;
using DesktopSticker.Imaging;
using DesktopSticker.Localization;

namespace DesktopSticker.UI;

public sealed class StickerTabEntry
{
    public required StickerItemConfig Config { get; init; }
    public Bitmap? Bitmap { get; init; }
    public LoadedStickerImage? LoadedImage { get; init; }
}

/// <summary>
/// Senkrechte Bilder-Leiste (Vertical Tab) auf der linken Fensterseite:
/// - Ganz oben befindet sich der prominente „+ Neues Bild“-Button.
/// - Darunter stapeln sich alle hinzugefügten Bilder untereinander als taktile Karten.
/// - Jedes Bild kann per Klick ausgewählt (zum Bearbeiten von Position/Größe/Radius) oder direkt gelöscht werden.
/// </summary>
public sealed class StickerVerticalTabControl : Control
{
    private const int ItemHeight = 68;
    private const int ItemSpacing = 10;
    private const int ListPadding = 8;

    private readonly List<StickerTabEntry> _entries = new();
    private string _selectedStickerId = string.Empty;
    private int _hoveredIndex = -1;
    private int _hoveredDeleteIndex = -1;
    private int _scrollOffset;

    public event EventHandler<string>? StickerSelected;
    public event EventHandler<string>? StickerDeleteRequested;
    public event EventHandler? AddStickerRequested;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string SelectedStickerId => _selectedStickerId;

    public StickerVerticalTabControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);

        BackColor = StyleGuidePalette.PureWhite;
        Cursor = Cursors.Default;
    }

    public void SetEntries(IEnumerable<StickerTabEntry> entries, string? selectedStickerId)
    {
        _entries.Clear();
        _entries.AddRange(entries);
        _selectedStickerId = selectedStickerId ?? (_entries.FirstOrDefault()?.Config.Id ?? string.Empty);
        ClampScrollOffset();
        Invalidate();
    }

    private int GetTotalContentHeight()
    {
        if (_entries.Count == 0)
        {
            return 0;
        }

        return ListPadding * 2 + _entries.Count * ItemHeight + (_entries.Count - 1) * ItemSpacing;
    }

    private void ClampScrollOffset()
    {
        int maxScroll = Math.Max(0, GetTotalContentHeight() - ClientSize.Height);
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScroll);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ClampScrollOffset();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_entries.Count == 0)
        {
            return;
        }

        int delta = e.Delta > 0 ? -36 : 36;
        _scrollOffset += delta;
        ClampScrollOffset();
        UpdateHoverState(PointToClient(Cursor.Position));
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateHoverState(e.Location);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoveredIndex != -1 || _hoveredDeleteIndex != -1)
        {
            _hoveredIndex = -1;
            _hoveredDeleteIndex = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_entries.Count == 0)
        {
            AddStickerRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        for (int i = 0; i < _entries.Count; i++)
        {
            RectangleF itemRect = GetItemRect(i);
            if (!itemRect.Contains(e.Location))
            {
                continue;
            }

            RectangleF deleteRect = GetDeleteButtonRect(itemRect);
            string id = _entries[i].Config.Id;

            if (deleteRect.Contains(e.Location))
            {
                StickerDeleteRequested?.Invoke(this, id);
                return;
            }

            if (_selectedStickerId != id)
            {
                _selectedStickerId = id;
                Invalidate();
            }

            StickerSelected?.Invoke(this, id);
            return;
        }
    }

    private void UpdateHoverState(Point location)
    {
        int newHover = -1;
        int newDeleteHover = -1;

        for (int i = 0; i < _entries.Count; i++)
        {
            RectangleF itemRect = GetItemRect(i);
            if (itemRect.Contains(location))
            {
                newHover = i;
                if (GetDeleteButtonRect(itemRect).Contains(location))
                {
                    newDeleteHover = i;
                }
                break;
            }
        }

        if (newHover != _hoveredIndex || newDeleteHover != _hoveredDeleteIndex)
        {
            _hoveredIndex = newHover;
            _hoveredDeleteIndex = newDeleteHover;
            Cursor = newHover >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    private RectangleF GetItemRect(int index)
    {
        float y = ListPadding + index * (ItemHeight + ItemSpacing) - _scrollOffset;
        float w = Math.Max(40f, ClientSize.Width - ListPadding * 2f);
        return new RectangleF(ListPadding, y, w, ItemHeight);
    }

    private static RectangleF GetDeleteButtonRect(RectangleF itemRect)
    {
        float btnSize = 26f;
        return new RectangleF(
            itemRect.Right - btnSize - 10f,
            itemRect.Y + (itemRect.Height - btnSize) * 0.5f,
            btnSize,
            btnSize);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);

        if (_entries.Count == 0)
        {
            var emptyBox = new RectangleF(10f, 16f, Math.Max(20f, ClientSize.Width - 20f), Math.Max(60f, ClientSize.Height - 32f));
            using GraphicsPath emptyPath = StyleGuidePalette.CreateRoundedRectPath(emptyBox, 14f);
            using var emptyFill = new SolidBrush(StyleGuidePalette.SurfaceSoft);
            using var emptyPen = new Pen(StyleGuidePalette.BorderSoft, 1.2f) { DashStyle = DashStyle.Dash };
            g.FillPath(emptyFill, emptyPath);
            g.DrawPath(emptyPen, emptyPath);

            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            using var txtBrush = new SolidBrush(StyleGuidePalette.TextSecondary);
            g.DrawString(
                Localizer.T("Tab.Empty"),
                Font,
                txtBrush,
                emptyBox,
                sf);
            return;
        }

        using var boldFont = new Font(Font.FontFamily, 9.5f, FontStyle.Bold);
        using var subFont = new Font(Font.FontFamily, 8.0f, FontStyle.Regular);

        for (int i = 0; i < _entries.Count; i++)
        {
            StickerTabEntry entry = _entries[i];
            RectangleF itemRect = GetItemRect(i);
            if (itemRect.Bottom < 0 || itemRect.Top > ClientSize.Height)
            {
                continue;
            }

            bool isSelected = string.Equals(entry.Config.Id, _selectedStickerId, StringComparison.OrdinalIgnoreCase);
            bool isHovered = i == _hoveredIndex;
            bool isDeleteHovered = i == _hoveredDeleteIndex;

            var cardCap = new RectangleF(itemRect.X, itemRect.Y, itemRect.Width, itemRect.Height - 3f);
            var cardBevel = new RectangleF(itemRect.X, itemRect.Y + 3f, itemRect.Width, itemRect.Height - 3f);
            float radius = 14f;

            // 1. Leuchtring bei Auswahl oder Hover
            if (isSelected || isHovered)
            {
                var glowRect = RectangleF.Inflate(cardCap, 1.5f, 1.5f);
                using GraphicsPath glowPath = StyleGuidePalette.CreateRoundedRectPath(glowRect, radius + 1.5f);
                using var glowPen = new Pen(
                    Color.FromArgb(isSelected ? 110 : 60, StyleGuidePalette.LightBlue),
                    isSelected ? 2.4f : 1.6f);
                g.DrawPath(glowPen, glowPath);
            }

            // 2. 3D-Unterkante (Bevel)
            using (GraphicsPath bevelPath = StyleGuidePalette.CreateRoundedRectPath(cardBevel, radius))
            using (var bevelBrush = new SolidBrush(isSelected ? StyleGuidePalette.DarkBlue : StyleGuidePalette.BevelLipLight))
            {
                g.FillPath(bevelBrush, bevelPath);
            }

            // 3. Karten-Oberfläche
            using (GraphicsPath capPath = StyleGuidePalette.CreateRoundedRectPath(cardCap, radius))
            {
                Color topCol = isSelected
                    ? Color.FromArgb(234, 245, 255)
                    : StyleGuidePalette.PureWhite;
                Color botCol = isSelected
                    ? Color.FromArgb(216, 236, 255)
                    : (isHovered ? Color.FromArgb(244, 249, 255) : StyleGuidePalette.SurfaceSoft);

                using var fillGrad = new LinearGradientBrush(cardCap, topCol, botCol, LinearGradientMode.Vertical);
                g.FillPath(fillGrad, capPath);

                Color borderCol = isSelected
                    ? StyleGuidePalette.MediumBlue
                    : (isHovered ? StyleGuidePalette.LightBlue : StyleGuidePalette.BorderSoft);
                using var borderPen = new Pen(borderCol, isSelected ? 1.8f : 1.1f);
                g.DrawPath(borderPen, capPath);
            }

            // 4. Senkrechter aktiver Tab-Balken links (#0098FE -> #0042E9)
            if (isSelected)
            {
                var pillBar = new RectangleF(cardCap.X + 4f, cardCap.Y + 12f, 4.5f, cardCap.Height - 24f);
                using GraphicsPath barPath = StyleGuidePalette.CreateRoundedRectPath(pillBar, 2.2f);
                using var barGrad = new LinearGradientBrush(
                    pillBar,
                    StyleGuidePalette.LightBlue,
                    StyleGuidePalette.DarkBlue,
                    LinearGradientMode.Vertical);
                g.FillPath(barGrad, barPath);
            }

            // 5. Vorschaubild-Kachel (wie im Styleguide unter "Cards")
            var thumbBox = new RectangleF(cardCap.X + 13f, cardCap.Y + 9f, 46f, 46f);
            using (GraphicsPath thumbPath = StyleGuidePalette.CreateRoundedRectPath(thumbBox, 10f))
            {
                using var thumbBg = new SolidBrush(StyleGuidePalette.PureWhite);
                g.FillPath(thumbBg, thumbPath);

                if (entry.Bitmap is not null)
                {
                    Region oldClip = g.Clip;
                    g.SetClip(thumbPath, CombineMode.Intersect);

                    float scale = Math.Min((thumbBox.Width - 6f) / entry.Bitmap.Width, (thumbBox.Height - 6f) / entry.Bitmap.Height);
                    float drawW = Math.Max(1f, entry.Bitmap.Width * scale);
                    float drawH = Math.Max(1f, entry.Bitmap.Height * scale);
                    float drawX = thumbBox.X + (thumbBox.Width - drawW) * 0.5f;
                    float drawY = thumbBox.Y + (thumbBox.Height - drawH) * 0.5f;

                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(entry.Bitmap, drawX, drawY, drawW, drawH);
                    g.Clip = oldClip;
                }

                using var thumbBorder = new Pen(isSelected ? StyleGuidePalette.LightBlue : StyleGuidePalette.BorderSoft, 1f);
                g.DrawPath(thumbBorder, thumbPath);
            }

            // 6. Titel und Positions-Kurzinfo
            RectangleF deleteRect = GetDeleteButtonRect(itemRect);
            float textLeft = thumbBox.Right + 10f;
            float textWidth = Math.Max(20f, deleteRect.Left - textLeft - 6f);

            string title = string.IsNullOrWhiteSpace(entry.Config.DisplayName)
                ? Localizer.T("Sticker.DefaultName", i + 1)
                : entry.Config.DisplayName;
            string gifPrefix = entry.LoadedImage?.IsGif == true ? "GIF · " : string.Empty;
            string subtitle = $"{gifPrefix}{entry.Config.RelativeWidth * 100.0:0}% · X:{entry.Config.RelativeX * 100.0:0}% Y:{entry.Config.RelativeY * 100.0:0}%";

            var titleRect = new RectangleF(textLeft, cardCap.Y + 11f, textWidth, 20f);
            var subRect = new RectangleF(textLeft, cardCap.Y + 32f, textWidth, 18f);

            using var sfLine = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            using (var titleBrush = new SolidBrush(isSelected ? StyleGuidePalette.DarkBlue : StyleGuidePalette.TextPrimary))
            using (var subBrush = new SolidBrush(StyleGuidePalette.TextSecondary))
            {
                g.DrawString(title, boldFont, titleBrush, titleRect, sfLine);
                g.DrawString(subtitle, subFont, subBrush, subRect, sfLine);
            }

            // 7. Löschen-Button (×) rechts auf jeder Karte
            Color delFill = isDeleteHovered
                ? StyleGuidePalette.MediumBlue
                : StyleGuidePalette.PureWhite;
            Color delBorder = isDeleteHovered
                ? StyleGuidePalette.DarkBlue
                : StyleGuidePalette.BorderSoft;
            Color delCross = isDeleteHovered
                ? StyleGuidePalette.PureWhite
                : StyleGuidePalette.TextSecondary;

            using (var delBrush = new SolidBrush(delFill))
            using (var delPen = new Pen(delBorder, 1.2f))
            {
                g.FillEllipse(delBrush, deleteRect);
                g.DrawEllipse(delPen, deleteRect);
            }

            using (var crossPen = new Pen(delCross, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float cx = deleteRect.X + deleteRect.Width * 0.5f;
                float cy = deleteRect.Y + deleteRect.Height * 0.5f;
                float d = 4.2f;
                g.DrawLine(crossPen, cx - d, cy - d, cx + d, cy + d);
                g.DrawLine(crossPen, cx + d, cy - d, cx - d, cy + d);
            }
        }
    }
}
