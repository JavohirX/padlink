using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BlastemReceiver;

/// <summary>
/// Handles keystroke injection via Win32 SendInput for Sega Genesis controller button states.
///
/// Wire protocol — 2-byte bitmask (little-endian):
///   Byte 0 (low):
///     Bit 0 = Up
///     Bit 1 = Down
///     Bit 2 = Left
///     Bit 3 = Right
///     Bit 4 = A
///     Bit 5 = B
///     Bit 6 = C
///     Bit 7 = Start
///   Byte 1 (high):
///     Bit 0 = X
///     Bit 1 = Y
///     Bit 2 = Z
///     Bit 3 = Mode
///
/// BlastEm default key mapping (Player 1):
///   Up=↑  Down=↓  Left=←  Right=→  A=A  B=S  C=D  Start=Enter
///   X=Q  Y=W  Z=E  Mode=Backspace
///
/// Player 2 (configure BlastEm to match):
///   Up=Num8  Down=Num2  Left=Num4  Right=Num6
///   A=Num7  B=Num9  C=Num3  Start=Num0
///   X=Num1  Y=Num5  Z=Num.  Mode=Num-
/// </summary>
internal static class KeyboardInput
{
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL, wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
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
    // Virtual key constants
    // -------------------------------------------------------------------------

    private const ushort VK_UP     = 0x26;
    private const ushort VK_DOWN   = 0x28;
    private const ushort VK_LEFT   = 0x25;
    private const ushort VK_RIGHT  = 0x27;

    // Player 1 face buttons (BlastEm defaults)
    private const ushort VK_A      = 0x41; // A button → A key
    private const ushort VK_S      = 0x53; // B button → S key
    private const ushort VK_D      = 0x44; // C button → D key
    private const ushort VK_RETURN = 0x0D; // Start   → Enter
    private const ushort VK_Q      = 0x51; // X button → Q key
    private const ushort VK_W      = 0x57; // Y button → W key
    private const ushort VK_E      = 0x45; // Z button → E key
    private const ushort VK_BACK   = 0x08; // Mode    → Backspace

    // Player 2 face buttons (numpad — configure BlastEm to match)
    private const ushort VK_NUMPAD8   = 0x68; // Up
    private const ushort VK_NUMPAD2   = 0x62; // Down
    private const ushort VK_NUMPAD4   = 0x64; // Left
    private const ushort VK_NUMPAD6   = 0x66; // Right
    private const ushort VK_NUMPAD7   = 0x67; // A
    private const ushort VK_NUMPAD9   = 0x69; // B
    private const ushort VK_NUMPAD3   = 0x63; // C
    private const ushort VK_NUMPAD0   = 0x60; // Start
    private const ushort VK_NUMPAD1   = 0x61; // X
    private const ushort VK_NUMPAD5   = 0x65; // Y
    private const ushort VK_DECIMAL   = 0x6E; // Z  (Numpad .)
    private const ushort VK_SUBTRACT  = 0x6D; // Mode (Numpad -)

    // -------------------------------------------------------------------------
    // Key tables: 12 entries indexed by button bit position
    // Order: Up, Down, Left, Right, A, B, C, Start, X, Y, Z, Mode
    // -------------------------------------------------------------------------

    private static readonly ushort[] s_player1Keys =
    {
        VK_UP,      // bit  0 = Up
        VK_DOWN,    // bit  1 = Down
        VK_LEFT,    // bit  2 = Left
        VK_RIGHT,   // bit  3 = Right
        VK_A,       // bit  4 = A
        VK_S,       // bit  5 = B
        VK_D,       // bit  6 = C
        VK_RETURN,  // bit  7 = Start
        VK_Q,       // bit  8 = X
        VK_W,       // bit  9 = Y
        VK_E,       // bit 10 = Z
        VK_BACK,    // bit 11 = Mode
    };

    private static readonly ushort[] s_player2Keys =
    {
        VK_NUMPAD8,   // bit  0 = Up
        VK_NUMPAD2,   // bit  1 = Down
        VK_NUMPAD4,   // bit  2 = Left
        VK_NUMPAD6,   // bit  3 = Right
        VK_NUMPAD7,   // bit  4 = A
        VK_NUMPAD9,   // bit  5 = B
        VK_NUMPAD3,   // bit  6 = C
        VK_NUMPAD0,   // bit  7 = Start
        VK_NUMPAD1,   // bit  8 = X
        VK_NUMPAD5,   // bit  9 = Y
        VK_DECIMAL,   // bit 10 = Z
        VK_SUBTRACT,  // bit 11 = Mode
    };

    private static readonly string[] s_buttonNames =
    {
        "Up", "Down", "Left", "Right", "A", "B", "C", "Start", "X", "Y", "Z", "Mode"
    };

    // Extended keys (arrow keys need EXTENDEDKEY flag for correct scan codes)
    private static readonly HashSet<ushort> s_extendedKeys = new()
    {
        VK_UP, VK_DOWN, VK_LEFT, VK_RIGHT,
    };

    public static Action<string>? LogHandler { get; set; }

    private static ushort s_prevP1Lo = 0, s_prevP1Hi = 0;
    private static ushort s_prevP2Lo = 0, s_prevP2Hi = 0;

    private static readonly object s_lockP1 = new();
    private static readonly object s_lockP2 = new();

    /// <summary>
    /// Diffs newLo/newHi against previous 12-bit state for the given role.
    /// Sends key-down for bits newly set to 1, key-up for bits newly set to 0.
    /// </summary>
    public static void ApplyButtonState(string role, byte newLo, byte newHi)
    {
        bool isP1 = IsPlayer1(role);
        ushort[] keys = isP1 ? s_player1Keys : s_player2Keys;
        object lockObj = isP1 ? s_lockP1 : s_lockP2;

        ushort prevLo, prevHi;
        ushort changedLo, changedHi;

        lock (lockObj)
        {
            prevLo = isP1 ? s_prevP1Lo : s_prevP2Lo;
            prevHi = isP1 ? s_prevP1Hi : s_prevP2Hi;

            changedLo = (ushort)(prevLo ^ newLo);
            changedHi = (ushort)(prevHi ^ newHi);

            if (changedLo == 0 && changedHi == 0)
                return;

            if (isP1) { s_prevP1Lo = newLo; s_prevP1Hi = newHi; }
            else      { s_prevP2Lo = newLo; s_prevP2Hi = newHi; }
        }

        var inputs = new INPUT[12];
        int count = 0;
        var changes = new List<string>();

        for (int bit = 0; bit < 12; bit++)
        {
            byte changedByte = bit < 8 ? (byte)changedLo : (byte)changedHi;
            byte newByte     = bit < 8 ? newLo            : newHi;
            int  localBit    = bit < 8 ? bit               : bit - 8;

            if ((changedByte & (1 << localBit)) == 0)
                continue;

            bool isPressed = (newByte & (1 << localBit)) != 0;
            inputs[count++] = BuildKeyInput(keys[bit], isPressed);
            changes.Add(isPressed ? $"+{s_buttonNames[bit]}" : $"-{s_buttonNames[bit]}");
        }

        if (changes.Count > 0)
            LogHandler?.Invoke($"{role}: {string.Join(", ", changes)}");

        if (count > 0)
        {
            var trimmed = new INPUT[count];
            Array.Copy(inputs, trimmed, count);
            SendInput((uint)count, trimmed, Marshal.SizeOf<INPUT>());
        }
    }

    /// <summary>Sends key-up for all 12 buttons for the given role.</summary>
    public static void ReleaseAllKeys(string role)
    {
        bool isP1 = IsPlayer1(role);
        ushort[] keys = isP1 ? s_player1Keys : s_player2Keys;
        object lockObj = isP1 ? s_lockP1 : s_lockP2;

        lock (lockObj)
        {
            if (isP1) { s_prevP1Lo = 0; s_prevP1Hi = 0; }
            else      { s_prevP2Lo = 0; s_prevP2Hi = 0; }
        }

        var inputs = new INPUT[12];
        for (int i = 0; i < 12; i++)
            inputs[i] = BuildKeyInput(keys[i], pressed: false);

        SendInput(12, inputs, Marshal.SizeOf<INPUT>());
    }

    private static bool IsPlayer1(string role)
        => string.Equals(role, "player1", StringComparison.OrdinalIgnoreCase);

    private static INPUT BuildKeyInput(ushort vk, bool pressed)
    {
        ushort scanCode = (ushort)MapVirtualKey(vk, 0);
        uint flags = KEYEVENTF_SCANCODE;
        if (!pressed)        flags |= KEYEVENTF_KEYUP;
        if (s_extendedKeys.Contains(vk)) flags |= KEYEVENTF_EXTENDEDKEY;

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
