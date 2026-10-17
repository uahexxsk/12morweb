// Chặn xâm nhập: mặc định DENY. Chỉ cho phép User-Agent match whitelist
// của Roblox / các executor phổ biến. Mọi tool HTTPS GET (curl, wget,
// python, node, axios, httpx, postman, browser fetch/xhr...) đều bị chặn.
const EXECUTOR_UA_ALLOW = [
  "roblox",       // Roblox/WinInet (game:HttpGet trên client thật)
  "wininet",
  "winhttp",
  "delta",
  "codex",
  "arceus",
  "solara",
  "fluxus",
  "hydrogen",
  "krnl",
  "synapse",
  "wave",
  "sirhurt",
  "argon",
  "script-ware",
  "scriptware",
  "electron",
  "celery",
  "xeno",
  "velocity",
  "swift",
  "trigon",
  "vegax",
  "vega-x",
  "potassium",
  "matcha",
  "seliware",
  "awp",
  "ronin",
  "zorara",
  "real",
];

// Nếu UA giống trình duyệt hoặc là tool HTTP phổ biến -> chặn ngay.
const BROWSER_LIKE = ["mozilla/", "chrome/", "safari/", "firefox/", "edg/", "opr/", "gecko"];
const GENERIC_TOOLS = [
  "curl/", "wget/", "python-requests", "python-httpx", "python-urllib",
  "go-http-client", "postmanruntime", "insomnia", "httpie/", "axios/",
  "node-fetch", "undici", "libwww-perl", "java/", "okhttp/", "apache-httpclient",
  "restsharp", "guzzlehttp", "ruby", "dart/", "unirest", "reqwest",
  "faraday", "http_request", "lua-resty", "fetch", "xmlhttprequest",
];

export function isBlockedFetcher(request: Request): boolean {
  const ua = (request.headers.get("user-agent") ?? "").toLowerCase().trim();
  const accept = (request.headers.get("accept") ?? "").toLowerCase();
  const secFetchMode = request.headers.get("sec-fetch-mode");
  const secFetchDest = request.headers.get("sec-fetch-dest");
  const secFetchSite = request.headers.get("sec-fetch-site");

  // Bất kỳ request nào từ trình duyệt (có Sec-Fetch-* hoặc Accept: text/html) đều chặn.
  if (secFetchMode || secFetchDest || secFetchSite) return true;
  if (accept.includes("text/html")) return true;

  // UA rỗng -> chặn.
  if (!ua) return true;

  // Một số client PC/mobile ghép tên executor với thư viện mạng Android.
  // Nhận executor đã biết trước để không chặn nhầm Delta/Real dùng OkHttp.
  if (ua.includes("roblox")) {
    return false;
  }

  // Cho qua nếu match executor whitelist.
  if (EXECUTOR_UA_ALLOW.some((t) => ua.includes(t))) return false;

  // Chặn tool HTTP phổ biến khi request không có nhận dạng executor hợp lệ.
  if (GENERIC_TOOLS.some((t) => ua.includes(t))) return true;

  // Trình duyệt-like nhưng KHÔNG phải Roblox/executor -> chặn.
  if (BROWSER_LIKE.some((t) => ua.includes(t))) return true;

  // Mặc định: DENY tất cả UA không nằm trong whitelist.
  return true;
}
