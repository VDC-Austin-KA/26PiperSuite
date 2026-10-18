// Renders the tutorial videos: Piper narration -> timeline -> Chromium frames -> ffmpeg MP4 (+ SRT).
//   node build.mjs                 all videos
//   node build.mjs 03-line-numbers  selected ids
//   node build.mjs --still 03-line-numbers:12.5   one PNG frame for checking layouts
// Env: PIPER_PYTHON (python with piper-tts), PIPER_MODEL (.onnx voice), FPS (default 30), JOBS (parallel videos, default 3).
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { createRequire } from 'node:module';
import fs from 'node:fs';
import path from 'node:path';

const require = createRequire(import.meta.url);
const { chromium } = require(require.resolve('playwright', { paths: [process.cwd(), '/opt/node-tools/node_modules'] }));

const ROOT = path.dirname(new URL(import.meta.url).pathname);
const OUT = path.join(ROOT, 'out');
const FPS = Number(process.env.FPS ?? 30);
const JOBS = Number(process.env.JOBS ?? 3);
const PYTHON = process.env.PIPER_PYTHON ?? 'python3'; // a Python with piper-tts installed
const MODEL = process.env.PIPER_MODEL;
const RATE = 22050; // Piper medium voices

const args = process.argv.slice(2);
const still = args.includes('--still') ? args[args.indexOf('--still') + 1] : null;
const ids = args.filter(a => !a.startsWith('--') && a !== still);

const server = createServer((req, res) => {
  const file = path.join(ROOT, decodeURIComponent(new URL(req.url, 'http://x').pathname));
  if (!file.startsWith(ROOT) || !fs.existsSync(file)) { res.writeHead(404).end(); return; }
  const type = file.endsWith('.js') ? 'text/javascript' : 'text/html';
  res.writeHead(200, { 'content-type': type }).end(fs.readFileSync(file));
}).listen(0);
const url = `http://127.0.0.1:${server.address().port}/stage.html`;

const browser = await chromium.launch();
const newPage = async () => {
  const page = await browser.newPage({ viewport: { width: 1280, height: 720 }, deviceScaleFactor: 1.5 });
  page.on('pageerror', e => console.error('page error:', e.message));
  await page.goto(url);
  await page.waitForFunction(() => window.renderAt && document.fonts.status === 'loaded');
  return page;
};

try {
  if (still) {
    const [id, t] = still.split(':');
    const page = await newPage();
    const dir = path.join(OUT, id);
    fs.mkdirSync(dir, { recursive: true });
    const wavs = await tts(await page.evaluate(id => window.narration(id), id), dir);
    const info = await page.evaluate(([id, d]) => window.loadVideo(id, d), [id, wavs.map(w => (w ? w.samples.length / RATE : null))]);
    console.log('step starts:', info.starts.map(x => x.toFixed(1)).join(' '), 'total', info.total.toFixed(1));
    // Render every frame up to t so cursor paths and caches match the real render.
    for (let x = 0; x <= Number(t); x += 1 / FPS) await page.evaluate(x => window.renderAt(x), x);
    fs.mkdirSync(OUT, { recursive: true });
    const file = path.join(OUT, `still-${id}-${t}.png`);
    await page.screenshot({ path: file });
    console.log(file);
  } else {
    const page = await newPage();
    const list = (await page.evaluate(() => window.videoList())).filter(v => !ids.length || ids.includes(v.id));
    await page.close();
    const queue = [...list];
    await Promise.all(Array.from({ length: Math.min(JOBS, list.length) }, async () => {
      for (let v; (v = queue.shift());) await renderVideo(v);
    }));
  }
} finally {
  await browser.close();
  server.close();
}

async function renderVideo(v) {
  const dir = path.join(OUT, v.id);
  fs.mkdirSync(dir, { recursive: true });
  const page = await newPage();
  const texts = await page.evaluate(id => window.narration(id), v.id);
  const wavs = await tts(texts, dir);
  const durations = wavs.map(w => (w ? w.samples.length / RATE : null));
  const { total, starts, says } = await page.evaluate(([id, d]) => window.loadVideo(id, d), [v.id, durations]);

  // Narration track: each step's clip starts 0.25 s into the step.
  const track = new Int16Array(Math.ceil((total + 0.5) * RATE));
  wavs.forEach((w, i) => w && track.set(w.samples.subarray(0, track.length - Math.round((starts[i] + 0.25) * RATE)), Math.round((starts[i] + 0.25) * RATE)));
  const audioFile = path.join(dir, 'narration.wav');
  writeWav(audioFile, track);
  writeSrt(path.join(OUT, `${v.id}.srt`), starts, says, total);

  const file = path.join(OUT, `${v.id}.mp4`);
  const ff = spawn('ffmpeg', ['-y', '-loglevel', 'error', '-f', 'image2pipe', '-framerate', String(FPS), '-c:v', 'mjpeg', '-i', '-', '-i', audioFile,
    '-c:v', 'libx264', '-preset', 'medium', '-crf', '20', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-b:a', '128k', '-ar', '44100',
    '-shortest', '-movflags', '+faststart', file], { stdio: ['pipe', 'inherit', 'inherit'] });
  const frames = Math.ceil(total * FPS);
  for (let f = 0; f < frames; f++) {
    await page.evaluate(t => window.renderAt(t), f / FPS);
    const jpg = await page.screenshot({ type: 'jpeg', quality: 92 });
    if (!ff.stdin.write(jpg)) await new Promise(r => ff.stdin.once('drain', r));
    if (f % (FPS * 10) === 0) console.log(`${v.id}: ${(f / FPS).toFixed(0)}s / ${total.toFixed(0)}s`);
  }
  ff.stdin.end();
  await new Promise((ok, fail) => ff.on('close', c => (c ? fail(new Error(`ffmpeg exit ${c}`)) : ok())));
  await page.close();
  console.log(`done ${file} (${total.toFixed(1)}s)`);
}

async function tts(texts, dir) {
  const jobs = texts.map((text, i) => (text ? { text, output_file: path.join(dir, `say-${String(i).padStart(2, '0')}.wav`) } : null));
  const todo = jobs.filter(Boolean);
  if (todo.length) {
    const p = spawn(PYTHON, [path.join(ROOT, 'tts.py'), MODEL], { stdio: ['pipe', 'ignore', 'pipe'] });
    let err = '';
    p.stderr.on('data', d => (err += d));
    p.stdin.end(JSON.stringify(todo));
    await new Promise((ok, fail) => p.on('close', c => (c ? fail(new Error('piper failed: ' + err.slice(-500))) : ok())));
  }
  return jobs.map(j => (j ? readWav(j.output_file) : null));
}

function readWav(file) {
  const b = fs.readFileSync(file);
  let o = 12;
  while (o < b.length) {
    const id = b.toString('ascii', o, o + 4), size = b.readUInt32LE(o + 4);
    if (id === 'fmt ' && b.readUInt32LE(o + 12) !== RATE) throw new Error(`${file}: expected ${RATE} Hz`);
    if (id === 'data') return { samples: new Int16Array(b.buffer.slice(b.byteOffset + o + 8, b.byteOffset + o + 8 + size)) };
    o += 8 + size;
  }
  throw new Error(`${file}: no data chunk`);
}

function writeWav(file, samples) {
  const h = Buffer.alloc(44);
  h.write('RIFF', 0); h.writeUInt32LE(36 + samples.byteLength, 4); h.write('WAVEfmt ', 8);
  h.writeUInt32LE(16, 16); h.writeUInt16LE(1, 20); h.writeUInt16LE(1, 22); h.writeUInt32LE(RATE, 24);
  h.writeUInt32LE(RATE * 2, 28); h.writeUInt16LE(2, 32); h.writeUInt16LE(16, 34); h.write('data', 36); h.writeUInt32LE(samples.byteLength, 40);
  fs.writeFileSync(file, Buffer.concat([h, Buffer.from(samples.buffer)]));
}

function writeSrt(file, starts, says, total) {
  const ts = s => { const ms = Math.round(s * 1000); return new Date(ms).toISOString().slice(11, 23).replace('.', ','); };
  const cues = says.map((say, i) => (say && i > 0 ? { a: starts[i], b: starts[i + 1] ?? total, say } : null)).filter(Boolean);
  fs.writeFileSync(file, cues.map((c, n) => `${n + 1}\n${ts(c.a)} --> ${ts(c.b - 0.05)}\n${c.say}\n`).join('\n'));
}
