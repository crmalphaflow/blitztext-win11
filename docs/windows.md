# Blitztext Windows

This repository now contains a first Windows 11 port in `BlitztextWindows/`.

The original app is a native macOS SwiftUI/AppKit menu-bar app. The Windows
version is a separate WPF tray app because the macOS implementation depends on
Apple-only APIs such as AppKit, AVFoundation, Keychain, Accessibility events,
ServiceManagement, WhisperKit, and CoreML.

## Requirements

- Windows 11
- .NET SDK 10
- An OpenAI API key
- A working microphone

## Build

```powershell
dotnet build .\BlitztextWindows\BlitztextWindows.csproj
```

## Run

```powershell
dotnet run --project .\BlitztextWindows\BlitztextWindows.csproj
```

The app stores the OpenAI API key encrypted with Windows DPAPI for the current
Windows user in `%APPDATA%\Blitztext\openai.key`.

Workflow settings, custom prompts, Razer hotkeys, sound preferences, and
per-mode paste behavior are stored in `%APPDATA%\Blitztext\settings.json`.
Text shortcut contents are stored in the same settings file, but encrypted with
Windows DPAPI for the current Windows user.

## Hotkeys

- Razer side button 1 mapped to `NumPad1` or `F13`: start/stop direct transcription
- Razer side button 2 mapped to `NumPad2` or `F14`: start/stop professional email creation
- Razer side button 3 mapped to `NumPad3` or `F15`: start/stop social media post creation
- `Ctrl+Shift+Space`: start/stop transcription
- `Ctrl+Alt+Space`: start/stop professional email creation
- `Ctrl+Shift+D`: start/stop social media post creation
- `Escape`: cancel recording while the Blitztext window is focused

In Razer Synapse, assign the three left-side mouse buttons to `NumPad1`,
`NumPad2`, `NumPad3` or, preferably, `F13`, `F14`, `F15`. The app intentionally
does not capture the normal top-row `1`, `2`, `3` keys globally because that
would break regular typing in other apps.

You can also use the in-app learning buttons:

1. Open Blitztext.
2. Click `Lernen` next to the workflow.
3. Press the matching Razer mouse button.

If the mouse button sends a keyboard key, Blitztext saves it in
`%APPDATA%\Blitztext\settings.json` and registers it globally. If the button
does not get detected, configure that Razer button in Synapse to send a keyboard
key such as `F13`, `F14`, or `F15`, then run the learning step again.

## Current scope

Implemented:

- Windows tray app
- Tray menu for starting the three workflows without opening the main window
- Ghost mode with a small topmost microphone overlay and a learnable extra hotkey
- Three encrypted text shortcuts for inserting email addresses, addresses,
  passwords, or other reusable snippets via mouse/keyboard hotkeys
- Microphone recording via NAudio
- OpenAI `gpt-4o-mini-transcribe` transcription with automatic `whisper-1` fallback
- OpenAI `gpt-4o-mini` rewrite workflows for professional emails and social posts
- Configurable workflow names, prompts, language, temperature, and per-mode auto-paste
- Raw transcript and final result tabs
- Visual recording/processing/success/error status colors
- Optional sounds for start, done, and error states
- Optional Windows startup registration
- Optional startup directly into Ghost mode
- Friendlier API, microphone, network, and model-access error messages
- Clipboard copy and optional automatic `Ctrl+V` paste
- Encrypted API key storage for the current Windows user

Not ported yet:

- Local WhisperKit/CoreML mode, because those dependencies are Apple-specific
- The macOS popover/menu-bar UI
- Custom vocabulary presets beyond the editable prompt fields
