using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using DesktopSticker.Configuration;
using DesktopSticker.Integration;
using DesktopSticker.Localization;
using DesktopSticker.Overlay;
using Microsoft.Win32;

namespace DesktopSticker.UI;

/// <summary>
/// Moderner 1-Klick-Installationsassistent für Desktop Sticker.
/// Wird automatisch geöffnet, wenn die Datei als „DesktopSticker-Setup.exe“ (oder mit „--setup“) gestartet wird.
/// </summary>
public sealed class SetupForm : Form
{
    public const string UninstallRegistrySubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopSticker";

    /// <summary>
    /// Anzeigeversion (aus der Assembly-Version der .csproj abgeleitet, damit Setup, „Installierte Apps“ und Release übereinstimmen).
    /// </summary>
    private static readonly string AppVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.0";

    private readonly TactileCheckChip _chkDesktopShortcut;
    private readonly TactileCheckChip _chkStartMenuShortcut;
    private readonly TactileCheckChip _chkContextMenu;
    private readonly TactileCheckChip _chkLaunchAfterInstall;
    private readonly TactilePillButton _btnInstall;
    private readonly TactilePillButton _btnCancel;
    private readonly Label _lblStatus;

    private readonly string _installDir;
    private readonly string _targetExePath;

    public SetupForm()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _installDir = Path.Combine(localAppData, "Programs", "DesktopSticker");
        _targetExePath = Path.Combine(_installDir, "DesktopSticker.exe");

        Text = Localizer.T("Setup.WindowTitle", AppVersion);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(540, 470);
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
            Padding = new Padding(22)
        };

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 10,
            BackColor = StyleGuidePalette.PureWhite
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var lblTitle = new Label
        {
            Text = Localizer.T("Setup.Title", AppVersion),
            Font = new Font(Font.FontFamily, 14.0f, FontStyle.Bold),
            ForeColor = StyleGuidePalette.DarkBlue,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };

        var lblSubtitle = new Label
        {
            Text = Localizer.T("Setup.Subtitle"),
            Font = new Font(Font.FontFamily, 9.2f, FontStyle.Regular),
            ForeColor = StyleGuidePalette.TextSecondary,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 14)
        };

        var lblPathInfo = new Label
        {
            Text = Localizer.T("Setup.TargetFolder", _installDir),
            Font = new Font(Font.FontFamily, 8.3f, FontStyle.Regular),
            ForeColor = StyleGuidePalette.TextSecondary,
            AutoSize = true,
            Margin = new Padding(2, 0, 0, 14)
        };

        _chkDesktopShortcut = new TactileCheckChip
        {
            Text = Localizer.T("Setup.DesktopShortcut"),
            Checked = true,
            Dock = DockStyle.Fill,
            Height = 38,
            Font = new Font(Font.FontFamily, 9.2f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8)
        };

        _chkStartMenuShortcut = new TactileCheckChip
        {
            Text = Localizer.T("Setup.StartMenuShortcut"),
            Checked = true,
            Dock = DockStyle.Fill,
            Height = 38,
            Font = new Font(Font.FontFamily, 9.2f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8)
        };

        _chkContextMenu = new TactileCheckChip
        {
            Text = Localizer.T("Setup.ContextMenu"),
            Checked = true,
            Dock = DockStyle.Fill,
            Height = 38,
            Font = new Font(Font.FontFamily, 9.2f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8)
        };

        _chkLaunchAfterInstall = new TactileCheckChip
        {
            Text = Localizer.T("Setup.LaunchAfterInstall"),
            Checked = true,
            Dock = DockStyle.Fill,
            Height = 38,
            Font = new Font(Font.FontFamily, 9.2f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 12)
        };

        _lblStatus = new Label
        {
            Text = Localizer.T("Setup.Status.Ready"),
            Font = new Font(Font.FontFamily, 8.8f, FontStyle.Bold),
            ForeColor = StyleGuidePalette.MediumBlue,
            AutoSize = true,
            Margin = new Padding(2, 2, 0, 8)
        };

        var spacer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = StyleGuidePalette.PureWhite
        };

        var buttonRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Height = 46,
            Margin = new Padding(0),
            BackColor = StyleGuidePalette.PureWhite
        };
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65f));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35f));

        _btnInstall = new TactilePillButton
        {
            Text = Localizer.T("Setup.Button.Install"),
            ButtonStyle = TactileButtonStyle.PrimaryBlue,
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 10.2f, FontStyle.Bold),
            Margin = new Padding(0, 0, 6, 0)
        };
        _btnInstall.Click += OnInstallClick;

        _btnCancel = new TactilePillButton
        {
            Text = Localizer.T("Common.Cancel"),
            ButtonStyle = TactileButtonStyle.SecondaryWhite,
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 9.2f, FontStyle.Bold),
            Margin = new Padding(6, 0, 0, 0)
        };
        _btnCancel.Click += (_, _) => Close();

        buttonRow.Controls.Add(_btnInstall, 0, 0);
        buttonRow.Controls.Add(_btnCancel, 1, 0);

        stack.Controls.Add(lblTitle, 0, 0);
        stack.Controls.Add(lblSubtitle, 0, 1);
        stack.Controls.Add(lblPathInfo, 0, 2);
        stack.Controls.Add(_chkDesktopShortcut, 0, 3);
        stack.Controls.Add(_chkStartMenuShortcut, 0, 4);
        stack.Controls.Add(_chkContextMenu, 0, 5);
        stack.Controls.Add(_chkLaunchAfterInstall, 0, 6);
        stack.Controls.Add(_lblStatus, 0, 7);
        stack.Controls.Add(spacer, 0, 8);
        stack.Controls.Add(buttonRow, 0, 9);

        for (int i = 0; i <= 7; i++)
        {
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        card.Controls.Add(stack);
        outerLayout.Controls.Add(card, 0, 0);
        Controls.Add(outerLayout);
    }

    public static bool ShouldRunSetupMode(string[] args)
    {
        if (args.Length > 0)
        {
            string first = args[0].Trim();
            if (string.Equals(first, "--setup", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(first, "--install", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return false;
        }

        string fileName = Path.GetFileNameWithoutExtension(processPath);
        return fileName.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("install", StringComparison.OrdinalIgnoreCase);
    }

    private void OnInstallClick(object? sender, EventArgs e)
    {
        string? sourceExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(sourceExe) || !File.Exists(sourceExe))
        {
            MessageBox.Show(
                this,
                Localizer.T("Setup.Error.SourceExeNotFound"),
                Localizer.T("Setup.Error.Title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        _btnInstall.Enabled = false;
        _btnCancel.Enabled = false;
        Cursor = Cursors.WaitCursor;
        _lblStatus.Text = Localizer.T("Setup.Status.Installing");
        Application.DoEvents();

        try
        {
            // 1. Laufendes Overlay beenden, falls bereits eine ältere Version aktiv ist
            OverlayHostManager.StopOverlay();

            // 2. Zielverzeichnis anlegen und Programmdatei kopieren
            Directory.CreateDirectory(_installDir);

            if (!string.Equals(Path.GetFullPath(sourceExe), Path.GetFullPath(_targetExePath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourceExe, _targetExePath, overwrite: true);
            }

            // 3. Optionale Verknüpfungen auf Desktop und im Startmenü anlegen
            if (_chkDesktopShortcut.Checked)
            {
                string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (!string.IsNullOrWhiteSpace(desktopDir))
                {
                    CreateWindowsShortcut(
                        Path.Combine(desktopDir, "Desktop Sticker.lnk"),
                        _targetExePath,
                        _installDir,
                        Localizer.T("Setup.ShortcutDescription"));
                }
            }

            if (_chkStartMenuShortcut.Checked)
            {
                string startMenuDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                if (!string.IsNullOrWhiteSpace(startMenuDir))
                {
                    CreateWindowsShortcut(
                        Path.Combine(startMenuDir, "Desktop Sticker.lnk"),
                        _targetExePath,
                        _installDir,
                        Localizer.T("Setup.ShortcutDescription"));
                }
            }

            // 4. Desktop-Kontextmenü und Einstellungen synchronisieren
            var paths = new AppStoragePaths();
            paths.EnsureDirectoriesAndWriteAccess();
            var settingsManager = new SettingsManager(paths);
            SettingsLoadResult loaded = settingsManager.Load();
            AppSettings settings = loaded.Settings;

            var contextMenu = new ContextMenuRegistrar();
            if (_chkContextMenu.Checked)
            {
                contextMenu.Register(_targetExePath);
                settings.ContextMenuEnabled = true;
            }
            settingsManager.Save(settings);

            var autostart = new AutostartRegistrar();
            autostart.EnsureSynchronized(settings, _targetExePath);

            // 5. In Windows „Installierte Apps“ (HKCU Uninstall) registrieren
            RegisterUninstallEntry(_installDir, _targetExePath);

            _lblStatus.Text = Localizer.T("Setup.Status.Success");

            if (_chkLaunchAfterInstall.Checked)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _targetExePath,
                        WorkingDirectory = _installDir,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // Ignorieren
                }
            }
            else
            {
                MessageBox.Show(
                    this,
                    Localizer.T("Setup.Completed.Text", _targetExePath),
                    Localizer.T("Setup.Completed.Title"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            Close();
        }
        catch (Exception ex)
        {
            _btnInstall.Enabled = true;
            _btnCancel.Enabled = true;
            _lblStatus.Text = Localizer.T("Setup.Status.Failed");
            MessageBox.Show(
                this,
                Localizer.T("Setup.Error.InstallFailed", ex.Message),
                Localizer.T("Setup.Error.Title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static void CreateWindowsShortcut(string shortcutPath, string targetExePath, string workingDirectory, string description)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return;
            }

            object? shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return;
            }

            object? shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { shortcutPath });

            if (shortcut is null)
            {
                return;
            }

            Type scType = shortcut.GetType();
            scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetExePath });
            scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
            scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
            scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { $"{targetExePath},0" });
            scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
        catch
        {
            // Ignorieren, falls WScript.Shell eingeschränkt ist
        }
    }

    private static void RegisterUninstallEntry(string installDir, string targetExePath)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(UninstallRegistrySubKey, writable: true);
            if (key is null)
            {
                return;
            }

            key.SetValue("DisplayName", "Desktop Sticker", RegistryValueKind.String);
            key.SetValue("DisplayVersion", AppVersion, RegistryValueKind.String);
            key.SetValue("Publisher", "Desktop Sticker", RegistryValueKind.String);
            key.SetValue("DisplayIcon", targetExePath, RegistryValueKind.String);
            key.SetValue("InstallLocation", installDir, RegistryValueKind.String);
            key.SetValue("UninstallString", $"\"{targetExePath}\" --uninstall", RegistryValueKind.String);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        catch
        {
            // Ignorieren
        }
    }

    public static void RemoveShortcutsAndUninstallEntry()
    {
        try
        {
            string desktopShortcut = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "Desktop Sticker.lnk");
            if (File.Exists(desktopShortcut))
            {
                File.Delete(desktopShortcut);
            }
        }
        catch
        {
            // Ignorieren
        }

        try
        {
            string startMenuShortcut = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                "Desktop Sticker.lnk");
            if (File.Exists(startMenuShortcut))
            {
                File.Delete(startMenuShortcut);
            }
        }
        catch
        {
            // Ignorieren
        }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallRegistrySubKey, throwOnMissingSubKey: false);
        }
        catch
        {
            // Ignorieren
        }
    }
}
