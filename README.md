# Piping ISO Suite for Revit 2026

A Revit 2026 (.NET 8) add-in for mechanical piping. It traces headers and branches, assigns `BC_LINENUMBER`, generates one isometric view per line, places those views on sheets, and exports an EZ-ISO preparation map. The repo also builds a standalone installer `.exe`.

## Features

| Module | What it does |
|---|---|
| **A · Line numbering** | Walks pipe connectors to split each network into runs. A run goes straight through tees, using the most opposite connector pair and the larger size on a tie. The run with the largest diameter (or the run you picked) becomes the **Header**. Every other run becomes a **Branch** in a tree. Numbers come from templates such as `{Prefix}-{System}-{Level}-{Seq:000}` (header) and `{Header}-{BranchSeq:00}` (branch). The engine never reuses a number found elsewhere in the model. Existing numbers are kept unless you choose *Overwrite*. A newly drawn pipe inherits the number of the run it joins. **Validate** flags unassigned elements, runs carrying several numbers, and duplicate numbers. |
| **B · Isometrics / EZ-ISO** | Creates one 3D isometric per line number, with a section box fitted tightly around the line plus padding. It uses a SE/SW/NE/NW orientation, can lock the view, and applies a view template. A `PIS_LN_<number>` view filter hides every other line. Re-running updates the existing iso instead of duplicating it. **Export EZ-ISO** writes a CSV (one row per line) and a JSON file (with full element ids). **Propagate to Identical** finds repeating loops by topology signature and copies the chosen parameters to each copy. |
| **C · Dashboard** | A dockable WPF pane with the tree *System → Header → Branch… → ISO View → Sheet*. It has search, check-box batch selection, and batch actions: assign, create views, create sheets, rename views by rule, renumber sheets, delete, and select in model. |
| **D · Multi-user** | Before any batch edit, the add-in checks workshared elements. It skips elements owned by another user or changed in central and reports them, and it checks out the remaining elements so the edit can finish. Presets are saved locally and can be **published to or reloaded from a central JSON file** on a network drive. Writes go to a temp file first and then replace the target, so nobody reads a half-written file. |

## Solution layout

```
PipingIsoSuite.sln
├─ src/PipingIsoSuite.Core     net8.0 – no Revit refs: graph analysis, numbering, validation,
│                              templates, grid layout, iso math, EZ-ISO export, JSON config
├─ src/PipingIsoSuite.Revit    net8.0-windows – IExternalApplication + ribbon, commands,
│   ├─ Services/               Revit adapters (graph builder, worksharing guard, views, sheets)
│   ├─ UI/                     MVVM: DashboardViewModel, TreeItem, DashboardPage.xaml
│   └─ Infrastructure/         ExternalEvent bridge (RevitTask), ObservableObject/RelayCommand
├─ tests/PipingIsoSuite.Core.Tests   xunit tests (they run on any OS)
├─ PipingIsoSuite.addin        Revit manifest
├─ installer/PipingIsoSuite.iss      Inno Setup 6 script
└─ build/build.ps1             build → test → package installer
```

The Revit API comes from the `Nice3point.Revit.Api.*` 2026 reference packages and is not copied to the output. There are no third-party runtime dependencies: the UI uses plain WPF and JSON uses `System.Text.Json`. This avoids assembly version clashes with other add-ins loaded into the same Revit process.

## Build and install

You need the .NET 8 SDK, plus Inno Setup 6 for the installer (`winget install JRSoftware.InnoSetup`).

```powershell
./build/build.ps1 -Version 1.0.0     # → dist/PipingIsoSuite-Revit2026-Setup-1.0.0.exe
```

The CI workflow (`.github/workflows/build.yml`) runs the tests on Linux. It then builds the installer on Windows and uploads it as the `PipingIsoSuite-Installer` artifact.

The installer looks for Revit 2026 in the registry (`HKLM\SOFTWARE\Autodesk\Revit\2026\*\InstallationLocation`) and under `Program Files\Autodesk\Revit 2026`. If Revit is not found, it warns you before continuing. It installs to one of:

* **All users** (admin): `C:\ProgramData\Autodesk\Revit\Addins\2026\`
* **Current user**: `%AppData%\Autodesk\Revit\Addins\2026\`

You choose on the first page. For silent installs, pass `/ALLUSERS` or `/CURRENTUSER` together with `/VERYSILENT`. The installer places the manifest in the `2026` folder and the DLLs in its `PipingIsoSuite\` subfolder.

## Using it

`docs/tutorials/` holds ten narrated tutorial videos, one per feature, and the scripts that render them. See [docs/tutorials/README.md](docs/tutorials/README.md).


1. **Piping ISO Suite → Dashboard**, then **Refresh**. Scope options are *EntireModel*, *ActiveView* or *ConnectedToSelection*. With *ConnectedToSelection*, the run you picked becomes the header.
2. **Assign Line Nos** acts on the checked runs, or on every run in scope if none are checked. The add-in creates `BC_LINENUMBER` as a shared text parameter on pipes, fittings and accessories if it is missing. Its GUID is fixed by its name, so it is the same in every project.
3. **Validate**. Double-click an issue to select the offending elements.
4. **Create ISO Views**, then **Create Sheets** (rows × columns of viewports inside the title block margins).
5. **Export EZ-ISO** writes `<name>.csv` and `<name>.json`.

The ribbon buttons run the same actions without the pane. If you have a selection, they act on the connected networks; otherwise they act on the whole model.

### Presets

Settings are stored in `%AppData%\PipingIsoSuite\settings.json`. If you set a shared path such as `\\server\bim\PipingIsoSuite.json`, the presets are loaded from that file at startup. **Publish to Shared** pushes your copy to the share. The schema is `SuitePresets` in `src/PipingIsoSuite.Core/Config/SuitePresets.cs`.

### EZ-ISO map columns

`LineNumber, Role, ParentLineNumber, System, Level, Zone, MaxDiameterMm, PipeLengthM, ElementCount, LoopSignature, ViewName, SheetNumber, ElementIds`. This is a neutral format. Map these columns to your EZ-ISO import template.

## Notes and limits

* If the chosen view template controls *V/G Filters*, its settings are copied into the view once instead of being assigned. Otherwise the line-isolation filter would be overridden.
* Only connections between pipes, pipe fittings and pipe accessories are traced. Equipment ends a run.
* The installer and the Revit-side code compile against the Revit 2026 API. Only `PipingIsoSuite.Core` is covered by automated tests, so check the Revit workflows in a test model before production use.
