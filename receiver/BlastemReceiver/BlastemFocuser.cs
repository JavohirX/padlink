using System;
using System.Runtime.InteropServices;

namespace BlastemReceiver;

/// <summary>
/// Locates the BlastEm emulator window and brings it to the foreground before
/// keystrokes are injected. Uses the AttachThreadInput workaround to defeat
/// Windows focus-stealing prevention.
/// </summary>
internal static class BlastemFocuser
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    private static IntPtr s_blastemWindow = IntPtr.Zero;
    private static readonly object s_lock = new();

    public static bool IsFound
    {
        get
        {
            lock (s_lock)
                return s_blastemWindow != IntPtr.Zero;
        }
    }

    public static Action<string>? LogHandler { get; set; }

    /// <summary>
    /// Scans all top-level windows for one whose title contains "blastem" (case-insensitive).
    /// </summary>
    public static void RefreshBlastem()
    {
        IntPtr found = IntPtr.Zero;

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hWnd, sb, sb.Capacity);
            string title = sb.ToString();

            if (title.Contains("blastem", StringComparison.OrdinalIgnoreCase))
            {
                found = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        lock (s_lock)
        {
            s_blastemWindow = found;
        }
    }

    /// <summary>
    /// Brings BlastEm to the foreground only if it is not already the foreground window.
    /// </summary>
    public static void EnsureBlastemForeground()
    {
        IntPtr blastem;
        lock (s_lock)
        {
            blastem = s_blastemWindow;
        }

        if (blastem == IntPtr.Zero)
            return;

        IntPtr foreground = GetForegroundWindow();
        if (foreground == blastem)
            return;

        LogHandler?.Invoke("Bringing BlastEm window to the foreground");

        uint fgThread = GetWindowThreadProcessId(foreground, out _);
        uint ourThread = GetCurrentThreadId();

        bool attached = false;
        if (fgThread != 0 && fgThread != ourThread)
        {
            attached = AttachThreadInput(ourThread, fgThread, true);
        }

        try
        {
            BringWindowToTop(blastem);
            SetForegroundWindow(blastem);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(ourThread, fgThread, false);
            }
        }
    }
}
