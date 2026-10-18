# Tutorial videos

There are ten narrated tutorials, one per feature. Each video is an **animated walkthrough**. It does not record a live Revit session. Instead it redraws Revit's ribbon, the `Piping ISO Suite` tab, and the dashboard pane, matching `DashboardPage.xaml`, the button names, the settings fields and the status messages the add-in really shows. A small plant-room model is used throughout.

| # | Video | Covers |
|---|---|---|
| 1 | `01-install.mp4` | Installer, SmartScreen, all-users vs per-user install, ribbon tab, opening the dashboard |
| 2 | `02-dashboard.mp4` | Scopes, Refresh, how headers and branches are found, the tree, search, select in model |
| 3 | `03-line-numbers.mp4` | Numbering patterns and code maps, Assign Line Nos, the auto-created `BC_LINENUMBER` parameter |
| 4 | `04-validate.mp4` | Unassigned / Mixed / Duplicate issues, double-click to select, fixing with Assign |
| 5 | `05-iso-views.mp4` | View naming, template, orientation, padding, section box, the `PIS_LN_*` line filter |
| 6 | `06-sheets.mp4` | Title block, sheet number and name rules, viewport grid, margins, Create Sheets |
| 7 | `07-batch.mp4` | Checkbox selection, Rename Views, Renumber Sheets, Delete Checked |
| 8 | `08-propagate.mp4` | Copying parameters between identical loops |
| 9 | `09-eziso-export.mp4` | EZ-ISO CSV and JSON export and its columns |
| 10 | `10-team-worksharing.mp4` | Shared presets on a network drive, worksharing ownership checks |

Each video comes with a `.srt` subtitle file. The captions are also burned into the picture.

## Rebuilding

To rebuild you need Node 18+, Playwright with Chromium, ffmpeg, and Python with `piper-tts` plus a Piper voice. The videos were made with `en_US-lessac-medium` from `rhasspy/piper-voices`.

```bash
python3 -m venv .venv && .venv/bin/pip install piper-tts
export PIPER_PYTHON=$PWD/.venv/bin/python PIPER_MODEL=/path/to/en_US-lessac-medium.onnx
node build.mjs                            # all videos → out/*.mp4 + out/*.srt
node build.mjs 05-iso-views               # one video
node build.mjs --still 05-iso-views:30    # one PNG frame at 30 s, for checking layouts
```

* `scenes.js` holds the narration and the timed UI actions for each tutorial. Edit it to change a video.
* `engine.js` draws a frame for any time `t`. Each step lasts as long as its narration audio, so the picture always stays in sync with the voice.
* `build.mjs` creates the voice-over, captures 1920×1080 frames at 30 fps, and encodes H.264/AAC MP4 files.

If the add-in's UI or messages change, update `scenes.js` so the tutorials keep matching.
