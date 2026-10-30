using System.Drawing;
using DesktopSticker.Configuration;
using DesktopSticker.Localization;

namespace DesktopSticker.UI;

/// <summary>
/// Kleines Fenster zur Auswahl der Oberflächensprache beim allerersten Start (bzw. vor der Installation).
/// Die Auswahl wird in den AppSettings (UiLanguage) gespeichert.
/// </summary>
public sealed class LanguageSelectionForm : Form
{
    private readonly ListBox _lstLanguages;
    private readonly Label _lblTitle;
    private readonly Label _lblHint;
    private readonly TactilePillButton _btnOk;

    public string SelectedLanguageCode { get; private set; } = Localizer.DefaultLanguageCode;

    public LanguageSelectionForm(string preselectedLanguageCode)
    {
        // Bewusst mehrsprachiger Fenstertitel, da die Sprache noch nicht feststeht
        Text = "Desktop Sticker – Language · Sprache · Idioma · Langue · 语言 · Jezik";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(420, 430);
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = StyleGuidePalette.AppBackground;
        ForeColor = StyleGuidePalette.TextPrimary;

        var outerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(16),
            BackColor = StyleGuidePalette.AppBackground
        };
        outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var card = new TactileCardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20)
        };

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = StyleGuidePalette.PureWhite
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _lblTitle = new Label
        {
            Font = new Font(Font.FontFamily, 13.0f, FontStyle.Bold),
            ForeColor = StyleGuidePalette.DarkBlue,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        };

        _lblHint = new Label
        {
            Font = new Font(Font.FontFamily, 9.0f, FontStyle.Regular),
            ForeColor = StyleGuidePalette.TextSecondary,
            AutoSize = true,
            MaximumSize = new Size(340, 0),
            Margin = new Padding(0, 0, 0, 12)
        };

        _lstLanguages = new ListBox
        {
            Dock = DockStyle.Fill,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 36,
            BorderStyle = BorderStyle.None,
            BackColor = StyleGuidePalette.SurfaceSoft,
            ForeColor = StyleGuidePalette.TextPrimary,
            Font = new Font(Font.FontFamily, 10.0f, FontStyle.Bold),
            IntegralHeight = false,
            Margin = new Padding(0, 0, 0, 14)
        };
        foreach (UiLanguageInfo lang in Localizer.SupportedLanguages)
        {
            _lstLanguages.Items.Add(lang);
        }
        _lstLanguages.DrawItem += OnDrawLanguageItem;
        _lstLanguages.SelectedIndexChanged += (_, _) => UpdateTexts();
        _lstLanguages.DoubleClick += (_, _) => ConfirmSelection();

        _btnOk = new TactilePillButton
        {
            ButtonStyle = TactileButtonStyle.PrimaryBlue,
            Dock = DockStyle.Fill,
            Height = 44,
            Font = new Font(Font.FontFamily, 10.2f, FontStyle.Bold),
            Margin = new Padding(0)
        };
        _btnOk.Click += (_, _) => ConfirmSelection();
        AcceptButton = _btnOk;

        stack.Controls.Add(_lblTitle, 0, 0);
        stack.Controls.Add(_lblHint, 0, 1);
        stack.Controls.Add(_lstLanguages, 0, 2);
        stack.Controls.Add(_btnOk, 0, 3);

        card.Controls.Add(stack);
        outerLayout.Controls.Add(card, 0, 0);
        Controls.Add(outerLayout);

        string normalized = Localizer.NormalizeLanguageCode(preselectedLanguageCode);
        int preIndex = Localizer.SupportedLanguages
            .Select((l, i) => (l, i))
            .FirstOrDefault(t => t.l.Code == normalized).i;
        _lstLanguages.SelectedIndex = Math.Max(0, preIndex);
        UpdateTexts();
    }

    /// <summary>
    /// Zeigt das Auswahlfenster nur an, wenn in den AppSettings noch keine Sprache gespeichert ist,
    /// speichert die Auswahl und aktiviert sie anschließend für die laufende Sitzung.
    /// </summary>
    public static void EnsureLanguageSelected(SettingsManager settingsManager)
    {
        SettingsLoadResult loadResult;
        try
        {
            loadResult = settingsManager.Load();
        }
        catch
        {
            Localizer.SetLanguage(Localizer.DefaultLanguageCode);
            return;
        }

        // Beschädigte Konfiguration hier nicht überschreiben – das Hauptfenster bietet dafür eine eigene Wiederherstellung an
        if (loadResult.Status == SettingsLoadStatus.CorruptedOrIncomplete)
        {
            Localizer.SetLanguage(Localizer.DefaultLanguageCode);
            return;
        }

        AppSettings settings = loadResult.Settings;
        if (Localizer.IsSupportedLanguage(settings.UiLanguage))
        {
            Localizer.SetLanguage(settings.UiLanguage);
            return;
        }

        string chosen = Localizer.DefaultLanguageCode;
        using (var form = new LanguageSelectionForm(Localizer.DetectSystemLanguageCode()))
        {
            if (form.ShowDialog() == DialogResult.OK)
            {
                chosen = form.SelectedLanguageCode;
            }
        }

        settings.UiLanguage = chosen;
        try
        {
            settingsManager.Save(settings);
        }
        catch
        {
            // Speichern fehlgeschlagen: Sprache gilt dann nur für die aktuelle Sitzung
        }

        Localizer.SetLanguage(chosen);
    }

    private string CurrentPreviewCode =>
        _lstLanguages.SelectedItem is UiLanguageInfo info ? info.Code : Localizer.DefaultLanguageCode;

    private void UpdateTexts()
    {
        string code = CurrentPreviewCode;
        _lblTitle.Text = Localizer.TFor(code, "LanguageSelection.Title");
        _lblHint.Text = Localizer.TFor(code, "LanguageSelection.Hint");
        _btnOk.Text = Localizer.TFor(code, "LanguageSelection.Confirm");
    }

    private void ConfirmSelection()
    {
        SelectedLanguageCode = CurrentPreviewCode;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnDrawLanguageItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _lstLanguages.Items.Count)
        {
            return;
        }

        var lang = (UiLanguageInfo)_lstLanguages.Items[e.Index];
        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        Color back = selected ? StyleGuidePalette.MediumBlue : StyleGuidePalette.SurfaceSoft;
        Color fore = selected ? StyleGuidePalette.PureWhite : StyleGuidePalette.TextPrimary;
        Color sub = selected ? StyleGuidePalette.PureWhite : StyleGuidePalette.TextSecondary;

        using (var backBrush = new SolidBrush(back))
        {
            e.Graphics.FillRectangle(backBrush, e.Bounds);
        }

        var nameRect = new Rectangle(e.Bounds.X + 14, e.Bounds.Y, e.Bounds.Width - 28, e.Bounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            lang.NativeName,
            _lstLanguages.Font,
            nameRect,
            fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        using var subFont = new Font(Font.FontFamily, 8.5f, FontStyle.Regular);
        TextRenderer.DrawText(
            e.Graphics,
            lang.EnglishName,
            subFont,
            nameRect,
            sub,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }
}
