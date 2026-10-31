# Keybound Assist

Fills in the enemy prompts of the Keybound Brawler duty and stops; the player presses Enter to submit each word.

`/keyboundassist` · Dalamud API 15 · .NET 10

## What it does

- **Types the prompt, not the answer** — the shortest visible prompt is written into the duty's own text field one character at a time, then the plugin stops and waits for your Enter.
- **Never sends a keystroke** — it writes into the activity's text component in process. Your Enter is the only key pressed during a word, so nothing can land in chat.
- **Follows the duty's rules** — it does not write while the activity prohibits typing, and it will not delete or overwrite text you typed yourself.

## How it works

The duty exposes both ends of the loop as game UI, so no OCR and no screen capture are involved:

| Element | Source |
| --- | --- |
| Prompt words | `KTGEWord` addon — text label (node 20) inside each enemy's component node |
| Answer field | `KTGInput` addon — text input component at node 7 |
| Typing permission | `KTGInput` `AtkValues[2]` (allowed) and `AtkValues[4]` (lock seconds), plus the component's own `Enabled` flag |
| Typing lock during a cast | covered by the same permission pair |

Each frame the plugin reads the field, picks the next step, and writes at most one character (`SetText`), so the field itself is always the authority on what has been typed. It only activates the field (`Focus` + `SetFocusNode`) when it is not already the active text input, so your Enter always reaches it. Outside territory 1359 it does nothing. All reads resolve their addon pointers on the frame that uses them; nothing is cached across frames.

## Commands

| Command | Effect |
| --- | --- |
| `/keyboundassist` | Open the window |
| `/keyboundassist on` / `off` | Start or stop filling prompts |
| `/keyboundassist status` | Print what the plugin currently sees, in chat and the log |

## Settings

- **Fill prompts for me** — the master switch; it is on when the plugin first loads.
- **Delay per character** — 0–300 ms between characters, default 40 ms. The duty scores per submitted word, so the delay is only there to keep the field readable; 0 is one character per frame.

The plugin does not persist anything else. It is only active inside the duty.

## Build

Requires the .NET 10 SDK and the Dalamud dev distribution
(`%APPDATA%\XIVLauncher\addon\Hooks\dev`, or `DALAMUD_HOME`).

```
dotnet build KeyboundAssist.csproj -c Debug      # load bin/Debug/KeyboundAssist.dll as a dev plugin
dotnet build KeyboundAssist.csproj -c Release    # zip in bin/Release/KeyboundAssist/latest.zip
```

## Install (dev plugin)

1. `/xlsettings` → Experimental → Dev Plugin Locations → add `bin\Debug\KeyboundAssist.dll`.
2. `/xlplugins` → Dev Tools → enable **KeyboundAssist**.
3. Enter Keybound Brawler; `/keyboundassist` opens the window if it is not already open.

## Tests

The decision logic (`TypingPlan.cs`) has no Dalamud or native dependency, so the harness compiles the same file into a console project and drives it with no game running: shortest-prompt choice, prefix continuation, case and accent handling, text the plugin did not type, a prompt that vanishes mid-word, and the typing lock.

```
dotnet run --project tests/KeyboundAssist.Tests.csproj -c Release    # 12 checks
```

## Limits

- **Not verified in game.** The offline checks cover the decision logic only. The native reads and the write path come from two independent live-validated plugins (XivType and Keybound Pilot) but have not been exercised on this client yet; the first real run is the test.
- **If the duty keeps the submitted word in the field**, the plugin waits rather than deleting it — clear the field yourself (Backspace) and it resumes. This is why it never clears text silently.
- English prompt labels only (node 20). The Japanese client shows the hiragana reading at node 22 and is not read.
- Words hidden by Cryptic Communications are not read: the label is not visible, so the prompt is not a candidate.
- The plugin does not submit. Every word costs you one Enter.
- Territory ids and addon node ids are taken from the 7.x client; a patch that renumbers them makes the plugin wait rather than misbehave, and `/keyboundassist status` says which part is missing.

## Layout

```
Plugin.cs          entry point, command, per-frame tick, dispose
GameState.cs       KTGInput / KTGEWord reads, permission, focus
TypingPlan.cs      pure decision logic (compiled by the test harness)
Configuration.cs   enabled flag and character delay
AssistWindow.cs    the window
tests/             offline harness for TypingPlan.cs
```
