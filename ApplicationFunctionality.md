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
| **File Loading** | The user opens a CSV file via a file-open dialog. |
| **Timeline Slider** | A slider at the bottom ranges over all parsed actions (0 … N−1). Moving it recomputes and redraws the graph snapshot for that point in time. The current timestamp and action description are displayed above the slider. |
| **Play / Pause** | A ▶ / ⏸ button starts or stops automatic playback through the timeline. Playback preserves the proportional timing between actions — larger time gaps produce proportionally longer pauses between steps. Because the raw timestamp deltas are often in the millisecond range, the application normalises them on file load so that the median step takes approximately 400 ms at 1× speed. When playback reaches the last action it stops automatically; pressing Play again restarts from the beginning. |
| **Speed Control** | A logarithmic speed slider (0.25× … 8×) scales the playback speed. At 1× a typical (median) step takes ~400 ms; at higher multipliers steps are proportionally faster. If the original timestamp deltas are already in a human-perceivable range (≥ 400 ms median) the normalisation factor is 1 and playback matches real time. |
| **Graph Rendering** | Graph visualisation is provided by the **AutomaticGraphLayout (MSAGL)** WPF control (`GraphViewer`). Nodes are rendered as blue rounded boxes with white label text showing the Job ID (and SN/Level when available). Edges are drawn as routed spline curves with arrowheads. The layout, edge routing, zoom, and pan are handled by MSAGL. |
| **Hierarchical Layout** | MSAGL's Sugiyama (layered) layout arranges nodes top-to-bottom based on dependency direction. Node separation and minimum node dimensions are tuned for compact horizontal packing so the graph makes better use of the viewport. Layout is recomputed each time the graph snapshot changes. |
| **Zoom & Pan** | Built-in via MSAGL — mouse wheel to zoom, click-and-drag to pan. |

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
MainWindow.UpdateGraph()           →  MSAGL GraphViewer (nodes + edges)
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
