const eventId = required("DST_FIXTURE_EVENT_ID");
const siteUrl = required("SITE_URL");
const apiUrl = required("API_URL");

function required(name) {
  const value = process.env[name];
  if (!value) {
    console.error(`missing ${name}`);
    process.exit(2);
  }
  return value.replace(/\/$/, "");
}

async function fetchText(url, accept) {
  const response = await fetch(url, {
    headers: { accept },
    redirect: "follow",
    signal: AbortSignal.timeout(15000),
  });
  if (!response.ok) throw new Error(`${url} returned ${response.status}`);
  return response.text();
}

function findStartDate(node) {
  if (Array.isArray(node)) return node.map(findStartDate).find(Boolean);
  if (node && typeof node === "object") {
    if (typeof node.startDate === "string") return node.startDate;
    return Object.values(node).map(findStartDate).find(Boolean);
  }
  return undefined;
}

function startDateFromHtml(html) {
  const blocks = html.matchAll(/<script[^>]*type="application\/ld\+json"[^>]*>([\s\S]*?)<\/script>/g);
  for (const [, body] of blocks) {
    const startDate = findStartDate(JSON.parse(body));
    if (startDate) return startDate;
  }
  throw new Error("no JSON-LD startDate in Function HTML");
}

function offsetAt(instant, timeZone) {
  const parts = new Intl.DateTimeFormat("en-US", { timeZone, timeZoneName: "longOffset" }).formatToParts(instant);
  const name = parts.find((part) => part.type === "timeZoneName").value;
  return name === "GMT" ? "+00:00" : name.replace("GMT", "");
}

const [html, apiBody] = await Promise.all([
  fetchText(`${siteUrl}/e/${eventId}`, "text/html"),
  fetchText(`${apiUrl}/v1/events/${eventId}`, "application/json"),
]);

const api = JSON.parse(apiBody);
const jsonLdStart = startDateFromHtml(html);
const apiStart = api.starts_at ?? api.startsAt;
const timeZone = api.venue?.time_zone ?? api.venue?.timeZone;
if (!apiStart) throw new Error("API response has no starts_at");

const problems = [];
if (new Date(jsonLdStart).getTime() !== new Date(apiStart).getTime()) {
  problems.push(`instant mismatch: JSON-LD ${jsonLdStart} vs API ${apiStart}`);
}
const literalOffset = jsonLdStart.match(/([+-]\d{2}:\d{2}|Z)$/)?.[1];
if (timeZone && literalOffset) {
  const expected = offsetAt(new Date(apiStart), timeZone);
  const actual = literalOffset === "Z" ? "+00:00" : literalOffset;
  if (expected !== actual) problems.push(`offset mismatch in ${timeZone}: JSON-LD ${actual} vs expected ${expected}`);
}

if (problems.length > 0) {
  console.error(`DST mismatch for ${eventId}: ${problems.join("; ")}`);
  process.exit(1);
}
console.log(`DST check ok for ${eventId}: ${jsonLdStart}`);
