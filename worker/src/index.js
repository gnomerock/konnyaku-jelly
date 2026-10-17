// Konnyaku Jelly glossary API. The plugin never talks to D1 itself: this Worker holds the binding and only allows
//   POST /v1/terms     players share words they collected (term + meaning only)
//   GET  /v1/glossary  everyone downloads the reviewed glossary
// Nothing identifying is stored: no IPs, and the install ID is only kept as a salted hash.

const MAX_BODY_BYTES = 16 * 1024;
const MAX_TERMS = 50;
const GLOSSARY_CACHE_SECONDS = 3600;

export default {
  async fetch(req, env, ctx) {
    const url = new URL(req.url);
    try {
      if (req.method === "POST" && url.pathname === "/v1/terms") return await submitTerms(req, env);
      if (req.method === "GET" && url.pathname === "/v1/glossary") return await glossary(req, env, ctx);
      return text("not found", 404);
    } catch (err) {
      console.error(err);
      return text("server error", 500);
    }
  },
};

async function submitTerms(req, env) {
  const raw = await req.text();
  if (raw.length > MAX_BODY_BYTES) return text("too large", 413);

  let body;
  try {
    body = JSON.parse(raw);
  } catch {
    return text("bad request", 400);
  }
  if (typeof body?.installId !== "string" || !/^[0-9a-f-]{32,36}$/i.test(body.installId) || !Array.isArray(body.terms) || body.terms.length > MAX_TERMS)
    return text("bad request", 400);

  // Rate limit by install ID and by IP. The IP is only used as a limiter key and is never stored.
  const ip = req.headers.get("CF-Connecting-IP") ?? "unknown";
  for (const key of [`id:${body.installId}`, `ip:${ip}`]) {
    const { success } = await env.LIMITER.limit({ key });
    if (!success) return text("slow down", 429);
  }

  const reporter = await sha256(`${env.SALT}:${body.installId.toLowerCase()}`);
  const now = Math.floor(Date.now() / 1000);
  const terms = body.terms.map(clean).filter(Boolean);
  const stmts = terms.map(t => env.DB.prepare(
    `INSERT INTO submissions (term, meaning, reporter, created_at) VALUES (?1, ?2, ?3, ?4)
     ON CONFLICT (term, reporter) DO UPDATE SET meaning = excluded.meaning, created_at = excluded.created_at`,
  ).bind(t.term, t.meaning, reporter, now));
  if (stmts.length > 0) await env.DB.batch(stmts);
  return Response.json({ accepted: stmts.length, rejected: body.terms.length - stmts.length });
}

async function glossary(req, env, ctx) {
  // Every download would otherwise read the whole table, so keep a copy in the edge cache.
  const cache = caches.default;
  const cacheKey = new Request(new URL("/v1/glossary", req.url).toString());
  const hit = await cache.match(cacheKey);
  if (hit) return hit;

  const [version, rows] = await env.DB.batch([
    env.DB.prepare("SELECT value FROM meta WHERE key = 'glossary_version'"),
    env.DB.prepare("SELECT ja, en, note FROM glossary ORDER BY ja"),
  ]);
  const res = Response.json(
    { version: version.results[0]?.value ?? 0, terms: rows.results.map(({ ja, en, note }) => (note ? { ja, en, note } : { ja, en })) },
    { headers: { "Cache-Control": `public, max-age=${GLOSSARY_CACHE_SECONDS}` } },
  );
  ctx.waitUntil(cache.put(cacheKey, res.clone()));
  return res;
}

/**
 * Same rules as the plugin's TermCollector: a short term in Japanese with no Latin letters or digits (FFXIV character
 * names are Latin-only, so this keeps names out), and a short meaning. Returns null for anything else.
 */
function clean(t) {
  if (typeof t?.term !== "string" || typeof t?.meaning !== "string") return null;
  const term = t.term.trim();
  const meaning = t.meaning.trim();
  if (term.length === 0 || term.length > 12 || meaning.length === 0 || meaning.length > 60) return null;
  if (!/[぀-ヿ一-鿿ｦ-ﾟ]/.test(term)) return null;
  if (/[A-Za-z0-9０-９Ａ-Ｚａ-ｚ@<>\[\]\s]/.test(term)) return null;
  if (/[<>\n]/.test(meaning)) return null;
  return { term, meaning };
}

async function sha256(s) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(s));
  return [...new Uint8Array(digest)].map(b => b.toString(16).padStart(2, "0")).join("");
}

function text(body, status) {
  return new Response(body, { status, headers: { "Content-Type": "text/plain; charset=utf-8" } });
}
