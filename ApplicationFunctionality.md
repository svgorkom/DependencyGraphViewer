# Application Functionality – Dependency Graph Viewer

## Overview

The Dependency Graph Viewer is a WPF desktop application that visualises job
dependency graphs extracted from **SZC.MPG.SequencingGraphActions.csv** log files.
The graph builds up over time and the user can scrub through the timeline to
observe how dependencies are established.

---

## Components

### 1. GraphParser (Class Library)

| Responsibility | Details |
|---|---|
| **CSV Parsing** | Reads semicolon-delimited CSV files with columns: `Timestamp`, `Action`, `Parameters`, `Result`, `AdditionalInfo`. Malformed, empty, or unparseable rows are silently skipped so a single bad line does not abort the import. Only rows whose `Action` matches a known `GraphActionType` enum value (`AddJob`, `PickedUpFromOrtbByLift`, `AssignJob`, `RemoveJob`) are retained; all other action types are filtered out during parsing. If the `AdditionalInfo` field contains embedded semicolons, all remaining columns are joined to reconstruct the full value. |
| **Edge Extraction** | Parses the `AdditionalInfo` column for dependency edges in the format `source -> target` separated by `#`. |
| **Job Metadata** | Extracts job properties (`ID`, `SN`, `LEVEL`, `EXIT`) from the `Parameters` column of `AddJob` rows using regex. Numeric fields (`SN`, `LEVEL`) are parsed with `TryParse` and default to `0` on failure. |
| **Snapshot Building** | Given a list of parsed actions and an index, produces a `GraphSnapshot` containing all accumulated nodes and edges up to that index. `AddJob` actions add nodes; `PickedUpFromOrtbByLift` actions remove the referenced node and all its connected edges from the snapshot. |

**Public API**

- `SequencingGraphCsvParser.Parse(filePath)` → `List<GraphAction>`
- `SequencingGraphCsvParser.BuildSnapshot(actions, upToIndex)` → `GraphSnapshot`

### 2. DependencyGraphViewer (WPF Application)

| Feature | Details |
|---|---|
| **File Loading** | The user opens a CSV file via a file-open dialog. CSV parsing is performed on a background thread (`Task.Run`) so the UI remains responsive during large file imports. The window is disabled and a "Loading…" indicator is shown until parsing completes. The path of the most recently opened file is persisted to a JSON settings file in `%LOCALAPPDATA%\DependencyGraphViewer\settings.json`. On the next application launch, if the saved file still exists on disk, it is loaded automatically once the WebView2 control is ready and the initial graph snapshot (index 0) is rendered immediately. If the settings file is missing, corrupt, or the saved CSV no longer exists, the application starts normally with no file loaded. |
| **Timeline Slider** | A slider at the bottom ranges over all parsed actions (0 … N−1). Moving it recomputes and redraws the graph snapshot for that point in time. Snapshot building is also offloaded to a background thread to keep the UI responsive. The current timestamp and action description are displayed above the slider. |
| **Play / Pause** | A ▶ / ⏸ button starts or stops automatic playback through the timeline. Playback preserves the proportional timing between actions — larger time gaps produce proportionally longer pauses between steps. Because the raw timestamp deltas are often in the millisecond range, the application normalises them on file load so that the median step takes approximately 400 ms at 1× speed. When playback reaches the last action it stops automatically; pressing Play again restarts from the beginning. |
| **Speed Control** | A logarithmic speed slider (0.25× … 8×) scales the playback speed. At 1× a typical (median) step takes ~400 ms; at higher multipliers steps are proportionally faster. If the original timestamp deltas are already in a human-perceivable range (≥ 400 ms median) the normalisation factor is 1 and playback matches real time. |
| **Graph Rendering** | Graph visualisation is provided by **Cytoscape.js** running inside a **WebView2** control. The C# code serialises each `GraphSnapshot` to JSON and passes it to the Cytoscape.js `renderGraph()` function via `ExecuteScriptAsync`. Cytoscape.js renders nodes as rounded rectangles with white label text. Nodes are **colour-coded by job level** (a palette of 10 distinct hues cycles for levels 0–9+). Labels show the Job ID and, when available, SN and Level. Edges are drawn as smooth bézier curves with arrowheads. |
| **Hierarchical Layout** | The **ELK** (Eclipse Layout Kernel) layered algorithm, provided via **elkjs** and the **cytoscape-elk** Cytoscape.js extension, arranges nodes top-to-bottom based on dependency direction. ELK is configured with `BRANDES_KOEPF` node placement and `MULTI_EDGE` rank wrapping so that wide ranks are folded into multiple visual rows, preventing excessive horizontal spread when many nodes share the same depth. Node spacing is 40 px and rank spacing is 80 px. Post-layout compaction (`EDGE_LENGTH`) further reduces wasted space. A full layout with auto-fit is computed on the first render or when loading a completely new graph. On incremental updates (timeline scrubbing), the graph is updated via a diff — existing nodes keep their positions, only newly added nodes are positioned by ELK, and removed nodes/edges are cleaned up individually. |
| **Zoom & Pan** | Built-in via Cytoscape.js — mouse wheel to zoom, click-and-drag to pan. User zoom level and pan position are preserved across incremental graph updates. |
| **Reset View** | A "⟲ Fit All" button in the header bar resets zoom and pan so that all nodes fit within the visible viewport (calls `cy.fit()` with 30 px padding). The button is enabled only when a graph is loaded. |
| **Reset Layout** | A "↻ Reset Layout" button in the header bar re-runs the full ELK hierarchical layout algorithm, repositioning all nodes to their optimal locations and fitting the result to the viewport. This discards any custom positions set by dragging. The button is enabled only when a graph is loaded. |
| **Node Dragging** | Users can drag nodes to custom positions. These positions are preserved across timeline updates; only a full graph change (e.g., loading a new file) resets node positions. |
| **Interactive Highlighting** | Clicking a node highlights it and its immediate neighbours (connected edges turn red, unrelated elements are dimmed). Clicking the background resets the highlight. |
| **Tooltips** | Hovering over a node displays a tooltip showing ID, SN, Level, and Exit information. |

---

## Data Flow

```
CSV File
  │
  ▼
SequencingGraphCsvParser.Parse()   →  List<GraphAction>
  │
  ▼
SequencingGraphCsvParser.BuildSnapshot(actions, sliderIndex)  →  GraphSnapshot
  │
  ▼
MainWindow.UpdateGraph()           →  JSON → WebView2 (Cytoscape.js)
```

---

## Supported CSV Format

```
Timestamp;Action;Parameters;Result;AdditionalInfo
2026-01-05 08:03:49.582;AddJob;ID:job1-SN:1-LEVEL:1-EXIT:LD020101-ORTB:False-SHUTTLE:;-;
2026-01-05 08:03:49.585;AddJob;ID:job4-SN:2-LEVEL:1-EXIT:LD020101-ORTB:False-SHUTTLE:;-;job1 -> job4 # job2 -> job4 #
```

- **Delimiter**: semicolon (`;`)
- **Timestamp format**: `yyyy-MM-dd HH:mm:ss.fff`
- **Edge format** (in `AdditionalInfo`): `source -> target` segments separated by ` # `
