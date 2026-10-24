# Konnyaku Jelly

<img src="Konyakku/images/icon.png" alt="Konnyaku Jelly icon" width="96" align="right">

Dalamud plugin for FFXIV that translates chat with ChatGPT, Claude or Gemini.

**📖 Illustrated tutorial: https://gnomerock.github.io/konnyaku-jelly/**

- **Read chat**: Party and Alliance chat (plus FC, Tells, LS, CWLS … if you turn them on) is translated into your language (default: English) and shown in the Konnyaku Jelly window.
- **Write chat**: type in your own language, pick a channel from the **To** dropdown, and it goes out translated (default: Japanese).
- **Paste**: translate any Japanese text you copied, e.g. from a website or a Discord message.
- **Party Finder**: right-click a listing in the game's Party Finder → **Translate**.
- **Saves tokens**: by default chat is only translated while the window is open.
- Languages: English and Japanese both ways. Chat you read can also be translated into Thai. It shows in the Konnyaku Jelly window, not in game chat, because the game's font has no Thai letters.

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

Open the settings with **Settings** in the window's sidebar. On the **Translator** tab, pick one:

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

Type `/jelly`. Keep this window open while you play: incoming chat is only translated while it's open, so you don't pay for chat you never read. You can turn that off in **Settings → Chat**.

**Jelly tab:** a small vertical **Jelly** tab sits on the **left edge of the screen**. Click it and the window slides out from that edge like a drawer, covering the tab. Close the window (✕) and the tab comes back. Drag the tab (it wobbles like jelly) along the **left or right edge** to move it, or drag it **out onto the screen** and it turns into a normal window where you drop it. Drag that window against a screen edge and let go to dock it as a drawer again. In **Settings → Look** you can turn the tab off, put it back on the left edge, or turn off the drawer.

The **sidebar** on the left of the window has:

- The pages: **Chat** and **Paste**. A red dot on Chat means new translations arrived while you were on another page.
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

**From the game's own chat box:** a small blue translate button sits just right of the game's chat input. Type your message there as usual (a channel command like `/p` or `/t Name@World` at the start is fine), then click the button. Your text is replaced with the translation, and you press **Enter** to send it. You can turn the button off in **Settings → Chat**.

### 6. Translate pasted text

Click **Paste** in the sidebar, paste Japanese text (Ctrl+V or the **Paste** button) and click **Translate to English**. **Copy** puts the result on your clipboard.

### 7. Party Finder

In the game's Party Finder, right-click a listing (or inside its details window) → **Translate**. The description is shown in English in your game chat, marked `[Jelly PF]`, and in the Konnyaku Jelly log.

### 8. Commands

| Command | What it does |
|---|---|
| `/jelly` | Show or hide the Konnyaku Jelly window |
| `/jelly <text>` | Translate into your outgoing language and copy it to the clipboard |
| `/jelly /p <text>` | Translate and send to party right away. `/s`, `/fc`, `/a`, `/l1`, `/cwl1`, `/t Name@World` … also work |

These work well in macros, e.g. `/jelly /p Thanks for the run!`.

### 9. Settings

- **Translator**: provider, model and key, and **Test connection**.
- **Languages**:
  - **Chat I read** (English, Japanese or Thai) and **Messages I send** (English or Japanese).
  - **Custom prompt**: extra instructions added to the built-in FFXIV rules. It starts as `Used in FFXIV online communication and raiding contexts.` You can add your own, e.g. `Keep 'gg' and 'o/' as-is. Use casual Japanese.`
- **Chat**:
  - Only translate while the window is open.
  - Skip your own messages.
  - Show the original text.
  - Echo translations into game chat (`[Jelly]`).
  - Translate button next to the game's chat box.
  - Which channels to translate. Only Party (including cross-world party) and Alliance are on by default. Tick FC, Tells, LS and the others if you want them. Be careful with Shout and Yell: busy cities burn through your quota.
- **Words**:
  - **FFXIV glossary**: each Japanese message is sent to the AI with the meanings of the FFXIV words in it (散開 = spread, 消化 = farm …), so slang translates correctly. The shared list is built in and updated from the Konnyaku Jelly glossary server at startup.
  - **My words**: add your own word and meaning. Yours replaces the shared meaning for the same word.
- **Collecting words** (also on the Words tab): off by default. If you agree, the plugin keeps a list of FFXIV words (散開, 消化, 野良 …) that the AI spots in Japanese chat you receive, with each word's meaning and how often it was seen.
  - Words only come from Say, Shout, Yell, Party, Alliance, Novice Network and Party Finder, never from Tells, FC, linkshells or PvP teams.
  - It never keeps what you type, whole messages, names, worlds, channels or times. Words with Latin letters or digits are dropped, since FFXIV character names only use Latin letters.
  - The list stays on your PC (`collected-terms.json` in the plugin config folder). You can delete single words, clear the list (hold Ctrl), or **Copy list** as JSON. The **+** button moves a collected word into My words, where your meaning is used from then on.
  - **Share** sends the ticked words to the shared glossary, after a preview of exactly what will be sent: each word, its meaning, and a random ID for your install, so the server counts each player once. No messages, names, worlds, channels, times or counts are sent, and the server doesn't keep your IP address. Words several players share are reviewed and added to the glossary everyone downloads.
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
