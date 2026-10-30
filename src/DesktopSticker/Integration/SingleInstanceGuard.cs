using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopSticker.Integration;

/// <summary>
/// Verhindert den gleichzeitigen Lauf mehrerer Instanzen und holt ein bereits geöffnetes
/// Einstellungsfenster in den Vordergrund (Kapitel 18.6 & 21).
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    public const string DefaultMutexName = @"Local\DesktopSticker_SingleInstance_Mutex_v1";

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private bool _disposed;

    public bool IsFirstInstance => _ownsMutex;

    public SingleInstanceGuard(string mutexName = DefaultMutexName)
    {
        bool createdNew;
        try
        {
            _mutex = new Mutex(initiallyOwned: true, mutexName, out createdNew);
            if (!createdNew)
            {
                try
                {
                    _ownsMutex = _mutex.WaitOne(0, exitContext: false);
                }
                catch (AbandonedMutexException)
                {
                    // Vorherige Instanz wurde unerwartet beendet -> Mutex übernommen
                    _ownsMutex = true;
                }
            }
            else
            {
                _ownsMutex = true;
            }
        }
        catch
        {
            _mutex = new Mutex(initiallyOwned: false);
            _ownsMutex = false;
        }
    }

    /// <summary>
    /// Bringt das Hauptfenster einer bereits laufenden Desktop-Sticker-Instanz in den Vordergrund.
    /// </summary>
    public static void ActivateExistingInstanceWindow()
    {
        try
        {
            using Process current = Process.GetCurrentProcess();
            Process[] processes = Process.GetProcessesByName(current.ProcessName);
            foreach (Process proc in processes)
            {
                using (proc)
                {
                    if (proc.Id == current.Id || proc.SessionId != current.SessionId)
                    {
                        continue;
                    }

                    IntPtr hWnd = proc.MainWindowHandle;
                    if (hWnd != IntPtr.Zero &&
                        !string.Equals(proc.MainWindowTitle, "DesktopStickerOverlayHost", StringComparison.Ordinal))
                    {
                        if (IsIconic(hWnd))
                        {
                            ShowWindow(hWnd, SW_RESTORE);
                        }

                        SetForegroundWindow(hWnd);
                        break;
                    }
                }
            }
        }
        catch
        {
            // Ignorieren
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_ownsMutex)
            {
                _mutex.ReleaseMutex();
            }
        }
        catch
        {
            // Ignorieren
        }

        _mutex.Dispose();
    }
}
