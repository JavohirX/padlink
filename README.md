# Padlink

Two phones, one PC, the same Wi-Fi. Padlink turns an Android phone into a controller for [FCEUX](https://fceux.com/) (NES) or [BlastEm](https://www.retrodev.com/blastem/) (Sega Genesis).

The PC app shows a QR code. The phone scans it, you pick Player 1 or Player 2, and each touch becomes a key press in the emulator. There is no account, no cloud, and no virtual-gamepad driver. The receiver injects keys with Win32 `SendInput`.

NES, Famicom, and Sega Genesis are trademarks of their owners. Padlink is not affiliated with them, with FCEUX, or with BlastEm.

## Pieces

| | NES | Genesis |
|---|---|---|
| Phone app | `android/NesController` | `android/SegaController` |
| Windows receiver | `receiver/NesReceiver` | `receiver/BlastemReceiver` |
| Emulator | FCEUX | BlastEm |
| Port | 8787 | 8788 |

The two receivers can run at the same time. They listen on different ports.

## Play

1. Put the phone and the PC on the same Wi-Fi network. 5 GHz is steadier than 2.4 GHz.
2. Start the receiver that matches the emulator. It shows a QR code for `ws://<pc-lan-ip>:<port>`.
3. Bind the keys in the tables below inside the emulator. Player 1 on Genesis already matches BlastEm's defaults. Set the rest yourself.
4. Install the matching phone app (Android 7 or newer), scan the QR code, and choose a player.
5. Leave the emulator open. If another window is in front, the next button press brings the emulator forward.

A second phone that asks for a role already in use is told to pick the other player. Locking the phone, leaving the app, or dropping Wi-Fi releases that player's keys.

If the receiver reports a bind error on first launch, reserve the URL once from an elevated prompt:

```
netsh http add urlacl url=http://+:8787/ user=Everyone
netsh http add urlacl url=http://+:8788/ user=Everyone
```

If the phone cannot connect, allow inbound TCP in Windows Firewall:

```
netsh advfirewall firewall add rule name="Padlink NES" protocol=TCP dir=in localport=8787 action=allow
netsh advfirewall firewall add rule name="Padlink Genesis" protocol=TCP dir=in localport=8788 action=allow
```

`debug-server.ps1` checks whether anything is listening on 8787, and whether a firewall rule and a URL reservation exist. `debug-ws-client.ps1` sends the same hello handshake the phone sends.

## Keys

### FCEUX

| Button | Player 1 | Player 2 |
|---|---|---|
| Up | W | Numpad 8 |
| Down | S | Numpad 2 |
| Left | A | Numpad 4 |
| Right | D | Numpad 6 |
| B | J | Numpad 1 |
| A | K | Numpad 3 |
| Select | U | Numpad 7 |
| Start | I | Numpad 9 |

### BlastEm

| Button | Player 1 | Player 2 |
|---|---|---|
| Up | Up | Numpad 8 |
| Down | Down | Numpad 2 |
| Left | Left | Numpad 4 |
| Right | Right | Numpad 6 |
| A | A | Numpad 7 |
| B | S | Numpad 9 |
| C | D | Numpad 3 |
| Start | Enter | Numpad 0 |
| X | Q | Numpad 1 |
| Y | W | Numpad 5 |
| Z | E | Numpad . |
| Mode | Backspace | Numpad - |

## Build

### Receivers

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then from the repo root:

```
dotnet publish receiver/NesReceiver/NesReceiver.csproj -c Release
dotnet publish receiver/BlastemReceiver/BlastemReceiver.csproj -c Release
```

Each publish is a self-contained Windows x64 app, so the PC that runs it does not need a separate .NET install. The executables are:

- `receiver/NesReceiver/bin/Release/net8.0-windows/win-x64/publish/PadlinkNes.exe`
- `receiver/BlastemReceiver/bin/Release/net8.0-windows/win-x64/publish/PadlinkGenesis.exe`

### Phone apps

Open `android/NesController` or `android/SegaController` in Android Studio (compile SDK 34), or from that directory:

```
gradlew.bat assembleDebug
```

The first run downloads Gradle 8.4. The debug APK is under `app/build/outputs/apk/debug/`. `local.properties` is machine-specific and is not in the repo. Android Studio writes it when you open the project.

## Protocol

Right after the WebSocket opens, the phone sends one text frame:

```json
{ "type": "hello", "role": "player1" }
```

`role` is `player1` or `player2`. The receiver answers `hello_ack` or `hello_rejected` with reason `role_taken`.

After that, every touch change is one binary frame: a full snapshot of the buttons, not a delta. NES uses 1 byte. Genesis uses 2 bytes, little-endian. Bit positions are documented in `receiver/NesReceiver/KeyboardInput.cs` and `receiver/BlastemReceiver/KeyboardInput.cs`.

`task.md` is the original NES design spec. The Genesis app follows the same connection flow on port 8788.

## License

[MIT](LICENSE). Copyright (c) 2026 Javohir Xatamov.

Third-party libraries used by the apps:

- [QRCoder](https://github.com/codebude/QRCoder) 1.4.3, MIT, for the QR code on the receiver
- [OkHttp](https://square.github.io/okhttp/) 4.12.0, Apache-2.0, for the phone WebSocket
- [ZXing Android Embedded](https://github.com/journeyapps/zxing-android-embedded) 4.3.0, Apache-2.0, for scanning the QR code
