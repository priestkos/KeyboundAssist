import { chromium } from "playwright";
import { mkdir, rm } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const outDir = path.resolve(__dirname, "..");
const framesDir = path.join(__dirname, "frames");

const FPS = 30;
const DURATION_SEC = 21;
const FRAME_COUNT = FPS * DURATION_SEC;

const htmlPath = path.join(__dirname, "walkthrough.html");
const fileUrl = `file://${htmlPath}`;

await rm(framesDir, { recursive: true, force: true });
await mkdir(framesDir, { recursive: true });
await mkdir(outDir, { recursive: true });

const browser = await chromium.launch();
const page = await browser.newPage();
await page.setViewportSize({ width: 1280, height: 720 });
await page.goto(fileUrl, { waitUntil: "load" });

for (let i = 0; i < FRAME_COUNT; i++) {
  const t = i / FPS;
  await page.evaluate((sec) => window.seekWalkthrough(sec), t);
  const name = `frame-${String(i).padStart(5, "0")}.png`;
  await page.screenshot({ path: path.join(framesDir, name), type: "png" });
  if (i % 90 === 0) console.log(`Frame ${i}/${FRAME_COUNT}`);
}

await browser.close();

const mp4Path = path.join(outDir, "mockup-walkthrough.mp4");
const gifPath = path.join(outDir, "mockup-walkthrough.gif");

const encodeMp4 = spawnSync(
  "ffmpeg",
  [
    "-y",
    "-framerate",
    String(FPS),
    "-i",
    path.join(framesDir, "frame-%05d.png"),
    "-c:v",
    "libx264",
    "-pix_fmt",
    "yuv420p",
    "-crf",
    "28",
    "-movflags",
    "+faststart",
    mp4Path,
  ],
  { stdio: "inherit" },
);

if (encodeMp4.status !== 0) process.exit(encodeMp4.status ?? 1);

const encodeGif = spawnSync(
  "ffmpeg",
  [
    "-y",
    "-i",
    mp4Path,
    "-vf",
    "fps=12,scale=640:-1:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=128[p];[s1][p]paletteuse=dither=bayer",
    gifPath,
  ],
  { stdio: "inherit" },
);

if (encodeGif.status !== 0) process.exit(encodeGif.status ?? 1);

console.log("Wrote", mp4Path);
console.log("Wrote", gifPath);

await rm(framesDir, { recursive: true, force: true });
