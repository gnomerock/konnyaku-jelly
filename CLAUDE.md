# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

**Konnyaku Jelly** (蒟蒻ゼリー) is a Dalamud plugin for FFXIV that translates in-game chat between English and Japanese using ChatGPT (via a Codex CLI login), the OpenAI API, Claude or Gemini. It is a single C# project, `Konyakku/` (`Dalamud.NET.Sdk`, .NET 10). There are no tests and no linter.

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

- The display name (`Name` in `Konyakku/Konyakku.json` and Gnomod's `repo.json`, window titles, chat prefixes) is "Konnyaku Jelly".
- `InternalName`, the assembly, namespaces, the `Konyakku/` folder keep the old spelling on purpose. The GitHub repo is `gnomerock/konnyaku-jelly` (renamed from `konyakku`).
- **Never change `InternalName`.** Dalamud would treat the plugin as new, and existing installs would lose their settings and updates.
- ImGui window IDs (`###KonyakkuLog`, `###KonyakkuConfig`) must also stay stable, or saved window positions are lost.

## Architecture

- **`Plugin.cs`**: the entry point.
  - Registers `/jelly` (no args toggles the window; `/jelly <text>` translates and copies; `/jelly /p <text>` translates and sends).
  - Hooks `ChatGui.ChatMessage` and owns the in-memory log (`LogEntry`, under `logLock`).
  - Incoming chat is filtered in this order: `Enabled` → selected `Channels` → `TranslateOnlyWhenOpen` (skipped while the log window is closed) → own messages → `Language.IsAlreadyIn`, a cheap kana/ASCII check that avoids API calls. Only then does `TranslationService` run.
- **`Translation/TranslationService.cs`**: the only path to the AI.
  - Builds the shared system prompt (`Instructions`) and appends the user's `CustomPrompt`.
  - Wraps text in `<message>` tags, limits concurrency to 3 (`slots`), and caches results under a key of provider + custom prompt + target + text.
  - The model replies with `SameMarker` when the text is already in the target language, and that is cached as an empty result.
  - Exposes `LastError` for the UI.
- **`Translation/Glossary.cs`**: the shared glossary plus the player's `Config.MyTerms`.
  - The source of truth is the D1 `glossary` table, served by the Worker (`Glossary.Server` + `/v1/glossary`). `glossary.json` at the repo root is only the fallback embedded in the DLL. Refresh it with `npm run export` in `worker/` before a release, and don't edit it by hand.
  - At startup the newer of the built-in copy and `glossary-cache.json` is loaded, then a fresh copy is downloaded in the background and kept if its `version` is higher.
  - `ShareAsync` posts collected words to `/v1/terms` in batches of 50, with `Config.InstallId` (made on first share). Call it from the draw thread, because it may save the config before its first await.
  - `BlockFor(text)` returns a `<glossary>` block with only the terms found in the message (longest first, at most 15). It's prepended to the input, not the system prompt, and is part of the cache key.
- **`Translation/TermCollector.cs`**: the opt-in word list (`Config.CollectTerms`, off by default).
  - Callers pass `Received` (public channel? + sender) for chat from other players. Only then, for Japanese text, does `TranslationService` append `TermInstructions` and split the `term = meaning` lines after `<<TERMS>>` from the reply.
  - Filters: public channels only, the term must be in the original, contain Japanese and no Latin letters or digits (character names are Latin-only), and the meaning must not mention the sender's name.
  - Saved to `collected-terms.json` in the plugin config folder. Words leave the PC only through the Words tab's Share preview, and are then marked `Shared`. Outgoing, Paste and Test connection never pass `Received`.
- **`Translation/Providers.cs`**: one `ITranslationProvider.CompleteAsync(instructions, input)` per backend.
  - ChatGPT subscription: raw SSE against `chatgpt.com/backend-api/codex/responses` with tokens from `CodexAuth`.
  - OpenAI: chat completions over `HttpClient`.
  - Claude: the official `Anthropic` NuGet SDK, effort `Low`, with a check for `refusal` stop reasons.
  - Gemini: REST over `HttpClient`.
  - Adding a provider means adding a `ProviderKind`, a model list in `ProviderModels`, the provider class, a case in `TranslationService`'s switch, and a radio button plus fields in `ConfigWindow.DrawProvider`.
- **`Translation/CodexAuth.cs`**: reads and refreshes `~/.codex/auth.json` and writes the rotated refresh token back, so the user's Codex CLI keeps working.
- **`Configuration.cs`**: persisted settings.
  - `Normalize()` runs on every load and doubles as the migration step. It resets models that are no longer offered to the cheapest one (the first entry in each `ProviderModels` list), splits a legacy free-text `SendPrefix` such as `/t Name@World` into the dropdown channel plus `TellTarget`, and runs `Version`-gated one-time migrations (v3 added `DefaultCustomPrompt`).
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
- **`Windows/`**:
  - `LogWindow` is "the jelly window". A collapsible sidebar (`NavItem` rows, eased width, `Config.SidebarCollapsed`) switches between the Chat page (log plus composer) and the Paste page, and holds Pause/Resume, Clear log and Settings. The unread dot and auto-scroll compare against `Plugin.LogVersion`, because the log's length stops changing once it's full.
  - `EdgeTab` is not a `Window`. It's drawn every frame from `Plugin.DrawUi`: an always-visible vertical "Jelly" tab at `EdgeTab.Top()`, which toggles `LogWindow` (`Config.ShowEdgeTab`). It's dragged by hand (the ImGui window is `NoMove`): dropped within `SnapDistance` of the left or right screen edge it sticks there (`Config.TabEdge`, `TabY` as a fraction of the screen height). Dropped anywhere else, it previews and then opens `LogWindow` as a normal window at that spot (`LogWindow.OpenAt`, `Config.Detached`); the tab itself always stays on an edge. While dragged, the shape trails the grab point on a damped spring (`Wobble`/`Jelly`), drawn as a bent polygon with the clip rect widened to the screen. While `EdgeTab.Docked()` (`DockToEdge` on and not `Detached`), `LogWindow.PreDraw` pins the window to that edge at the same height as a drawer (`NoMove`, slides in on `OnOpen`). The tab hides while a docked or detached window is open. `LogWindow.TrackDocking` docks a detached window again when it's dragged with the mouse at a screen edge.
  - `ChatBoxButton`, also drawn from `Plugin.DrawUi`, is a translate button just right of the game's chat input. `ChatLog` has no named field for the input, so it's found by component type (`ComponentType.TextInput`) and positioned from its node's screen coordinates. A click reads `RawString`, keeps a leading channel command (`SplitCommand`), translates into `OutgoingTarget` and writes the result back with `SetText` on the framework thread, unless the input changed meanwhile (then it's copied instead). `Config.ShowChatBoxButton`.
  - `ConfigWindow` has the Translator, Languages, Chat, Words and Look tabs.
  - `Theme` holds the blue/white/red/yellow palette and shared widgets: `PrimaryButton`, `Badge`, and `Tooltip`, which also works on disabled items.
  - Dalamud's default font has no Thai, so `Plugin.ChatFont` merges Leelawadee UI or Tahoma from the Windows font folder into it. `LogWindow` and the Words table push it. Thai (`Language.Incoming` only) is never echoed into game chat, because the game's font can't draw it.
  - Translations finish on background tasks while the UI reads them from the draw loop. Clipboard writes are queued through `Plugin.CopyToClipboard` and applied in `DrawUi`, because ImGui calls must happen on the draw thread.

## Glossary Worker

- `worker/` is a Cloudflare Worker (plain JS, wrangler) in front of the D1 database: `POST /v1/terms` takes shared words, `GET /v1/glossary` serves the reviewed glossary in the same format as `glossary.json`. Deploy and review steps are in `worker/README.md`.
- The plugin must never get a Cloudflare token or talk to D1 directly. Everything goes through the Worker.
- `clean()` in `worker/src/index.js` repeats `TermCollector.Acceptable`'s rules. Change both together.
- Don't store IPs or raw install IDs. `reporter` is a SHA-256 of `SALT` + install ID.

## Docs

- `README.md` is the user tutorial, with developer notes at the end.
- `docs/index.html` is a self-contained GitHub Pages tutorial (https://gnomerock.github.io/konnyaku-jelly/) served from `main:/docs`. Its in-game windows are HTML/CSS mock-ups, not screenshots.
- When user-visible behaviour changes (defaults, commands, settings labels), update both.
