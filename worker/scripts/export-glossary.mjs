// Downloads the reviewed glossary from the deployed Worker into ../glossary.json, the copy built into the plugin.
// Run before a plugin release so new installs start with the current words: npm run export
import { writeFileSync } from "node:fs";

const url = process.env.GLOSSARY_URL ?? "https://konnyaku-glossary.nuttakorn-buajarern.workers.dev/v1/glossary";
const res = await fetch(url);
if (!res.ok) throw new Error(`${url} returned ${res.status}`);
const file = await res.json();
const lines = file.terms.map(t => "    " + JSON.stringify(t).replaceAll('","', '", "').replaceAll('":"', '": "').replace(/^\{/, "{ ").replace(/\}$/, " }"));
writeFileSync(new URL("../../glossary.json", import.meta.url), `{\n  "version": ${file.version},\n  "terms": [\n${lines.join(",\n")}\n  ]\n}\n`);
console.log(`Wrote version ${file.version} with ${file.terms.length} words to glossary.json`);
