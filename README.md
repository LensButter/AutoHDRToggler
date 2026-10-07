# HDR Toggler

A lightweight Windows system tray utility that lets you toggle HDR on and off **per monitor**.

## The problem

Windows 10 and 11 Xbox Gamebar button combo (Win+Alt+B) only allow toggling HDR globally, if you enable it, it turns on for every display at once. This is frustrating in multi-monitor setups where you have one HDR-capable screen alongside one that is not, or where you simply want HDR active only for specific content on a specific display.

Existing tools like *Quick HDR* only support the primary monitor.

## What HDR Toggler does

- Sits quietly in the system tray
- Detects all HDR-capable monitors on your system
- Lets you toggle HDR independently on each display with a single click
- Can enable HDR on a selected display while configured processes are running
- Left-click tray icon → opens the monitor panel
- Right-click tray icon → opens a compact quick-access menu

## Fork
- automatically enables HDR by selected process

## Requirements

- Windows 10 (version 1803+) or Windows 11
- .NET 10 runtime
- At least one HDR-capable monitor

## Usage

Run `HDRToggler.exe`. The monitor panel opens immediately on launch.

| Action | Result |
|---|---|
| Double-click exe | Opens the monitor panel |
| Left-click tray icon | Opens the monitor panel |
| Click a monitor box | Toggles HDR on that display |
| Right-click tray icon | Opens the quick-access menu |
| Automatic HDR... (quick-access menu) | Opens the main window's Auto-HDR settings |
| Exit (from menu) | Closes the app |

## Automatic HDR

Use the **Automatic HDR** section in the main window to enable the feature, add a rule, or remove a selected rule. Enter an executable name (for example, `game.exe` or `Minecraft.Windows`), select a running process, or use **Browse...** to choose an EXE file. Then select the HDR-capable display. The process list can be updated with **Refresh**. HDR is enabled while at least one configured process with that executable name is running. When the last matching process closes, HDR returns to the state it had before the automatic activation. If multiple rules target the same display, HDR is restored only after all matching processes have closed.

Rules are stored in `%LOCALAPPDATA%\HDRToggler\auto-hdr-rules.json`, and the enabled/disabled setting in `%LOCALAPPDATA%\HDRToggler\auto-hdr-enabled.json`. Processes are checked every two seconds. Closing HDR Toggler also restores any HDR state it changed.

## Stack

- C# / WPF / .NET 10
- Win32 `DisplayConfig` API (`DisplayConfigGetDeviceInfo` / `DisplayConfigSetDeviceInfo`)
