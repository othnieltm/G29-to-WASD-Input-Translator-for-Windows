# G29 to WASD Translator

A Windows 10/11 desktop application that reads a Logitech G29 racing wheel through DirectInput and emulates held W, A, S, and D keyboard inputs.

## Requirements

- Windows 10 or Windows 11, x64
- .NET 8 Desktop Runtime
- Logitech G29 connected over USB
- A keyboard-compatible game or application

Logitech G HUB is not required for DirectInput wheel data. The application is not a game overlay and sends normal Windows keyboard events.

For elevated or administrator-run games, launch G29WASDTranslator through **Run as administrator** or accept the elevation prompt. Windows blocks keyboard input injection when an application is running with higher integrity privileges than the sender.

## Run

The ready-to-run executable is published at:

`G29WASDTranslator/bin/Release/net8.0-windows/win-x64/publish/G29WASDTranslator.exe`

Alternatively:

```powershell
dotnet run --project G29WASDTranslator/G29WASDTranslator.csproj
```

## Build

Open `G29WASDTranslator.sln` in Visual Studio 2022 or build from PowerShell:

```powershell
dotnet build G29WASDTranslator.sln -c Release
dotnet publish G29WASDTranslator/G29WASDTranslator.csproj -c Release -r win-x64 --self-contained false
```

## Usage

1. Connect the G29 before or after launching the application.
2. Confirm that the connection indicator turns green.
3. Adjust axis assignments if the live axis values do not match the wheel:
   - Steering: wheel rotation axis
   - Accelerator: throttle pedal axis
   - Brake: brake pedal axis
4. Center the wheel and click **Set steering center**.
5. Click **Calibrate pedal ranges**, then release and fully press both pedals repeatedly during the 8-second capture.
6. If a pedal moves in the opposite direction, enable its **Invert** checkbox.
7. Adjust deadzone, activation thresholds, and axis inversion options.
8. Click **Start Translation**.

G29 pedal defaults are accelerator Y with inversion enabled and brake RotationZ with inversion enabled. Existing pedal settings migrate once to these defaults.

Default mappings:

| Wheel input | Keyboard |
|---|---|
| Accelerator | W held |
| Brake | S held |
| Steering left | A held |
| Steering right | D held |

The application polls at 200 Hz and sends key-down events only once per held key. Keys are released on neutral input, Stop, disconnect, window close, and application exit.

## Settings and logs

Settings are stored at:

`%LOCALAPPDATA%\G29WASDTranslator\settings.json`

Runtime diagnostics are written to:

`%LOCALAPPDATA%\G29WASDTranslator\translator.log`

The tray menu can restore or exit the application. Startup registration is optional and stored in the current Windows user registry.
