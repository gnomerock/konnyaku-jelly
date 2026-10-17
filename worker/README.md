# Konnyaku Jelly glossary Worker

A Cloudflare Worker in front of a D1 database. Players share FFXIV words they collected, and everyone downloads the reviewed glossary. The plugin never connects to D1 itself, and no Cloudflare token ships with it.

| Endpoint | What it does |
|---|---|
| `POST /v1/terms` | Body `{ "installId": "<guid>", "terms": [{ "term": "消化", "meaning": "farm" }] }`, up to 50 terms. Terms that break the rules are dropped and counted in `rejected`. |
| `GET /v1/glossary` | `{ "version": 108, "terms": [{ "ja", "en", "note"? }] }`, the same format as `glossary.json`. Cached at the edge for an hour. |

What is stored: the word, the meaning, a SHA-256 of `SALT` plus the install ID, and the time. IP addresses are only used as a rate-limit key and are never saved. Request logs are off (`observability.enabled = false`).

The Worker checks words with the same rules as the plugin: Japanese only, no Latin letters or digits (FFXIV character names are Latin-only), a term of at most 12 characters, a meaning of at most 60. It also rate-limits each install ID and each IP to 5 shares a minute.

## Deploy

Run these from this folder.

1. `npm install`
2. Put your D1 database's name and ID in `wrangler.toml`. `npx wrangler d1 list` shows them.
3. `npx wrangler login`
4. `npm run db:schema` creates the tables.
5. `npm run seed:build` and then `npm run seed` copy the words from `../glossary.json` into D1.
6. `npx wrangler secret put SALT`, then paste a long random string. Don't change it later, or every player starts counting as a new reporter.
7. `npm run deploy` prints the Worker URL, e.g. `https://konnyaku-glossary.<you>.workers.dev`.
8. Check it: open `<url>/v1/glossary` in a browser.

## Review shared words

`npm run candidates` lists words not in the glossary yet that at least 3 different players reported, with a vote count for each meaning.

To approve a word (fix the meaning if needed):

```
npx wrangler d1 execute DB --remote --command "INSERT INTO glossary (ja, en, note) VALUES ('ハムカツ', 'Hamukatsu strat', 'a raid positioning strat') ON CONFLICT (ja) DO UPDATE SET en = excluded.en, note = excluded.note"
```

To remove a word: `npx wrangler d1 execute DB --remote --command "DELETE FROM glossary WHERE ja = 'ハムカツ'"`

Every change to `glossary` raises `version` (a trigger in `schema.sql`), so plugins replace their saved copy. Because of the edge cache, players get the change within an hour.

Before a plugin release, run `npm run export`. It downloads the current glossary into `../glossary.json`, the copy built into the plugin for when the server can't be reached.

To clear out submissions for words you've handled: `DELETE FROM submissions WHERE term = '...'`.

## Try it locally

```
echo SALT=local-test-salt > .dev.vars
npm run db:schema:local
npm run seed:build && npm run seed:local
npm run dev
```

The local database lives in `.wrangler/`, which git ignores.
