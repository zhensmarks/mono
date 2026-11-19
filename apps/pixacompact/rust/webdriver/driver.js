// pxa-webdriver — driver web engine (port `PixaWebAutomationService.cs` DOM flow) ke
// Node+Playwright. Dipanggil dashboard Rust:
//   pxa-webdriver.exe <engine_tag> <out_dir> <file1> [file2 ...]
// Hasil: <out_dir>/<nama>-cutout.png per file. exit 0 = minimal satu sukses.
// engine_tag: pixa | bg_eraser | nobg.
//
// Konvensi (sengaja beda dari argumen lama dashboard, karena batch lebih umum):
// arg1 = engine, arg2 = out_dir, sisanya = list input file.

const { chromium } = require("playwright");
const fs = require("fs");
const path = require("path");

// Proxy + akun dari env (diteruskan oleh dashboard Rust).
const PX_PROXY = process.env.PX_WEB_PROXY || "";
const PX_ACCOUNTS = (process.env.PX_WEB_ACCOUNTS || "").split(",").map((s) => s.trim()).filter(Boolean);
// Akun aktif untuk sesi ini: round-robin sederhana berdasarkan index pemanggil.
// (Dashboard memanggil per-file; driver pilih akun berdasarkan urutan file bila banyak.)
function accountFor(idx) {
  if (PX_ACCOUNTS.length === 0) return "default";
  const [name] = PX_ACCOUNTS[idx % PX_ACCOUNTS.length].split(":");
  return name || "default";
}

const ENGINES = {
  pixa: {
    batchUrl: "https://www.pixelcut.ai/t/batch-edit",
    host: "pixelcut.ai",
    mark: ["pixelcut", "pixa"],
  },
  bg_eraser: {
    batchUrl: "https://www.bgeraser.com/",
    host: "bgeraser.com",
    mark: ["bgeraser"],
  },
  nobg: {
    batchUrl: "https://nobg.space/",
    host: "nobg.space",
    mark: ["nobg"],
  },
};

function arg(v) {
  return String(v ?? "");
}

function computeTimeoutMs(n) {
  return Math.min(180000 + 8000 * Math.max(0, n), 1800000);
}

function isPngSig(b) {
  return b.length >= 4 && b[0] === 0x89 && b[1] === 0x50 && b[2] === 0x4e && b[3] === 0x47;
}

// PNG punya alpha? (color type 4/6 atau ada chunk tRNS) — port `PngHasAlpha` C#.
function pngHasAlpha(bytes) {
  if (!isPngSig(bytes) || bytes.length < 33) return false;
  let offset = 8;
  let colorType = null;
  while (offset + 8 <= bytes.length) {
    const length =
      (bytes[offset] << 24) |
      (bytes[offset + 1] << 16) |
      (bytes[offset + 2] << 8) |
      bytes[offset + 3];
    if (offset + 12 + length > bytes.length) break;
    const tag = bytes.toString("latin1", offset + 4, offset + 8);
    if (tag === "IHDR" && length >= 9) colorType = bytes[offset + 8 + 8];
    if (tag === "tRNS") return true;
    if (tag === "IEND") break;
    offset += 12 + length;
  }
  return colorType === 4 || colorType === 6;
}

// Baca file input sebagai data URL (untuk skip src preview input).
function fileAsDataUrl(file) {
  try {
    const buf = fs.readFileSync(file);
    return "data:" + (file.toLowerCase().endsWith(".png") ? "image/png" : "image/jpeg") + ";base64," + buf.toString("base64");
  } catch {
    return null;
  }
}

async function main() {
  const engine = arg(process.argv[2]);
  const outDir = path.resolve(arg(process.argv[3]));
  const inputs = process.argv.slice(4).map(String).filter(Boolean);
  const cfg = ENGINES[engine];
  if (!cfg) {
    console.error(`engine tak dikenal: ${engine}`);
    process.exit(2);
  }
  if (inputs.length === 0) {
    console.error("tidak ada file input");
    process.exit(2);
  }
  fs.mkdirSync(outDir, { recursive: true });

  const userDataDir = path.join(outDir, ".pwprofile-" + engine + "-" + accountFor(0));
  fs.mkdirSync(userDataDir, { recursive: true });

  const launchOpts = {
    headless: true,
    args: [
      "--disable-blink-features=AutomationControlled",
      "--no-sandbox",
      "--disable-dev-shm-usage",
      "--mute-audio",
    ],
    ignoreDefaultArgs: ["--enable-automation"],
    viewport: { width: 1280, height: 800 },
    userAgent:
      "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36",
  };
  if (PX_PROXY.trim()) {
    // Satu proxy per sesi (Mix Proxy). Dashboard sudah memilihkan proxy yang valid.
    launchOpts.proxy = { server: PX_PROXY.trim() };
  }
  const context = await chromium.launchPersistentContext(userDataDir, launchOpts);
  context.setDefaultTimeout(computeTimeoutMs(inputs.length));

  let okCount = 0;
  try {
    for (const file of inputs) {
      if (!fs.existsSync(file)) {
        console.error(`[skip] input tak ada: ${file}`);
        continue;
      }
      const stem = path.basename(file, path.extname(file));
      const dest = path.join(outDir, `${stem}-cutout.png`);
      const page = await context.newPage();
      try {
        await page.goto(cfg.batchUrl, { waitUntil: "domcontentloaded", timeout: 180000 });
        const url = page.url();
        if (url.includes("/login") || url.includes("/auth")) {
          throw new Error("Website meminta Login. Mode otomatis tidak bisa lanjut.");
        }
        await page.waitForSelector('input[type="file"]', { timeout: 10000 }).catch(() => {});
        await page.waitForTimeout(2000);

        const fileInput = page.locator('input[type="file"]').first();
        if ((await fileInput.count()) === 0) {
          throw new Error("input file tidak ditemukan di editor");
        }
        await fileInput.setInputFiles([file]);
        await page.waitForTimeout(2000);

        // Klik semua tombol "Remove Background" (per engine).
        await page.waitForTimeout(1000);
        await clickRemoveBg(page, cfg);

        // Tunggu proses selesai.
        await page
          .waitForFunction(
            () => {
              const t = document.body.innerText;
              return !t.includes("Processing...") && !t.includes("Working...");
            },
            null,
            { timeout: computeTimeoutMs(1) }
          )
          .catch(() => {});
        await page.waitForTimeout(5000);

        // Ambil bytes PNG terbaik: src mengandung mark engine DAN punya alpha.
        // Abaikan `blob:` (itu preview/upload input) dan icon SVG CDN.
        const candidates = await page.evaluate((marks) => {
          const imgs = [...document.images];
          const seen = new Set();
          const out = [];
          for (const img of imgs) {
            if (!img || !img.src) continue;
            const s = String(img.src).toLowerCase();
            if (s.startsWith("blob:")) continue; // upload/preview, bukan hasil
            if (!marks.some((m) => s.includes(m))) continue;
            if (seen.has(img.src)) continue;
            seen.add(img.src);
            out.push(img.src);
          }
          return out;
        }, cfg.mark);

        let picked = null;
        for (const src of candidates) {
          const bytes = await downloadAsPng(page, src);
          if (bytes && isPngSig(bytes) && pngHasAlpha(bytes)) picked = bytes;
        }
        // Fallback: kalau tak ada yang alpha, pakai PNG apa pun yang bukan input.
        if (!picked) {
          for (const src of candidates) {
            const bytes = await downloadAsPng(page, src);
            if (bytes && isPngSig(bytes)) {
              picked = bytes;
              break;
            }
          }
        }
        if (!picked) {
          console.error(`[gagal] ${stem}: tidak ada output PNG`);
          continue;
        }
        fs.writeFileSync(dest, picked);
        okCount++;
        console.log(`[ok] ${stem} -> ${dest} (${picked.length} B)`);
      } catch (e) {
        console.error(`[gagal] ${stem}: ${e.message}`);
      } finally {
        await page.close().catch(() => {});
      }
    }
  } finally {
    await context.close().catch(() => {});
  }

  if (okCount === 0) {
    console.error("semua file gagal");
    process.exit(1);
  }
  process.exit(0);
}

async function clickRemoveBg(page, cfg) {
  const selectors = [
    'button:has-text("Remove Background")',
    'button:has-text("Remove BG")',
    'button:has-text("Erase")',
    'button:has-text("Remove background")',
    "text=Remove Background",
    "text=Remove BG",
  ];
  for (const sel of selectors) {
    try {
      const btns = page.locator(sel);
      const n = await btns.count();
      if (n === 0) continue;
      for (let i = 0; i < n; i++) {
        await btns.nth(i).scrollIntoViewIfNeeded().catch(() => {});
        await btns.nth(i).click({ force: true }).catch(() => {});
        await page.waitForTimeout(400);
      }
      return true;
    } catch {
      /* lanjut selector lain */
    }
  }
  return false;
}

async function downloadAsPng(page, src) {
  const b64 = await page.evaluate(async (url) => {
    try {
      const r = await fetch(url);
      const blob = await r.blob();
      return await new Promise((resolve) => {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result);
        reader.onerror = () => resolve(null);
        reader.readAsDataURL(blob);
      });
    } catch {
      return null;
    }
  }, src);
  if (!b64 || !b64.includes(",")) return null;
  const b64part = b64.split(",").pop();
  try {
    return Buffer.from(b64part, "base64");
  } catch {
    return null;
  }
}

main().catch((e) => {
  console.error("fatal:", e && e.stack ? e.stack : e);
  process.exit(1);
});
