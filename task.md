# Padlink — NES design spec

Padlink is the public name of this project: phone controllers for FCEUX and BlastEm. This document is the original NES spec. The Genesis receiver uses the same connection flow on port **8788** and a 2-byte button mask, documented in `receiver/BlastemReceiver/KeyboardInput.cs`.

# Wireless Phone-to-FCEUX NES Controller — Project Specification

## 1. Overview

We are building a system that lets two Android phones act as wireless NES controllers for **FCEUX**, an NES emulator running on a Windows laptop. Each phone displays an on-screen NES controller. Touch input on the phone is sent over the local WiFi network to a receiver program on the Windows laptop, which converts it into keyboard input that FCEUX reads as Joystick 1 / Joystick 2 input.

This is a personal/hobby project. Priorities, in order: **low latency, reliability (no stuck keys, clean reconnects), simple setup (QR scan), realistic NES-pad feel**. Polish beyond that (themes, settings depth, etc.) is nice-to-have, not required.

### 1.1 Why this approach (context for the team)

- FCEUX reads **keyboard input**, not raw gamepad/HID signals. So the receiver's job is to simulate keypresses, not to register as a virtual gamepad device. This avoids needing virtual-gamepad drivers (e.g. ViGEm) or elevated permissions — direct keystroke injection works out of the box on Windows.
- We validated this overall approach against existing prior art (a generic open-source "phone as PC gamepad" tool, and a hobbyist's near-identical "phone button → PC keypress" project). Both confirm: event-driven sends (not polled), TCP with no artificial delay, and direct keyboard injection are all sound, proven approaches for this exact problem.

---

## 2. System Architecture

```
┌─────────────────────┐                         ┌──────────────────────┐
│ Android Phone 1      │                         │                      │
│ (Player 1 role)      │──WiFi / WebSocket(TCP)──▶│                      │
│ NES controller UI    │                         │  Windows Receiver    │
└─────────────────────┘                         │  (C# / .NET)         │
                                                  │                      │
┌─────────────────────┐                         │  - WebSocket server  │
│ Android Phone 2      │                         │  - Role→key mapping  │
│ (Player 2 role)      │──WiFi / WebSocket(TCP)──▶│  - SendInput (P/Invoke) ──▶ FCEUX (focused window)
│ NES controller UI    │                         │  - FCEUX auto-focus  │
└─────────────────────┘                         │  - QR code generator │
                                                  └──────────────────────┘
```

Three deliverables:

1. **Android app** ("Controller App") — Kotlin, native.
2. **Windows receiver** ("Receiver") — C#/.NET.
3. **Wire protocol** connecting them — defined in this document; both teams build against it independently.

---

## 3. Wire Protocol

### 3.1 Transport

- **WebSocket over TCP.**
- Windows receiver hosts a WebSocket server using `System.Net.WebSockets.HttpListener` (built into .NET, no third-party dependency required).
- Android app connects as a WebSocket client using OkHttp's WebSocket support.
- **`TCP_NODELAY` must be set on both ends** (disable Nagle's algorithm). This is critical for latency — without it, small messages can be buffered for tens of milliseconds before transmission.
- Default port: **8787** (arbitrary, but pick one and keep it consistent; must not collide with common local services). Configurable, but the QR code always encodes whatever port is actually in use.

### 3.2 Connection lifecycle

1. Receiver starts, opens WebSocket server, displays a QR code encoding `ws://<laptop-local-ip>:<port>`.
2. Phone scans the QR code (or reuses a previously cached IP:port — see §5.6).
3. Phone opens WebSocket connection to that address.
4. **Immediately after connecting**, phone sends a **Hello** message declaring its role (see §3.3).
5. Receiver binds that connection to the role's key mapping (Joystick 1 or Joystick 2 key set).
6. Phone then sends **Button State** messages on every input change (see §3.4).
7. On disconnect (any reason), receiver immediately releases all keys associated with that role (see §6.3) and marks the role "unassigned," ready for a new/reconnecting phone to claim it.

### 3.3 Message: Hello (role declaration)

Sent once, immediately after connection, and again immediately after every reconnect.

```json
{ "type": "hello", "role": "player1" }
```

- `role` is one of: `"player1"`, `"player2"`.
- If the receiver already has an active connection claiming that role, it must reject the new claim:
  ```json
  { "type": "hello_rejected", "reason": "role_taken", "role": "player1" }
  ```
  The rejected phone's app should show a clear message ("Player 1 already connected — choose Player 2") and let the user pick a different role. Do **not** silently bump the existing connection.
- If accepted:
  ```json
  { "type": "hello_ack", "role": "player1" }
  ```

### 3.4 Message: Button State

Sent on every press/release event — i.e. immediately on the underlying touch event, never batched, never polled. One message per state change, not one message per frame.

**Format: single-byte bitmask**, sent as a raw WebSocket **binary** frame (not text/JSON) for minimum size and parsing overhead:

| Bit | Button |
|-----|--------|
| 0   | Up     |
| 1   | Down   |
| 2   | Left   |
| 3   | Right  |
| 4   | A      |
| 5   | B      |
| 6   | Select |
| 7   | Start  |

- Bit = 1 means "currently pressed," bit = 0 means "currently released."
- Each message is a **full snapshot** of all 8 buttons' current state, not a delta. This means a single dropped/out-of-order message can't cause a permanently-stuck button — the next message always re-states full truth. (Even though TCP guarantees order/delivery, this remains good defensive practice for the reconnect case where state must resync from scratch.)
- On Hello/reconnect, the receiver should treat the role's state as "all released" until the first Button State message arrives.

This is intentionally the *only* application-level message sent at runtime after the handshake — keep it minimal. Avoid JSON for this message; the framing/parsing overhead is unnecessary for a value this simple and frequent.

### 3.5 Message: Disconnect handling

No explicit "goodbye" message is required. Disconnect is detected by the WebSocket close event (clean close) or read-loop exception (unclean close/network drop) on the receiver side. Both cases must be treated identically: release all keys for that role immediately.

---

## 4. Windows Receiver — Specification

**Language/runtime:** C#/.NET (any current LTS version). Self-contained `dotnet publish` build so the laptop doesn't need a separate .NET runtime installed.

### 4.1 Responsibilities

1. **WebSocket server** via `HttpListener`, accepting connections from up to 2 phones simultaneously.
2. **QR code generation**, displayed in the receiver's own UI window, encoding `ws://<local-ip>:<port>` (use any small QR-generation library; this is a solved problem, pick something lightweight).
   - Display the laptop's actual LAN IP (not `localhost`/`127.0.0.1`) — detect the active network interface IP automatically. If multiple network interfaces exist, prefer one on the same WiFi network as the phones, and allow manual override/selection in case auto-detection picks the wrong adapter.
3. **Connection management**: track 0–2 active connections, each tagged with a role (`player1` / `player2`) once their Hello message arrives. Enforce the one-connection-per-role rule from §3.3.
4. **Key mapping table** (hardcoded, see §7 for the full table) — translates a role + bitmask into a Win32 virtual-key-code set.
5. **Keystroke injection** via `SendInput` (P/Invoke against `user32.dll`). For each Button State message:
   - Diff the new bitmask against the previously-known state for that role.
   - For every bit that changed 0→1, send a key-down event for the mapped key.
   - For every bit that changed 1→0, send a key-up event for the mapped key.
   - Do **not** re-send key-down for bits that are already 1 (no need — Windows/FCEUX doesn't need repeated down-events for a held key; key-repeat is irrelevant here since we control press/release explicitly).
6. **FCEUX window auto-focus** (see §4.2).
7. **Disconnect handling**: on any connection close (clean or unclean), immediately send key-up for all 8 mapped keys for that role, then mark the role unassigned.
8. **Status UI**: a simple window showing:
   - The QR code.
   - Connection status per role (not connected / connected / reconnecting).
   - Whether the FCEUX window has been found (see §4.2).
   - This does not need to be fancy — a minimal WinForms or WPF window is fine. No need for elaborate visual design; function over form.

### 4.2 FCEUX window auto-focus

FCEUX must be the OS-level foreground window for `SendInput` keystrokes to reach it. The receiver should not require the user to manually keep FCEUX focused.

- On startup, locate the FCEUX window via `FindWindow`/`EnumWindows`, matching on window title (containing "FCEUX") or window class name.
- Before sending input, check `GetForegroundWindow()`. **Only call to bring FCEUX to the foreground if it is not already the foreground window** — do not call this on every single keystroke, both for performance and to avoid visible taskbar flicker.
- Plain `SetForegroundWindow` is frequently silently ignored by Windows due to OS-level focus-stealing prevention. Use the standard workaround: `AttachThreadInput` to temporarily attach to the foreground thread, then call `SetForegroundWindow`, then detach. (This is a well-documented pattern; search "AttachThreadInput SetForegroundWindow C#" for reference implementations.)
- Re-scan for the FCEUX window periodically (e.g. every 2–3 seconds) in case it was closed and reopened — don't assume the window handle found at startup remains valid forever.
- If FCEUX cannot be found at all, the status UI must say so clearly (e.g. "FCEUX not detected — input will not be sent"). Do not fail silently.
- **Known tradeoff, by design**: because focus is grabbed automatically before sending input, if the user alt-tabs away from FCEUX to do something else on the laptop, the next button press from either phone will yank focus back to FCEUX. This is accepted behavior, not a bug. (A future "pause input" toggle could address this if it becomes annoying in practice — not required for v1.)

### 4.3 Reconnection support

- The server simply keeps accepting new WebSocket connections at all times — there is no special "reconnect mode." A disconnect followed by a new connection sending the same role in its Hello message is treated as a normal continuation, not a special case.
- Role assignment is driven entirely by what each phone declares in its Hello message, **never** by connection order. This makes it robust to phones reconnecting in any order.

### 4.4 Non-functional requirements

- No admin/elevated privileges required for normal operation (standard `SendInput` works without elevation under normal circumstances). Note: if FCEUX itself is ever run elevated, the receiver would also need to run elevated, since Windows blocks input injection from a lower-privilege process into a higher-privilege one. Document this; do not engineer around it for v1.
- The server should handle the "0 phones connected" and "1 phone connected" states gracefully — both are valid normal operating states, not errors.

---

## 5. Android Controller App — Specification

**Language:** Kotlin, native Android (no cross-platform framework).
**Networking:** OkHttp for the WebSocket client.

### 5.1 App flow

1. **Connect screen**: scan a QR code (use a standard QR-scanning library, e.g. ZXing or a CameraX-based scanner) to obtain the receiver's `ws://ip:port` address. If a previously-scanned address is cached (see §5.6), offer "Reconnect to last device" as a shortcut alongside "Scan new QR code."
2. **Role picker screen**: after the WebSocket connection opens, show a simple "Player 1 / Player 2" choice. On selection, send the Hello message (§3.3).
   - If `hello_rejected` is received, show an inline message and let the user pick the other role instead, without leaving this screen.
   - On `hello_ack`, proceed to the controller screen.
3. **Controller screen**: full-screen NES controller UI (see §5.3). Stays active for the duration of play.

### 5.2 Networking behavior

- Use OkHttp's WebSocket client with `TCP_NODELAY` enabled on the underlying socket.
- **Auto-reconnect with backoff** on disconnect: retry immediately, then at 1s, 2s, 5s, and cap at 5s for further retries (keep retrying indefinitely at the 5s interval until it succeeds or the user navigates away).
- On reconnect, immediately re-send the Hello message with the same role as before — this should happen automatically, with no user action required.
- While disconnected/reconnecting, show a non-blocking "Reconnecting…" banner overlaid on the controller screen. Do not block input visually — the user should still see the controller, just with a status indicator.
- Cache the last successful `ws://ip:port` (see §5.6) so reconnects and app relaunches don't require a fresh QR scan every time.

### 5.3 Controller UI — layout and rendering

Render a visual likeness of a NES controller: D-pad on the left, Select/Start in the middle, A/B on the right (mirroring real NES pad layout — B to the left of A, matching their real-world relative positions).

**Critical implementation requirement — D-pad:**
Do **not** implement the D-pad as four separate button views. Touch handoff between adjacent sibling views is unreliable when a finger slides from one to another (e.g. rolling from Left to Up-Left to Up without lifting). Instead:

- Implement the D-pad as a **single custom View** that performs its own hit-testing based on raw touch X/Y coordinates relative to the view's bounds.
- Divide the D-pad's touchable area into a logical 3×3 grid (corners = diagonals, edges = cardinal directions, center = dead zone / no input).
- Diagonal zones should map to **two simultaneous bits** (e.g. touching the up-right corner sets both Up and Right bits in the same outgoing message).

**Other buttons (A, B, Select, Start):** can be implemented as individual views, since chord combinations between these and the D-pad are handled via independent multi-touch tracking (§5.4), not view-to-view handoff.

### 5.4 Multi-touch handling

- Use raw touch event handling (`onTouchEvent`, `ACTION_DOWN` / `ACTION_POINTER_DOWN` / `ACTION_UP` / `ACTION_POINTER_UP` / `ACTION_CANCEL`). **Do not use `OnClickListener`** — click listeners wait for a full tap-and-release cycle and add unacceptable latency for a hold-style button.
- Track each active touch by its **pointer ID** independently. Maintain a mapping of "which button(s) is pointer ID N currently over," so that lifting one finger never affects the pressed-state of a button still held by a different finger.
- This combined with the D-pad design above must support realistic chords: e.g. holding Right + B (running) while also tapping A (jumping) — three simultaneous inputs across two fingers (one on the D-pad, one on B, a third briefly on A) must all register correctly and independently release.
- On every state change (any bit flips), recompute the full 8-bit bitmask and send a Button State message (§3.4) immediately — don't wait for a frame tick, don't debounce.

### 5.5 Feel / realism requirements

- **Visual feedback**: the touched button must visually depress (e.g. color or elevation change) on the same call stack as the raw `ACTION_DOWN`/`ACTION_POINTER_DOWN` event — must not wait for any network round-trip or acknowledgment.
- **Network send**: dispatch the Button State message from the same touch-event handler, synchronously alongside the visual update. Visual feedback and network send should not be sequenced to wait on one another.
- **Haptic feedback**: on by default, user-toggleable in a small settings area.
  - Trigger on press only (down-stroke), never on release.
  - Use a short, sharp pulse (~15–20ms), not a long buzz.
  - Prefer `VibrationEffect.createOneShot` with explicit short duration and (where supported) low amplitude, over the legacy duration-only `Vibrator.vibrate()` call.
  - Fire from the same touch handler as the visual/network updates — no sequencing dependency between the three.
- **Keep screen on**: set `FLAG_KEEP_SCREEN_ON` on the controller activity so the phone doesn't sleep mid-session.

### 5.6 Connection persistence & QR caching

- Cache the last successfully-connected `ip:port` locally (e.g. `SharedPreferences`) after a successful Hello handshake.
- On app launch, if a cached address exists, offer it as a one-tap reconnect option rather than forcing a fresh QR scan every session. Still allow scanning a new code (e.g. if the laptop's IP changed).

### 5.7 Lifecycle / edge case handling

- **`onPause` (app backgrounded — screen lock, notification pulled focus, etc.)**: immediately send an all-released Button State message (bitmask `0x00`) before any potential disconnect, so a backgrounded phone never leaves a stuck key on the laptop side.
- **App force-closed or crashes**: the receiver's disconnect-triggered "release all keys" logic (§4.1.7) is the backstop here — the app doesn't need special crash-handling beyond what's listed above, since the server-side cleanup covers it.
- **Duplicate role claims**: handled per §3.3 — show a clear in-app message, let the user pick the other role.

---

## 6. Cross-cutting Concerns

### 6.1 Latency budget (informational, not a hard requirement, but the target to design toward)

Expected end-to-end latency on a healthy local WiFi network: roughly 20–40ms (touch → app → WiFi → receiver → SendInput → FCEUX polling → frame). This is well within "very playable" territory for NES games. Contributing factors within our control: avoiding polling/batching, `TCP_NODELAY`, minimal message format, no unnecessary processing on either end. Factor outside our control: WiFi quality (recommend 5GHz band over 2.4GHz to the end user, but this is a network-environment note, not something the software can enforce).

### 6.2 Network environment assumptions

- Both phones and the laptop must be on the same local WiFi network.
- No internet/cloud dependency — this is entirely a local-network, local-only system. No external servers, no accounts, no cloud relay.

### 6.3 Stuck-key prevention (summary — this logic appears in multiple places above, listed together here for clarity since it's a cross-cutting safety requirement)

A "stuck key" (a key that gets pressed but never released due to a dropped connection, crash, or background event) is considered a significant bug, not a minor one — it can ruin gameplay. All of the following must independently guarantee release:

1. Receiver releases all keys for a role the instant that role's WebSocket connection closes, for any reason.
2. Phone app sends an all-released bitmask in `onPause`, before any disconnect even occurs.
3. Both of these are independent safety nets — neither depends on the other firing correctly.

### 6.4 Out of scope for this version

- iOS support.
- True FCEUX-to-FCEUX netplay (FCEUX has its own unrelated built-in TCP/IP netplay feature for syncing two separate emulator instances — not relevant to this project and should not be confused with it).
- Virtual gamepad/HID emulation (e.g. via ViGEm) — not needed since FCEUX accepts keyboard input directly.
- Support for more than 2 simultaneous phones/players.
- In-app ROM loading, save states, or any FCEUX feature control — this system only sends button input, nothing else.
- OLED burn-in mitigation — confirmed unnecessary, target phones are LCD.

---

## 7. Key Binding Reference (final — implement exactly as specified)

These are the exact Windows virtual keys the receiver must send, and the exact bindings the user will separately configure inside FCEUX's own input settings (FCEUX configuration is a manual one-time setup step done by the project owner, not something the software needs to automate).

| NES Button | Joystick 1 (Player 1) | Joystick 2 (Player 2) |
|---|---|---|
| Up | `W` | `Numpad 8` |
| Down | `S` | `Numpad 2` |
| Left | `A` | `Numpad 4` |
| Right | `D` | `Numpad 6` |
| B | `J` | `Numpad 1` |
| A | `K` | `Numpad 3` |
| Select | `U` | `Numpad 7` |
| Start | `I` | `Numpad 9` |

Win32 virtual-key codes for reference (receiver implementation): standard `VK_W`, `VK_S`, `VK_A`, `VK_D`, `VK_J`, `VK_K`, `VK_U`, `VK_I` for Player 1; `VK_NUMPAD8`, `VK_NUMPAD2`, `VK_NUMPAD4`, `VK_NUMPAD6`, `VK_NUMPAD1`, `VK_NUMPAD3`, `VK_NUMPAD7`, `VK_NUMPAD9` for Player 2.

---

## 8. Acceptance Criteria / Test Checklist

Before considering this done, verify:

- [ ] Scanning the QR code on a phone successfully connects it to the receiver.
- [ ] Selecting "Player 1" on one phone and "Player 2" on the other correctly maps each phone's buttons to the corresponding key set.
- [ ] Attempting to claim an already-taken role shows a clear rejection message and allows picking the other role.
- [ ] Pressing each of the 8 buttons individually on each phone produces the correct single keypress in FCEUX (verify visually in FCEUX's input config "live" view, or in-game).
- [ ] Holding a D-pad direction and pressing A/B simultaneously registers as a true simultaneous chord (e.g. running + jumping works in a test game like Super Mario Bros.).
- [ ] Sliding a thumb across the D-pad from one direction to an adjacent diagonal registers the transition smoothly, without dropped input.
- [ ] Disconnecting a phone (e.g. turning off WiFi) immediately releases all of that phone's keys in FCEUX — no stuck keys.
- [ ] Reconnecting that phone resumes correct input within the backoff window, without requiring a re-scan or role re-selection.
- [ ] Backgrounding the app (e.g. screen lock) releases all keys immediately.
- [ ] FCEUX regains foreground focus automatically if it lost it, before the next keypress is sent.
- [ ] If FCEUX is closed entirely, the receiver UI clearly indicates it cannot find the FCEUX window.
- [ ] Haptic feedback fires on press only, is short, and can be disabled in settings.
- [ ] Phone screen does not sleep during an active controller session.
- [ ] Round-trip input latency feels responsive in practice (informal playtest — no formal benchmark required, but should not feel "floaty" or delayed).

---

## 9. Open Items for the Dev Team to Decide During Implementation

These were intentionally left flexible since they don't affect the architecture:

- Exact QR-code generation library choice (any lightweight, actively-maintained option is fine).
- Exact QR-scanning library choice on Android (ZXing or CameraX-based, either is fine).
- Visual design/theming of both the controller UI and the receiver status window (functional clarity matters more than visual polish for this project).
- Whether the receiver status window uses WinForms or WPF (either is acceptable; pick whichever the team is faster with).
- Specific structure of unit/integration tests, if any are written — not mandated by this spec, left to team discretion given this is a hobby project.
