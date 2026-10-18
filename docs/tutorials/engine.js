// Deterministic tutorial renderer: renderAt(t) draws the stage for time t (seconds).
// The scene data (scenes.js) is a list of narrated steps, each with timed actions.
import { TUTORIALS, MODEL, initialState, viewName } from './scenes.js';

const stage = document.getElementById('stage');
const TITLE_STEP = 'title', END_STEP = 'end';
let video = null; // { def, steps:[{start,dur,say,acts:[{t,...}]}], total }

// ---------- timeline ----------

/** Builds absolute timing from per-step narration durations (seconds, null = silent). */
window.loadVideo = (id, audioDurations) => {
  const def = TUTORIALS.find(v => v.id === id);
  const all = [{ kind: TITLE_STEP, say: def.titleSay ?? def.title + '.', act: [] }, ...def.steps, { kind: END_STEP, say: null, act: [] }];
  let start = 0;
  const steps = all.map((s, i) => {
    const audio = audioDurations?.[i] ?? 0;
    const acts = expand(s.act ?? []);
    const lastAct = acts.reduce((m, a) => Math.max(m, a.t + (a.dur ?? 0)), 0);
    const dur = Math.max(audio + (s.say ? 0.9 : 0), lastAct + 0.6, s.min ?? 0, s.kind === TITLE_STEP ? 3.4 : s.kind === END_STEP ? 3.2 : 1.5);
    const step = { ...s, index: i, start, dur, audio, acts: acts.map(a => ({ ...a, t: a.t + start })) };
    start += dur;
    return step;
  });
  video = { def, steps, total: start, acts: steps.flatMap(s => s.acts) };
  posCache.clear();
  return { total: start, starts: steps.map(s => s.start), says: steps.map(s => s.say) };
};

window.narration = id => {
  const def = TUTORIALS.find(v => v.id === id);
  return [def.titleSay ?? def.title + '.', ...def.steps.map(s => s.speak ?? s.say ?? null), null];
};
window.videoList = () => TUTORIALS.map(v => ({ id: v.id, title: v.title, n: v.n }));

// Sugar: tap = move + click + optional set; dbl = move + double click.
function expand(acts) {
  const out = [];
  for (const [t, a] of acts) {
    if (a.tap || a.dbl) {
      const target = a.tap ?? a.dbl, md = a.mdur ?? 0.7;
      out.push({ t, move: target, dur: md });
      out.push({ t: t + md + 0.05, click: target, double: !!a.dbl });
      if (a.set) out.push({ t: t + md + 0.2, set: a.set });
    } else out.push({ t, ...a });
  }
  return out.sort((x, y) => x.t - y.t);
}

// ---------- state ----------

function stateAt(t) {
  const s = structuredClone(initialState(video.def));
  s._at = {};
  for (const a of video.acts) {
    if (a.t > t) break;
    if (a.set) for (const [k, v] of Object.entries(a.set)) {
      if (k === 'settingsScroll') s.settingsScrollFrom = s.settingsScroll ?? 0;
      setPath(s, k, typeof v === 'function' ? v(s) : v); s._at[k] = a.t;
    }
    if (a.type) { setPath(s, 'fields.' + a.type, a.text); s._at['fields.' + a.type] = a.t; s._typing = { key: a.type, t: a.t, dur: a.dur ?? 1.2 }; }
    if (a.click) { s._click = { target: a.click, t: a.t, double: a.double }; }
  }
  return s;
}

function setPath(o, path, v) {
  const keys = path.split('.');
  let cur = o;
  for (const k of keys.slice(0, -1)) cur = cur[k] ??= {};
  cur[keys.at(-1)] = v;
}

const since = (s, key, t) => (s._at[key] === undefined ? Infinity : t - s._at[key]);
const clamp01 = x => Math.max(0, Math.min(1, x));
const ease = x => (x < 0.5 ? 2 * x * x : 1 - Math.pow(-2 * x + 2, 2) / 2);
const esc = x => String(x ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

// ---------- render ----------

window.renderAt = t => {
  const s = stateAt(t);
  const step = video.steps.findLast(st => st.start <= t) ?? video.steps[0];
  s._t = t;
  let html = s.screen === 'revit' ? revit(s, t) : desktop(s, t);
  html += overlays(s, t, step);
  stage.innerHTML = html;
  decorate(s, t);
  return true;
};

function revit(s, t) {
  const viewTitle = s.view === 'sheet' ? s.sheetTitle : s.view === 'iso' ? s.isoTitle : '{3D}';
  const doc = 'Plant_Room_MEP.rvt';
  return `<div class="revit">
    <div class="titlebar"><div class="logo">R</div><div class="qat"><i></i><i></i><i></i><i></i></div>
      <div class="title">Autodesk Revit 2026 - ${doc} - ${s.view === 'sheet' ? 'Sheet' : '3D View'}: ${esc(viewTitle)}</div><div style="width:80px"></div></div>
    <div class="tabs">${tabs(s)}</div>
    <div class="ribbon">${s.tab === 'pis' ? pisRibbon() : genericRibbon()}</div>
    <div class="body">
      ${s.props ? props(s) : ''}
      <div class="view" id="view"><div class="vtab"><div>${s.view === 'sheet' ? 'Sheet: ' : ''}${esc(viewTitle)}</div></div>${viewSvg(s, t)}${viewCube()}</div>
      ${s.pane ? pane(s, t) : ''}
    </div>
    <div class="statusbar">${esc(s.revitStatus ?? 'Click to select, TAB for alternates, CTRL adds, SHIFT unselects.')}</div>
  </div>${s.dialog ? dialog(s.dialog) : ''}${s.csv ? csvWindow(s) : ''}`;
}

function tabs(s) {
  const names = ['Architecture', 'Structure', 'Steel', 'Systems', 'Insert', 'Annotate', 'Analyze', 'Massing & Site', 'Collaborate', 'View', 'Manage', 'Add-Ins'];
  return `<div class="tab file">File</div>` + names.map(n => `<div class="tab ${s.tab === n ? 'active' : ''}">${n}</div>`).join('') +
    (s.installed ? `<div class="tab ${s.tab === 'pis' ? 'active' : ''}" id="tab-pis">Piping ISO Suite</div>` : '');
}

const RIBBON = [
  ['rb-dashboard', 'Dashboard', 'D', '#4682b4'], null,
  ['rb-assign', 'Assign\nLine Nos', '#', '#2e8b57'], ['rb-validate', 'Validate', '✓', '#ff8c00'], null,
  ['rb-views', 'ISO\nViews', '3D', '#9370db'], ['rb-sheets', 'ISO\nSheets', 'S', '#cd5c5c'], ['rb-export', 'EZ-ISO\nExport', 'EZ', '#008080'],
];
function pisRibbon() {
  const items = RIBBON.map(b => b ? `<div class="rbtn" id="${b[0]}"><div class="ic" style="background:${b[3]}">${b[2]}</div>${b[1]}</div>` : '<div class="rsep"></div>').join('');
  return `<div class="rpanel"><div class="items">${items}</div><div class="ptitle">Piping</div></div>`;
}
function genericRibbon() {
  const g = n => `<div class="rbtn ghost"><div class="ic"></div>${n}</div>`;
  return `<div class="rpanel"><div class="items">${['Duct', 'Duct\nFitting', 'Air\nTerminal'].map(g).join('')}</div><div class="ptitle">HVAC</div></div>
          <div class="rpanel"><div class="items">${['Pipe', 'Pipe\nFitting', 'Pipe\nAccessory', 'Plumbing\nFixture'].map(g).join('')}</div><div class="ptitle">Plumbing & Piping</div></div>
          <div class="rpanel"><div class="items">${['Mechanical\nEquipment', 'Component'].map(g).join('')}</div><div class="ptitle">Mechanical</div></div>`;
}

function props(s) {
  const p = s.propsData;
  const rows = (g, list) => `<div class="pgroup">${g}</div>` + list.map(([k, v, id]) => `<div class="prow"><div>${k}</div><div ${id ? `id="${id}"` : ''}>${esc(v)}</div></div>`).join('');
  return `<div class="props"><div class="phead">Properties</div><div class="ptype"><b>Pipe Types</b><br>Standard</div>
    ${rows('Constraints', [['Reference Level', 'Level 1'], ['Offset', '3000.0']])}
    ${rows('Mechanical', [['System Classification', p.cls], ['System Type', p.systype], ['System Name', p.sysname], ['System Abbreviation', p.abbr]])}
    ${rows('Identity Data', [['Comments', typed(s, 'comments', s._t), 'pp-comments'], ['BC_SPEC', typed(s, 'spec', s._t), 'pp-spec'], ['BC_LINENUMBER', p.line]])}</div>`;
}

function viewCube() {
  return `<svg class="viewcube" viewBox="0 0 54 54"><polygon points="27,6 47,17 27,28 7,17" fill="#e6ebf0" stroke="#8f98a3"/><polygon points="7,17 27,28 27,50 7,39" fill="#d3d9e0" stroke="#8f98a3"/><polygon points="47,17 27,28 27,50 47,39" fill="#c2cad3" stroke="#8f98a3"/><text x="27" y="20" font-size="7" text-anchor="middle" fill="#59616b">TOP</text></svg>`;
}

// ---------- model view (isometric SVG) ----------

const P = p => [0.866 * (p[0] + p[1]), 0.5 * (p[0] - p[1]) - p[2]];

function fit(points, w, h, fill = 0.82) {
  const pr = points.map(P);
  const xs = pr.map(p => p[0]), ys = pr.map(p => p[1]);
  const [x0, x1, y0, y1] = [Math.min(...xs), Math.max(...xs), Math.min(...ys), Math.max(...ys)];
  const k = Math.min((w * fill) / Math.max(1e-6, x1 - x0), (h * fill) / Math.max(1e-6, y1 - y0));
  const ox = w / 2 - k * (x0 + x1) / 2, oy = h / 2 - k * (y0 + y1) / 2;
  return { k, f: p => { const q = P(p); return [ox + k * q[0], oy + k * q[1]]; } };
}

function runPoints(id, s) {
  const r = MODEL.runs[id];
  const pts = [...r.pts];
  if (id === 'h1' && s.newPipe) pts.push([46, 0, 10]);
  return pts;
}

function viewSvg(s, t) {
  const w = 1279 - (s.props ? 221 : 0) - (s.pane ? 440 : 0), h = 524;
  if (s.view === 'sheet') return sheetSvg(s, w, h);
  const runs = s.view === 'iso' ? s.isoRuns : Object.keys(MODEL.runs);
  const pts = runs.flatMap(id => runPoints(id, s));
  if (s.view === 'model') for (const e of MODEL.equipment) pts.push(...boxCorners(e));
  const pad = s.view === 'iso' ? 2 : 0;
  const box = s.view === 'iso' ? bbox(pts, pad) : null;
  const { k, f } = fit(box ? boxCorners({ min: box.min, max: box.max }) : pts, w, h, s.view === 'iso' ? 0.78 : 0.86);
  return `<svg width="${w}" height="${h}" viewBox="0 0 ${w} ${h}">${grid(f, s)}${s.view === 'model' ? MODEL.equipment.map(e => equip(e, f)).join('') : ''}
    ${drawRuns(runs, s, t, f, k)}${box ? sectionBox(box, f) : ''}</svg>`;
}

function grid(f, s) {
  if (s.view !== 'model') return '';
  let g = '';
  for (let x = -6; x <= 48; x += 6) { const a = f([x, -16, 0]), b = f([x, 14, 0]); g += `<line x1="${a[0]}" y1="${a[1]}" x2="${b[0]}" y2="${b[1]}" stroke="#eef1f4"/>`; }
  for (let y = -16; y <= 14; y += 6) { const a = f([-6, y, 0]), b = f([48, y, 0]); g += `<line x1="${a[0]}" y1="${a[1]}" x2="${b[0]}" y2="${b[1]}" stroke="#eef1f4"/>`; }
  return g;
}

function bbox(pts, pad) {
  const mn = [0, 1, 2].map(i => Math.min(...pts.map(p => p[i])) - pad), mx = [0, 1, 2].map(i => Math.max(...pts.map(p => p[i])) + pad);
  return { min: mn, max: mx };
}
function boxCorners(b) {
  const out = [];
  for (const x of [b.min[0], b.max[0]]) for (const y of [b.min[1], b.max[1]]) for (const z of [b.min[2], b.max[2]]) out.push([x, y, z]);
  return out;
}
function equip(e, f) {
  const [a, b] = [e.min, e.max];
  const face = (ps, fill) => `<polygon points="${ps.map(p => f(p).join(',')).join(' ')}" fill="${fill}" stroke="#7c8792" stroke-width="1"/>`;
  const top = face([[a[0], a[1], b[2]], [b[0], a[1], b[2]], [b[0], b[1], b[2]], [a[0], b[1], b[2]]], '#e9edf1');
  const front = face([[a[0], a[1], a[2]], [b[0], a[1], a[2]], [b[0], a[1], b[2]], [a[0], a[1], b[2]]], '#d2d8df');
  const side = face([[b[0], a[1], a[2]], [b[0], b[1], a[2]], [b[0], b[1], b[2]], [b[0], a[1], b[2]]], '#c0c8d1');
  const c = f([(a[0] + b[0]) / 2, (a[1] + b[1]) / 2, b[2]]);
  return `${front}${side}${top}<text x="${c[0]}" y="${c[1] + 4}" font-size="10" text-anchor="middle" fill="#3a424b" font-weight="600">${e.name}</text>`;
}

function sectionBox(b, f) {
  const c = boxCorners(b), idx = [[0, 1], [2, 3], [4, 5], [6, 7], [0, 2], [1, 3], [4, 6], [5, 7], [0, 4], [1, 5], [2, 6], [3, 7]];
  return idx.map(([i, j]) => { const a = f(c[i]), d = f(c[j]); return `<line x1="${a[0]}" y1="${a[1]}" x2="${d[0]}" y2="${d[1]}" stroke="#1f7ae0" stroke-width="1.4" stroke-dasharray="5 3"/>`; }).join('') +
    c.map(p => { const q = f(p); return `<rect x="${q[0] - 3}" y="${q[1] - 3}" width="6" height="6" fill="#1f7ae0"/>`; }).join('');
}

function drawRuns(runs, s, t, f, k) {
  const sel = new Set(s.sel ?? []);
  const scale = Math.max(0.6, Math.min(1.8, k / 11));
  let under = '', main = '', over = '';
  const tagReveal = since(s, 'tags', t);
  runs.forEach((id, i) => {
    const r = MODEL.runs[id];
    const pts = runPoints(id, s);
    const color = r.sys === 'chw' ? '#2b6cb0' : '#c53030';
    const width = (2 + (r.dia / 150) * 6) * scale;
    const d = 'M' + pts.map(p => f(p).join(',')).join(' L');
    if (sel.has(id)) under += `<path d="${d}" stroke="#00a2ff" stroke-opacity=".45" stroke-width="${width + 9}" fill="none" stroke-linecap="round" stroke-linejoin="round"/>`;
    const hm = f(r.tag ?? mid(pts));
    over += `<circle id="hit-${id}" cx="${hm[0]}" cy="${hm[1]}" r="3" fill="transparent"/>`;
    main += `<path id="pipe-${id}" d="${d}" stroke="${sel.has(id) ? '#0a84e0' : color}" stroke-width="${width}" fill="none" stroke-linecap="round" stroke-linejoin="round"/>`;
    // fittings: elbows at interior vertices, tees on headers
    pts.slice(1, -1).forEach(p => { const q = f(p); main += `<circle cx="${q[0]}" cy="${q[1]}" r="${width * 0.75}" fill="#4a5560"/>`; });
    (r.tees ?? []).forEach(x => { const q = f([x, r.pts[0][1], r.pts[0][2]]); main += `<rect x="${q[0] - width}" y="${q[1] - width}" width="${width * 2}" height="${width * 2}" rx="2" fill="#4a5560"/>`; });
    if (r.valve) { const q = f(r.valve); main += `<polygon points="${q[0] - 7},${q[1] - 6} ${q[0] + 7},${q[1] + 6} ${q[0] + 7},${q[1] - 6} ${q[0] - 7},${q[1] + 6}" fill="#f2b632" stroke="#7a5a00"/>`; }
    if (id === 'h1' && s.newPipe) { const a = f([40, 0, 10]), b = f([46, 0, 10]); over += `<line x1="${a[0]}" y1="${a[1]}" x2="${b[0]}" y2="${b[1]}" stroke="${s.newPipeFlag ? '#e53935' : color}" stroke-width="${width}" stroke-linecap="round" ${s.newPipeFlag ? 'stroke-dasharray="6 4"' : ''}/>`; }
    // line number tags
    const number = (s.tagSkip ?? []).includes(id) ? null : (s.tagOverride ?? {})[id] ?? (s.numbered ? MODEL.runs[id].line : null);
    const show = s.view === 'iso' ? !!number : number && tagReveal >= i * 0.22;
    if (show && s.tagsOn !== false) {
      const m = f(r.tag ?? mid(pts)), bad = (s.badTags ?? []).includes(id);
      const wtag = number.length * 6.4 + 12;
      over += `<g><rect x="${m[0] - wtag / 2}" y="${m[1] - 26}" width="${wtag}" height="17" rx="3" fill="${bad ? '#fde8e8' : '#fff'}" stroke="${bad ? '#e53935' : '#59616b'}"/>
               <text x="${m[0]}" y="${m[1] - 14}" font-size="10.5" text-anchor="middle" font-family="DejaVu Sans Mono" fill="${bad ? '#b71c1c' : '#1f2329'}">${number}</text></g>`;
    }
    if ((s.locked ?? []).includes(id)) { const m = f(mid(pts)); over += lock(m[0] + 18, m[1] + 6, s.lockOwner ?? 'jsmith'); }
  });
  return under + main + over;
}

function mid(pts) {
  const a = pts[Math.floor((pts.length - 1) / 2)], b = pts[Math.floor((pts.length - 1) / 2) + 1] ?? a;
  return [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (a[2] + b[2]) / 2];
}

function lock(x, y, owner) {
  return `<g><rect x="${x - 7}" y="${y - 2}" width="14" height="11" rx="2" fill="#e53935"/><path d="M${x - 4},${y - 2} v-4 a4,4 0 0 1 8,0 v4" stroke="#e53935" stroke-width="2" fill="none"/>
    <rect x="${x + 10}" y="${y - 6}" width="${owner.length * 6.5 + 70}" height="17" rx="3" fill="#fff" stroke="#e53935"/><text x="${x + 15}" y="${y + 6}" font-size="10.5" fill="#b71c1c">Owned by ${owner}</text></g>`;
}

function sheetSvg(s, w, h) {
  const sw = w * 0.9, sh = sw * 594 / 841, x0 = (w - sw) / 2, y0 = (h - sh) / 2;
  const strip = sw * 0.14, m = 8;
  const area = { x: x0 + m, y: y0 + m, w: sw - strip - 2 * m, h: sh - 2 * m };
  const views = s.sheetViews ?? [];
  const rows = s.sheetRows ?? 2, cols = s.sheetCols ?? 2;
  const cw = area.w / cols, ch = area.h / rows;
  let out = `<rect x="${x0}" y="${y0}" width="${sw}" height="${sh}" fill="#fff" stroke="#3a424b" stroke-width="1.5"/>
    <rect x="${x0 + m / 2}" y="${y0 + m / 2}" width="${sw - m}" height="${sh - m}" fill="none" stroke="#3a424b"/>
    <rect x="${x0 + sw - strip}" y="${y0 + m / 2}" width="${strip - m / 2}" height="${sh - m}" fill="#f7f8fa" stroke="#3a424b"/>
    ${[0.18, 0.3, 0.42, 0.54].map(f => `<line x1="${x0 + sw - strip}" y1="${y0 + sh * f}" x2="${x0 + sw - m / 2}" y2="${y0 + sh * f}" stroke="#3a424b" stroke-width=".6"/>`).join('')}
    <text x="${x0 + sw - strip / 2}" y="${y0 + 26}" text-anchor="middle" font-size="9" font-weight="700">PLANT ROOM MEP</text>
    <text x="${x0 + sw - strip / 2}" y="${y0 + 40}" text-anchor="middle" font-size="7" fill="#59616b">A1 Metric</text>
    <text x="${x0 + sw - strip / 2}" y="${y0 + sh * 0.25}" text-anchor="middle" font-size="7" fill="#59616b">PIPING ISOMETRICS</text>
    <text x="${x0 + sw - strip / 2}" y="${y0 + sh * 0.37}" text-anchor="middle" font-size="7" fill="#59616b">Drawn: PIS  Checked: —</text>
    <text x="${x0 + sw - strip / 2}" y="${y0 + sh * 0.49}" text-anchor="middle" font-size="7" fill="#59616b">Scale: as indicated</text>
    <text x="${x0 + sw - strip / 2}" y="${y0 + sh * 0.6}" text-anchor="middle" font-size="7.5">${esc(s.sheetName)}</text>
    <text x="${x0 + sw - strip / 2}" y="${y0 + sh * 0.68}" text-anchor="middle" font-size="17" font-weight="700">${esc(s.sheetNumber)}</text>`;
  views.forEach((id, i) => {
    const r = Math.floor(i / cols), c = i % cols;
    const cx = area.x + c * cw, cy = area.y + r * ch;
    const { f } = fit(runPoints(id, s), cw, ch - 18, 0.7);
    const run = MODEL.runs[id];
    const d = 'M' + runPoints(id, s).map(p => { const q = f(p); return `${q[0] + cx},${q[1] + cy}`; }).join(' L');
    const lm = f(run.tag ?? mid(runPoints(id, s)));
    out += `<path d="${d}" stroke="#111" stroke-width="${run.role === 'Header' ? 2.6 : 1.8}" fill="none" stroke-linejoin="round"/>
      ${runPoints(id, s).slice(1, -1).map(p => { const q = f(p); return `<circle cx="${q[0] + cx}" cy="${q[1] + cy}" r="2.4" fill="#111"/>`; }).join('')}
      <text x="${lm[0] + cx}" y="${lm[1] + cy - 8}" font-size="7.5" text-anchor="middle" font-family="DejaVu Sans Mono">${run.line}</text>
      <line x1="${cx + 10}" y1="${cy + ch - 16}" x2="${cx + cw * 0.6}" y2="${cy + ch - 16}" stroke="#111"/>
      <circle cx="${cx + 18}" cy="${cy + ch - 22}" r="6" fill="none" stroke="#111"/><text x="${cx + 18}" y="${cy + ch - 19}" font-size="7" text-anchor="middle">${i + 1}</text>
      <text x="${cx + 28}" y="${cy + ch - 19}" font-size="8" font-weight="700">${esc(viewName(s, id))}</text>`;
  });
  return `<svg width="${w}" height="${h}" viewBox="0 0 ${w} ${h}"><rect width="${w}" height="${h}" fill="#7f8a95"/>${out}</svg>`;
}

// ---------- dockable pane ----------

const BADGE = { System: ['SYS', '#2F6FB3'], Header: ['HDR', '#2E8B57'], Branch: ['BR', '#5FA777'], View: ['ISO', '#7B61B8'], Sheet: ['SHT', '#C0504D'] };

function pane(s, t) {
  const tab = s.paneTab ?? 'model';
  return `<div class="pane" id="pane"><div class="phdr"><span>Piping ISO Suite</span><span>✕</span></div><div class="inner">
    <div class="tc"><div id="tab-model" class="${tab === 'model' ? 'on' : ''}">Model</div><div id="tab-settings" class="${tab === 'settings' ? 'on' : ''}">Settings</div></div>
    <div class="tbody">${tab === 'model' ? modelTab(s, t) : settingsTab(s, t)}</div>
    <div class="pstatus" id="status">${esc(s.status)}</div></div></div>`;
}

function modelTab(s, t) {
  const b = (id, text, primary) => `<span class="btn ${primary ? 'primary' : ''}" id="${id}">${text}</span>`;
  const q = typed(s, 'search', t);
  return `<div class="row"><span>Scope</span><div class="combo" id="scope">${s.scope}</div>${b('btn-refresh', 'Refresh')}</div>
    <div class="wrap">${b('btn-assign', 'Assign Line Nos', 1)}${b('btn-validate', 'Validate')}${b('btn-propagate', 'Propagate to Identical')}</div>
    <div class="wrap">${b('btn-views', 'Create ISO Views', 1)}${b('btn-sheets', 'Create Sheets', 1)}${b('btn-export', 'Export EZ-ISO')}</div>
    <div class="wrap">${b('btn-rename', 'Rename Views')}${b('btn-renumber', 'Renumber Sheets')}${b('btn-delete', 'Delete Checked')}</div>
    <div class="search" id="search">${q ? esc(q) + caretIf(s, 'search', t) : '<span class="ph">Search line numbers, systems, levels, views…</span>'}</div>
    <div class="tree" id="tree">${treeHtml(s, q)}</div>
    <div class="split"></div><div class="issues-l">Issues (double-click to select)</div>
    <div class="issues" id="issues">${(s.issues ?? []).map((i, n) => `<div class="issue ${s.issueSel === n ? 'sel' : ''}" id="is-${n}"><b>${i[0]}</b><span>${esc(i[1])}</span></div>`).join('')}</div>`;
}

function treeHtml(s, q) {
  const rows = [];
  const visit = (node, depth) => {
    const kids = node.children ?? [];
    const match = n => !q || n.text.toLowerCase().includes(q.toLowerCase()) || (n.detail ?? '').toLowerCase().includes(q.toLowerCase()) || (n.children ?? []).some(match);
    if (!match(node)) return;
    const expanded = q ? true : (s.expanded ?? []).includes(node.id);
    const checked = (s.checked ?? []).includes(node.id);
    const [badge, color] = BADGE[node.kind];
    rows.push(`<div class="tn ${s.treeSel === node.id ? 'sel' : ''}" id="tn-${node.id}" style="padding-left:${depth * 16}px">
      <span class="ex">${kids.length ? (expanded ? '▼' : '▶') : ''}</span><span class="cb ${checked ? 'on' : ''}" id="cb-${node.id}">${checked ? '✓' : ''}</span>
      <span class="badge" style="background:${color}">${badge}</span><span class="tx">${esc(node.text)}</span><span class="dt">${esc(node.detail ?? '')}</span></div>`);
    if (expanded) kids.forEach(k => visit(k, depth + 1));
  };
  (s.tree ?? []).forEach(n => visit(n, 0));
  return rows.join('');
}

const SETTINGS = [
  ['sec', 'Team presets'], ['src'], ['shared', 'Shared JSON path (network drive)'], ['btns'],
  ['sec', 'Line numbering'], ['lineParam', 'Line number parameter'], ['prefix', 'Prefix'],
  ['headerPattern', 'Header pattern  {Prefix} {System} {SystemType} {Level} {Zone} {Seq:000}'],
  ['branchPattern', 'Branch pattern  + {Header} {BranchSeq:00}'], ['startAt', 'Start sequence at'],
  ['chk:overwrite', 'Overwrite existing line numbers'], ['zoneParam', 'Zone parameter (optional)'],
  ['systemCodes', 'System codes  e.g. CHWS=CW; HWS=HW'], ['levelCodes', 'Level codes  e.g. Level 1=L01'], ['zoneCodes', 'Zone codes'],
  ['propagate', 'Parameters propagated to identical loops (; separated)'],
  ['sec', 'Isometric views'], ['viewName', 'View name rule  {LineNumber} {System} {Level} {Role} {Header}'],
  ['viewType', '3D view type (blank = first)'], ['viewTemplate', 'View template (blank = none)'], ['combo:orientation', 'Orientation'],
  ['padding', 'Section box padding (mm)'], ['scale', 'Scale 1: (0 = template/type default)'],
  ['chk:lock', 'Lock view orientation'], ['chk:update', 'Update existing iso for the same line'],
  ['sec', 'Sheets'], ['titleBlock', "Title block  'Family : Type' (blank = first loaded)"],
  ['sheetNumber', 'Sheet number rule  {Seq:000} {LineNumber} {LastLineNumber} {Count} {System}'], ['sheetName', 'Sheet name rule'],
  ['sheetStart', 'First sheet sequence'], ['g2:rows,cols', 'Viewport grid (rows × columns)'], ['combo:fill', 'Fill order'],
  ['g4:mL,mR,mT,mB', 'Margins inside title block (mm): left, right, top, bottom'],
];

function settingsTab(s, t) {
  const fld = k => `<div class="fld ${s._typing?.key === k && since(s, 'fields.' + k, t) < (s._typing.dur + 0.6) ? 'focus' : ''}" id="f-${k}">${esc(typed(s, k, t))}${caretIf(s, k, t)}</div>`;
  const html = SETTINGS.map(([k, label]) => {
    if (k === 'sec') return `<div class="sec">${label}</div>`;
    if (k === 'src') return `<div style="color:#7a818c" id="f-src">${esc(s.settingsSource)}</div>`;
    if (k === 'btns') return `<div class="wrap" style="margin-top:6px"><span class="btn primary" id="btn-save">Save</span><span class="btn" id="btn-publish">Publish to Shared</span><span class="btn" id="btn-reload">Reload from Shared</span></div>`;
    if (k.startsWith('chk:')) { const key = k.slice(4); const on = s.fields[key]; return `<div class="chk" id="f-${key}"><span class="cb ${on ? 'on' : ''}">${on ? '✓' : ''}</span>${label}</div>`; }
    if (k.startsWith('combo:')) { const key = k.slice(6); return `<div class="lbl">${label}</div><div class="combo" id="f-${key}">${esc(s.fields[key])}</div>`; }
    if (k.startsWith('g2:') || k.startsWith('g4:')) { const keys = k.slice(3).split(','); return `<div class="lbl">${label}</div><div class="${k.startsWith('g2') ? 'grid2' : 'grid4'}">${keys.map(fld).join('')}</div>`; }
    return `<div class="lbl">${label}</div>${fld(k)}`;
  }).join('');
  return `<div class="settings" id="settings"><div class="sc">${html}</div></div>`;
}

// settingsScroll is a pixel offset or a field id ("f-viewName") to bring near the top; animated after layout.
function applyScroll(s, t) {
  const sc = stage.querySelector('.settings .sc');
  if (!sc) return;
  const off = k => (typeof k === 'number' ? k : Math.max(0, (stage.querySelector('#' + k)?.offsetTop ?? 0) - 48));
  const to = off(s.settingsScroll ?? 0), from = off(s.settingsScrollFrom ?? 0);
  sc.style.top = -(from + (to - from) * ease(clamp01(since(s, 'settingsScroll', t) / 0.6))) + 'px';
}

function typed(s, key, t) {
  const v = s.fields[key] ?? '';
  if (s._typing?.key !== key) return v;
  const p = clamp01(since(s, 'fields.' + key, t) / s._typing.dur);
  return v.slice(0, Math.round(v.length * p));
}
function caretIf(s, key, t) {
  if (s._typing?.key !== key) return '';
  const el = since(s, 'fields.' + key, t);
  return el < s._typing.dur + 0.6 && Math.floor(el * 2.5) % 2 === 0 ? '<span class="caret"></span>' : '';
}

function dialog(d) {
  if (d.kind === 'save') return `<div class="modal"><div class="dlg" style="width:560px"><div class="dh">Export EZ-ISO map</div>
    <div class="db" style="font-size:12px;white-space:normal"><div style="display:flex;gap:10px"><div style="width:130px;color:#505862;line-height:2">Desktop<br>Documents<br><b>Projects</b><br>Network</div>
    <div style="flex:1;border:1px solid #d5d9de;height:130px;padding:6px;line-height:1.9">📁 Exports<br>📄 Plant_Room_MEP_Model.csv</div></div>
    <div style="display:grid;grid-template-columns:90px 1fr;gap:6px;margin-top:12px;align-items:center"><span>File name:</span><div class="fld">${esc(d.file)}</div><span>Save as type:</span><div class="combo">CSV (*.csv)</div></div></div>
    <div class="df"><span class="btn primary" id="dlg-save">Save</span><span class="btn">Cancel</span></div></div></div>`;
  const btns = (d.buttons ?? ['OK']).map(b => `<span class="btn ${b === d.primary || b === 'OK' ? 'primary' : ''}" id="dlg-${b.toLowerCase()}">${b}</span>`).join('');
  return `<div class="modal"><div class="dlg"><div class="dh">Piping ISO Suite</div><div class="db">${esc(d.text)}</div><div class="df">${btns}</div></div></div>`;
}

function csvWindow(s) {
  const rows = s.csv.map((r, i) => `<tr><td class="rh">${i + 1}</td>${r.map(c => `<td>${esc(c)}</td>`).join('')}</tr>`).join('');
  return `<div class="sheetwin"><div class="eh">Plant_Room_MEP_EZISO.csv</div><div style="padding:10px;overflow:hidden"><table class="csv">${rows}</table></div></div>`;
}

// ---------- Windows desktop & installer ----------

function desktop(s, t) {
  let html = `<div class="desk"><div class="taskbar"><i class="win"></i><i></i><i></i><i></i><i></i></div>`;
  if (s.explorer) html += `<div class="explorer"><div class="eh">Downloads</div><div class="ep"><div class="side">Home<br>Desktop<br><b>Downloads</b><br>Documents<br>This PC</div>
    <div class="files"><div class="file ${s.fileSel ? 'sel' : ''}" id="setup-file"><div class="fi">EXE</div><div><b>PipingIsoSuite-Revit2026-Setup-1.0.0.exe</b><div style="color:#6b737d">Application · 2.1 MB</div></div></div></div></div></div>`;
  if (s.screen === 'smartscreen') html += `<div class="smart"><h2>Windows protected your PC</h2><p>Microsoft Defender SmartScreen prevented an unrecognized app from starting. Running this app might put your PC at risk.</p>
    ${s.moreInfo ? '<p>App: PipingIsoSuite-Revit2026-Setup-1.0.0.exe<br>Publisher: Unknown publisher</p>' : '<span class="lnk" id="more-info">More info</span>'}
    <div class="sbtns">${s.moreInfo ? '<div id="run-anyway">Run anyway</div>' : ''}<div>Don't run</div></div></div>`;
  if (s.screen === 'mode') html += `<div class="mode"><div class="wt" style="padding:7px 12px;border-bottom:1px solid #e1e4e8">Select Setup Install Mode</div><div class="mb">
    Piping ISO Suite for Revit 2026 can be installed for all users (requires administrative privileges), or for you only.
    <div class="opt" id="mode-all"><b>Install for all users (recommended)</b>C:\\ProgramData\\Autodesk\\Revit\\Addins\\2026</div>
    <div class="opt" id="mode-me"><b>Install for me only</b>%AppData%\\Autodesk\\Revit\\Addins\\2026</div></div>
    <div style="padding:10px 22px 16px;text-align:right"><span class="btn">Cancel</span></div></div>`;
  if (s.screen?.startsWith('wiz')) html += wizard(s, t);
  return html + '</div>';
}

function wizard(s, t) {
  const page = s.screen;
  const folder = s.allUsers ? 'C:\\ProgramData\\Autodesk\\Revit\\Addins\\2026' : 'C:\\Users\\you\\AppData\\Roaming\\Autodesk\\Revit\\Addins\\2026';
  const head = { 'wiz-ready': ['Ready to Install', 'Setup is now ready to begin installing Piping ISO Suite for Revit 2026 on your computer.'],
                 'wiz-installing': ['Installing', 'Please wait while Setup installs Piping ISO Suite for Revit 2026 on your computer.'],
                 'wiz-finish': ['Completing the Piping ISO Suite for Revit 2026 Setup Wizard', 'Setup has finished installing Piping ISO Suite for Revit 2026 on your computer.'] }[page];
  let body = '';
  if (page === 'wiz-ready') body = `Click Install to continue with the installation.<div class="memo">Add-in folder:\n      ${folder}</div>`;
  if (page === 'wiz-installing') { const p = clamp01(since(s, 'screen', t) / 2.2); body = `Extracting files...<br><span style="color:#59616b">${folder}\\PipingIsoSuite\\${p < 0.5 ? 'PipingIsoSuite.Core.dll' : 'PipingIsoSuite.Revit.dll'}</span><div class="bar"><div style="width:${p * 100}%"></div></div>`; }
  if (page === 'wiz-finish') body = `The add-in will load the next time Revit 2026 starts.<br><br>Click Finish to exit Setup.`;
  const btn = page === 'wiz-ready' ? '<span class="btn">&lt; Back</span><span class="btn primary" id="wiz-next">Install</span>' : page === 'wiz-finish' ? '<span class="btn primary" id="wiz-next">Finish</span>' : '<span class="btn">Cancel</span>';
  return `<div class="wiz"><div class="wt">Setup - Piping ISO Suite for Revit 2026 version 1.0.0</div>
    <div class="wh"><div><b>${head[0]}</b><span>${head[1]}</span></div><div class="wico">ISO</div></div><div class="wb">${body}</div><div class="wf">${btn}</div></div>`;
}

// ---------- overlays: caption, cursor, ripple, focus, cards ----------

function overlays(s, t, step) {
  let html = '';
  if (step.say && step.kind !== TITLE_STEP) {
    const a = clamp01((t - step.start) / 0.3);
    // Keep captions over the model view so the pane's status line stays readable.
    const left = s.screen === 'revit' ? (s.props ? 221 : 0) : 0, width = s.screen === 'revit' ? 1279 - left - (s.pane ? 440 : 0) : 1280;
    html += `<div id="caption" style="opacity:${a};left:${left + width / 2}px;max-width:${Math.min(940, width - 40)}px">${esc(step.say)}</div>`;
  }
  const def = video.def;
  if (step.kind === TITLE_STEP || (video.steps[1] && t < video.steps[1].start + 0.5)) {
    const fade = step.kind === TITLE_STEP ? 1 : 1 - clamp01((t - video.steps[1].start) / 0.5);
    html += `<div class="card" style="opacity:${fade}"><div class="k">Piping ISO Suite for Revit 2026</div><div class="n">Tutorial ${def.n} of ${TUTORIALS.length}</div>
      <h1>${esc(def.title)}</h1><p>${esc(def.subtitle)}</p>${cardArt()}</div>`;
  }
  if (step.kind === END_STEP) {
    const next = TUTORIALS.find(v => v.n === def.n + 1);
    const a = clamp01((t - step.start) / 0.5);
    html += `<div class="card" style="opacity:${a}"><div class="k">Piping ISO Suite for Revit 2026</div><h1>${esc(def.title)}</h1>
      <p>${next ? `Up next — Tutorial ${next.n}: ${esc(next.title)}` : 'That completes the tutorial series.'}</p>${cardArt()}</div>`;
  }
  return html + `<svg id="cursor" viewBox="0 0 22 22"><path d="M3 2 L3 18 L7.5 14 L10.5 20.5 L13.2 19.3 L10.3 13 L16.5 13 Z" fill="#fff" stroke="#111" stroke-width="1.3" stroke-linejoin="round"/></svg>`;
}

function cardArt() {
  return `<svg class="pipes" width="380" height="240" viewBox="0 0 380 240"><path d="M10 200 L190 96 L370 200" stroke="#fff" stroke-width="12" fill="none" stroke-linecap="round"/>
    <path d="M100 148 L100 40 M190 96 L190 10 M280 148 L280 40" stroke="#fff" stroke-width="7" fill="none" stroke-linecap="round"/></svg>`;
}

// Cursor, hover, pressed state, ripples and focus rings need DOM positions, so they run after layout.
const posCache = new Map();
function center(target) {
  if (Array.isArray(target)) return target;
  const el = stage.querySelector(target);
  if (el) {
    const r = el.getBoundingClientRect();
    if (r.width || r.height) posCache.set(target, [r.left + r.width / 2, r.top + r.height / 2]);
  }
  return posCache.get(target);
}

function decorate(s, t) {
  applyScroll(s, t);
  // cursor path
  let pos = [900, 520], hoverTarget = null;
  for (const a of video.acts) {
    if (!a.move || a.t > t) continue;
    const to = center(a.move) ?? pos;
    const p = ease(clamp01((t - a.t) / a.dur));
    if (!a._from) a._from = pos;
    pos = [a._from[0] + (to[0] - a._from[0]) * p, a._from[1] + (to[1] - a._from[1]) * p];
    if (p >= 1) { pos = to; hoverTarget = typeof a.move === 'string' ? a.move : null; } else hoverTarget = null;
  }
  const cur = document.getElementById('cursor');
  cur.style.left = pos[0] - 3 + 'px'; cur.style.top = pos[1] - 2 + 'px';
  if (hoverTarget) stage.querySelector(hoverTarget)?.classList.add('hover');
  if (s._click && t - s._click.t < 0.22) stage.querySelector(s._click.target)?.classList.add('pressed');
  // ripples
  for (const a of video.acts) {
    if (!a.click || a.t > t) continue;
    for (const off of a.double ? [0, 0.18] : [0]) {
      const e = t - a.t - off;
      if (e < 0 || e > 0.5) continue;
      const r = 6 + e * 60, c = center(a.click) ?? pos;
      stage.insertAdjacentHTML('beforeend', `<div class="ripple" style="left:${c[0] - r}px;top:${c[1] - r}px;width:${2 * r}px;height:${2 * r}px;opacity:${1 - e / 0.5}"></div>`);
    }
  }
  // focus rings + callouts
  for (const a of video.acts) {
    if (!a.focus || t < a.t || t > a.t + (a.dur ?? 2)) continue;
    const el = stage.querySelector(a.focus);
    if (!el) continue;
    const r = el.getBoundingClientRect(), pulse = 0.6 + 0.4 * Math.sin((t - a.t) * 6);
    stage.insertAdjacentHTML('beforeend', `<div class="focus-ring" style="left:${r.left - 4}px;top:${r.top - 4}px;width:${r.width + 8}px;height:${r.height + 8}px;opacity:${pulse}"></div>`);
    if (a.label) stage.insertAdjacentHTML('beforeend', `<div class="callout" style="left:${Math.min(1100, r.left)}px;top:${r.top - 30 < 4 ? r.bottom + 8 : r.top - 30}px">${esc(a.label)}</div>`);
  }
}
