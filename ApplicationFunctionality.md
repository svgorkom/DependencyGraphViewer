# Application Functionality – Dependency Graph Viewer

**This tool is AI generated.**

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
| **Job Metadata** | Extracts job properties (`ID`, `SN`, `LEVEL`, `EXIT`) from the `Parameters` column of `AddJob` rows. `SN` is parsed with `TryParse` and defaults to `0` on failure. `LEVEL` is stored as a string: if the `LEVEL:` key is present and contains a valid integer it is kept as-is (e.g. `"0"`, `"1"`); if the key is missing, empty, or unparseable the level is set to `"Unknown"`. Nodes implicitly created from edge references (i.e. without a corresponding `AddJob` action) also receive a level of `"Unknown"`. |
| **Snapshot Building** | Given a list of parsed actions and an index, produces a `GraphSnapshot` containing all accumulated nodes and edges up to that index. `AddJob` actions add nodes; `PickedUpFromOrtbByLift` actions remove the referenced node and all its connected edges from the snapshot. The node ID is extracted from the `Parameters` column: if it contains the `ID:` key-value prefix the value after the prefix is used, otherwise the entire parameter string is treated as a bare job ID. |

**Public API**

- `SequencingGraphCsvParser.Parse(filePath)` → `List<GraphAction>`
- `SequencingGraphCsvParser.BuildSnapshot(actions, upToIndex)` → `GraphSnapshot`

### 2. DependencyGraphViewer (WPF Application)

| Feature | Details |
|---|---|
| **Window** | The application window uses a **custom Windows Chrome** title bar via `WindowChrome`. The default OS title bar is replaced with a custom one that displays an app icon and title on the left, and minimize / maximize-restore / close buttons on the right (using **Segoe MDL2 Assets** glyphs). A pill-shaped **"🤖 AI-Generated"** badge is displayed next to the title to clearly indicate AI provenance. The close button shows a red hover state matching Windows 11 conventions. The title bar area is draggable and supports standard window management (double-click to maximize/restore, drag to move, snap layouts). The window starts **maximized**; the defined width (1100) and height (750) are retained as the restore size. The maximize/restore button glyph toggles automatically when the window state changes. |
| **AI Disclaimer Footer** | A thin footer bar is displayed at the very bottom of the window with the text *"🤖 This application was generated with AI assistance. Verify outputs independently."* The footer is always visible regardless of application state. |
| **File Loading** |
| **Timeline Slider** | A slider at the bottom ranges over all parsed actions (0 … N−1). Moving it recomputes and redraws the graph snapshot for that point in time. Snapshot building is also offloaded to a background thread to keep the UI responsive. The current timestamp and action description are displayed above the slider. |
| **Play / Pause** | A ▶ / ⏸ button starts or stops automatic playback through the timeline. Playback preserves the proportional timing between actions — larger time gaps produce proportionally longer pauses between steps. Because the raw timestamp deltas are often in the millisecond range, the application normalises them on file load so that the median step takes approximately 400 ms at 1× speed. When playback reaches the last action it stops automatically; pressing Play again restarts from the beginning. |
| **Speed Control** | A −/+ button pair allows the user to set an integer playback speed multiplier from 1× to 20×. At 1× a typical (median) step takes ~400 ms; at higher multipliers steps are proportionally faster. If the original timestamp deltas are already in a human-perceivable range (≥ 400 ms median) the normalisation factor is 1 and playback matches real time. |
| **Graph Rendering** | Graph visualisation is provided by **Cytoscape.js** running inside a **WebView2** control. The C# code serialises each `GraphSnapshot` to JSON and passes it to the Cytoscape.js `renderGraph()` function via `ExecuteScriptAsync`. Cytoscape.js renders nodes as rounded rectangles with white label text. Nodes are **colour-coded by job level** (a palette of 10 distinct hues cycles for numeric levels 0–9+; nodes with an `"Unknown"` level are rendered in a neutral grey). Labels show the Job ID and, when available, SN and Level. Edges are drawn as smooth bézier curves with arrowheads. |
| **Colour Legend** | A collapsible legend overlay is displayed in the top-right corner of the graph area. It shows a colour swatch and label for each job level that is currently present in the graph. The legend updates automatically whenever the graph changes (timeline scrubbing, file load, or clear). An ℹ / × toggle button shows or hides the legend panel so it does not obstruct the graph when not needed. |
| **Hierarchical Layout** | The **ELK** (Eclipse Layout Kernel) layered algorithm, provided via **elkjs** and the **cytoscape-elk** Cytoscape.js extension, arranges nodes top-to-bottom based on dependency direction. ELK is configured with `BRANDES_KOEPF` node placement and `MULTI_EDGE` rank wrapping so that wide ranks are folded into multiple visual rows, preventing excessive horizontal spread when many nodes share the same depth. Node spacing is 40 px and rank spacing is 80 px. Post-layout compaction (`EDGE_LENGTH`) further reduces wasted space. A full layout with auto-fit is computed on the first render or when loading a completely new graph. On incremental updates (timeline scrubbing), the graph is updated via a diff — existing nodes keep their positions, newly added nodes are placed near their connected neighbours (below parents / above children in the layered direction, with horizontal spread to avoid overlap), and removed nodes/edges are cleaned up individually. Any previously running layout is cancelled before starting a new one to prevent race conditions. |
| **Zoom & Pan** | Built-in via Cytoscape.js — mouse wheel to zoom, click-and-drag to pan. User zoom level and pan position are preserved across incremental graph updates. |
| **Reset View** | A "⟲ Fit All" button in the header bar resets zoom and pan so that all nodes fit within the visible viewport (calls `cy.fit()` with 30 px padding). The button is enabled only when a graph is loaded. |
| **Reset Layout** | A "↻ Reset Layout" button in the header bar re-runs the full ELK hierarchical layout algorithm, repositioning all nodes to their optimal locations and fitting the result to the viewport. This discards any custom positions set by dragging. The button is enabled only when a graph is loaded. |
| **Node Dragging** | Users can drag nodes to custom positions. These positions are preserved across timeline updates; only a full graph change (e.g., loading a new file) resets node positions. |
| **Interactive Highlighting** | Clicking a node highlights it and all neighbours within a configurable **highlight depth** (connected edges turn red, unrelated elements are dimmed). At depth 1 (default) only immediate neighbours are highlighted; at depth 2 the dependencies of dependencies are included as well, and so on up to a maximum depth of 10. A −/+ button pair labelled "Highlight Depth" in the timeline area lets the user adjust the depth at any time; the change takes effect on the next node click. Clicking the background resets the highlight. |
| **Job List Panel** | A resizable panel to the left of the graph viewport lists all jobs present in the current graph snapshot, sorted alphabetically (case-insensitive). The panel includes a **search box** that supports wildcard patterns: `*` matches any sequence of characters and `?` matches a single character (matching is case-insensitive). The filter is applied as the user types; the panel displays a count of matched/total jobs. Selecting a job in the list highlights that node on the graph using the same highlight-depth logic as clicking a node directly, and smoothly pans the viewport to centre the selected node. The job list is refreshed each time the graph snapshot changes (timeline scrubbing, file load). A `GridSplitter` between the panel and graph allows the user to resize the panel width (default 250 px, min 150 px, max 500 px). A horizontal `GridSplitter` between the job list and exit list panels allows the user to resize the vertical split. |
| **Exit List Panel** | Displayed below the Job List Panel within the same left-side column, the Exit List Panel shows all distinct exit values extracted from the current graph snapshot, sorted alphabetically (case-insensitive). It includes a **search box** with the same wildcard support as the job panel (`*` and `?`, case-insensitive) and a matched/total count label. Selecting an exit highlights **all nodes that share that exit value** on the graph, using the same highlight-depth and dim/highlight visual logic. The viewport smoothly fits to show all matching nodes. Selecting an exit clears any active job selection and vice-versa, ensuring only one type of highlight is active at a time. The exit list is refreshed each time the graph snapshot changes. Empty or null exit values are excluded from the list. |
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
