import { createFileRoute } from "@tanstack/react-router";

// Decoy endpoint: mọi URL /api/public/raw/decoy/* trả về fake payload
// giống Luraph/Ironbrew để đánh lạc hướng ai scan/scrape link.
function randHex(n: number) {
  let s = "";
  for (let i = 0; i < n; i++) s += Math.floor(Math.random() * 16).toString(16);
  return s;
}
function fakePayload(): string {
  const junkA = randHex(1024);
  const junkB = randHex(1024);
  const consts = Array.from({ length: 24 }, () => Math.floor(Math.random() * 65535)).join(",");
  return `-- Luraph v14.2 | build ${randHex(6)} | (c) Luraph Obfuscator
-- LPH_NO_VIRTUALIZE
local LPH_ENC="${junkA}"
local LPH_JMP={${consts}}
-- Ironbrew v2 | vmtype=STANDARD
local IB_HEADER="${junkB}"
local IB_OP=function(a,b) return (a*0x100+b)%0x10000 end
-- [ Obfuscated by Discord.gg/lonelyyy ]
error("Invalid script token. 0.00% compatible with deobf tools.")
`;
}

export const Route = createFileRoute("/api/public/raw/decoy/$id")({
  server: {
    handlers: {
      GET: async () => new Response(fakePayload(), {
        status: 200,
        headers: {
          "content-type": "text/plain; charset=utf-8",
          "cache-control": "no-store",
          "x-robots-tag": "noindex",
        },
      }),
    },
  },
});
