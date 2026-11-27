# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

**Konyaku Jelly** (蒟蒻ゼリー) is a Dalamud plugin for FFXIV that translates in-game chat between English and Japanese using Google Translate's free web endpoint (no key, no login). It is a single C# project, `Konyakku/` (`Dalamud.NET.Sdk`, .NET 10). There are no tests and no linter.

## Commands

```
dotnet build Konyakku/Konyakku.csproj -c Release   # or `dotnet build -c Release` inside Konyakku/
```

- Building needs XIVLauncher/Dalamud installed locally. References resolve from `%APPDATA%\XIVLauncher\addon\Hooks\dev`; CI downloads Dalamud into `DALAMUD_HOME` instead.
- A Release build runs DalamudPackager, which writes `Konyakku/bin/Release/Konyakku/latest.zip` and the generated manifest `Konyakku.json`.
- To try it in game, add `Konyakku\bin\Release\Konyakku.dll` under `/xlsettings` → Experimental → Dev Plugin Locations.

## Releasing

- Bump `<Version>` in `Konyakku/Konyakku.csproj`, commit and push to `main`, then push a 4-part tag such as `git tag v0.4.3.0 && git push origin v0.4.3.0`.
- `.github/workflows/release.yml` builds the tagged commit with `-p:Version=<tag>` and creates the GitHub release with `latest.zip`.
- The plugin list players install from is not in this repo. It's `repo.json` in **gnomerock/Gnomod** (https://raw.githubusercontent.com/gnomerock/Gnomod/main/repo.json), shared by all of gnomerock's plugins. Locally it's checked out at `D:\repo\Gnomod`.
- After releasing, the workflow checks out Gnomod with the `GNOMOD_TOKEN` secret and updates only the entry whose `InternalName` matches `PLUGIN`. It sets the version, download links, API level and `LastUpdate`, so don't edit those fields by hand. The workflow fails up front if the secret is missing.
- Descriptive fields in Gnomod's entry (`Name`, `Punchline`, `Description`, `Tags`, `IconUrl`) are not synced from `Konyakku/Konyakku.json`. Change both when editing them.

## Naming

- The display name (`Name` in `Konyakku/Konyakku.json` and Gnomod's `repo.json`, window titles, chat prefixes) is "Konyaku Jelly".
- `InternalName`, the assembly, namespaces, the `Konyakku/` folder keep the old spelling on purpose. The GitHub repo is `gnomerock/konnyaku-jelly` (renamed from `konyakku`).
- **Never change `InternalName`.** Dalamud would treat the plugin as new, and existing installs would lose their settings and updates.
- ImGui window IDs (`###KonyakkuLog`, `###KonyakkuConfig`) must also stay stable, or saved window positions are lost.

## Architecture

- **`Plugin.cs`**: the entry point.
  - Registers `/jelly` (no args toggles the window; `/jelly <text>` translates and copies; `/jelly /p <text>` translates and sends).
  - Hooks `ChatGui.ChatMessage` and owns the in-memory log (`LogEntry`, under `logLock`).
  - Incoming text drops the game's auto-translate phrases first (`Language.WithoutAutoTranslate`, also used for cached Party Finder descriptions): every client already shows them in its own language.
  - Incoming chat is filtered in this order: `Enabled` → selected `Channels` → `TranslateOnlyWhenOpen` (skipped while the log window is closed) → own messages → `Language.IsAlreadyIn`, a cheap kana/ASCII check that avoids requests. Only then does `TranslationService` run.
- **`Translation/TranslationService.cs`**: the only path to the translator.
  - POSTs the text (form field `q`, so long Party Finder descriptions stay out of the URL) to `translate.googleapis.com/translate_a/single?client=gtx&dt=t&sl=auto&tl=<code>`. It's unofficial and may be rate-limited; the Cloud Translation API (with a key) is the upgrade path if it gets blocked.
  - `Language.Code` maps the display names (`English`, `Japanese`, `Thai`) to Google's codes.
  - The reply's detected source language (`json[2]`) equal to the target means the text is already in that language: cached as an empty result and returned as null.
  - Limits concurrency to 3 (`slots`), caches results under target code + text, and exposes `LastError` for the UI.
- **`Configuration.cs`**: persisted settings.
  - `Normalize()` runs on every load and doubles as the migration step. It splits a legacy free-text `SendPrefix` such as `/t Name@World` into the dropdown channel plus `TellTarget`, and runs `Version`-gated one-time migrations. Settings from the removed AI providers, custom prompt and word list are simply ignored when an old config loads.
  - When a new default has to reach existing users, bump `Version` and add a migration there. Otherwise, saved values override new defaults.
- **`ChannelNames.cs`**:
  - `Selectable` is the incoming channel list used by settings and log badges.
  - `Outgoing` is the list of `(prefix, name)` pairs behind the **To** dropdown.
  - `Theme.ChannelColor` colours channels by these display names.
- **`ChatSender.cs`**: sends a line through `UIModule.ProcessChatBoxEntry`, as if the player had typed it. It must run on the framework thread (`Framework.RunOnFrameworkThread`) and trims to 500 bytes.
- **`PartyFinderTranslator.cs`**:
  - Caches listing descriptions from `PartyFinderGui.ReceiveListing`.
  - Adds a **Translate** context-menu item in the `LookingForGroup`/`LookingForGroupDetail` addons. When the cache misses, it reads `AgentLookingForGroup.LastViewedListing`.
  - Echoes the result into game chat as `[Jelly PF]`.
- **`DancingMad.cs`**: Dancing Mad (Ultimate, territory 1363) helper, ported from cactbot's `dancing_mad.ts` and Nainaiowo's DMU Helper.
  - Hooks `ActionEffectHandler.Receive` (counts Kefka's `BABE` Path of Light casts) and `PacketDispatcher.HandleActorControlPacket` (headmarkers, category 34). The headmarker you get after the 3rd cast sends `DmuP2Circle`/`DmuP2Cone`. The stop-sign marker is `/mk ignore` (there is no `/mk stop`); `Normalize` rewrites saved `/mk stop` commands.
  - `Framework.Update` sends the P3 mark when you gain First/Second/Third in Line (3004–3006). That also starts the Black Hole clock: `Waves`/`Tethers` (from DMU Helper's timeline and Standard/Fae table) give your current and next tether. It builds the P4 lines from your statuses plus the real/fake tell (status 2056) on Neo Exdeath or Chaos, matched by `BNpcName` ID. Everything resets when combat ends.
  - `Config.DmuEnabled` is the master switch: off, the hooks and `Framework.Update` do nothing and the overlay (preview included) is hidden.
  - Marks go through `QueueMark`, which waits `DmuMarkDelayMs` (or a random time in `DmuDelayMinMs`–`DmuDelayMaxMs` with `DmuHumanize`, all capped at `Configuration.MaxDmuDelayMs`) and sends the `/mk` command via `ChatSender` from `Framework.Update` (`DmuP2Marks`, `DmuP3Marks`). The settings window's Test buttons use it too, so it runs outside DMU.
  - The overlay (`###KonyakkuDmu`) is drawn from `Plugin.DrawUi`: Black Hole lines next to your In Line icon, then one `Callout` tile per P4 debuff copied from DMU Helper's layout (call, status icon from the `Status` sheet framed in the call's colour, timer, instruction on hover). Besides your own debuffs it adds Chaos's Tsunami/Inferno cast (`BB21`/`BB20`, read from `IsCasting`, action icon) and a gaze tile for every other player with Cursed Shriek (5543). Real/fake is kept per (player, status) in `realities`. `DancingMad.Preview` shows sample content; `ConfigWindow.OnClose` turns it off.
- **`Windows/`**:
  - `LogWindow` is "the jelly window". A collapsible sidebar (`NavItem` rows, eased width, `Config.SidebarCollapsed`) switches between the Chat page (log plus composer) and the Paste page, and holds Pause/Resume, Clear log and Settings. The unread dot and auto-scroll compare against `Plugin.LogVersion`, because the log's length stops changing once it's full.
  - `EdgeTab` is not a `Window`. It's drawn every frame from `Plugin.DrawUi`: an always-visible vertical "Jelly" tab at `EdgeTab.Top()`, which toggles `LogWindow` (`Config.ShowEdgeTab`). It's dragged by hand (the ImGui window is `NoMove`): dropped within `SnapDistance` of the left or right screen edge it sticks there (`Config.TabEdge`, `TabY` as a fraction of the screen height). Dropped anywhere else, it previews and then opens `LogWindow` as a normal window at that spot (`LogWindow.OpenAt`, `Config.Detached`); the tab itself always stays on an edge. While dragged, the shape trails the grab point on a damped spring (`Wobble`/`Jelly`), drawn as a bent polygon with the clip rect widened to the screen. While `EdgeTab.Docked()` (`DockToEdge` on and not `Detached`), `LogWindow.PreDraw` pins the window to that edge at the same height as a drawer (`NoMove`, slides in on `OnOpen`). The tab hides while a docked or detached window is open. `LogWindow.TrackDocking` docks a detached window again when it's dragged with the mouse at a screen edge.
  - `ChatBoxButton`, also drawn from `Plugin.DrawUi`, is a translate button just right of the game's chat input. `ChatLog` has no named field for the input, so it's found by component type (`ComponentType.TextInput`) and positioned from its node's screen coordinates. A click reads `RawString`, keeps a leading channel command (`SplitCommand`), translates into `OutgoingTarget` and writes the result back with `SetText` on the framework thread, unless the input changed meanwhile (then it's copied instead). `Config.ShowChatBoxButton`.
  - `ConfigWindow` has the Translator (Test connection), Languages, Chat, Look and DMU tabs.
  - `Theme` holds the blue/white/red/yellow palette and shared widgets: `PrimaryButton`, `Badge`, and `Tooltip`, which also works on disabled items.
  - Dalamud's default font has no Thai, so `Plugin.ChatFont` merges Leelawadee UI or Tahoma from the Windows font folder into it. `LogWindow` pushes it. Thai (`Language.Incoming` only) is never echoed into game chat, because the game's font can't draw it.
  - Translations finish on background tasks while the UI reads them from the draw loop. Clipboard writes are queued through `Plugin.CopyToClipboard` and applied in `DrawUi`, because ImGui calls must happen on the draw thread.

## Glossary Worker (unused)

- `worker/` (a Cloudflare Worker in front of a D1 glossary database) and `glossary.json` are still in the repo, but the plugin no longer uses them: the glossary and word sharing were AI-only features and were removed with the switch to Google Translate. The deployed Worker keeps running until it's taken down separately.

## Docs

- `README.md` is the user tutorial, with developer notes at the end.
- `docs/index.html` is a self-contained GitHub Pages tutorial (https://gnomerock.github.io/konnyaku-jelly/) served from `main:/docs`. Its in-game windows are HTML/CSS mock-ups, not screenshots.
- When user-visible behaviour changes (defaults, commands, settings labels), update both.
