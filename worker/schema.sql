-- Words players shared. One row per word per player, so a word's reporter count can't be inflated by one person.
CREATE TABLE IF NOT EXISTS submissions (
  term       TEXT NOT NULL,
  meaning    TEXT NOT NULL,
  reporter   TEXT NOT NULL,     -- SHA-256 of SALT + install ID; the raw ID is never stored
  created_at INTEGER NOT NULL,  -- unix seconds
  PRIMARY KEY (term, reporter)
);

-- The reviewed glossary that GET /v1/glossary serves. Only you write to it, after reviewing submissions.
CREATE TABLE IF NOT EXISTS glossary (
  ja   TEXT PRIMARY KEY,
  en   TEXT NOT NULL,
  note TEXT
);

-- glossary_version goes up on every change, so the plugin knows when to replace its saved copy.
CREATE TABLE IF NOT EXISTS meta (
  key   TEXT PRIMARY KEY,
  value INTEGER NOT NULL
);
INSERT OR IGNORE INTO meta (key, value) VALUES ('glossary_version', 0);

CREATE TRIGGER IF NOT EXISTS glossary_insert AFTER INSERT ON glossary
BEGIN UPDATE meta SET value = value + 1 WHERE key = 'glossary_version'; END;
CREATE TRIGGER IF NOT EXISTS glossary_update AFTER UPDATE ON glossary
BEGIN UPDATE meta SET value = value + 1 WHERE key = 'glossary_version'; END;
CREATE TRIGGER IF NOT EXISTS glossary_delete AFTER DELETE ON glossary
BEGIN UPDATE meta SET value = value + 1 WHERE key = 'glossary_version'; END;
