# Konyakku — วุ้นแปลภาษา

Dalamud plugin for FFXIV that translates chat with ChatGPT.

- **ข้อความเข้า**: แปลแชท (Say / Party / FC / Tell / LS / CWLS …) เป็นภาษาไทย แสดงในหน้าต่าง Konyakku (ฟอนต์ในเกมแสดงภาษาไทยไม่ได้ ปลั๊กอินเลยใช้ฟอนต์ Leelawadee UI ใน ImGui แทน)
- **ข้อความออก**: พิมพ์ภาษาไทยในช่องล่างของหน้าต่าง → แปลเป็นภาษาที่เลือก → Send (ส่งเข้า channel ตาม prefix เช่น `/p`, `/fc`, `/s`) หรือ Copy

## Provider

| Mode | ใช้อะไร | หมายเหตุ |
|---|---|---|
| **ChatGPT subscription** (default) | login จาก [Codex CLI](https://github.com/openai/codex) (`~/.codex/auth.json`) | ใช้โควต้าของ ChatGPT Plus/Pro ไม่เสียเงินเพิ่ม เป็น backend เดียวกับที่ Codex ใช้ ไม่ใช่ API ทางการสำหรับแอปอื่น OpenAI อาจเปลี่ยนหรือปิดได้ |
| **OpenAI API key** | `https://api.openai.com/v1/chat/completions` | คิดเงินตามการใช้งาน ทางการ เสถียรกว่า |

Setup for subscription mode:

```
npm i -g @openai/codex
codex login        # เลือก "Sign in with ChatGPT"
```

ปลั๊กอินจะ refresh token เองแล้วเขียนกลับไฟล์ `auth.json` ให้ Codex CLI ยังใช้งานต่อได้ (refresh token เปลี่ยนทุกครั้งที่ refresh)

## Commands

- `/konyakku` — เปิด/ปิดหน้าต่างแปล · `/konyakku config` — ตั้งค่า
- `/tl <text>` — แปลเป็นภาษาขาออกแล้ว copy ไว้ใน clipboard
- `/tl /p <text>` — แปลแล้วส่งเข้า party ทันที (ใช้ `/fc`, `/s`, `/l1`, `/t Name@World` ฯลฯ ได้)

## Build / dev install

Requires .NET 10 SDK and XIVLauncher with Dalamud installed (references come from `%APPDATA%\XIVLauncher\addon\Hooks\dev`).

```
dotnet build Konyakku/Konyakku.csproj -c Release
```

In game: `/xlsettings` → Experimental → Dev Plugin Locations → add
`D:\repo\konyaku\Konyakku\bin\Release\Konyakku.dll`, then enable it in `/xlplugins` → Dev Tools.

## Notes

- Default channels skip Shout/Yell (busy cities would burn your quota). You can turn them on in settings.
- Translations are cached and at most 3 run at once.
- ImGui doesn't do full Thai text shaping, so stacked vowels and tone marks can sit a little off. The text is still readable.
