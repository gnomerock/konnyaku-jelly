# Konnyaku Jelly

<img src="Konyakku/images/icon.png" alt="Konnyaku Jelly icon" width="96" align="right">

Dalamud plugin for FFXIV that translates chat with ChatGPT, Claude or Gemini.

**📖 Illustrated tutorial: https://gnomerock.github.io/konnyaku-jelly/**

- **Read chat**: Party and Alliance chat (plus FC, Tells, LS, CWLS … if you turn them on) is translated into your language (default: English) and shown in the Konnyaku Jelly window.
- **Write chat**: type in your own language, pick a channel from the **To** dropdown, and it goes out translated (default: Japanese).
- **Paste**: translate any Japanese text you copied, e.g. from a website or a Discord message.
- **Party Finder**: right-click a listing → **Translate**.
- **Saves tokens**: by default chat is only translated while the window is open.
- Languages: English and Japanese for now.

---

## Tutorial

### 1. Install

1. In game, type `/xlsettings` → **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste:
   ```
   https://raw.githubusercontent.com/gnomerock/Gnomod/main/repo.json
   ```
   tick **Enabled**, click **+**, then **Save and Close**.
3. Type `/xlplugins` → **All Plugins** → search for **Konnyaku Jelly** → **Install**.

### 2. Choose who translates

Open the settings with `/konyakku config` (or **Settings** in the window's sidebar). On the **Translator** tab, pick one:

| Option | What you need | Cost |
|---|---|---|
| **ChatGPT subscription** (default) | A ChatGPT Plus/Pro plan and a [Codex CLI](https://github.com/openai/codex) login | No extra cost, uses your plan's quota |
| **OpenAI API key** | A key from platform.openai.com | Pay per use |
| **Claude API key** | A key from console.anthropic.com | Pay per use |
| **Gemini API key** | A key from Google AI Studio | Pay per use |

For the **ChatGPT subscription**, run this once in a terminal (needs [Node.js](https://nodejs.org)):

```
npm i -g @openai/codex
codex login        # choose "Sign in with ChatGPT"
```

The settings window shows **"ChatGPT login found"** when it's ready.

For an **API key**, paste it into the **API key** box. Models are listed cheapest first, and the cheapest is the default, which is plenty for chat.

Click **Test connection**. You should see `It works! → ありがとう…`.

### 3. Open the window

Type `/jelly` (or `/konyakku`). Keep this window open while you play: incoming chat is only translated while it's open, so you don't pay for chat you never read. You can turn that off in **Settings → Chat**.

**Quick toggle:** move your mouse to the **left edge of the screen**. A small **Jelly** tab slides out; click it to show or hide the window. Its edge is yellow while the window is open. You can turn the tab off in **Settings → Look**.

The **sidebar** on the left of the window has:

- **Chat** and **Paste**, the two pages. A red dot on Chat means new translations arrived while you were on Paste.
- **Translating / Paused** at the bottom, to pause and resume chat translation.
- **Clear log** and **Settings**.
- The **«** button at the top collapses the sidebar to icons only, to save space. Click **»** to expand it again.

### 4. Read chat

Messages from the channels you picked show up on the **Chat** page, translated, with a coloured channel badge, the sender and the time. The original text is shown underneath in grey.

- **Right-click** a line to copy the translation.
- Messages already in your language are skipped, so they cost nothing.

### 5. Write chat

At the bottom of the **Chat** page:

1. Pick a channel in the **To** dropdown: Party, Say, Alliance, Free Company, Tell, Novice Network, Shout, Yell, LS1–8, CWLS1–8. The right side shows which language it will be sent in.
2. For **Tell**, also type who it's for, as `Name@World`.
3. Type your message in your own language and press **Enter** or **Send**. It's translated and sent.
4. Click **Copy** instead to translate it and copy it to the clipboard without sending, so you can check it first or paste it somewhere else.

### 6. Translate pasted text

Click **Paste** in the sidebar, paste Japanese text (Ctrl+V or the **Paste** button) and click **Translate to English**. **Copy** puts the result on your clipboard.

### 7. Party Finder

Right-click a listing in the Party Finder (or inside its details window) → **Translate**. The description is shown in English in your game chat, marked `[KY PF]`, and in the Konnyaku Jelly log.

### 8. Commands

| Command | What it does |
|---|---|
| `/jelly` | Open the Konnyaku Jelly window |
| `/jelly <text>` | Translate into your outgoing language and copy it to the clipboard |
| `/jelly /p <text>` | Translate and send to party right away. `/s`, `/fc`, `/a`, `/l1`, `/cwl1`, `/t Name@World` … also work |
| `/konyakku` | Toggle the window |
| `/konyakku config` | Open settings |

These work well in macros, e.g. `/jelly /p Thanks for the run!`.

### 9. Settings

- **Translator**: provider, model and key, and **Test connection**.
- **Languages**:
  - **Chat I read** and **Messages I send**.
  - **Custom prompt**: extra instructions added to the built-in FFXIV rules. It starts as `Used in FFXIV online communication and raiding contexts.` You can add your own, e.g. `Keep 'gg' and 'o/' as-is. Use casual Japanese.`
- **Chat**:
  - Only translate while the window is open.
  - Skip your own messages.
  - Show the original text.
  - Echo translations into game chat (`[KY]`).
  - Which channels to translate. Only Party (including cross-world party) and Alliance are on by default. Tick FC, Tells, LS and the others if you want them. Be careful with Shout and Yell: busy cities burn through your quota.
- **Look**: the blue & white theme (turn it off to use your Dalamud style) and how many lines the log keeps.

### Tips and troubleshooting

- **"Something went wrong" in the window**: open Settings → **Test connection** to see the full error. For the ChatGPT subscription, run `codex login` again.
- **Nothing is translated**: check that the checkbox at the top of the window is ticked, that the window is open, and that the channel is ticked in **Settings → Chat**.
- **Keeping costs down**: leave Shout/Yell off, keep "only translate while open" on, and use the cheapest model. Identical messages are cached, so they're only translated once.
- API keys are saved in plain text in the Dalamud plugin config, so don't share your config file.

---

## Developers

### Provider details

| Mode | Uses | Notes |
|---|---|---|
| **ChatGPT subscription** | Login from Codex CLI (`~/.codex/auth.json`) | Same backend Codex uses; not an official API for third-party apps, so OpenAI may change or close it. The plugin refreshes the token itself and writes it back to `auth.json`, so Codex CLI keeps working. |
| **OpenAI API key** | `https://api.openai.com/v1/chat/completions` | Official and more stable. |
| **Claude API key** | Anthropic Messages API | |
| **Gemini API key** | Google Gemini API | |

Each provider has a fixed list of models, cheapest first; the cheapest is the default (`gpt-6-luna`, `gpt-5-nano`, `claude-haiku-5-5`, `gemini-2.5-flash-lite`). A saved model that is no longer in the list resets to the default. Translations are cached and at most 3 run at once.

### Build / dev install

Requires .NET 10 SDK and XIVLauncher with Dalamud installed (references come from `%APPDATA%\XIVLauncher\addon\Hooks\dev`).

```
dotnet build Konyakku/Konyakku.csproj -c Release
```

In game: `/xlsettings` → Experimental → Dev Plugin Locations → add
`D:\repo\konyaku\Konyakku\bin\Release\Konyakku.dll`, then enable it in `/xlplugins` → Dev Tools.

### Releasing

Push a version tag (4 parts, matching the assembly version):

```
git tag v0.4.0.0
git push origin v0.4.0.0
```

GitHub Actions builds the tagged commit, creates the release with `latest.zip`, and updates the Konnyaku Jelly entry in [gnomerock/Gnomod](https://github.com/gnomerock/Gnomod)'s `repo.json`, so the update shows up in `/xlplugins`. This needs a `GNOMOD_TOKEN` secret in this repo that can write to Gnomod.

The tutorial site is `docs/index.html`, served by GitHub Pages from the `docs` folder on `main`.
