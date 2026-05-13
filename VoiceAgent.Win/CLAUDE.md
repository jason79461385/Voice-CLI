# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Windows-only voice-to-text assistant. Avalonia 11 desktop app on .NET 8 (`<OutputType>WinExe</OutputType>`, `AssemblyName=VoiceAssistant`). The repo root is one level up (`..`); the `.csproj` and all source live in this `VoiceAgent.Win/` directory.

## Build & run

```
dotnet build
dotnet run                           # GUI mode (no args)
dotnet run -- -help                  # CLI command mode
dotnet run -- -calibrate             # auto-tune mic noise threshold
dotnet run -- -key "C:\path\to\google.json"
dotnet run -- -addword <word> <replacement>
```

There are no tests, no linter config, and no CI in the repo.

`app.manifest` declares `requireAdministrator`, so launching always triggers UAC. `Program.Main` also re-launches itself elevated if it detects it isn't admin — when iterating, expect the dev process to spawn a child and exit. For tight inner loops, run the built `.exe` directly from an already-elevated terminal.

## Runtime architecture

Two entry modes share a single executable, gated by `args.Length` in `Program.cs`:

- **CLI mode** (any args): parses subcommands (`-calibrate`, `-threshold`, `-holdtime`, `-key`, `-addword`, `-delword`, `-listwords`, `-autostart`, `-reset`, `-setpath`, `-help`). Uses `AttachConsole(ATTACH_PARENT_PROCESS)` to print to the parent shell, then `Environment.Exit`. State changes are written to JSON and the process exits — never enters Avalonia.
- **GUI mode** (no args): `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`. `App.OnFrameworkInitializationCompleted` shows `WelcomeWindow` on first run (when `voice_agent_config.json` is missing), then loads `MainWindow` as a small overlay-style window with a tray icon.

### Persistent state lives next to the .exe

`Program.AppDir` is `Path.GetDirectoryName(Environment.ProcessPath)` — **not** the cwd. Both files are written to that directory:

- `voice_agent_config.json` — `AppConfig { GoogleKeyPath, Threshold, HoldTime }`. Read by `Program.LoadConfig()`.
- `keywords.json` — `Dictionary<string,string>` for post-STT text replacement. Read by `Program.LoadKeywords()`.

`MainWindow` watches `keywords.json` via `FileSystemWatcher` and hot-reloads `_keywordsCache` on change, so CLI edits via `-addword`/`-delword` take effect in a running GUI without restart.

### Voice pipeline (the big picture)

The whole interaction is driven by a global hotkey thread, an audio capture thread, and an HTTP correction service, all coordinated through `MainWindow`:

1. **Trigger** — `MainWindow` registers a `SharpHook.TaskPoolGlobalHook`. `OnKeyPressed` implements a "double-Ctrl with no interference" gesture: it tracks `_lastCtrlPressTime` and a `_hasOtherKeyInterfered` flag that any non-Ctrl keypress sets to `true`. A second Ctrl within 500ms only counts if no other key was pressed in between (this is what prevents Ctrl+C / Ctrl+V from triggering it). The lock + 50ms debounce + 800ms toggle cooldown are all load-bearing — don't simplify them.
2. **Capture target window first** — before showing the overlay, it stashes `_targetWindowHandle = NativeInputInjector.GetForegroundWindow()`. Avalonia's window will steal focus once shown, so the original target must be remembered now.
3. **Streaming STT** — `CrossPlatformSpeechService.StartListeningAsync` opens a Google Cloud Speech V1 `StreamingRecognize` call (zh-TW, LINEAR16, 16kHz, automatic punctuation, interim results) and a `PvRecorder` (frame=512, default device). Each captured frame is RMS-checked against `NoiseThreshold`; `_lastVoiceTime` advances only when the frame's average abs-amplitude exceeds threshold. When the silence gap exceeds `HoldTimeSeconds`, `_isListening` flips false and the loop exits.
4. **Correction** — `OnFinalResult` calls `WindowsInputInjector.ExtractCurrentTextAsync()` to grab the target field's existing text (Ctrl+A, Ctrl+C, Right-arrow to deselect, read clipboard), then POSTs `{ text: context, command: transcript }` to the hardcoded endpoint `http://140.115.54.55:1228/error_correction/` (`ErrorCorrectionService`). Response shape is `CorrectionResponse { status, type, result_text, command_type }`. `type == "command"` triggers a select-all-then-paste (full replacement) instead of a plain paste.
5. **Inject** — clipboard is saved → set to corrected text → `NativeInputInjector.ForceForegroundWindow(_targetWindowHandle)` (uses `AttachThreadInput`/`BringWindowToTop`/`SetForegroundWindow` to defeat Windows' focus-stealing protection) → 200ms delay → `SendInput` Ctrl+V (or Ctrl+A then Ctrl+V for commands) → 200ms delay → original clipboard restored. After-effects also run keyword post-replacement from `_keywordsCache`.

### Two input injectors — they are not interchangeable

- `NativeInputInjector` — `SendInput` with full `INPUT` struct. Used for the **output** path (paste into target). Required because Chrome and other UIPI-protected apps reject `keybd_event`. The struct layout and `INPUT.Size` (40 bytes on x64) are deliberate; the `[Debug]` log of `SendInput` return values exists to detect when Windows silently drops the input (admin elevation is what makes this work).
- `WindowsInputInjector` — legacy `keybd_event`. Only used for `ExtractCurrentTextAsync` (reading source text via clipboard). Don't migrate one to the other casually; they coexist on purpose.

### Threading

- `SharpHook` callback fires on its own thread → all UI mutations must go through `Dispatcher.UIThread.Post/InvokeAsync`.
- `_speechService.IsProcessing` acts as a re-entrancy guard: hotkey is ignored while a result is being processed, and the silence-detection loop is also suspended during it (so the user's still-mic isn't interpreted as "done speaking" while we're injecting).
- `_keyLock` serializes the hotkey FSM. The `_lastCtrlPressTime = DateTime.MinValue` reset after a successful double-press is what prevents a third Ctrl from immediately re-triggering.

## Conventions specific to this code

- Comments and string literals are mostly Traditional Chinese (zh-TW). Preserve them when editing.
- Many `catch { }` blocks swallow errors silently. The intentional logging path is `MainWindow.WriteDebugLog` → `debug_log.txt` in `AppDir`. Crashes from `Program.Main` go to `CRASH_LOG.txt` in the same directory. Prefer extending these over adding new logging schemes.
- The Google STT credential path is set via `Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", ...)` immediately before `SpeechClient.CreateAsync()` — don't move this; the SDK reads it during client construction.
- `MainWindow.PreWarmApiAsync` issues an empty correction request at startup to warm the HTTP connection — the user notices the latency on first real request without it.
