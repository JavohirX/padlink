using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace NesReceiver;

/// <summary>
/// Handles keystroke injection via Win32 SendInput for NES controller button states.
///
/// Bit layout of the 1-byte bitmask sent by the phone:
///   Bit 0 = Up
///   Bit 1 = Down
///   Bit 2 = Left
///   Bit 3 = Right
///   Bit 4 = A
///   Bit 5 = B
///   Bit 6 = Select
///   Bit 7 = Start
///
/// FCEUX default key mapping:
///   Player 1: Up=W  Down=S  Left=A  Right=D  B=J  A=K  Select=U  Start=I
///   Player 2: Up=Num8  Down=Num2  Left=Num4  Right=Num6  B=Num1  A=Num3  Select=Num7  Start=Num9
/// </summary>
internal static class KeyboardInput
{
    // -------------------------------------------------------------------------
    // Win32 P/Invoke declarations
    // -------------------------------------------------------------------------

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001; // needed for numpad keys when NumLock is on

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    // -------------------------------------------------------------------------
    // Virtual key code constants
    // -------------------------------------------------------------------------

    // Player 1 keys (standard keyboard letters)
    private const ushort VK_W = 0x57; // Up
    private const ushort VK_S = 0x53; // Down
    private const ushort VK_A = 0x41; // Left
    private const ushort VK_D = 0x44; // Right
    private const ushort VK_J = 0x4A; // B
    private const ushort VK_K = 0x4B; // A
    private const ushort VK_U = 0x55; // Select
    private const ushort VK_I = 0x49; // Start

    // Player 2 keys (numpad)
    private const ushort VK_NUMPAD8 = 0x68; // Up
    private const ushort VK_NUMPAD2 = 0x62; // Down
    private const ushort VK_NUMPAD4 = 0x64; // Left
    private const ushort VK_NUMPAD6 = 0x66; // Right
    private const ushort VK_NUMPAD1 = 0x61; // B
    private const ushort VK_NUMPAD3 = 0x63; // A
    private const ushort VK_NUMPAD7 = 0x67; // Select
    private const ushort VK_NUMPAD9 = 0x69; // Start

    // -------------------------------------------------------------------------
    // Key mapping tables indexed by bit position [0..7]
    // Order: Up, Down, Left, Right, A, B, Select, Start
    // -------------------------------------------------------------------------

    private static readonly ushort[] s_player1Keys =
    {
        VK_W,       // bit 0 = Up
        VK_S,       // bit 1 = Down
        VK_A,       // bit 2 = Left
        VK_D,       // bit 3 = Right
        VK_K,       // bit 4 = A
        VK_J,       // bit 5 = B
        VK_U,       // bit 6 = Select
        VK_I,       // bit 7 = Start
    };

    private static readonly ushort[] s_player2Keys =
    {
        VK_NUMPAD8, // bit 0 = Up
        VK_NUMPAD2, // bit 1 = Down
        VK_NUMPAD4, // bit 2 = Left
        VK_NUMPAD6, // bit 3 = Right
        VK_NUMPAD3, // bit 4 = A
        VK_NUMPAD1, // bit 5 = B
        VK_NUMPAD7, // bit 6 = Select
        VK_NUMPAD9, // bit 7 = Start
    };

    // -------------------------------------------------------------------------
    // Logging and Diagnostics
    // -------------------------------------------------------------------------

    public static Action<string>? LogHandler { get; set; }

    private static readonly string[] s_buttonNames =
    {
        "Up",
        "Down",
        "Left",
        "Right",
        "A",
        "B",
        "Select",
        "Start"
    };

    // -------------------------------------------------------------------------
    // Per-role previous state tracking
    // -------------------------------------------------------------------------

    private static byte s_prevP1 = 0;
    private static byte s_prevP2 = 0;

    private static readonly object s_lockP1 = new();
    private static readonly object s_lockP2 = new();

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Diffs newBitmask against previous state for the given role.
    /// Sends key-down for bits newly set to 1, key-up for bits newly set to 0.
    /// </summary>
    public static void ApplyButtonState(string role, byte newBitmask)
    {
        bool isP1 = IsPlayer1(role);
        ushort[] keys = isP1 ? s_player1Keys : s_player2Keys;
        object lockObj = isP1 ? s_lockP1 : s_lockP2;

        byte prev;
        byte changed;

        lock (lockObj)
        {
            prev = isP1 ? s_prevP1 : s_prevP2;
            changed = (byte)(prev ^ newBitmask);

            if (changed == 0)
                return; // nothing changed

            if (isP1)
                s_prevP1 = newBitmask;
            else
                s_prevP2 = newBitmask;
        }

        // Build the array of INPUT structs for all changed bits
        // Max 8 bits = max 8 INPUT structs
        var inputs = new INPUT[8];
        int count = 0;
        var changes = new List<string>();

        for (int bit = 0; bit < 8; bit++)
        {
            if ((changed & (1 << bit)) == 0)
                continue; // this bit didn't change

            bool isPressed = (newBitmask & (1 << bit)) != 0;
            ushort vk = keys[bit];

            inputs[count] = BuildKeyInput(vk, isPressed);
            count++;

            string btnName = s_buttonNames[bit];
            changes.Add(isPressed ? $"+{btnName}" : $"-{btnName}");
        }

        if (changes.Count > 0 && LogHandler != null)
        {
            LogHandler($"{role}: {string.Join(", ", changes)}");
        }

        if (count > 0)
        {
            var trimmed = new INPUT[count];
            Array.Copy(inputs, trimmed, count);
            SendInput((uint)count, trimmed, Marshal.SizeOf<INPUT>());
        }
    }

    /// <summary>
    /// Sends key-up for all 8 keys for the given role.
    /// Called on disconnect (clean or unclean) to prevent stuck keys.
    /// </summary>
    public static void ReleaseAllKeys(string role)
    {
        bool isP1 = IsPlayer1(role);
        ushort[] keys = isP1 ? s_player1Keys : s_player2Keys;
        object lockObj = isP1 ? s_lockP1 : s_lockP2;

        lock (lockObj)
        {
            if (isP1)
                s_prevP1 = 0;
            else
                s_prevP2 = 0;
        }

        var inputs = new INPUT[8];
        for (int i = 0; i < 8; i++)
        {
            inputs[i] = BuildKeyInput(keys[i], pressed: false);
        }

        SendInput(8, inputs, Marshal.SizeOf<INPUT>());
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static bool IsPlayer1(string role)
        => string.Equals(role, "player1", StringComparison.OrdinalIgnoreCase);

    private const uint KEYEVENTF_SCANCODE = 0x0008;

    private static INPUT BuildKeyInput(ushort vk, bool pressed)
    {
        ushort scanCode = (ushort)MapVirtualKey(vk, 0);

        uint flags = KEYEVENTF_SCANCODE;
        if (!pressed)
            flags |= KEYEVENTF_KEYUP;

        // Check if this key is an extended key in standard Windows layouts
        bool isExtended = vk == 0x25 || vk == 0x26 || vk == 0x27 || vk == 0x28 || // Arrow keys
                          vk == 0x2D || vk == 0x2E || // Insert, Delete
                          vk == 0x24 || vk == 0x23 || // Home, End
                          vk == 0x21 || vk == 0x22;   // PgUp, PgDn

        if (isExtended)
            flags |= KEYEVENTF_EXTENDEDKEY;

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = scanCode,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                }
            }
        };
    }
}
