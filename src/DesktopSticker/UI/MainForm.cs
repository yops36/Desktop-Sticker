using System.Drawing;
using DesktopSticker.Backup;
using DesktopSticker.Configuration;
using DesktopSticker.Imaging;
using DesktopSticker.Integration;
using DesktopSticker.Localization;
using DesktopSticker.Overlay;
using DesktopSticker.Wallpaper;

namespace DesktopSticker.UI;

/// <summary>
/// Hauptfenster von Desktop Sticker im Stil des Product UI Styleguide (#0098FE, #0074FC, #0042E9, #FFFFFF).
/// - Links: Senkrechtes Bilder-Tab mit „+ Neues Bild“-Button ganz oben und gestapelten Bildern darunter (Auswählen & Löschen).
/// - Mitte: Interaktive Live-Vorschau aller gestapelten Bilder (direktes Anklicken und Verschieben).
/// - Rechts: Taktile Schieberegler und getrennte Buttons „Anwenden“, „Sticker deaktivieren“ und „Schließen“.
/// </summary>
public sealed class MainForm : Form
{
    private readonly AppStoragePaths _paths;
    private readonly IWallpaperService _wallpaperService;
    private readonly SettingsManager _settingsManager;
    private readonly BackupManager _backupManager;
    private readonly WallpaperActivationManager _activationManager;
    private readonly ContextMenuRegistrar _contextMenuRegistrar;
    private readonly AutostartRegistrar _autostartRegistrar;

    private WallpaperState _sessionStartOsState = new();
    private AppSettings _persistedSettings = new();
    private AppSettings _workingSettings = new();

    private Bitmap? _originalWallpaperBitmap;
    private readonly Dictionary<string, LoadedStickerImage> _loadedStickersById = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _newlyImportedStickerPathsInSession = new(StringComparer.OrdinalIgnoreCase);

    private bool _suppressUiEvents;

    // UI-Steuerelemente: Linke Spalte (Senkrechtes Bilder-Tab)
    private readonly TactilePillButton _btnAddNewSticker;
    private readonly StickerVerticalTabControl _verticalStickerTab;
    private readonly Label _lblLayerCountBadge;

    // UI-Steuerelemente: Mittlere Spalte (Vorschau)
    private readonly DesktopPreviewControl _previewControl;
    private readonly Label _lblMonitorInfo;
    private readonly Label _lblStickerInfo;

    // UI-Steuerelemente: Rechte Spalte (Bearbeitung & Aktionen)
    private readonly Label _lblSelectedStickerHeader;
    private readonly TactilePillButton _btnReplaceImage;
    private readonly TactilePillButton _btnDeleteSelected;
    private readonly TactileSlider _trackWidth;
    private readonly NumericUpDown _numWidthPercent;
    private readonly TactileSlider _trackPosX;
    private readonly NumericUpDown _numPosXPercent;
    private readonly TactileSlider _trackPosY;
    private readonly NumericUpDown _numPosYPercent;
    private readonly TactileSlider _trackCornerRadius;
    private readonly NumericUpDown _numCornerRadiusPercent;
    private readonly TactilePillButton _btnReset;
    private readonly TactileCheckChip _chkContextMenu;
    private readonly TactilePillButton _btnApply;
    private readonly TactilePillButton _btnDeactivate;
    private readonly TactilePillButton _btnClose;

    public MainForm(
        AppStoragePaths paths,
        IWallpaperService wallpaperService,
        SettingsManager settingsManager,
        BackupManager backupManager,
        WallpaperActivationManager activationManager,
        ContextMenuRegistrar contextMenuRegistrar,
        AutostartRegistrar? autostartRegistrar = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _wallpaperService = wallpaperService ?? throw new ArgumentNullException(nameof(wallpaperService));
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _backupManager = backupManager ?? throw new ArgumentNullException(nameof(backupManager));
        _activationManager = activationManager ?? throw new ArgumentNullException(nameof(activationManager));
        _contextMenuRegistrar = contextMenuRegistrar ?? throw new ArgumentNullException(nameof(contextMenuRegistrar));
        _autostartRegistrar = autostartRegistrar ?? new AutostartRegistrar();

        Text = "Desktop Sticker";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1080, 650);
        ClientSize = new Size(1220, 710);
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = StyleGuidePalette.AppBackground;
        ForeColor = StyleGuidePalette.TextPrimary;
        MaximizeBox = true;
        MinimizeBox = true;

        // Hauptlayout: 3 Spalten
        // [0] Links: Senkrechtes Bilder-Tab (245 px)
        // [1] Mitte: Desktop-Vorschau (100 %)
        // [2] Rechts: Einstellungen & Aktionen (345 px)
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(14),
            BackColor = StyleGuidePalette.AppBackground
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 248f));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 348f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // =====================================================================
        // SPALTE 1 (LINKS): SENKRECHTES BILDER-TAB (Stapel + „+ Neues Bild“ oben)
        // =====================================================================
        var leftCard = new TactileCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(14)
        };

        var leftLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = StyleGuidePalette.PureWhite
        };
        leftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // Ganz oben: „+ Neues Bild“-Button
        _btnAddNewSticker = new TactilePillButton
        {
            Text = Localizer.T("Main.AddNewImage"),
            ButtonStyle = TactileButtonStyle.PrimaryBlue,
            Dock = DockStyle.Fill,
            Height = 44,
            Font = new Font(Font.FontFamily, 10.0f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 10)
        };
        _btnAddNewSticker.Click += OnAddNewStickerClick;

        _lblLayerCountBadge = new Label
        {
            Text = Localizer.T("Main.LayerCountBadge", 0),
            Font = new Font(Font.FontFamily, 8.2f, FontStyle.Bold),
            ForeColor = StyleGuidePalette.TextSecondary,
            AutoSize = true,
            Margin = new Padding(4, 2, 0, 6)
        };

        _verticalStickerTab = new StickerVerticalTabControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };
        _verticalStickerTab.StickerSelected += OnVerticalTabStickerSelected;
        _verticalStickerTab.StickerDeleteRequested += OnVerticalTabStickerDeleteRequested;
        _verticalStickerTab.AddStickerRequested += OnAddNewStickerClick;

        leftLayout.Controls.Add(_btnAddNewSticker, 0, 0);
        leftLayout.Controls.Add(_lblLayerCountBadge, 0, 1);
        leftLayout.Controls.Add(_verticalStickerTab, 0, 2);
        leftCard.Controls.Add(leftLayout);

        // =====================================================================
        // SPALTE 2 (MITTE): INTERAKTIVE DESKTOP-VORSCHAU
        // =====================================================================
        var centerCard = new TactileCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(16)
        };

        var previewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = StyleGuidePalette.PureWhite
        };
        previewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var lblPreviewHeader = new Label
        {
            Text = Localizer.T("Main.PreviewHeader"),
            Font = new Font(Font.FontFamily, 10.0f, FontStyle.Bold),
            ForeColor = StyleGuidePalette.DarkBlue,
            AutoSize = true,
            Margin = new Padding(2, 0, 0, 8)
        };

        _previewControl = new DesktopPreviewControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };
        _previewControl.StickerPositionChanged += OnPreviewStickerPositionChanged;
        _previewControl.StickerSelectedInPreview += OnPreviewStickerSelected;

        var statusPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = StyleGuidePalette.PureWhite,
            Margin = new Padding(2, 10, 0, 0)
        };

        _lblMonitorInfo = new Label
        {
            AutoSize = true,
            ForeColor = StyleGuidePalette.TextSecondary,
            Font = new Font(Font.FontFamily, 8.8f, FontStyle.Regular),
            Text = Localizer.T("Main.LoadingBackground")
        };

        _lblStickerInfo = new Label
        {
            AutoSize = true,
            ForeColor = StyleGuidePalette.DarkBlue,
            Font = new Font(Font.FontFamily, 9.0f, FontStyle.Bold),
            Margin = new Padding(0, 3, 0, 0),
            Text = Localizer.T("Main.NoImageSelectedShort")
        };

        statusPanel.Controls.Add(_lblMonitorInfo);
        statusPanel.Controls.Add(_lblStickerInfo);

        previewLayout.Controls.Add(lblPreviewHeader, 0, 0);
        previewLayout.Controls.Add(_previewControl, 0, 1);
        previewLayout.Controls.Add(statusPanel, 0, 2);
        centerCard.Controls.Add(previewLayout);

        // =====================================================================
        // SPALTE 3 (RECHTS): EINSTELLUNGEN & GETRENNTE AKTIONSBUTTONS
        // =====================================================================
        var rightCard = new TactileCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(16)
        };

        var controlsStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 17,
            BackColor = StyleGuidePalette.PureWhite
        };
        controlsStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64f));
        controlsStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36f));

        // 0. Überschrift des ausgewählten Bildes
        _lblSelectedStickerHeader = new Label
        {
            Text = Localizer.T("Main.EditSelectedImage"),
            Font = new Font(Font.FontFamily, 10.0f, FontStyle.Bold),
            ForeColor = StyleGuidePalette.DarkBlue,
            AutoSize = true,
            Margin = new Padding(2, 0, 0, 8)
        };
        controlsStack.Controls.Add(_lblSelectedStickerHeader, 0, 0);
        controlsStack.SetColumnSpan(_lblSelectedStickerHeader, 2);

        // 1. Aktionszeile für das ausgewählte Bild: „Bild ersetzen…“ & „Bild löschen“
        var itemActionRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Height = 38,
            Margin = new Padding(0, 0, 0, 10),
            BackColor = StyleGuidePalette.PureWhite
        };
        itemActionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56f));
        itemActionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44f));

        _btnReplaceImage = new TactilePillButton
        {
            Text = Localizer.T("Main.ReplaceImage"),
            ButtonStyle = TactileButtonStyle.SecondaryWhite,
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 8.8f, FontStyle.Bold),
            Margin = new Padding(0, 0, 4, 0)
        };
        _btnReplaceImage.Click += OnReplaceSelectedImageClick;

        _btnDeleteSelected = new TactilePillButton
        {
            Text = Localizer.T("Main.DeleteImage"),
            ButtonStyle = TactileButtonStyle.AccentOutline,
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 8.8f, FontStyle.Bold),
            Margin = new Padding(4, 0, 0, 0)
        };
        _btnDeleteSelected.Click += OnDeleteSelectedStickerClick;

        itemActionRow.Controls.Add(_btnReplaceImage, 0, 0);
        itemActionRow.Controls.Add(_btnDeleteSelected, 1, 0);
        controlsStack.Controls.Add(itemActionRow, 0, 1);
        controlsStack.SetColumnSpan(itemActionRow, 2);

        // 2. Bildgröße (% der Bildschirmbreite)
        var lblSize = CreateSectionLabel(Localizer.T("Main.Label.Size"));
        _numWidthPercent = CreateStyledNumeric(2.0m, 100.0m, 1.0m, 20.0m, decimals: 1);
        _numWidthPercent.ValueChanged += OnWidthNumericChanged;

        _trackWidth = new TactileSlider
        {
            Minimum = 20,   // 2.0 %
            Maximum = 1000, // 100.0 %
            SmallChange = 5,
            LargeChange = 25,
            Value = 200,
            Dock = DockStyle.Fill,
            Height = 32,
            Margin = new Padding(0, 0, 0, 8)
        };
        _trackWidth.Scroll += OnWidthTrackScroll;

        controlsStack.Controls.Add(lblSize, 0, 2);
        controlsStack.Controls.Add(_numWidthPercent, 1, 2);
        controlsStack.Controls.Add(_trackWidth, 0, 3);
        controlsStack.SetColumnSpan(_trackWidth, 2);

        // 3. Position X (% links) mit taktilem Slider
        var lblPosX = CreateSectionLabel(Localizer.T("Main.Label.PosX"));
        _numPosXPercent = CreateStyledNumeric(0.0m, 100.0m, 0.5m, 40.0m, decimals: 1);
        _numPosXPercent.ValueChanged += OnPositionNumericChanged;

        _trackPosX = new TactileSlider
        {
            Minimum = 0,
            Maximum = 1000,
            SmallChange = 5,
            LargeChange = 25,
            Value = 400,
            Dock = DockStyle.Fill,
            Height = 32,
            Margin = new Padding(0, 0, 0, 8)
        };
        _trackPosX.Scroll += OnPosXTrackScroll;

        controlsStack.Controls.Add(lblPosX, 0, 4);
        controlsStack.Controls.Add(_numPosXPercent, 1, 4);
        controlsStack.Controls.Add(_trackPosX, 0, 5);
        controlsStack.SetColumnSpan(_trackPosX, 2);

        // 4. Position Y (% oben) mit taktilem Slider
        var lblPosY = CreateSectionLabel(Localizer.T("Main.Label.PosY"));
        _numPosYPercent = CreateStyledNumeric(0.0m, 100.0m, 0.5m, 35.0m, decimals: 1);
        _numPosYPercent.ValueChanged += OnPositionNumericChanged;

        _trackPosY = new TactileSlider
        {
            Minimum = 0,
            Maximum = 1000,
            SmallChange = 5,
            LargeChange = 25,
            Value = 350,
            Dock = DockStyle.Fill,
            Height = 32,
            Margin = new Padding(0, 0, 0, 8)
        };
        _trackPosY.Scroll += OnPosYTrackScroll;

        controlsStack.Controls.Add(lblPosY, 0, 6);
        controlsStack.Controls.Add(_numPosYPercent, 1, 6);
        controlsStack.Controls.Add(_trackPosY, 0, 7);
        controlsStack.SetColumnSpan(_trackPosY, 2);

        // 5. Eckenradius (%)
        var lblRadius = CreateSectionLabel(Localizer.T("Main.Label.CornerRadius"));
        _numCornerRadiusPercent = CreateStyledNumeric(0m, 100m, 1m, 0m, decimals: 0);
        _numCornerRadiusPercent.ValueChanged += OnCornerRadiusNumericChanged;

        _trackCornerRadius = new TactileSlider
        {
            Minimum = 0,
            Maximum = 100,
            SmallChange = 1,
            LargeChange = 10,
            Value = 0,
            Dock = DockStyle.Fill,
            Height = 32,
            Margin = new Padding(0, 0, 0, 8)
        };
        _trackCornerRadius.Scroll += OnCornerRadiusTrackScroll;

        controlsStack.Controls.Add(lblRadius, 0, 8);
        controlsStack.Controls.Add(_numCornerRadiusPercent, 1, 8);
        controlsStack.Controls.Add(_trackCornerRadius, 0, 9);
        controlsStack.SetColumnSpan(_trackCornerRadius, 2);

        // 6. Position & Größe zurücksetzen
        _btnReset = new TactilePillButton
        {
            Text = Localizer.T("Main.ResetPositionSize"),
            ButtonStyle = TactileButtonStyle.SecondaryWhite,
            Dock = DockStyle.Fill,
            Height = 36,
            Font = new Font(Font.FontFamily, 9.0f, FontStyle.Bold),
            Margin = new Padding(0, 2, 0, 8)
        };
        _btnReset.Click += OnResetClick;
        controlsStack.Controls.Add(_btnReset, 0, 10);
        controlsStack.SetColumnSpan(_btnReset, 2);

        // 7. Desktop-Kontextmenü-Chip
        _chkContextMenu = new TactileCheckChip
        {
            Text = Localizer.T("Main.ShowInContextMenu"),
            Dock = DockStyle.Fill,
            Height = 36,
            Font = new Font(Font.FontFamily, 8.8f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8)
        };
        _chkContextMenu.CheckedChanged += OnContextMenuCheckedChanged;
        controlsStack.Controls.Add(_chkContextMenu, 0, 11);
        controlsStack.SetColumnSpan(_chkContextMenu, 2);

        // Flexibler Abstandhalter
        var spacer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = StyleGuidePalette.PureWhite,
            Margin = new Padding(0)
        };
        controlsStack.Controls.Add(spacer, 0, 12);
        controlsStack.SetColumnSpan(spacer, 2);

        // 8. Getrennte Haupt-Aktionsbuttons unten:
        //    - „Anwenden“ (Primärblau #0098FE -> #0074FC mit #0042E9 Kante)
        //    - „Sticker deaktivieren“
        //    - „Schließen“ (separater Button!)
        _btnApply = new TactilePillButton
        {
            Text = Localizer.T("Main.Apply"),
            ButtonStyle = TactileButtonStyle.PrimaryBlue,
            Dock = DockStyle.Fill,
            Height = 44,
            Font = new Font(Font.FontFamily, 10.2f, FontStyle.Bold),
            Margin = new Padding(0, 4, 0, 6)
        };
        _btnApply.Click += OnApplyClick;

        var bottomSecondaryRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Height = 40,
            Margin = new Padding(0),
            BackColor = StyleGuidePalette.PureWhite
        };
        bottomSecondaryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54f));
        bottomSecondaryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46f));

        _btnDeactivate = new TactilePillButton
        {
            Text = Localizer.T("Main.Deactivate"),
            ButtonStyle = TactileButtonStyle.SecondaryWhite,
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 9.0f, FontStyle.Bold),
            Margin = new Padding(0, 0, 4, 0)
        };
        _btnDeactivate.Click += OnDeactivateClick;

        _btnClose = new TactilePillButton
        {
            Text = Localizer.T("Main.Close"),
            ButtonStyle = TactileButtonStyle.SecondaryWhite,
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 9.0f, FontStyle.Bold),
            Margin = new Padding(4, 0, 0, 0)
        };
        _btnClose.Click += OnCloseClick;

        bottomSecondaryRow.Controls.Add(_btnDeactivate, 0, 0);
        bottomSecondaryRow.Controls.Add(_btnClose, 1, 0);

        controlsStack.Controls.Add(_btnApply, 0, 13);
        controlsStack.SetColumnSpan(_btnApply, 2);
        controlsStack.Controls.Add(bottomSecondaryRow, 0, 14);
        controlsStack.SetColumnSpan(bottomSecondaryRow, 2);

        for (int r = 0; r <= 11; r++)
        {
            controlsStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        controlsStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // 12 Spacer
        controlsStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 13 Apply
        controlsStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 14 Deactivate + Close

        rightCard.Controls.Add(controlsStack);

        mainLayout.Controls.Add(leftCard, 0, 0);
        mainLayout.Controls.Add(centerCard, 1, 0);
        mainLayout.Controls.Add(rightCard, 2, 0);
        Controls.Add(mainLayout);
    }

    private Label CreateSectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = StyleGuidePalette.TextPrimary,
            Font = new Font(Font.FontFamily, 9.0f, FontStyle.Bold),
            Margin = new Padding(2, 2, 0, 2)
        };
    }

    private NumericUpDown CreateStyledNumeric(decimal min, decimal max, decimal increment, decimal val, int decimals)
    {
        return new NumericUpDown
        {
            DecimalPlaces = decimals,
            Minimum = min,
            Maximum = max,
            Increment = increment,
            Value = val,
            Dock = DockStyle.Fill,
            BackColor = StyleGuidePalette.SurfaceSoft,
            ForeColor = StyleGuidePalette.DarkBlue,
            Font = new Font(Font.FontFamily, 9.2f, FontStyle.Bold),
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(6, 1, 2, 2)
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!InitializeEditorSession())
        {
            Close();
        }
    }

    private bool InitializeEditorSession()
    {
        try
        {
            _paths.EnsureDirectoriesAndWriteAccess();
            _paths.CleanupTempDirectory();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                Localizer.T("Main.WriteError.Title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }

        _sessionStartOsState = _wallpaperService.GetCurrentState();
        if (_sessionStartOsState.ScreenWidth <= 0 || _sessionStartOsState.ScreenHeight <= 0)
        {
            MessageBox.Show(
                this,
                Localizer.T("Main.UnsupportedResolution.Text"),
                Localizer.T("Main.UnsupportedResolution.Title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        SettingsLoadResult loadResult = _settingsManager.Load();
        if (loadResult.Status == SettingsLoadStatus.CorruptedOrIncomplete)
        {
            DialogResult recoverChoice = MessageBox.Show(
                this,
                Localizer.T("Main.ConfigCorrupted.Text", loadResult.ErrorMessage),
                Localizer.T("Main.ConfigCorrupted.Title"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (recoverChoice != DialogResult.Yes)
            {
                return false;
            }
        }

        _persistedSettings = loadResult.Settings.Clone();
        _workingSettings = loadResult.Settings.Clone();

        // Falls in Windows noch ein früheres generiertes Wallpaper aktiv ist, das Original nahtlos wiederherstellen
        if (_backupManager.IsGeneratedWallpaper(_sessionStartOsState, _workingSettings) &&
            _backupManager.HasValidOriginalBackup(_workingSettings))
        {
            _ = _backupManager.RestoreOriginalWallpaper(
                _workingSettings,
                _wallpaperService,
                _settingsManager,
                forceRestoreEvenIfExternalChanged: true);
            _workingSettings.IsStickerActive = _persistedSettings.IsStickerActive;
            _settingsManager.Save(_workingSettings);
            _sessionStartOsState = _wallpaperService.GetCurrentState();
        }

        // Aktuelles Hintergrundbild (z. B. von Wallpaper Engine oder Windows) geräuschlos als Vorschau-Basis übernehmen
        if (_sessionStartOsState.IsSupportedConfiguration &&
            !_backupManager.IsGeneratedWallpaper(_sessionStartOsState, _workingSettings))
        {
            bool wasActive = _workingSettings.IsStickerActive;
            _backupManager.CreateOrUpdateOriginalBackup(_sessionStartOsState, _workingSettings);
            _workingSettings.IsStickerActive = wasActive;
            _settingsManager.Save(_workingSettings);
            _persistedSettings = _workingSettings.Clone();
        }
        else if (!_workingSettings.HasOriginalBackup)
        {
            _workingSettings.HasOriginalBackup = true;
            _workingSettings.OriginalWasSolidColorOnly = true;
            _workingSettings.OriginalBackgroundColorR = _sessionStartOsState.BackgroundColor.R;
            _workingSettings.OriginalBackgroundColorG = _sessionStartOsState.BackgroundColor.G;
            _workingSettings.OriginalBackgroundColorB = _sessionStartOsState.BackgroundColor.B;
            _workingSettings.OriginalScreenWidth = _sessionStartOsState.ScreenWidth;
            _workingSettings.OriginalScreenHeight = _sessionStartOsState.ScreenHeight;
        }

        _contextMenuRegistrar.EnsureUpdatedIfRegistered(_workingSettings.ContextMenuEnabled);
        _workingSettings.ContextMenuEnabled = _contextMenuRegistrar.IsRegistered();
        _autostartRegistrar.EnsureSynchronized(_workingSettings);

        // Hintergrundbild für die Vorschau laden
        _originalWallpaperBitmap?.Dispose();
        _originalWallpaperBitmap = null;

        if (!_workingSettings.OriginalWasSolidColorOnly)
        {
            try
            {
                _originalWallpaperBitmap = StickerImageLoader.LoadAndNormalizeBitmap(_workingSettings.OriginalBackupFilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    Localizer.T("Main.BackgroundLoadError.Text", ex.Message),
                    Localizer.T("Main.BackgroundLoadError.Title"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }
        }

        _previewControl.SetDesktopBackground(
            _originalWallpaperBitmap,
            _workingSettings.OriginalWasSolidColorOnly,
            _workingSettings.OriginalBackgroundColor,
            _workingSettings.OriginalPositionMode,
            _sessionStartOsState.ScreenWidth,
            _sessionStartOsState.ScreenHeight);

        // Alle gespeicherten Sticker-Bilder aus der Liste laden
        DisposeAllLoadedStickers();
        _workingSettings.SyncLegacyAndMultiStickerFields();

        var validStickers = new List<StickerItemConfig>();
        foreach (StickerItemConfig item in _workingSettings.Stickers)
        {
            if (string.IsNullOrWhiteSpace(item.StickerFilePath) || !File.Exists(item.StickerFilePath))
            {
                continue;
            }

            try
            {
                LoadedStickerImage loaded = StickerImageLoader.LoadFromLocalCopy(item.StickerFilePath);
                _loadedStickersById[item.Id] = loaded;
                validStickers.Add(item);
            }
            catch
            {
                // Nicht lesbare Datei überspringen
            }
        }

        _workingSettings.Stickers = validStickers;
        RenumberStickerDefaultNames();
        _workingSettings.SyncLegacyAndMultiStickerFields();

        RefreshStickerLayersInUi();
        SyncUiFromWorkingSettings();
        return true;
    }

    private StickerItemConfig? GetSelectedStickerConfig()
    {
        if (_workingSettings.Stickers.Count == 0)
        {
            return null;
        }

        StickerItemConfig? found = _workingSettings.Stickers
            .FirstOrDefault(s => string.Equals(s.Id, _workingSettings.SelectedStickerId, StringComparison.OrdinalIgnoreCase));
        if (found is null)
        {
            found = _workingSettings.Stickers[0];
            _workingSettings.SelectedStickerId = found.Id;
        }

        return found;
    }

    private LoadedStickerImage? GetSelectedLoadedSticker()
    {
        StickerItemConfig? cfg = GetSelectedStickerConfig();
        if (cfg is null)
        {
            return null;
        }

        return _loadedStickersById.TryGetValue(cfg.Id, out LoadedStickerImage? img) ? img : null;
    }

    private void RenumberStickerDefaultNames()
    {
        for (int i = 0; i < _workingSettings.Stickers.Count; i++)
        {
            StickerItemConfig s = _workingSettings.Stickers[i];
            if (string.IsNullOrWhiteSpace(s.DisplayName) ||
                Localizer.IsDefaultStickerName(s.DisplayName))
            {
                s.DisplayName = Localizer.T("Sticker.DefaultName", i + 1);
            }
        }
    }

    private void RefreshStickerLayersInUi()
    {
        _workingSettings.SyncLegacyAndMultiStickerFields();

        var tabEntries = new List<StickerTabEntry>(_workingSettings.Stickers.Count);
        foreach (StickerItemConfig item in _workingSettings.Stickers)
        {
            _loadedStickersById.TryGetValue(item.Id, out LoadedStickerImage? loaded);
            tabEntries.Add(new StickerTabEntry
            {
                Config = item,
                Bitmap = loaded?.Bitmap,
                LoadedImage = loaded
            });
        }

        _lblLayerCountBadge.Text = Localizer.T("Main.LayerCountBadge", tabEntries.Count);
        _verticalStickerTab.SetEntries(tabEntries, _workingSettings.SelectedStickerId);
        _previewControl.SetStickerLayers(tabEntries, _workingSettings.SelectedStickerId);
    }

    private void SyncUiFromWorkingSettings(string? customStatusNote = null)
    {
        _suppressUiEvents = true;
        try
        {
            StickerItemConfig? selectedCfg = GetSelectedStickerConfig();
            LoadedStickerImage? selectedImg = GetSelectedLoadedSticker();
            bool hasSelected = selectedCfg is not null && selectedImg is not null;

            double maxRelWidth = hasSelected
                ? StickerGeometry.GetMaxAllowedRelativeWidth(
                    selectedImg!.Width,
                    selectedImg.Height,
                    _sessionStartOsState.ScreenWidth,
                    _sessionStartOsState.ScreenHeight)
                : 1.0;

            decimal maxWidthPercent = Math.Clamp((decimal)Math.Round(maxRelWidth * 100.0, 1), 2.0m, 100.0m);
            _numWidthPercent.Maximum = maxWidthPercent;
            _trackWidth.Maximum = Math.Max(_trackWidth.Minimum, (int)Math.Round((double)maxWidthPercent * 10.0));

            if (hasSelected)
            {
                StickerPlacementResult placement = StickerGeometry.CalculatePlacement(
                    selectedImg!.Width,
                    selectedImg.Height,
                    _sessionStartOsState.ScreenWidth,
                    _sessionStartOsState.ScreenHeight,
                    selectedCfg!.RelativeWidth,
                    selectedCfg.RelativeX,
                    selectedCfg.RelativeY);

                selectedCfg.RelativeWidth = placement.RelativeWidth;
                selectedCfg.RelativeX = placement.RelativeX;
                selectedCfg.RelativeY = placement.RelativeY;
                _workingSettings.SyncLegacyAndMultiStickerFields();
            }

            double curRelW = selectedCfg?.RelativeWidth ?? AppSettings.DefaultRelativeWidth;
            double curRelX = selectedCfg?.RelativeX ?? AppSettings.DefaultRelativeX;
            double curRelY = selectedCfg?.RelativeY ?? AppSettings.DefaultRelativeY;
            double curRad = selectedCfg?.CornerRadiusPercent ?? AppSettings.DefaultCornerRadiusPercent;

            decimal widthPercentVal = Math.Clamp(
                (decimal)Math.Round(curRelW * 100.0, 1),
                _numWidthPercent.Minimum,
                _numWidthPercent.Maximum);
            _numWidthPercent.Value = widthPercentVal;

            _trackWidth.Value = Math.Clamp(
                (int)Math.Round(curRelW * 1000.0),
                _trackWidth.Minimum,
                _trackWidth.Maximum);

            decimal posXVal = Math.Clamp(
                (decimal)Math.Round(curRelX * 100.0, 1),
                _numPosXPercent.Minimum,
                _numPosXPercent.Maximum);
            _numPosXPercent.Value = posXVal;
            _trackPosX.Value = Math.Clamp((int)Math.Round(curRelX * 1000.0), _trackPosX.Minimum, _trackPosX.Maximum);

            decimal posYVal = Math.Clamp(
                (decimal)Math.Round(curRelY * 100.0, 1),
                _numPosYPercent.Minimum,
                _numPosYPercent.Maximum);
            _numPosYPercent.Value = posYVal;
            _trackPosY.Value = Math.Clamp((int)Math.Round(curRelY * 1000.0), _trackPosY.Minimum, _trackPosY.Maximum);

            decimal radiusVal = Math.Clamp(
                (decimal)Math.Round(curRad, 0),
                _numCornerRadiusPercent.Minimum,
                _numCornerRadiusPercent.Maximum);
            _numCornerRadiusPercent.Value = radiusVal;
            _trackCornerRadius.Value = (int)radiusVal;

            _chkContextMenu.Checked = _workingSettings.ContextMenuEnabled;

            _lblSelectedStickerHeader.Text = hasSelected
                ? Localizer.T("Main.EditingHeader", selectedCfg!.DisplayName)
                : Localizer.T("Main.NoImageSelected");

            _btnReplaceImage.Enabled = hasSelected;
            _btnDeleteSelected.Enabled = hasSelected;
            _trackWidth.Enabled = hasSelected;
            _numWidthPercent.Enabled = hasSelected;
            _trackPosX.Enabled = hasSelected;
            _numPosXPercent.Enabled = hasSelected;
            _trackPosY.Enabled = hasSelected;
            _numPosYPercent.Enabled = hasSelected;
            _trackCornerRadius.Enabled = hasSelected;
            _numCornerRadiusPercent.Enabled = hasSelected;
            _btnReset.Enabled = hasSelected;

            // „Anwenden“ ist aktiv, sobald mindestens ein Bild vorhanden ist ODER zuvor Bilder aktiv waren (um Löschung anzuwenden)
            _btnApply.Enabled = hasSelected || _persistedSettings.IsStickerActive;

            _btnDeactivate.Enabled = _persistedSettings.IsStickerActive ||
                                     OverlayHostManager.IsOverlayRunning() ||
                                     _backupManager.IsGeneratedWallpaper(_sessionStartOsState, _persistedSettings);

            if (hasSelected)
            {
                _previewControl.UpdateStickerParameters(
                    selectedCfg!.RelativeWidth,
                    selectedCfg.RelativeX,
                    selectedCfg.RelativeY,
                    selectedCfg.CornerRadiusPercent);
            }

            _verticalStickerTab.Invalidate();
            UpdateStatusLabels(customStatusNote);
        }
        finally
        {
            _suppressUiEvents = false;
        }
    }

    private void UpdateStatusLabels(string? customStatusNote = null)
    {
        string modeText = _workingSettings.OriginalWasSolidColorOnly
            ? Localizer.T("Main.Mode.SolidColor")
            : FormatPositionMode(_workingSettings.OriginalPositionMode);

        _lblMonitorInfo.Text = Localizer.T(
            "Main.MonitorInfo",
            _sessionStartOsState.ScreenWidth,
            _sessionStartOsState.ScreenHeight,
            modeText,
            _workingSettings.Stickers.Count);

        StickerItemConfig? selectedCfg = GetSelectedStickerConfig();
        LoadedStickerImage? selectedImg = GetSelectedLoadedSticker();

        if (selectedCfg is null || selectedImg is null)
        {
            _lblStickerInfo.Text = customStatusNote ?? Localizer.T("Main.NoImageInStackHint");
            return;
        }

        StickerPlacementResult p = StickerGeometry.CalculatePlacement(
            selectedImg.Width,
            selectedImg.Height,
            _sessionStartOsState.ScreenWidth,
            _sessionStartOsState.ScreenHeight,
            selectedCfg.RelativeWidth,
            selectedCfg.RelativeX,
            selectedCfg.RelativeY);

        string gifInfo = selectedImg.IsAnimatedGif
            ? Localizer.T("Main.GifInfo.Animated", selectedImg.FrameCount)
            : (selectedImg.IsGif ? Localizer.T("Main.GifInfo.Static") : string.Empty);

        string baseInfo = Localizer.T(
            "Main.StickerInfo",
            selectedCfg.DisplayName,
            p.Width,
            p.Height,
            p.X,
            p.Y,
            selectedCfg.CornerRadiusPercent,
            gifInfo);

        _lblStickerInfo.Text = string.IsNullOrWhiteSpace(customStatusNote)
            ? baseInfo
            : $"{baseInfo}   —   {customStatusNote}";
    }

    private static string FormatPositionMode(WallpaperPositionMode mode) => mode switch
    {
        WallpaperPositionMode.Fill => Localizer.T("Main.Mode.Fill"),
        WallpaperPositionMode.Fit => Localizer.T("Main.Mode.Fit"),
        WallpaperPositionMode.Stretch => Localizer.T("Main.Mode.Stretch"),
        WallpaperPositionMode.Center => Localizer.T("Main.Mode.Center"),
        WallpaperPositionMode.Tile => Localizer.T("Main.Mode.Tile"),
        WallpaperPositionMode.Span => Localizer.T("Main.Mode.Span"),
        _ => mode.ToString()
    };

    private static string SupportedFileDialogFilter => Localizer.T("Main.FileDialogFilter");

    private string? NotifyUserIfGifSelected(LoadedStickerImage imported, string originalFilePath)
    {
        if (!imported.IsGif)
        {
            return null;
        }

        string fileName = Path.GetFileName(originalFilePath);
        if (!imported.IsAnimatedGif)
        {
            return Localizer.T("Main.GifStatic.Note", fileName);
        }

        int animatedGifCount = _loadedStickersById.Values.Count(v => v.IsAnimatedGif);
        double approxMb = Math.Max(0.1, imported.EstimatedUncompressedRamBytes / (1024.0 * 1024.0));
        bool isHeavyGif = imported.FrameCount > 50 || approxMb > 35.0 || animatedGifCount > 1;

        MessageBox.Show(
            this,
            Localizer.T("Main.GifAnimated.Intro", fileName, imported.FrameCount, imported.Width, imported.Height) +
            Localizer.T("Main.GifAnimated.ResourceHeader") +
            Localizer.T("Main.GifAnimated.Background") +
            (isHeavyGif
                ? Localizer.T("Main.GifAnimated.Heavy", approxMb)
                : Localizer.T("Main.GifAnimated.Light", approxMb)),
            Localizer.T("Main.GifAnimated.Title"),
            MessageBoxButtons.OK,
            isHeavyGif ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

        return Localizer.T("Main.GifAnimated.Note", imported.FrameCount);
    }

    private void OnAddNewStickerClick(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = Localizer.T("Main.AddDialog.Title"),
            Filter = SupportedFileDialogFilter,
            CheckFileExists = true,
            Multiselect = true,
            RestoreDirectory = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.FileNames.Length == 0)
        {
            return;
        }

        string? lastGifNote = null;

        foreach (string fileName in dialog.FileNames)
        {
            try
            {
                LoadedStickerImage imported = StickerImageLoader.ImportFromUserFile(fileName, _paths);
                _newlyImportedStickerPathsInSession.Add(imported.LocalFilePath);

                int index = _workingSettings.Stickers.Count;
                // Leichter Versatz für jedes neu hinzugefügte Bild, damit mehrere Bilder sofort sichtbar nebeneinander liegen
                double offsetStep = (index % 5) * 0.04;
                double initialX = Math.Clamp(AppSettings.DefaultRelativeX + offsetStep, 0.05, 0.85);
                double initialY = Math.Clamp(AppSettings.DefaultRelativeY + offsetStep, 0.05, 0.85);

                string shortName = Path.GetFileNameWithoutExtension(fileName);
                if (string.IsNullOrWhiteSpace(shortName))
                {
                    shortName = Localizer.T("Sticker.DefaultName", index + 1);
                }
                else if (shortName.Length > 18)
                {
                    shortName = shortName[..18] + "…";
                }

                var newItem = new StickerItemConfig
                {
                    Id = Guid.NewGuid().ToString("N"),
                    DisplayName = Localizer.T("Sticker.DefaultNameWithFile", index + 1, shortName),
                    StickerFilePath = imported.LocalFilePath,
                    RelativeWidth = AppSettings.DefaultRelativeWidth,
                    RelativeX = initialX,
                    RelativeY = initialY,
                    CornerRadiusPercent = 20.0
                };

                _loadedStickersById[newItem.Id] = imported;
                _workingSettings.Stickers.Add(newItem);
                _workingSettings.SelectedStickerId = newItem.Id;

                string? gifNote = NotifyUserIfGifSelected(imported, fileName);
                if (!string.IsNullOrWhiteSpace(gifNote))
                {
                    lastGifNote = gifNote;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    Localizer.T("Main.InvalidImage.Text", Path.GetFileName(fileName), ex.Message),
                    Localizer.T("Main.InvalidImage.Title"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        RefreshStickerLayersInUi();
        SyncUiFromWorkingSettings(lastGifNote);
    }

    private void OnReplaceSelectedImageClick(object? sender, EventArgs e)
    {
        StickerItemConfig? selectedCfg = GetSelectedStickerConfig();
        if (selectedCfg is null)
        {
            OnAddNewStickerClick(sender, e);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = Localizer.T("Main.ReplaceDialog.Title"),
            Filter = SupportedFileDialogFilter,
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            LoadedStickerImage imported = StickerImageLoader.ImportFromUserFile(dialog.FileName, _paths);
            _newlyImportedStickerPathsInSession.Add(imported.LocalFilePath);

            if (_loadedStickersById.TryGetValue(selectedCfg.Id, out LoadedStickerImage? oldImg))
            {
                oldImg.Dispose();
            }

            _loadedStickersById[selectedCfg.Id] = imported;
            selectedCfg.StickerFilePath = imported.LocalFilePath;

            string shortName = Path.GetFileNameWithoutExtension(dialog.FileName);
            int idx = _workingSettings.Stickers.IndexOf(selectedCfg);
            if (!string.IsNullOrWhiteSpace(shortName))
            {
                if (shortName.Length > 18)
                {
                    shortName = shortName[..18] + "…";
                }
                selectedCfg.DisplayName = Localizer.T("Sticker.DefaultNameWithFile", idx + 1, shortName);
            }

            string? gifNote = NotifyUserIfGifSelected(imported, dialog.FileName);

            RefreshStickerLayersInUi();
            SyncUiFromWorkingSettings(gifNote);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                Localizer.T("Main.InvalidSelectedImage.Text", ex.Message),
                Localizer.T("Main.InvalidImage.Title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnDeleteSelectedStickerClick(object? sender, EventArgs e)
    {
        StickerItemConfig? selectedCfg = GetSelectedStickerConfig();
        if (selectedCfg is not null)
        {
            DeleteStickerById(selectedCfg.Id);
        }
    }

    private void OnVerticalTabStickerSelected(object? sender, string stickerId)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        _workingSettings.SelectedStickerId = stickerId;
        RefreshStickerLayersInUi();
        SyncUiFromWorkingSettings();
    }

    private void OnVerticalTabStickerDeleteRequested(object? sender, string stickerId)
    {
        DeleteStickerById(stickerId);
    }

    private void DeleteStickerById(string stickerId)
    {
        int idx = _workingSettings.Stickers.FindIndex(s => string.Equals(s.Id, stickerId, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
        {
            return;
        }

        StickerItemConfig removed = _workingSettings.Stickers[idx];
        _workingSettings.Stickers.RemoveAt(idx);

        if (_loadedStickersById.TryGetValue(removed.Id, out LoadedStickerImage? img))
        {
            img.Dispose();
            _loadedStickersById.Remove(removed.Id);
        }

        if (_workingSettings.Stickers.Count > 0)
        {
            int nextIdx = Math.Clamp(idx, 0, _workingSettings.Stickers.Count - 1);
            _workingSettings.SelectedStickerId = _workingSettings.Stickers[nextIdx].Id;
        }
        else
        {
            _workingSettings.SelectedStickerId = string.Empty;
        }

        RefreshStickerLayersInUi();
        SyncUiFromWorkingSettings();
    }

    private void OnPreviewStickerSelected(object? sender, string stickerId)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        _workingSettings.SelectedStickerId = stickerId;
        _verticalStickerTab.SetEntries(
            _workingSettings.Stickers.Select(s =>
            {
                _loadedStickersById.TryGetValue(s.Id, out LoadedStickerImage? l);
                return new StickerTabEntry
                {
                    Config = s,
                    Bitmap = l?.Bitmap,
                    LoadedImage = l
                };
            }),
            stickerId);
        SyncUiFromWorkingSettings();
    }

    private void OnPreviewStickerPositionChanged(object? sender, EventArgs e)
    {
        StickerItemConfig? selectedCfg = GetSelectedStickerConfig();
        if (_suppressUiEvents || selectedCfg is null)
        {
            return;
        }

        selectedCfg.RelativeX = _previewControl.RelativeX;
        selectedCfg.RelativeY = _previewControl.RelativeY;
        _workingSettings.SyncLegacyAndMultiStickerFields();

        _suppressUiEvents = true;
        try
        {
            _numPosXPercent.Value = Math.Clamp(
                (decimal)Math.Round(selectedCfg.RelativeX * 100.0, 1),
                _numPosXPercent.Minimum,
                _numPosXPercent.Maximum);
            _trackPosX.Value = Math.Clamp(
                (int)Math.Round(selectedCfg.RelativeX * 1000.0),
                _trackPosX.Minimum,
                _trackPosX.Maximum);

            _numPosYPercent.Value = Math.Clamp(
                (decimal)Math.Round(selectedCfg.RelativeY * 100.0, 1),
                _numPosYPercent.Minimum,
                _numPosYPercent.Maximum);
            _trackPosY.Value = Math.Clamp(
                (int)Math.Round(selectedCfg.RelativeY * 1000.0),
                _trackPosY.Minimum,
                _trackPosY.Maximum);

            _verticalStickerTab.Invalidate();
            UpdateStatusLabels();
        }
        finally
        {
            _suppressUiEvents = false;
        }
    }

    private void OnWidthTrackScroll(object? sender, EventArgs e)
    {
        StickerItemConfig? selectedCfg = GetSelectedStickerConfig();
        if (_suppressUiEvents || selectedCfg is null)
        {
            return;
        }

        selectedCfg.RelativeWidth = _trackWidth.Value / 1000.0;
        SyncUiFromWorkingSettings();
    }

    private void OnWidthNumericChanged(object? sender, EventArgs e)
    {
        StickerItemConfig? selectedCfg = GetSelectedStickerConfig();
        if (_suppressUiEvents || selectedCfg is null)
        {
            return;
        }

        selectedCfg.RelativeWidth = (double)_numWidthPercent.Value / 100.0;
        SyncUiFromWorkingSettings();
    }

    private void OnPosXTrackScroll(object? sender, EventArgs e)
    {
        StickerItemConfig? selectedCfg = GetSelectedStickerConfig();
        if (_suppressUiEvents || selectedCfg is null)
        {
            return;
        }

        selectedCfg.RelativeX = _trackPosX.Value / 1000.0;
        SyncUiFromWorkingSettings();
    }

    private void OnPosYTrackScroll(object? sender, EventArgs e)
    {
        StickerItemConfig? selectedCfg = GetSelectedStickerConfig();
        if (_suppressUiEvents || selectedCfg is null)
        {
            return;
        }

        selectedCfg.RelativeY = _trackPosY.Value / 1000.0;
        SyncUiFromWorkingSettings();
    }

    private void OnPositionNumericChanged(object? sender, EventArgs e)
    {
        StickerItemConfig? selectedCfg = GetSelectedStickerConfig();
        if (_suppressUiEvents || selectedCfg is null)
        {
            return;
        }

        selectedCfg.RelativeX = (double)_numPosXPercent.Value / 100.0;
        selectedCfg.RelativeY = (double)_numPosYPercent.Value / 100.0;
        SyncUiFromWorkingSettings();
    }

    private void OnCornerRadiusTrackScroll(object? sender, EventArgs e)
    {
        StickerItemConfig? selectedCfg = GetSelectedStickerConfig();
        if (_suppressUiEvents || selectedCfg is null)
        {
            return;
        }

        selectedCfg.CornerRadiusPercent = _trackCornerRadius.Value;
        SyncUiFromWorkingSettings();
    }

    private void OnCornerRadiusNumericChanged(object? sender, EventArgs e)
    {
        StickerItemConfig? selectedCfg = GetSelectedStickerConfig();
        if (_suppressUiEvents || selectedCfg is null)
        {
            return;
        }

        selectedCfg.CornerRadiusPercent = (double)_numCornerRadiusPercent.Value;
        SyncUiFromWorkingSettings();
    }

    private void OnContextMenuCheckedChanged(object? sender, EventArgs e)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        bool wantEnabled = _chkContextMenu.Checked;
        try
        {
            if (wantEnabled)
            {
                _contextMenuRegistrar.Register();
            }
            else
            {
                _contextMenuRegistrar.Unregister();
            }

            _workingSettings.ContextMenuEnabled = _contextMenuRegistrar.IsRegistered();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                Localizer.T("Main.ContextMenuError.Text", ex.Message),
                Localizer.T("Main.ContextMenuError.Title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            _suppressUiEvents = true;
            _chkContextMenu.Checked = _contextMenuRegistrar.IsRegistered();
            _suppressUiEvents = false;
        }
    }

    private void OnResetClick(object? sender, EventArgs e)
    {
        StickerItemConfig? selectedCfg = GetSelectedStickerConfig();
        LoadedStickerImage? selectedImg = GetSelectedLoadedSticker();
        if (selectedCfg is null || selectedImg is null)
        {
            return;
        }

        double defaultWidth = AppSettings.DefaultRelativeWidth;
        StickerPlacementResult centered = StickerGeometry.CalculatePlacement(
            selectedImg.Width,
            selectedImg.Height,
            _sessionStartOsState.ScreenWidth,
            _sessionStartOsState.ScreenHeight,
            defaultWidth,
            0.5,
            0.5);

        double centerRelX = Math.Max(0.0, (_sessionStartOsState.ScreenWidth - centered.Width) * 0.5 / _sessionStartOsState.ScreenWidth);
        double centerRelY = Math.Max(0.0, (_sessionStartOsState.ScreenHeight - centered.Height) * 0.5 / _sessionStartOsState.ScreenHeight);

        selectedCfg.RelativeWidth = centered.RelativeWidth;
        selectedCfg.RelativeX = centerRelX;
        selectedCfg.RelativeY = centerRelY;
        selectedCfg.CornerRadiusPercent = 0.0;

        SyncUiFromWorkingSettings();
    }

    /// <summary>
    /// Wendet alle Bilder im Stapel sofort auf das Live-Desktop-Overlay an, OHNE das Fenster zu schließen.
    /// </summary>
    private void OnApplyClick(object? sender, EventArgs e)
    {
        if (_workingSettings.Stickers.Count == 0)
        {
            // Falls der Benutzer alle Bilder aus dem Stapel gelöscht hat und auf „Anwenden“ klickt -> Overlay beenden
            OverlayHostManager.StopOverlay();
            _autostartRegistrar.Unregister();
            _workingSettings.IsStickerActive = false;
            _workingSettings.SyncLegacyAndMultiStickerFields();
            _settingsManager.Save(_workingSettings);
            StickerImageLoader.CleanupUnusedStickerCopies(_paths, Array.Empty<string>());
            _newlyImportedStickerPathsInSession.Clear();
            _persistedSettings = _workingSettings.Clone();
            SyncUiFromWorkingSettings(Localizer.T("Main.Status.AllRemoved"));
            return;
        }

        Cursor = Cursors.WaitCursor;
        try
        {
            int screenWidth = Math.Max(1, _sessionStartOsState.ScreenWidth);
            int screenHeight = Math.Max(1, _sessionStartOsState.ScreenHeight);

            foreach (StickerItemConfig item in _workingSettings.Stickers)
            {
                if (_loadedStickersById.TryGetValue(item.Id, out LoadedStickerImage? img))
                {
                    StickerPlacementResult finalPlacement = StickerGeometry.CalculatePlacement(
                        img.Width,
                        img.Height,
                        screenWidth,
                        screenHeight,
                        item.RelativeWidth,
                        item.RelativeX,
                        item.RelativeY);

                    item.RelativeWidth = finalPlacement.RelativeWidth;
                    item.RelativeX = finalPlacement.RelativeX;
                    item.RelativeY = finalPlacement.RelativeY;
                }
            }

            _workingSettings.IsStickerActive = true;
            _workingSettings.LastGeneratedScreenWidth = screenWidth;
            _workingSettings.LastGeneratedScreenHeight = screenHeight;
            _workingSettings.SyncLegacyAndMultiStickerFields();

            _settingsManager.Save(_workingSettings);
            _autostartRegistrar.Register();

            if (!OverlayHostManager.StartOrReloadOverlay())
            {
                Cursor = Cursors.Default;
                MessageBox.Show(
                    this,
                    Localizer.T("Main.OverlayStartFailed.Text"),
                    Localizer.T("Main.ApplyError.Title"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            StickerImageLoader.CleanupUnusedStickerCopies(
                _paths,
                _workingSettings.Stickers.Select(s => s.StickerFilePath));
            _paths.CleanupTempDirectory();

            _newlyImportedStickerPathsInSession.Clear();
            _persistedSettings = _workingSettings.Clone();

            SyncUiFromWorkingSettings(Localizer.T("Main.Status.Applied"));
        }
        catch (Exception ex)
        {
            Cursor = Cursors.Default;
            MessageBox.Show(
                this,
                Localizer.T("Main.ApplyError.Text", ex.Message),
                Localizer.T("Main.ApplyError.Title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void OnDeactivateClick(object? sender, EventArgs e)
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            OverlayHostManager.StopOverlay();
            _autostartRegistrar.Unregister();

            WallpaperState currentOs = _wallpaperService.GetCurrentState();
            if (_backupManager.IsGeneratedWallpaper(currentOs, _persistedSettings) &&
                _backupManager.HasValidOriginalBackup(_persistedSettings))
            {
                _ = _backupManager.RestoreOriginalWallpaper(
                    _persistedSettings,
                    _wallpaperService,
                    _settingsManager,
                    forceRestoreEvenIfExternalChanged: true);
            }

            _persistedSettings.IsStickerActive = false;
            _workingSettings.IsStickerActive = false;
            _settingsManager.Save(_persistedSettings);

            SyncUiFromWorkingSettings(Localizer.T("Main.Status.Deactivated"));
        }
        catch (Exception ex)
        {
            Cursor = Cursors.Default;
            MessageBox.Show(
                this,
                Localizer.T("Main.DeactivateError.Text", ex.Message),
                Localizer.T("Main.DeactivateError.Title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void OnCloseClick(object? sender, EventArgs e)
    {
        Close();
    }

    private void CleanupUncommittedSessionStickers()
    {
        if (_newlyImportedStickerPathsInSession.Count == 0)
        {
            return;
        }

        List<string> persistedPaths = _persistedSettings.GetEffectiveStickers()
            .Select(s => s.StickerFilePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        foreach (string tempImportedPath in _newlyImportedStickerPathsInSession.ToList())
        {
            bool isPersisted = persistedPaths.Any(p => BackupManager.PathsEqual(p, tempImportedPath));
            if (!isPersisted && File.Exists(tempImportedPath))
            {
                try
                {
                    foreach (var kv in _loadedStickersById.Where(k => BackupManager.PathsEqual(k.Value.LocalFilePath, tempImportedPath)).ToList())
                    {
                        kv.Value.Dispose();
                        _loadedStickersById.Remove(kv.Key);
                    }

                    File.Delete(tempImportedPath);
                }
                catch
                {
                    // Ignorieren
                }
            }
        }

        _newlyImportedStickerPathsInSession.Clear();
    }

    private void DisposeAllLoadedStickers()
    {
        foreach (LoadedStickerImage img in _loadedStickersById.Values)
        {
            img.Dispose();
        }
        _loadedStickersById.Clear();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        CleanupUncommittedSessionStickers();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _previewControl.Dispose();
            _verticalStickerTab.Dispose();
            _originalWallpaperBitmap?.Dispose();
            _originalWallpaperBitmap = null;
            DisposeAllLoadedStickers();
        }

        base.Dispose(disposing);
    }
}
