// Tutorial content: a small plant-room model, dashboard tree builders and the ten narrated scripts.
// Each step: { say: caption, speak?: TTS text, act: [[secondsIntoStep, action], ...] }.
// Actions: { set }, { tap: selector, set? }, { dbl: selector, set? }, { move, dur }, { type: fieldKey, text, dur }, { focus: selector, dur, label? }.

export const MODEL = {
  runs: {
    h1: { sys: 'chw', role: 'Header', no: 1, dia: 150, len: 20.0, el: 7, line: 'L-CHWS-L1-001', pts: [[-1, 0, 10], [40, 0, 10]], tees: [8, 20, 32], children: ['b1', 'b2', 'b3'], tag: [26, 0, 10] },
    b1: { sys: 'chw', role: 'Branch', no: 2, dia: 80, len: 7.5, el: 3, line: 'L-CHWS-L1-001-01', pts: [[8, 0, 10], [8, 10, 10], [8, 10, 6]], tag: [8, 7, 10] },
    b2: { sys: 'chw', role: 'Branch', no: 3, dia: 65, len: 8.5, el: 5, line: 'L-CHWS-L1-001-02', pts: [[20, 0, 10], [20, 12, 10], [20, 12, 6]], valve: [20, 6, 10], tag: [20, 9, 10] },
    b3: { sys: 'chw', role: 'Branch', no: 4, dia: 80, len: 7.5, el: 3, line: 'L-CHWS-L1-001-03', pts: [[32, 0, 10], [32, 10, 10], [32, 10, 6]], tag: [32, 7, 10] },
    h2: { sys: 'hw', role: 'Header', no: 5, dia: 100, len: 14.0, el: 3, line: 'L-HWS-L1-001', pts: [[0, -6, 13], [28, -6, 13]], tees: [16], children: ['b4'], tag: [8, -6, 13] },
    b4: { sys: 'hw', role: 'Branch', no: 6, dia: 50, len: 7.0, el: 3, line: 'L-HWS-L1-001-01', pts: [[16, -6, 13], [16, -15, 13], [16, -15, 9]], tag: [16, -11, 13] },
  },
  equipment: [
    { name: 'CH-1', min: [-6, -2.5, 7], max: [-1, 2.5, 12] },
    { name: 'AHU-1', min: [6, 8, 3], max: [10, 12, 6] },
    { name: 'AHU-2', min: [18, 10, 3], max: [22, 14, 6] },
    { name: 'AHU-3', min: [30, 8, 3], max: [34, 12, 6] },
    { name: 'FCU-1', min: [14.5, -17, 7], max: [17.5, -13, 9] },
  ],
};

const SYSTEMS = [
  { id: 'sys-chw', text: 'Chilled Water Supply 1', header: 'h1' },
  { id: 'sys-hw', text: 'Heating Water Supply 1', header: 'h2' },
];
const ORDER = ['h1', 'b1', 'b2', 'b3', 'h2', 'b4'];
const SHEET_OF = { h1: 0, b1: 0, b2: 0, b3: 0, h2: 1, b4: 1 };

export function viewName(s, id) {
  const r = MODEL.runs[id];
  return (s.viewRule ?? 'ISO - {LineNumber}').replace('{LineNumber}', r.line).replace('{System}', r.sys === 'chw' ? 'CHWS' : 'HWS');
}

/** Dashboard tree. o: numbered, views, sheets, override{id:number}, skip[ids], viewRule, sheetNums[], deleted[], newPipe, only[systemIds] */
export function tree(o = {}) {
  const number = id => (o.skip ?? []).includes(id) ? null : (o.override ?? {})[id] ?? (o.numbered ? MODEL.runs[id].line : null);
  const runNode = id => {
    const r = MODEL.runs[id], n = number(id);
    const el = r.el + (id === 'h1' && o.newPipe ? 1 : 0);
    const node = { id, kind: r.role, text: n ?? `(unnumbered ${r.role.toLowerCase()} #${r.no})`, detail: `Ø${r.dia} mm · ${r.len.toFixed(1)} m · ${el} el · Level 1`, children: [] };
    if (o.views && n && !(o.deleted ?? []).includes('v-' + id)) {
      const v = { id: 'v-' + id, kind: 'View', text: viewName({ viewRule: o.viewRule }, id), children: [] };
      if (o.sheets) {
        const k = SHEET_OF[id], num = (o.sheetNums ?? ['ISO-001', 'ISO-002'])[k];
        v.children.push({ id: 's-' + id, kind: 'Sheet', text: `${num} - ISOMETRIC - ${k === 0 ? 'L-CHWS-L1-001' : 'L-HWS-L1-001'}`, children: [] });
      }
      node.children.push(v);
    }
    (r.children ?? []).forEach(c => node.children.push(runNode(c)));
    return node;
  };
  return SYSTEMS.filter(sy => !o.only || o.only.includes(sy.id)).map(sy => {
    const runs = ORDER.filter(id => MODEL.runs[id].sys === MODEL.runs[sy.header].sys);
    const un = runs.filter(id => !number(id)).length;
    return { id: sy.id, kind: 'System', text: sy.text, detail: `1 network(s), ${runs.length} run(s), ${un} unnumbered`, children: [runNode(sy.header)] };
  });
}

/** Ids of a node and its descendants, as the dashboard's check cascade sets them (views do not take their sheet). */
export function cascade(nodes, id) {
  const out = [];
  const find = list => { for (const n of list) { if (n.id === id) return n; const f = find(n.children ?? []); if (f) return f; } };
  const walk = n => { out.push(n.id); if (n.kind !== 'View') (n.children ?? []).forEach(walk); };
  const n = find(nodes);
  if (n) walk(n);
  return out;
}

const UNASSIGNED = [
  ['Unassigned', 'Header run #1 (Chilled Water Supply 1): 7 element(s) without a line number'],
  ['Unassigned', 'Branch run #2 (Chilled Water Supply 1): 3 element(s) without a line number'],
  ['Unassigned', 'Branch run #3 (Chilled Water Supply 1): 5 element(s) without a line number'],
  ['Unassigned', 'Branch run #4 (Chilled Water Supply 1): 3 element(s) without a line number'],
  ['Unassigned', 'Header run #5 (Heating Water Supply 1): 3 element(s) without a line number'],
  ['Unassigned', 'Branch run #6 (Heating Water Supply 1): 3 element(s) without a line number'],
];
const ST = {
  start: 'Press Refresh to trace piping networks.',
  refresh: i => `Plant_Room_MEP: 2 network(s), 6 run(s), ${i} issue(s).`,
};
const ALL = ['h1', 'b1', 'b2', 'b3', 'h2', 'b4'];
const EXP = ['sys-chw', 'sys-hw', 'h1', 'h2'];
const EXP_VIEWS = [...EXP, 'v-h2', 'b4', 'v-b4'];
const EXP_SHEETS = [...EXP, 'v-h1', 'v-h2'];

const DEFAULT_FIELDS = {
  shared: '', lineParam: 'BC_LINENUMBER', prefix: 'L', headerPattern: '{Prefix}-{System}-{Level}-{Seq:000}', branchPattern: '{Header}-{BranchSeq:00}',
  startAt: '1', overwrite: false, zoneParam: '', systemCodes: '', levelCodes: '', zoneCodes: '', propagate: 'Comments',
  viewName: 'ISO - {LineNumber}', viewType: '', viewTemplate: '', orientation: 'SouthEast', padding: '300', scale: '0', lock: true, update: true,
  titleBlock: '', sheetNumber: 'ISO-{Seq:000}', sheetName: 'ISOMETRIC - {LineNumber}', sheetStart: '1', rows: '1', cols: '1', fill: 'RowMajor',
  mL: '25', mR: '120', mT: '15', mB: '15', search: '', comments: '', spec: '',
};

export function initialState(def) {
  const base = {
    screen: 'revit', installed: true, tab: 'pis', pane: true, paneTab: 'model', props: false, view: 'model', scope: 'EntireModel',
    status: ST.start, tree: [], expanded: [], checked: [], issues: [], sel: [], numbered: false,
    settingsSource: 'Local presets (C:\\Users\\you\\AppData\\Roaming\\PipingIsoSuite\\settings.json)',
    propsData: { cls: 'Supply Hydronic', systype: 'Chilled Water Supply', sysname: 'Chilled Water Supply 1', abbr: 'CHWS', line: '' },
    fields: { ...DEFAULT_FIELDS },
  };
  const init = def.init ?? {};
  return { ...base, ...init, fields: { ...base.fields, ...(init.fields ?? {}) } };
}

// Shared starting points
const NUMBERED = { numbered: true, tree: tree({ numbered: true }), expanded: EXP, status: ST.refresh(0), fields: { levelCodes: 'Level 1=L1' } };
const WITH_VIEWS = { ...NUMBERED, tree: tree({ numbered: true, views: true }), expanded: EXP };
const WITH_SHEETS = { ...NUMBERED, tree: tree({ numbered: true, views: true, sheets: true }), expanded: EXP_VIEWS };
const settingsTab = (scroll, extra = {}) => ({ tap: '#tab-settings', set: { paneTab: 'settings', settingsScroll: scroll, ...extra } });

export const TUTORIALS = [
  {
    id: '01-install', n: 1, title: 'Installing and launching', subtitle: 'Run the installer, choose who gets the add-in, and open the dashboard in Revit 2026.',
    init: { screen: 'desktop', explorer: true, installed: false, tab: 'Systems', pane: false },
    steps: [
      { say: 'Download the installer, PipingIsoSuite-Revit2026-Setup-1.0.0.exe. Close Revit first, then double-click the file.',
        speak: 'Download the Piping ISO Suite setup file. Close Revit first, then double-click the file.',
        act: [[0.6, { tap: '#setup-file', set: { fileSel: true } }], [3.2, { dbl: '#setup-file', set: { screen: 'smartscreen' } }]] },
      { say: "The installer isn't code-signed yet, so Windows SmartScreen may warn you. Click More info, then Run anyway.",
        act: [[3.2, { tap: '#more-info', set: { moreInfo: true } }], [5.0, { tap: '#run-anyway', set: { screen: 'mode', explorer: false } }]] },
      { say: 'Choose who gets the add-in. Install for all users puts it in ProgramData and needs administrator rights. Install for me only uses your AppData folder.',
        act: [[2.4, { focus: '#mode-all', dur: 3.6 }], [6.4, { focus: '#mode-me', dur: 2.6 }], [9.4, { tap: '#mode-all', set: { screen: 'wiz-ready', allUsers: true } }]] },
      { say: 'Setup checks that Revit 2026 is installed and shows the add-in folder it will use. Click Install.',
        act: [[0.6, { focus: '.memo', dur: 3 }], [4.0, { tap: '#wiz-next', set: { screen: 'wiz-installing' } }]] },
      { say: 'The manifest goes into the Addins 2026 folder. The add-in files go into their own PipingIsoSuite subfolder.', speak: 'The manifest goes into the add-ins twenty twenty-six folder. The add-in files go into their own Piping ISO Suite subfolder.',
        act: [[2.6, { set: { screen: 'wiz-finish' } }]] },
      { say: 'Click Finish, then start Revit 2026.', act: [[0.8, { tap: '#wiz-next', set: { screen: 'revit', installed: true } }]], min: 3 },
      { say: 'A new Piping ISO Suite tab appears on the ribbon. Its Piping panel holds every command.',
        act: [[0.8, { tap: '#tab-pis', set: { tab: 'pis' } }], [2.6, { focus: '.rpanel', dur: 3 }]] },
      { say: 'If you have elements selected, the ribbon buttons act on the networks connected to them. With nothing selected, they act on the whole model.',
        act: [[0.6, { focus: '#rb-assign', dur: 2.6 }], [3.4, { focus: '#rb-views', dur: 2.6 }], [6.2, { focus: '#rb-export', dur: 2.4 }]] },
      { say: 'Click Dashboard to open the dockable Piping ISO Suite pane. The rest of this series works from it.',
        act: [[0.8, { tap: '#rb-dashboard', set: { pane: true } }], [2.6, { focus: '#pane', dur: 3 }]] },
      { say: 'To uninstall later, open Windows Settings, then Apps, and remove Piping ISO Suite for Revit 2026.', act: [] },
    ],
  },
  {
    id: '02-dashboard', n: 2, title: 'Dashboard tour and network tracing', subtitle: 'Choose a scope, trace headers and branches, and find your way around the tree.',
    init: { status: ST.start },
    steps: [
      { say: 'The dashboard traces your piping and shows it as a tree. Start by choosing a scope.', act: [[2.4, { focus: '#scope', dur: 2.4, label: 'Scope' }]] },
      { say: 'Entire Model traces everything. Active View uses only what the current view shows. Connected To Selection traces only the networks you have selected.',
        act: [[0.4, { tap: '#scope' }], [2.6, { set: { scope: 'ActiveView' } }], [5.4, { set: { scope: 'ConnectedToSelection' } }], [9.0, { set: { scope: 'EntireModel' } }]] },
      { say: 'Click Refresh. The add-in follows pipe connectors through every pipe, fitting and accessory.',
        act: [[0.4, { tap: '#btn-refresh', set: { tree: tree(), expanded: ['sys-chw', 'sys-hw'], status: ST.refresh(6), issues: UNASSIGNED } }],
              [1.6, { set: { sel: ALL } }], [4.6, { set: { sel: [] } }]] },
      { say: "Each network's largest pipe run becomes the header. A run continues straight through tees, and each run that leaves a side outlet becomes a branch.",
        act: [[0.4, { tap: '#tn-sys-chw', set: { expanded: EXP, treeSel: 'h1', sel: ['h1'] } }], [5.2, { set: { sel: ['b1', 'b2', 'b3'], treeSel: null } }], [9.5, { set: { sel: [] } }]] },
      { say: 'The tree reads system, header, branches, then iso views and sheets. Badges show each item\u2019s type. The grey text shows size, pipe length, element count and level.',
        act: [[0.4, { focus: '#tree', dur: 4 }], [5.4, { focus: '#tn-h1 .badge', dur: 2.4 }], [8.0, { focus: '#tn-h1 .dt', dur: 3 }]] },
      { say: 'Type in the search box to filter the tree. Matching items stay visible along with their parents.',
        act: [[0.6, { tap: '#search' }], [1.5, { type: 'search', text: 'Heating', dur: 1.2 }], [5.6, { set: { 'fields.search': '' } }]] },
      { say: 'Double-click any item to select its elements in the model and zoom to them.',
        act: [[0.6, { dbl: '#tn-b2', set: { sel: ['b2'], treeSel: 'b2' } }]] },
      { say: 'To trace one network only, select a pipe on it, set the scope to Connected To Selection, and click Refresh. The run you picked becomes the header.',
        act: [[0.6, { tap: '#hit-h2', set: { sel: ['h2'], treeSel: null } }], [3.4, { tap: '#scope', set: { scope: 'ConnectedToSelection' } }],
              [6.0, { tap: '#btn-refresh', set: { tree: tree({ only: ['sys-hw'] }), expanded: ['sys-hw', 'h2'], issues: UNASSIGNED.slice(4), status: 'Plant_Room_MEP: 1 network(s), 2 run(s), 2 issue(s).' } }]] },
      { say: 'Issues found while tracing are listed at the bottom. The next tutorials show how to fix them.', act: [[0.4, { focus: '#issues', dur: 3 }]] },
    ],
  },
  {
    id: '03-line-numbers', n: 3, title: 'Assigning line numbers', subtitle: 'Set up the numbering scheme and write BC_LINENUMBER to every header and branch.',
    titleSay: 'Assigning line numbers.',
    init: { tree: tree(), expanded: EXP, status: ST.refresh(6), issues: UNASSIGNED },
    steps: [
      { say: 'Line numbers are written to the BC_LINENUMBER parameter. Open the Settings tab to set up the numbering scheme.', speak: 'Line numbers are written to the B C line number parameter. Open the Settings tab to set up the numbering scheme.',
        act: [[3.4, settingsTab('f-lineParam')]] },
      { say: 'Headers use the header pattern. Prefix, System, Level and Zone come from each run. Seq is the next free number, and :000 pads it to three digits.',
        speak: 'Headers use the header pattern. Prefix, system, level and zone come from each run. Seq is the next free number, and colon triple zero pads it to three digits.',
        act: [[0.4, { focus: '#f-headerPattern', dur: 9 }]] },
      { say: "Branches use the branch pattern. Header is the parent run's number, and BranchSeq counts the branches along it.",
        act: [[0.4, { focus: '#f-branchPattern', dur: 6 }]] },
      { say: 'Code maps shorten names. Here, Level 1 becomes L1.', speak: 'Code maps shorten names. Here, level one becomes L one.',
        act: [[0.2, { set: { settingsScroll: 'f-startAt' } }], [1.2, { tap: '#f-levelCodes' }], [2.0, { type: 'levelCodes', text: 'Level 1=L1', dur: 1.2 }]] },
      { say: 'Leave Overwrite unchecked to keep existing numbers. New numbers never reuse one that already exists anywhere in the model.',
        act: [[0.4, { focus: '#f-overwrite', dur: 4 }]] },
      { say: 'Click Save, then go back to the Model tab.',
        act: [[0.2, { set: { settingsScroll: 0 } }], [0.9, { tap: '#btn-save', set: { status: 'Settings saved locally.' } }], [2.4, { tap: '#tab-model', set: { paneTab: 'model' } }]] },
      { say: 'Check the runs you want to number, or leave everything unchecked to number every run in scope. Then click Assign Line Nos.',
        act: [[6.2, { tap: '#btn-assign', set: { numbered: true, tags: 1, tree: tree({ numbered: true }), issues: [], status: 'Numbered 6 new run(s), kept 0; wrote 24 element(s).' } }]] },
      { say: 'The chilled water header gets L-CHWS-L1-001. Its branches follow as -01, -02 and -03.',
        speak: 'The chilled water header gets L, C H W S, L one, zero zero one. Its branches follow as zero one, zero two, and zero three.',
        act: [[0.4, { focus: '#tn-h1', dur: 4.5 }], [5.0, { focus: '#tn-b1', dur: 1.6 }], [6.4, { focus: '#tn-b2', dur: 1.4 }], [7.6, { focus: '#tn-b3', dur: 1.6 }]] },
      { say: "If the project doesn't have the parameter yet, it is created automatically as a shared text parameter. Pipe added to a numbered run later inherits the run's number the next time you click Assign.",
        act: [[1.0, { set: { sel: ['h1'] } }], [6.0, { set: { sel: [] } }]] },
    ],
  },
  {
    id: '04-validate', n: 4, title: 'Validating line numbers', subtitle: 'Find unassigned, mixed and duplicate line numbers, and fix them in one click.',
    init: { ...NUMBERED },
    steps: [
      { say: 'Models change. Here someone extended the header with new pipe, and someone else copied the wrong value onto a branch.',
        act: [[2.4, { set: { newPipe: true, sel: [] } }], [5.6, { set: { tagOverride: { b3: 'L-CHWS-L1-001-02' } } }]] },
      { say: 'Click Validate. The tree and the issue list are refreshed.',
        act: [[0.6, { tap: '#btn-validate', set: { tree: tree({ numbered: true, newPipe: true, override: { b3: 'L-CHWS-L1-001-02' } }), status: ST.refresh(2),
          issues: [['Unassigned', 'Header run #1 (Chilled Water Supply 1): 1 element(s) without a line number'], ['Duplicate', 'Line number L-CHWS-L1-001-02 is used by 2 separate runs']] } }]] },
      { say: 'Unassigned means elements without a number. Mixed means one run carries several numbers. Duplicate means separate runs share a number.',
        act: [[0.4, { focus: '#issues', dur: 8 }]] },
      { say: 'Double-click an issue to select the elements involved.',
        act: [[0.4, { dbl: '#is-0', set: { issueSel: 0, newPipeFlag: true } }], [3.0, { dbl: '#is-1', set: { issueSel: 1, newPipeFlag: false, sel: ['b2', 'b3'], badTags: ['b2', 'b3'] } }]] },
      { say: 'The Validate button on the ribbon runs the same check, selects the offending elements, and lists the issues in a dialog.',
        act: [[0.6, { tap: '#rb-validate', set: { dialog: { text: '2 issue(s); offending elements are selected.\n\n• Header run #1 (Chilled Water Supply 1): 1 element(s) without a line number\n• Line number L-CHWS-L1-001-02 is used by 2 separate runs' } } }],
              [6.0, { tap: '#dlg-ok', set: { dialog: null } }]] },
      { say: "To fix both, click Assign Line Nos again. The new pipe inherits the header's number. The first run keeps the duplicated number, and the other gets the next free one.",
        act: [[1.4, { tap: '#btn-assign', set: { tagOverride: {}, badTags: [], sel: [], issueSel: null, issues: [], tree: tree({ numbered: true, newPipe: true }),
          status: 'Numbered 1 new run(s), kept 5; wrote 4 element(s).' } }], [6.5, { focus: '#tn-b3', dur: 3 }]] },
      { say: 'Validate again to confirm there are no issues. Run it before you create isometrics or export to EZ-ISO.', speak: 'Validate again to confirm there are no issues. Run it before you create isometrics or export to E Z ISO.',
        act: [[0.8, { tap: '#btn-validate', set: { status: ST.refresh(0) } }]] },
    ],
  },
  {
    id: '05-iso-views', n: 5, title: 'Creating isometric views', subtitle: 'One 3D isometric per line number, cropped to the line, with every other line hidden.',
    init: { ...NUMBERED },
    steps: [
      { say: 'Every line number gets its own 3D isometric. Open Settings and scroll to Isometric views.', act: [[2.4, settingsTab('f-viewName')]] },
      { say: 'The view name rule builds names like ISO - L-CHWS-L1-001. Choose a 3D view type and an optional view template for consistent pipe graphics.',
        speak: 'The view name rule builds names like ISO, followed by the line number. Choose a 3D view type and an optional view template for consistent pipe graphics.',
        act: [[0.4, { focus: '#f-viewName', dur: 3.6 }], [5.4, { tap: '#f-viewTemplate' }], [6.2, { type: 'viewTemplate', text: 'Piping Isometric', dur: 1.3 }]] },
      { say: 'Choose the corner to look from, the section box padding and an optional scale. Lock orientation keeps the view fixed so it can be tagged on sheets.',
        act: [[0.4, { focus: '#f-orientation', dur: 2.6 }], [3.2, { focus: '#f-padding', dur: 2.4 }], [5.6, { focus: '#f-scale', dur: 2 }], [7.8, { focus: '#f-lock', dur: 2.6 }]] },
      { say: 'With Update existing on, running it again re-crops the existing isometric instead of making a duplicate. Save, then go back to the Model tab.',
        act: [[0.4, { focus: '#f-update', dur: 4 }], [5.0, { set: { settingsScroll: 0 } }], [5.8, { tap: '#btn-save', set: { status: 'Settings saved locally.' } }], [7.4, { tap: '#tab-model', set: { paneTab: 'model' } }]] },
      { say: 'Check runs, or leave them all unchecked, and click Create ISO Views.',
        act: [[2.8, { tap: '#btn-views', set: { tree: tree({ numbered: true, views: true }), status: 'Isometric views: 6 created, 0 updated.' } }]] },
      { say: 'Each line now has an ISO view in the tree. Double-click one to open it.',
        act: [[0.6, { focus: '#tn-v-h1', dur: 2 }], [3.0, { dbl: '#tn-v-h1', set: { view: 'iso', isoRuns: ['h1'], isoTitle: 'ISO - L-CHWS-L1-001', treeSel: 'v-h1' } }]] },
      { say: 'The section box wraps the line, plus your padding. A view filter named PIS_LN plus the line number hides every other line. It also links the view to its line, even if you rename the view.',
        speak: 'The section box wraps the line, plus your padding. A view filter named P I S, L N, plus the line number, hides every other line. It also links the view to its line, even if you rename the view.',
        act: [[0.4, { focus: '#view', dur: 3.5, label: 'Section box = line extents + padding' }], [5.0, { focus: '#view', dur: 5, label: 'Filter PIS_LN_L-CHWS-L1-001 hides other lines' }]] },
      { say: 'If your template controls view filters, its settings are copied into the view once instead of being assigned, so the line filter keeps working.', act: [] },
      { say: 'Branch isometrics work the same way.',
        act: [[0.6, { dbl: '#tn-v-b2', set: { isoRuns: ['b2'], isoTitle: 'ISO - L-CHWS-L1-001-02', treeSel: 'v-b2' } }]], min: 3.5 },
    ],
  },
  {
    id: '06-sheets', n: 6, title: 'Creating sheets', subtitle: 'Place the isometrics on title-block sheets in a configurable viewport grid.',
    init: { ...WITH_VIEWS },
    steps: [
      { say: 'Next, put the isometrics on sheets. In Settings, under Sheets, enter the title block as Family : Type, or leave it blank to use the first one loaded.',
        speak: 'Next, put the isometrics on sheets. In Settings, under Sheets, enter the title block as family, colon, type, or leave it blank to use the first one loaded.',
        act: [[1.6, settingsTab('f-titleBlock')], [4.4, { tap: '#f-titleBlock' }], [5.2, { type: 'titleBlock', text: 'A1 Metric : A1', dur: 1.4 }]] },
      { say: 'Sheet numbers and names have their own rules. Seq counts up from the first sheet sequence, and LineNumber is the first line placed on the sheet.',
        act: [[0.4, { focus: '#f-sheetNumber', dur: 4 }], [4.8, { focus: '#f-sheetName', dur: 4 }]] },
      { say: "Set the viewport grid. Two rows by two columns fits four isometrics per sheet. The margins keep viewports clear of the title block's border and title strip.",
        act: [[0.4, { set: { settingsScroll: 'f-sheetStart' } }], [1.2, { tap: '#f-rows' }], [1.8, { type: 'rows', text: '2', dur: 0.3 }], [2.6, { tap: '#f-cols' }], [3.2, { type: 'cols', text: '2', dur: 0.3 }],
              [6.0, { focus: '#f-mL', dur: 1.6 }], [6.8, { focus: '#f-mR', dur: 2.6, label: 'Title strip' }]] },
      { say: 'Save, then return to the Model tab.',
        act: [[0.2, { set: { settingsScroll: 0 } }], [0.9, { tap: '#btn-save', set: { status: 'Settings saved locally.' } }], [2.2, { tap: '#tab-model', set: { paneTab: 'model' } }]] },
      { say: 'Click Create Sheets. Views are placed in line-number order, filling the grid across and then down.',
        act: [[0.8, { tap: '#btn-sheets', set: { tree: tree({ numbered: true, views: true, sheets: true }), expanded: EXP_SHEETS, status: 'Created 2 sheet(s) holding 6 iso view(s); 0 already placed.' } }]] },
      { say: 'Each view now shows its sheet in the tree. Double-click a sheet to open it.',
        act: [[0.6, { focus: '#tn-s-h1', dur: 2 }], [2.8, { dbl: '#tn-s-h1', set: { view: 'sheet', sheetViews: ['h1', 'b1', 'b2', 'b3'], sheetNumber: 'ISO-001', sheetName: 'ISOMETRIC - L-CHWS-L1-001', sheetTitle: 'ISO-001 - ISOMETRIC - L-CHWS-L1-001', treeSel: 's-h1' } }]] },
      { say: 'The heating water lines are on sheet ISO-002. Views already on a sheet are skipped, so you can run it again after adding lines.',
        speak: 'The heating water lines are on sheet ISO zero zero two. Views already on a sheet are skipped, so you can run it again after adding lines.',
        act: [[0.6, { dbl: '#tn-s-h2', set: { sheetViews: ['h2', 'b4'], sheetNumber: 'ISO-002', sheetName: 'ISOMETRIC - L-HWS-L1-001', sheetTitle: 'ISO-002 - ISOMETRIC - L-HWS-L1-001', treeSel: 's-h2' } }]] },
    ],
  },
  {
    id: '07-batch', n: 7, title: 'Managing views and sheets in bulk', subtitle: 'Rename views by rule, renumber sheets, and delete what you no longer need.',
    init: { ...WITH_SHEETS },
    steps: [
      { say: 'The dashboard can also change many views and sheets at once. Checkboxes pick the items. Checking a parent checks everything under it, except the sheet under a view.',
        act: [[2.6, { tap: '#cb-h2', set: { checked: s => cascade(s.tree, 'h2') } }], [8.4, { tap: '#cb-h2', set: { checked: [] } }]] },
      { say: 'To rename, change the view name rule. Here, the system goes first.',
        act: [[0.4, settingsTab('f-viewName')], [2.0, { tap: '#f-viewName' }], [2.6, { type: 'viewName', text: '{System} ISO {LineNumber}', dur: 1.6 }],
              [5.0, { set: { settingsScroll: 0 } }], [5.6, { tap: '#btn-save', set: { status: 'Settings saved locally.' } }]] },
      { say: 'Back on the Model tab, check both systems and click Rename Views.',
        act: [[0.4, { tap: '#tab-model', set: { paneTab: 'model' } }], [1.6, { tap: '#cb-sys-chw', set: { checked: s => cascade(s.tree, 'sys-chw') } }],
              [2.8, { tap: '#cb-sys-hw', set: { checked: s => [...s.checked, ...cascade(s.tree, 'sys-hw')] } }],
              [4.2, { tap: '#btn-rename', set: { viewRule: '{System} ISO {LineNumber}', tree: tree({ numbered: true, views: true, sheets: true, viewRule: '{System} ISO {LineNumber}' }), status: 'Renamed 6 view(s).' } }]] },
      { say: 'To renumber sheets, set the first sheet sequence and click Renumber Sheets. Checked sheets are renumbered in tree order. If none are checked, all of them are.',
        act: [[0.2, { set: { checked: [] } }], [0.6, settingsTab('f-sheetStart')], [2.0, { tap: '#f-sheetStart' }], [2.6, { type: 'sheetStart', text: '101', dur: 0.6 }],
              [4.0, { tap: '#tab-model', set: { paneTab: 'model', settingsScroll: 0 } }],
              [6.0, { tap: '#btn-renumber', set: { tree: tree({ numbered: true, views: true, sheets: true, viewRule: '{System} ISO {LineNumber}', sheetNums: ['ISO-101', 'ISO-102'] }), status: 'Renumbered 2 sheet(s).' } }]] },
      { say: 'To delete, check only what you want removed, click Delete Checked, and confirm.',
        act: [[0.4, { tap: '#cb-v-b4', set: { checked: ['v-b4'] } }], [2.0, { tap: '#btn-delete', set: { dialog: { text: 'Delete 1 iso view(s) and 0 sheet(s)?', buttons: ['Yes', 'No'], primary: 'Yes' } } }],
              [4.2, { tap: '#dlg-yes', set: { dialog: null, checked: [], tree: tree({ numbered: true, views: true, sheets: true, viewRule: '{System} ISO {LineNumber}', sheetNums: ['ISO-101', 'ISO-102'], deleted: ['v-b4'] }), status: 'Deleted 1 item(s) (2 element(s) incl. dependents).' } }]] },
      { say: 'The sheet is kept, because only the view was checked. Recreate the view any time with Create ISO Views. Right-click the tree for the same commands, plus Select in Model.',
        act: [[0.4, { focus: '#tn-b4', dur: 3 }]] },
    ],
  },
  {
    id: '08-propagate', n: 8, title: 'Propagating to identical loops', subtitle: 'Fill in one repeating loop and copy its values to every identical copy.',
    init: { ...NUMBERED, props: true, fields: { levelCodes: 'Level 1=L1' } },
    steps: [
      { say: 'Plant rooms often repeat the same loop, like these identical coil connections. Propagate copies parameter values from one loop to every identical loop.',
        act: [[2.6, { set: { sel: ['b1', 'b3'] } }], [7.6, { set: { sel: [] } }]] },
      { say: 'In Settings, list the parameters to copy, separated by semicolons. Line numbers are never copied, because identical loops are still separate lines.',
        act: [[0.4, settingsTab('f-systemCodes')], [2.0, { tap: '#f-propagate' }], [2.8, { type: 'propagate', text: 'Comments; BC_SPEC', dur: 1.4 }],
              [6.8, { set: { settingsScroll: 0 } }], [7.4, { tap: '#btn-save', set: { status: 'Settings saved locally.' } }], [8.8, { tap: '#tab-model', set: { paneTab: 'model' } }]] },
      { say: 'Fill in the values on one loop. Here, branch 01 gets a comment and a pipe spec.', speak: 'Fill in the values on one loop. Here, branch zero one gets a comment and a pipe spec.',
        act: [[0.4, { tap: '#hit-b1', set: { sel: ['b1'], 'propsData.line': 'L-CHWS-L1-001-01' } }], [2.2, { tap: '#pp-comments' }], [2.8, { type: 'comments', text: 'AHU-1 coil connection', dur: 1.4 }],
              [4.8, { tap: '#pp-spec' }], [5.4, { type: 'spec', text: 'CS-150#', dur: 0.8 }]] },
      { say: 'Select that branch in the tree, then click Propagate to Identical.',
        act: [[0.4, { tap: '#tn-b1', set: { treeSel: 'b1' } }], [2.2, { tap: '#btn-propagate', set: { sel: ['b3'], status: 'Propagated to 1 identical loop(s): 6 value(s) written.' } }]] },
      { say: 'Loops count as identical when their element types and sizes match in order, in either direction, including any branches. Branch 02 has a valve, so it is not matched.',
        speak: 'Loops count as identical when their element types and sizes match in order, in either direction, including any branches. Branch zero two has a valve, so it is not matched.',
        act: [[7.0, { focus: '#hit-b2', dur: 3, label: 'Has a valve: not identical' }]] },
      { say: 'Select branch 03 to check. It now has the same comment and spec.', speak: 'Select branch zero three to check. It now has the same comment and spec.',
        act: [[0.6, { tap: '#hit-b3', set: { sel: ['b3'], 'propsData.line': 'L-CHWS-L1-001-03' } }], [2.2, { focus: '#pp-comments', dur: 2.6 }]] },
    ],
  },
  {
    id: '09-eziso-export', n: 9, title: 'Exporting the EZ-ISO map', subtitle: 'Write one CSV row per line, with its isometric view and sheet, plus a JSON file.',
    titleSay: 'Exporting the E Z ISO map.',
    init: { ...WITH_SHEETS },
    steps: [
      { say: 'Export EZ-ISO writes the map for your EZ-ISO workflow: one row per line number, with its iso view and sheet.', speak: 'Export E Z ISO writes the map for your E Z ISO workflow: one row per line number, with its iso view and sheet.',
        act: [[2.6, { focus: '#btn-export', dur: 3 }]] },
      { say: 'Click Export EZ-ISO and choose where to save the CSV.', speak: 'Click Export E Z ISO and choose where to save the C S V.',
        act: [[0.6, { tap: '#btn-export', set: { dialog: { kind: 'save', file: 'Plant_Room_MEP_EZISO.csv' } } }],
              [3.4, { tap: '#dlg-save', set: { dialog: null, status: 'Exported 6 iso group(s) to C:\\Projects\\Plant_Room_MEP_EZISO.csv and Plant_Room_MEP_EZISO.json.' } }]] },
      { say: 'A JSON file with the same name is saved next to it, with the full element lists.', speak: 'A JSON file with the same name is saved next to it, with the full element lists.',
        act: [[0.4, { focus: '#status', dur: 4 }]] },
      { say: 'Each CSV row has the line number, role, parent line, system, level, zone, maximum size, pipe length, element count, a loop signature, the view name, the sheet number and the element IDs.',
        speak: 'Each C S V row has the line number, role, parent line, system, level, zone, maximum size, pipe length, element count, a loop signature, the view name, the sheet number and the element IDs.',
        act: [[0.4, { set: { csv: [
          ['LineNumber', 'Role', 'ParentLineNumber', 'System', 'Level', 'Zone', 'MaxDiameterMm', 'PipeLengthM', 'ElementCount', 'LoopSignature', 'ViewName', 'SheetNumber', 'ElementIds'],
          ['L-CHWS-L1-001', 'Header', '', 'CHWS', 'Level 1', '', '150', '20', '7', 'A93C5E1F02D7', 'ISO - L-CHWS-L1-001', 'ISO-001', '348211;348212;348213;…'],
          ['L-CHWS-L1-001-01', 'Branch', 'L-CHWS-L1-001', 'CHWS', 'Level 1', '', '80', '7.5', '3', '4F2A91C07B3E', 'ISO - L-CHWS-L1-001-01', 'ISO-001', '348220;348221;348222'],
          ['L-CHWS-L1-001-02', 'Branch', 'L-CHWS-L1-001', 'CHWS', 'Level 1', '', '65', '8.5', '5', '7D10B6E4C952', 'ISO - L-CHWS-L1-001-02', 'ISO-001', '348230;348231;…'],
          ['L-CHWS-L1-001-03', 'Branch', 'L-CHWS-L1-001', 'CHWS', 'Level 1', '', '80', '7.5', '3', '4F2A91C07B3E', 'ISO - L-CHWS-L1-001-03', 'ISO-001', '348240;348241;348242'],
          ['L-HWS-L1-001', 'Header', '', 'HWS', 'Level 1', '', '100', '14', '3', 'C1E8037A5F64', 'ISO - L-HWS-L1-001', 'ISO-002', '348301;348302;348303'],
          ['L-HWS-L1-001-01', 'Branch', 'L-HWS-L1-001', 'HWS', 'Level 1', '', '50', '7', '3', '2B97D4A0E81C', 'ISO - L-HWS-L1-001-01', 'ISO-002', '348310;348311;348312'],
        ] } }]] },
      { say: 'Identical loops share a loop signature, so you can group repeating isometrics. Branches 01 and 03 have the same signature here.',
        speak: 'Identical loops share a loop signature, so you can group repeating isometrics. Branches zero one and zero three have the same signature here.', act: [] },
      { say: 'The column names never change, so you only map them to your EZ-ISO import template once. Runs without a line number are left out, so validate first.',
        speak: 'The column names never change, so you only map them to your E Z ISO import template once. Runs without a line number are left out, so validate first.',
        act: [[5.4, { set: { csv: null } }]] },
    ],
  },
  {
    id: '10-team-worksharing', n: 10, title: 'Team presets and worksharing', subtitle: 'Share one set of rules through a network file, and edit safely in workshared models.',
    init: { tree: tree(), expanded: EXP, status: ST.refresh(6), issues: UNASSIGNED, fields: { levelCodes: 'Level 1=L1' } },
    steps: [
      { say: 'A team can share one set of rules for line numbering, view and sheet naming, and sheet layout. In Settings, enter a shared JSON path on a network drive.',
        act: [[3.0, settingsTab(0)], [6.0, { tap: '#f-shared' }], [6.8, { type: 'shared', text: '\\\\fileserver\\bim\\PipingIsoSuite.json', dur: 1.8 }]] },
      { say: 'Click Save, then Publish to Shared. The shared file is replaced in a single step, so nobody reads a half-written file.',
        act: [[0.6, { tap: '#btn-save', set: { status: 'Settings saved locally.' } }], [2.4, { tap: '#btn-publish', set: { status: 'Presets published to the shared file.' } }]] },
      { say: "On a colleague's machine, enter the same path and click Reload from Shared. The shared presets also load every time Revit starts.",
        act: [[3.4, { tap: '#btn-reload', set: { settingsSource: 'Shared presets (\\\\fileserver\\bim\\PipingIsoSuite.json)', status: 'Shared presets (\\\\fileserver\\bim\\PipingIsoSuite.json)' } }], [5.4, { focus: '#f-src', dur: 3 }]] },
      { say: "If the shared file can't be reached, the add-in falls back to local presets and says so here.", act: [[0.4, { focus: '#f-src', dur: 4 }]] },
      { say: 'In a workshared model, the add-in checks who owns each element before any batch edit. Here, another user owns branch 02.',
        speak: 'In a workshared model, the add-in checks who owns each element before any batch edit. Here, another user owns branch zero two.',
        act: [[0.4, { tap: '#tab-model', set: { paneTab: 'model' } }], [4.6, { set: { locked: ['b2'] } }]] },
      { say: 'Click Assign Line Nos. Elements owned by others are skipped and reported. Everything else is checked out to you first, so the batch cannot fail halfway through.',
        act: [[1.2, { tap: '#btn-assign', set: { numbered: true, tags: 1, tagSkip: ['b2'], tree: tree({ numbered: true, skip: ['b2'] }), issues: [UNASSIGNED[2]],
          status: 'Numbered 6 new run(s), kept 0; wrote 19 element(s).\n5 element(s) owned by jsmith were skipped.' } }], [4.0, { focus: '#status', dur: 4 }]] },
      { say: 'Elements changed in central are skipped too. Use Reload Latest, then run it again. Once your colleague relinquishes the branch, click Assign again to finish.',
        act: [[6.4, { set: { locked: [] } }], [7.6, { tap: '#btn-assign', set: { tagSkip: [], tree: tree({ numbered: true }), issues: [], status: 'Numbered 1 new run(s), kept 5; wrote 5 element(s).' } }]] },
    ],
  },
];
