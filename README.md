# Konnyaku Jelly

Dalamud plugin for FFXIV that translates chat with ChatGPT, Claude or Gemini.

- **Incoming**: translates chat (Say / Party / FC / Tell / LS / CWLS …) into your incoming language (default: English) and shows it in the Konnyaku Jelly window.
- **Outgoing**: type in the box at the bottom of the window → translated to your outgoing language (default: Japanese) → Send (to the channel picked in the **To** dropdown: Party, Say, FC, Tell, LS, CWLS …) or Copy.
- **Saves tokens**: by default chat is only translated while the Konnyaku Jelly window is open (`/jelly`). Turn this off in Settings to translate all the time.
- **Paste**: the Paste tab in the Konnyaku Jelly window translates any pasted Japanese text into English, with a button to copy the result.
- Languages: English and Japanese only for now.

## Provider

| Mode | Uses | Notes |
|---|---|---|
| **ChatGPT subscription** (default) | Login from [Codex CLI](https://github.com/openai/codex) (`~/.codex/auth.json`) | Uses your ChatGPT Plus/Pro quota at no extra cost. Same backend Codex uses; not an official API for third-party apps, so OpenAI may change or close it. |
| **OpenAI API key** | `https://api.openai.com/v1/chat/completions` | Pay-as-you-go. Official and more stable. |
| **Claude API key** | Anthropic Messages API | Pay-as-you-go. |
| **Gemini API key** | Google Gemini API | Pay-as-you-go. |

Each provider has a fixed list of models in settings, cheapest first; the cheapest is the default (`gpt-6-luna`, `gpt-5-nano`, `claude-haiku-5-5`, `gemini-2.5-flash-lite`). A saved model that is no longer in the list resets to the default.

Settings → Languages has a **Custom prompt** box; its text is added to the built-in FFXIV translation instructions (e.g. slang to keep, tone, names).

Setup for subscription mode:

```
npm i -g @openai/codex
codex login        # choose "Sign in with ChatGPT"
```

The plugin refreshes the token itself and writes it back to `auth.json`, so Codex CLI keeps working (the refresh token changes on every refresh).

## Commands

- `/konyakku` — toggle the translation window · `/konyakku config` — settings
- `/jelly <text>` — translate to your outgoing language and copy it to the clipboard
- `/jelly /p <text>` — translate and send to party right away (`/fc`, `/s`, `/l1`, `/t Name@World` etc. also work)
- Party Finder: right-click a listing (or inside its detail window) → **Translate** echoes the description in English in game chat

## Install (custom repo)

1. In game, type `/xlsettings` → **Experimental** tab
2. Under **Custom Plugin Repositories**, paste:
   ```
   https://raw.githubusercontent.com/gnomerock/konyakku/main/repo.json
   ```
   tick **Enabled**, then click **+** and **Save and Close**
3. `/xlplugins` → **All Plugins** → search for **Konnyaku Jelly** → Install

## Build / dev install

Requires .NET 10 SDK and XIVLauncher with Dalamud installed (references come from `%APPDATA%\XIVLauncher\addon\Hooks\dev`).

```
dotnet build Konyakku/Konyakku.csproj -c Release
```

In game: `/xlsettings` → Experimental → Dev Plugin Locations → add
`D:\repo\konyaku\Konyakku\bin\Release\Konyakku.dll`, then enable it in `/xlplugins` → Dev Tools.

## Releasing

Push a version tag (4 parts, matching the assembly version):

```
git tag v0.2.0.0
git push origin v0.2.0.0
```

GitHub Actions builds the plugin, creates the release with `latest.zip`, and updates `repo.json` on `main`, so players get the update in `/xlplugins`.

## Notes

- Default channels skip Shout/Yell (busy cities would burn your quota). You can turn them on in settings.
- Translations are cached and at most 3 run at once.
