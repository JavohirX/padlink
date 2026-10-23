using System;
using System.Runtime.InteropServices;

namespace NesReceiver;

/// <summary>
/// Locates the FCEUX emulator window and brings it to the foreground before
/// keystrokes are injected. Uses the AttachThreadInput workaround to defeat
/// Windows focus-stealing prevention.
/// </summary>
internal static class FceusFocuser
{
    // -------------------------------------------------------------------------
    // Win32 P/Invoke declarations
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    private static IntPtr s_fceuWindow = IntPtr.Zero;
    private static readonly object s_lock = new();

    /// <summary>
    /// True if a visible FCEUX window was found during the last <see cref="RefreshFceux"/> call.
    /// </summary>
    public static bool IsFound
    {
        get
        {
            lock (s_lock)
                return s_fceuWindow != IntPtr.Zero;
        }
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public static Action<string>? LogHandler { get; set; }

    /// <summary>
    /// Scans all top-level windows for one whose title contains "FCEUX" (case-insensitive).
    /// Call this on a timer (e.g. every 2 seconds) from the UI thread or a background thread.
    /// </summary>
    public static void RefreshFceux()
    {
        IntPtr found = IntPtr.Zero;

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true; // continue enumeration

            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hWnd, sb, sb.Capacity);
            string title = sb.ToString();

            if (title.Contains("FCEUX", StringComparison.OrdinalIgnoreCase))
            {
                found = hWnd;
                return false; // stop enumeration
            }

            return true; // continue
        }, IntPtr.Zero);

        lock (s_lock)
        {
            s_fceuWindow = found;
        }
    }

    /// <summary>
    /// Brings FCEUX to the foreground only if it is not already the foreground window.
    /// Uses the AttachThreadInput workaround so SetForegroundWindow succeeds even when
    /// another application currently owns focus.
    ///
    /// Must be called from a thread that has a message pump (or any thread, actually —
    /// GetCurrentThreadId works from any thread, but AttachThreadInput requires that
    /// both threads have message queues; for a WinForms app the UI thread qualifies).
    ///
    /// Safe to call from background threads: the Win32 calls themselves are thread-safe.
    /// </summary>
    public static void EnsureFceusForeground()
    {
        IntPtr fceux;
        lock (s_lock)
        {
            fceux = s_fceuWindow;
        }

        if (fceux == IntPtr.Zero)
            return; // FCEUX not found, nothing to do

        IntPtr foreground = GetForegroundWindow();
        if (foreground == fceux)
            return; // already in foreground, skip

        LogHandler?.Invoke("Bringing FCEUX window to the foreground");

        // AttachThreadInput workaround:
        //   1. Get the thread that owns the current foreground window.
        //   2. Attach our thread's input to that thread.
        //   3. Call SetForegroundWindow — now succeeds because we share input state.
        //   4. Detach.
        uint fgThread = GetWindowThreadProcessId(foreground, out _);
        uint ourThread = GetCurrentThreadId();

        bool attached = false;
        if (fgThread != 0 && fgThread != ourThread)
        {
            attached = AttachThreadInput(ourThread, fgThread, true);
        }

        try
        {
            BringWindowToTop(fceux);
            SetForegroundWindow(fceux);
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
