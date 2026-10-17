// Writes sql/seed.sql from the repo's glossary.json, so D1 starts with the same words the plugin has built in.
// Usage: npm run seed:build, then npm run seed
import { readFileSync, writeFileSync } from "node:fs";

const file = JSON.parse(readFileSync(new URL("../../glossary.json", import.meta.url), "utf8"));
const q = s => (s == null ? "NULL" : `'${String(s).replaceAll("'", "''")}'`);
const lines = file.terms.map(t =>
  `INSERT INTO glossary (ja, en, note) VALUES (${q(t.ja)}, ${q(t.en)}, ${q(t.note)}) ` +
  `ON CONFLICT (ja) DO UPDATE SET en = excluded.en, note = excluded.note;`);
writeFileSync(new URL("../sql/seed.sql", import.meta.url), lines.join("\n") + "\n");
console.log(`Wrote ${lines.length} words to sql/seed.sql`);
