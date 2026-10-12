# GMK87 Control

Unofficial Windows app for the Zuoya GMK87 keyboard: lighting, keymap, screen images and device info.
WinUI 3 / .NET 10. Talks to the keyboard over USB HID (cable mode, VID `0x320F` PID `0x5055`).

> Not affiliated with or endorsed by Zuoya. Use at your own risk. The app never flashes firmware.

## Features

| Page | What it does | Writes to keyboard? |
|---|---|---|
| Device | Connection, firmware version and CRC, VIA protocol, HID interface list | No |
| Lighting | Effect, brightness, speed, direction, colour or rainbow, logo light bar, Win lock, clock sync, animated preview | Only on "Write to keyboard" |
| Keymap | All 4 layers on the real layout, single-key remap, JSON backup and restore | Only on "Write key" / "Restore" (with confirmation) |
| Screen | PNG/JPG/BMP/GIF into the two 240x135 slots, frame delay, which slot is shown | Only on "Upload" / "Switch" (with confirmation) |

## Supported firmware

Detected automatically from the VIA protocol version the keyboard reports.

| Firmware | VIA protocol | Keycodes | Backlight |
|---|---|---|---|
| V0104 | 9 (V2) | legacy (MO = `0x51xx`, vendor keys `0x5F80`) | vendor `0xFF1C` settings block |
| V0112 | 11 (V3) | modern (MO = `0x5220`, vendor keys `0x7F00`) | VIA channel 3 for effect/brightness/speed; vendor block for colour, rainbow, direction |

Keymap backups record the protocol and won't restore across numberings.

## Download

Grab the zip for your machine (`win-x64` for most PCs, `win-arm64` for ARM laptops) from
[Releases](https://github.com/SrilalS/GMK87Control/releases/latest), extract it anywhere and run `GMK87Control.exe`.
No install and no .NET runtime needed. Requires Windows 10 1809 or later.

Builds of the latest `main` are attached to each run on the [Actions](https://github.com/SrilalS/GMK87Control/actions) tab.

## Build and run

Requires Windows 10 1809+ and the .NET 10 SDK.

```bash
dotnet build src/Gmk87.App/Gmk87.App.csproj -p:Platform=x64
```

Output: `src/Gmk87.App/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/GMK87Control.exe` (unpackaged, Windows App SDK self-contained).

```bash
dotnet test
```

## Project layout

- `src/Gmk87.Core`: HID I/O, protocols (VIA `0xFF60`, vendor settings `0xFF1C`, read-only firmware info `0xFFEF`), models, KLE layout parser, effect simulator, RGB565 imaging. No UI dependencies.
- `src/Gmk87.App`: WinUI 3 pages and view models. `Services/KeyboardService` serializes all device access.
- `tests/Gmk87.Core.Tests`: protocol and model tests against fake HID channels and data captured from a real keyboard.

## Things to know

- Close VIA and the vendor app first. They share the same HID interfaces and can steal each other's replies.
- Colour, the rainbow flag and direction go through the vendor `0xFF1C` block. (V0112 also accepts a VIA hue, but it's hidden while rainbow is on.)
- Reading the `0xFF1C` settings block sends INIT/COMMIT, exactly as the vendor app does.
- On V0112 the settings block is 64 bytes, and byte `0x31` is the VIA protocol the keyboard reports. Tools that write only 48 bytes (older versions of this app, gmk87-node) leave it at `0xFF`, which turns off VIA encoder support. This app writes all 64 bytes and resets a damaged value to 11 on the next lighting or screen write.
- Don't use VIA's "load saved layout" on V0112: its bulk keymap command (`0x13`) is really a single-key write in this firmware.
- Screen uploads always rewrite both image slots; images can't be read back.
- The Fn key is `0x5F10` on both firmwares. On V0112 the `MO(1)` code (`0x5221`) does not work as Fn.
- The lighting preview is an approximation; the firmware's real animations have not been decoded.
- Screen upload has not yet been tested on V0112.

## Credits

- Vendor settings (`0xFF1C`) and screen upload protocol: [gmk87-node](https://github.com/codedgar/gmk87-node) and the Python reference by Jochen Eisinger.
- Keycode numbering checked against [VIA](https://github.com/the-via/app)'s protocol tables.

## License

[MIT](LICENSE).
