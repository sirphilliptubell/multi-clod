# Spec: Session Log — Tree View

**Status:** Designed, not yet implemented.
**Author context:** Produced from a grilling/design session on 2026-07-17. This document captures
not just *what* to build but *why* every decision was made, what alternatives were rejected, and
what concerns remain — so a future implementer has the full context and doesn't relitigate settled
questions or repeat discarded approaches.

Companion working plan (may drift; this spec is the durable record):
`~/.claude/plans/currently-the-session-log-steady-tarjan.md`.

---

## 1. Motivation

The Session Log window (`SessionLog\SessionLogWindow`) is today a **List-style viewer**: a fixed
left nav (Main Session header + a flat `SubagentsListBox`) and a right `TranscriptViewerControl`
that renders **one** selected source's entries as a flat, vertically stacked list. You inspect one
agent at a time, and there is no way to see how the main session and its subagents relate
**structurally** (who spawned whom) or **temporally** (what ran concurrently vs what blocked).

We are adding a second, **Tree-style viewer** that renders the whole run — main session plus every
subagent — on one zoom/pan canvas. The goal is to make three things visible at a glance:
1. **Which entry spawned which agent** (parent → child edges).
2. **Concurrency**: background agents run *alongside* their parent; foreground agents *block* it.
3. **Where each agent's result returned** to the parent.

The List view is wrapped unchanged; a window-level toggle switches List ↔ Tree.

---

## 2. Background: how Claude Code session logs are structured

This was reverse-engineered from real logs and is the foundation for the whole feature. A future
agent should understand this before touching the graph builder.

### 2.1 File layout
- A top-level session is `~/.claude/projects/<encoded-cwd>/<sessionId>.jsonl` (path built by
  `Validation\ClaudeProjectPath.GetSessionFilePath`).
- Subagents it spawns live in a **sibling folder** `<sessionId>/subagents/`, one pair of files per
  agent:
  - `agent-<agentId>.jsonl` — the subagent's own transcript.
  - `agent-<agentId>.meta.json` — a small sidecar:
    `{ "agentType": str, "description": str, "toolUseId": str, "parentAgentId"?: str,
       "spawnDepth": int }`.
- All subagent files are **flat** in that one `subagents/` folder regardless of nesting depth —
  there is **no** nested `subagents/subagents/` directory. Hierarchy is expressed purely through
  `parentAgentId` + `spawnDepth` in the meta files, not the filesystem.

### 2.2 Correlating a subagent to the entry that spawned it (both directions)
- **Forward (parent → child):** in the parent transcript, the spawn is an assistant `tool_use`
  block (`name` is `Agent`/`Task`) with its own `id` (a `toolu_...`). The next `user`-role entry is
  the `tool_result` for that same `tool_use_id`, and its `toolUseResult.agentId` field is the
  spawned agent's id.
- **Reverse (child → parent):** the child's `agent-<id>.meta.json` stores `toolUseId` — exactly the
  `toolu_...` id of the parent's spawning `tool_use` block. So from a child file you can find the
  precise spawning entry by matching `meta.toolUseId` to a `tool_use` `id` in the parent transcript.
- **`spawnDepth`**: 1 = spawned directly by the main session; ≥2 = spawned by another subagent.
- **`parentAgentId`**: present only when `spawnDepth ≥ 2`; names the immediate parent agent.
  Absent ⇒ parent is the main session.

### 2.3 Nested spawning is real
Subagents **can** spawn their own subagents. Confirmed in real logs (`spawnDepth:2` observed). To
reconstruct a deep lineage: start at a child's meta.json, follow `parentAgentId` up until you reach
an agent with no `parentAgentId` (depth 1, spawned by main), whose `toolUseId` resolves in the
top-level session log.

### 2.4 Foreground vs background agents (the crux of the layout)
- **Foreground (blocking):** the parent issues the spawning `tool_use`, then **blocks** — it logs
  **nothing** until the child's `tool_result` returns. In the parent transcript there are literally
  zero entries between the spawn line and the result line. Detected in the tool_use input as
  `run_in_background: false` (and structurally: result immediately follows spawn).
- **Background:** the parent keeps logging while the child runs. Its transcript has entries
  interleaved between the spawning `tool_use` and the later entry that collects the result
  (`run_in_background: true`; structurally: intervening parent entries exist).

**Key insight that drove the design:** we do **not** need to special-case foreground vs background
in the layout. A single structural rule (§5) produces the correct visual for both because the
difference is already in the data — a blocked foreground parent simply *has no entries* during the
child's run.

---

## 3. Current List-view architecture (what we reuse)

All under `src\MultiClod.App\SessionLog\`. Verified present during exploration.

- **Window:** `SessionLogWindow.xaml(.cs)` — 2-col grid, no splitter (260px left nav / `*` right).
  Left nav = pinned "Main Session" header + `SubagentsListBox` bound to
  `ObservableCollection<SessionLogSourceViewModel> subagents`. Clicking a source calls
  `Viewer.SetSource(filePath)`. Computes `mainSessionFilePath` + `sessionDir` around
  `SessionLogWindow.xaml.cs:93-94`. Re-points itself if `ClaudeSessionId` changes mid-run
  (`/clear`, `/resume`). Opened from `MainWindow.xaml.cs:1310` via `SessionLogWindowRegistry`
  (dedupes/focuses one window per session).
- **List body:** `TranscriptViewerControl.xaml(.cs)` — "Show all events" `CheckBox`, a
  `ScrollViewer`+`ItemsControl` of rows (filtered `CollectionViewSource`), a "Waiting…" text, and a
  floating **new-arrivals banner**. `SetSource` tears down the old tailer, makes a fresh
  `TranscriptRowFactory` + `TranscriptFileTailer`, and appends in 200-line batches yielding to the
  dispatcher.
- **Parse → rows:** `Parsing\TranscriptLineParser.Parse` (→ `ParsedLine`, never throws);
  `Rendering\TranscriptRowFactory.ProcessLine(rawLine)` → 0+ row VMs. tool_use↔tool_result paired
  **within one file** by `tool_use_id` via a per-factory
  `Dictionary<string,ToolCallRowViewModel> pendingToolUses`. A `tool_use` registers a
  `ToolCallRowViewModel`; a later matching `tool_result` calls `ApplyToolResult` on it **and
  produces no new row** (this matters — see §7 risk #1). Each factory is per-source.
- **Row VMs:** base `Rendering\TranscriptRowViewModel` (`Category, Timestamp, SummaryText,
  ExpandedBodyText, CopyableJson, IsExpanded, LineCostText`); subclasses `MessageTextRowViewModel`,
  `ThinkingRowViewModel`, `ToolCallRowViewModel` (`ToolName/IsPending/IsError/ApplyToolResult`),
  `SystemMetaRowViewModel`, `UnrecognizedRowViewModel`. Categories:
  `{User,Assistant,Thinking,ToolCall,SystemMeta,Unrecognized}`.
- **Templates:** `Rendering\TranscriptCategoryStyles.xaml` — one implicit `DataTemplate` per row-VM
  type (Border → Expander with body TextBox + nested "Source" JSON + copy button). **Merged
  app-wide in `App.xaml:15`**, so any `ContentControl` whose `Content` is a row VM renders the full
  card automatically — this is exactly what the Tree detail pane reuses. Copy button binds to
  `SessionLog\SessionLogCommands.CopyEntryJson`, whose `CommandBinding` is added in
  `TranscriptViewerControl`'s ctor (must be re-added wherever the template is hosted).
- **Converters/badges:** `CategoryToGlyphConverter`, `CategoryToBrushConverter` (frozen accent
  brushes: User `#3A96DD`, Assistant `#DA7756`, Thinking `#9B8AC4`, ToolCall `#D08B2C`, SystemMeta
  `#8A8A8A`, Unrecognized `#D9A93A`), `Costs\CostBadge.xaml`, `Costs\LineCostDisplay` (tri-state:
  None / `$X.XX`|`<$0.01` / `$?.??`).
- **Watchers/tailing:** `SubagentTranscriptWatcher` (raises `SubagentDiscovered` per
  `subagents/agent-*.jsonl`, preexisting-by-ctime then live), `DirectoryExistenceWatcher`,
  `Tailing\TranscriptFileTailer` (per-file live tail).
- **`.meta.json` path derivation** reference: `Import\ClaudeSessionSearchService.cs:~172`
  (`Path.ChangeExtension(path, null) + ".meta.json"`).

**Gaps that make this net-new work:**
1. `spawnDepth` / `parentAgentId` / `agentId` / the spawn `toolUseId`→agent correlation are read
   **nowhere** today. The left nav is a flat list with no hierarchy.
2. The `tool_use` `id` is **not exposed** on `ToolCallRowViewModel` (only used as a dict key, then
   discarded).
3. There is **no** zoom/pan/Canvas infrastructure anywhere in the app to build on.

---

## 4. Confirmed design decisions (with rationale, alternatives, concerns)

Each was explicitly agreed during the design session. Do not silently reverse these.

### D1 — Window-level toggle, full-body swap
A List/Tree `ToggleButton` in the window chrome swaps the **entire** window body via `Visibility`
(both bodies always present, never torn down — matches the app-wide convention where panels are
visibility-switched, not removed). List mode = today's exact 2-col layout, untouched. Tree mode = a
single full-width zoom/pan canvas, **no left nav**.
- **Why full-body, not just the right pane:** the Tree is inherently *cross-source* (main + all
  subagents in one canvas). The left nav is a per-source picker that has no meaning in Tree mode.
- **Why visibility-swap:** toggling back to List preserves the selected source + scroll position
  for free, and matches how `SessionViewHost`/`MarkdownEditor`/`SettingsView` are handled in
  `MainWindow`.
- **Rejected:** putting the toggle inside `TranscriptViewerControl` (only makes sense if List and
  Tree are views of the *same single source*, which they are not).
- **User confirmed** the left nav does **not** need to remain in Tree mode.

### D2 — Node granularity = one box per rendered row VM
Reuse `TranscriptRowFactory` output per agent file. Each box renders **minimal**: category glyph +
short type word + `CostBadge` (`LineCostText`). No summary text. System-meta rows are gated behind
the same "Show all events" toggle the List uses.
- **Why row VMs, not raw jsonl lines:** keeps tool_use+tool_result merged into one box (that box is
  also the spawn anchor — clean), matches what the user sees in List, and reuses all existing
  parsing.
- **Concern:** hiding/showing system-meta changes geometry, so the toggle must **rebuild layout**
  (not just filter a view like List does). See D7 / §7 #6.

### D3 — Structural layout, one row per entry per column (THE core algorithm)
Each agent is a column; its boxes stack top-to-bottom, one row each, in file order. Two constraints:
1. A subagent's **first** box is pinned to the same row as the parent box that spawned it (placed to
   its right).
2. The parent box that **collects the child's result** (matching `tool_result`) is forced to a row
   strictly **below** the child's last box, with a **return arrow** from the child's last box to it.
Everything else flows one row at a time.
- **Why this over a global-timestamp layout:** we explicitly chose structural ordering over
  time-proportional Y. Timestamps are fine to rely on for *ordering* (user said "not worrying about
  clock skew"), but proportional spacing would create large empty gaps for idle periods. Ordinal
  (1 row per entry) gives order without gratuitous space. The one hard ordering fact we lean on —
  "a result can't be collected before its child finishes" — is guaranteed by file order (the
  `tool_result` line always follows the spawn line), so we don't even need timestamps to get it
  right.
- **Foreground/background emerges for free** (the payoff): a blocked foreground parent has no boxes
  between spawn and result → its column is **blank** beside the child, result lands just below the
  child's tail (gap = child height, which is *meaningful* — the parent truly waited). A background
  parent's boxes fill its column **right beside** the child (that adjacency *is* the concurrency
  signal), and only the result box is pushed below the tail.
- **User's explicit goal:** "spacing should visually help the user understand what happened in what
  order, but not have extra spacing when it's not necessary." This rule satisfies both — the only
  gaps that ever appear are a blocked parent or a result waiting on its child.
- **Deferred idea:** an explicit background-concurrency cue (faint shaded band spanning the
  concurrent rows, and/or distinct arrow colors for bg vs fg). We decided the emergent spacing +
  return arrow reads clearly enough; add the band **only if** it feels ambiguous in practice. Not
  in v1.

### D4 — Depth-based lane packing (horizontal column reuse)
A subagent takes the leftmost column **at its depth** that is free for its vertical row-span
(`FirstRow..LastRow`). Non-overlapping agents reuse a lane; concurrent siblings get adjacent lanes;
deeper depth is always further right than shallower.
- **Why reuse, not one-column-per-agent:** the user explicitly wants to avoid unnecessary spacing
  **horizontally** too. A session that spawns 50 *sequential* subagents should use ~1 subagent
  column, not 50. One-column-per-agent (simpler, connectors never dodge unrelated columns) was
  **rejected** for this reason.
- **Concern:** connectors may need to route under/over unrelated columns when lanes are reused;
  handled by the connector offset/under-routing rules (D5).

### D5 — Connectors
- **Spawn connector:** horizontal line from parent spawn box (top-right) to child's first box
  (top-left). When one box spawns **multiple** children, each connector is offset vertically, uses a
  distinct color, and routes **under** the sibling boxes between them (so lines don't overlap).
- **Return connector:** an **arrow** from the child's last box → the parent's result box.
- **Palette:** fixed 6-entry set reusing the existing category accent hues. Single-child spawns use
  the first entry, no offset.
- **Concern:** multi-child-from-one-box is rare in practice (one `Task` tool_use = one agent); build
  the grouping but keep routing simple. See §7 #5.

### D6 — Detail pane docked on the RIGHT
A `GridSplitter`-separated pane on the right of the canvas. Reuses the existing row `DataTemplate`s
from `TranscriptCategoryStyles.xaml` (via a `ContentControl` bound to the selected box's row VM,
`IsExpanded=true`) to show full body + Source JSON + copy. **Single selection**; the selected box
gets a **yellow highlight border**.
- **User picked right** over bottom-docked. Inline box expansion was **rejected** (fights the
  zoom/pan model, forces a full relayout).
- **Concern:** the copy button's `CommandBinding` (`SessionLogCommands.CopyEntryJson`) must be
  re-registered in the Tree host or it silently no-ops. The reused template shares `IsExpanded`
  two-way with the same VM the List would show — harmless since only one body is visible at a time.

### D7 — Snapshot + manual refresh (NOT live)
Build the full graph from disk on entering Tree mode (and on a manual Refresh). A lightweight
watcher shows a **"New entries — Refresh"** banner mirroring the List's arrivals banner. Refresh
**preserves** current zoom scale + viewport offset (no jump to origin).
- **Why not live:** a live tree spanning N files would relayout continuously (a growing foreground
  child keeps pushing the parent's resume box down, columns grow, connectors reroute) — unusable
  while panning/zooming, and N concurrent tailers. The List already covers live watching; the Tree
  is for *inspecting* a run. Live incremental relayout was **rejected** for v1 (stable layout under
  mutation + preserving zoom/pan/selection across relayouts is a lot of complexity for little gain).
- **User asked** for the arrivals-style banner and for refresh to preserve view so they aren't
  jolted to the top-left.

### D8 — Zoom/pan
Wheel = vertical pan, Shift+wheel = horizontal pan, Ctrl+wheel = **cursor-anchored** zoom,
mouse-drag = pan any direction. **Zoom-in capped at 1.0×** (native size — user explicitly wants no
zoom past 100%; also means text never upscales/blurs). **Zoom-out floor = the computed "fit entire
graph in viewport" scale** (so you can always pull back to see everything), hard-floored at ~0.05×
for degenerate huge logs. Viewport clamped so you **can't pan/zoom past the top-left origin** (the
first main entry; small padding allowed — nothing exists up-left of `m0`). "Reset view" (button /
double-click empty space) → 1× at origin.
- **User refinements:** zoom-in limit = 1×; zoom-out = 0.2× *or* computed fit-scale (we chose
  fit-scale as the floor so the whole log always fits).

### D9 — Graph building
Discover agents via `SubagentTranscriptWatcher`; read each `agent-<id>.meta.json`
(`toolUseId`/`parentAgentId`/`spawnDepth`); parse each agent file with its **own**
`TranscriptRowFactory`. Spawn box = the `ToolCallRowViewModel` whose tool_use `id == meta.toolUseId`
(in the parent file); its matching `tool_result` = the result box.
- **Depth** should be derived from the **resolved parent chain** (`parent.Depth + 1`), using
  `meta.spawnDepth` only as a fallback/sanity check (see §7 #4).
- **Edge cases (agreed):**
  - *Orphan* (meta missing, or `toolUseId` matches no box, or spawn line not yet flushed): render the
    column anchored at depth 1, **unlinked** (muted/dashed border), **no** connector — never drop it.
  - *In-progress child* (no result yet): draw the child column, **omit** the return arrow.
  - *No subagents:* just the single main column (degenerate but valid).

---

## 5. Layout algorithm (reference)

**Phase A — row assignment (DFS from main).** One global integer row coordinate; parallel columns
may share a row number.

```
LayoutAgentRows(agent, startRow) -> lastRow:
    r = startRow;  agent.FirstRow = startRow
    for box in agent.Boxes (file order):
        Entry:          box.Row = r;  r += 1
        SubagentSpawn:  box.Row = r
                        childLast = LayoutAgentRows(box.LinkedChild, startRow = r)  # child pinned to r
                        childTail[box.LinkedChild] = childLast
                        r += 1                                                       # parent resumes at r+1
        SubagentReturn: tail = childTail[child for box.toolUseId]
                        box.Row = max(r, tail + 1)                                   # forced below child tail
                        r = box.Row + 1
    agent.LastRow = r - 1;  return agent.LastRow
```
Invariant (assert): a `SubagentReturn` is processed *after* its child is laid out (result line
follows spawn line in file order). Malformed (result before spawn) ⇒ treat child as in-progress: no
force-down, no return connector.

Worked outcomes (must match these):
- *Foreground* `[…, spawn(C), return(C), …]`: after spawn at `S`, `r=S+1`;
  `return.Row = max(S+1, childLast+1) = childLast+1`. Rows `S+1..childLast` blank in parent column.
- *Background* `[…, spawn(C), e1, e2, …, return(C), …]`: `e1=S+1, e2=S+2 …` fill parent column beside
  child; `return.Row = max(currentR, childLast+1)` pushes only the return below the tail.

**Phase B — lane packing (interval partitioning), per depth ascending.**
`columnBase[d] = columnBase[d-1] + laneCount[d-1]` (main = depth 0 = column 0). Within a depth, sort
agents by `FirstRow` then spawn-box column; assign each the **leftmost lane whose last-used row <
agent.FirstRow** (append a lane if none free); set `laneEndRow[lane] = agent.LastRow`,
`agent.Column = columnBase[d] + lane`. Every box inherits `Owner.Column` (a `SubagentReturn` is
owned by the **parent** ⇒ parent column — correct).

**Phase C — pixels + extent.** `metrics { BoxW, BoxH, HGap, VGap, Pad, ConnGap }`.
`X = Pad + Column*(BoxW+HGap)`, `Y = Pad + Row*(BoxH+VGap)`.
`PixelExtent = ( Pad + (maxCol+1)*(BoxW+HGap), Pad + (maxRow+1)*(BoxH+VGap) )` in unscaled graph
space; scaled canvas size = `PixelExtent × scale`. Recompute once per snapshot build.

**Connectors:**
- Spawn: `(spawnBox.X+BoxW, spawnBox.Y+BoxH*0.25)` → `(childFirst.X, childFirst.Y+BoxH*0.25)`. For
  child *i* of a multi-child box: vertical offset `BoxH*0.25 + i*ConnGap`, horizontal leg routed at
  `spawnBox.Y + BoxH + i*ConnGap` (under the box band), color `palette[i % len]`.
- Return: arrow `(childLast.X+BoxW/2, childLast.Y+BoxH)` → `(returnBox.X, returnBox.Y+BoxH/2)`.
  Omitted if `child.ReturnBox == null`.

---

## 6. Component breakdown (new files)

All under `src\MultiClod.App\SessionLog\Tree\` unless noted.

- **`SessionTreeView.xaml(.cs)`** — Tree body. `Grid`: `[canvas host *][splitter][detail ~420px]`,
  overlaid toolbar ("Show all events" mirrored from List, "Reset view"), bottom-center refresh
  banner cloned from `TranscriptViewerControl.NewArrivalsBanner`. Owns builder/graph/layout/watcher.
  `BuildSnapshot(preserveView)` rebuilds. Detail pane = `ScrollViewer > ContentControl`; re-registers
  `SessionLogCommands.CopyEntryJson`.
- **Zoom/pan canvas (in SessionTreeView):** `ScrollViewer x:Name="ViewPort"` (scrollbars Auto)
  wrapping `Canvas x:Name="GraphCanvas"` with a `ScaleTransform` `RenderTransform`;
  `GraphCanvas.Width/Height = extent × scale`.
- **`TreeGraph.cs`** — `Agents`, `Boxes`, `Connectors`, `PixelExtent`, `MainSession`.
- **`AgentNode.cs`** — `AgentId, FilePath, Depth, ParentAgentId, SpawnToolUseId, Boxes` (file order),
  own `TranscriptRowFactory`, `Column/FirstRow/LastRow`, `IsOrphan`, `SpawnBox`, `ReturnBox`.
- **`BoxNode.cs`** — `RowVm, Kind {Entry,SubagentSpawn,SubagentReturn}, Owner, SourceLineOrdinal,
  ToolUseId, LinkedChild, Column/Row/X/Y, IsSelected` (INotifyPropertyChanged).
- **`AgentMeta.cs` + `AgentMetaReader.cs`** — record + `TryRead(agentJsonlPath)` (path via
  `Path.ChangeExtension(path,null)+".meta.json"`), tolerant of missing `parentAgentId`; `null` on
  missing/malformed → orphan path.
- **`TreeGraphBuilder.cs`** — pure, non-WPF. Discover, read meta, parse each file with its own factory
  recording line ordinals, synthesize `SubagentReturn` boxes at parent `tool_result` lines whose
  `tool_use_id ∈ spawnToolUseIds`, then `LinkSpawns`.
- **`TreeLayoutEngine.cs`** — pure. Implements §5.
- **`ConnectorOverlay.cs`** — one `FrameworkElement` over `GraphCanvas`, `OnRender` draws all
  connectors in one pass (non-interactive → no per-edge element). `InvalidateVisual` on rebuild only.
- **`TreeSnapshotWatcher.cs`** — wraps `SubagentTranscriptWatcher` + length/mtime watch on main +
  known agent files; debounced `ChangesPending` → show refresh banner. Nothing relayouts until
  Refresh.
- **`TreeBoxStyles.xaml`** — minimal box template (Border + glyph + type word + CostBadge;
  `DataTrigger IsSelected=True` → yellow border). New `BoxTypeWordConverter` (User→"user",
  Assistant→"assistant", Thinking→"thinking", ToolCall→`ToolName`, SystemMeta→"system",
  Unrecognized→"?", SubagentReturn→"↩ result"). Merge into `App.xaml`.

**Additive edits to shared code (keep List behavior identical):**
- `Rendering\ToolCallRowViewModel.cs` — add `public string? ToolUseId { get; }`, set from ctor.
- `Rendering\TranscriptRowFactory.cs` — in `ProcessToolUse`, pass the tool_use `id` into the new
  property.
- `SessionLogWindow.xaml(.cs)` — chrome row (List/Tree toggle; Reset shown in Tree) + body grid
  hosting both `ListBodyRoot` (today's grid, unchanged) and `TreeBodyRoot` (`SessionTreeView`,
  collapsed). Toggle flips `Visibility`; first Tree switch calls
  `BuildSnapshot(mainSessionFilePath, sessionDir)` and starts the watcher.

---

## 7. Rendering / interaction decisions & rationale

- **Boxes as real elements** (not `DrawingVisual`): via `ItemsControl` with `ItemsPanel=Canvas`,
  `ItemContainerStyle` binding `Canvas.Left→X` / `Canvas.Top→Y`, item template = the minimal box
  template. Gives free hit-testing for click-select and free reuse of `CostBadge`/converters/
  selection trigger. Boxes are tiny; a few hundred are trivial.
- **Connectors in one `OnRender` overlay:** biggest perf win vs a `Path` per edge; they're
  non-interactive so need no hit-testing.
- **`RenderTransform ScaleTransform`, not `LayoutTransform`:** WPF re-rasterizes vector text through
  the render transform (crisp), and `LayoutTransform` would re-run layout every zoom step and fight
  Canvas absolute positioning. Zoom-in cap of 1× means we never upscale.
- **Pan/clamp on `ScrollViewer` offsets** (not raw `TranslateTransform`): offsets are naturally in
  `[0, scrollable]`, so "can't pan past origin" is automatic once `Canvas.Width/Height =
  extent×scale`. Manual translate would need hand-written clamping everywhere. Cursor-anchored zoom
  recomputes canvas size and adjusts both scroll offsets to keep the point under the cursor fixed.
  Fit-scale floor = `min(viewportW/extentW, viewportH/extentH)`.
- **Viewport culling: out of scope for v1.** Canvas/ItemsControl don't virtualize. Ship without it;
  only add if a real session's box count causes visible jank (then: on ScrollChanged/zoom collapse
  containers whose rect doesn't intersect the visible rect — every box rect is known). Not a blocker.

---

## 8. Risks & pitfalls (read before implementing)

1. **`tool_use`+`tool_result` merge into ONE VM (highest risk).** The layout needs the spawn box
   (at the tool_use row) and the result box (at the tool_result row) to be **two grid slots**, but
   the factory produces a single VM and returns nothing on the result line. Resolution: the builder
   synthesizes a **`SubagentReturn` box** that reuses the spawn VM but is positioned at the
   tool_result line ordinal. The builder must detect result lines itself (factory tells it nothing).
2. **Filter return candidates by `tool_use_id ∈ spawnToolUseIds`** — a subagent's own file also
   contains tool_results for its *own* tools; those must not be mistaken for a return to the parent.
3. **Ordering invariant** in `LayoutAgentRows`: a `SubagentReturn` must be laid out after its child
   subtree. Holds because the result line follows the spawn line in file order. Guard the malformed
   case (result before spawn) → in-progress (no force-down, no connector).
4. **`spawnDepth` vs actual parent chain.** Column packing uses depth; pinning uses the actual
   `SpawnBox` row. If `meta.spawnDepth` disagrees with the resolved parent's depth, columns look
   wrong. **Derive depth from `parent.Depth + 1`**, use `meta.spawnDepth` only as fallback.
5. **Multi-child from one spawn box** (D5) is rare (one `Task` = one agent); build the grouping but
   don't over-invest in routing.
6. **System-meta toggle changes geometry** — unlike List (a `CollectionView.Refresh`), the Tree must
   **rebuild layout** on toggle. Wire the checkbox to `BuildSnapshot(preserveView:true)`.
7. **No virtualization by default** — main perf watch-item for very large sessions (see §7 culling).
8. **Detail pane shares `IsExpanded`** with the List body's VM; harmless (one body visible at a time)
   but noted.
9. **Copy `CommandBinding`** must be re-registered in `SessionTreeView` or the detail pane's copy
   button silently no-ops.

---

## 9. State preservation on rebuild (`BuildSnapshot(preserveView)`)

Before rebuild capture `{ scale, ViewPort.HorizontalOffset, ViewPort.VerticalOffset, selection
identity = (owner agent id + source line ordinal) }`. After rebuild: restore scale, clamp offsets to
the new scrollable extent and re-apply, re-select the box matching the captured identity if it still
exists (else clear selection + empty detail pane). `preserveView` is **true** for Refresh and the
"Show all events" toggle; **false** for initial Tree entry (1× at origin).

---

## 10. Error handling for meta

`AgentMetaReader.TryRead` returns `null` only when the file is missing/unparseable. A parsed meta
missing `toolUseId` ⇒ orphan (rendered, unlinked, depth 1). Missing `parentAgentId` ⇒ parent = main
(normal depth-1). No user-facing error dialog; orphan columns are visually distinct (muted/dashed
border) so the state is legible. Log malformed meta via the app's existing logging path.

---

## 11. Verification (end-to-end)

Build `dotnet build src\MultiClod.App` (Debug). Run the Debug build (title has " (Debug)" suffix +
visible border; **never** kill by bare process name — filter by `*bin\x64\Debug*` per CLAUDE.md,
because Debug and Release share the process name and a name-only kill can take out the Release
install hosting the current session).

Use a real session with subagents (e.g. project `C---Gits-GS-ai-green-agg-greenforge-backend`
session `c7acf199-…` has depth-2 agents; `dcd48ded-…` has simple foreground spawns):
1. Open Session Log; confirm List mode unchanged.
2. Toggle to Tree; main column renders vertically with glyph + type + cost boxes.
3. Foreground spawn: child to the right, blank parent column beside it, result box below the child's
   tail + return arrow.
4. Background spawn: parent entries interleaved beside the child, only the result box pushed below
   the tail.
5. Depth-2 agent nests one column further right under its subagent parent.
6. Sequential subagents reuse a lane (canvas stays narrow); concurrent siblings get adjacent lanes
   with offset/colored connectors.
7. Click a box → yellow highlight + right detail pane shows full body + Source JSON; copy works.
8. Wheel = vertical pan, Shift+wheel = horizontal, Ctrl+wheel = zoom at cursor (capped 1×, floors at
   fit-scale); can't pan/zoom past top-left; Reset → 1× origin.
9. While Tree open, live session appends entries → "New entries — Refresh" banner; Refresh rebuilds
   preserving zoom + viewport.
10. Orphan/in-progress: a still-running child shows no return arrow; a missing meta.json renders an
    unlinked depth-1 column without a connector.

---

## 12. Open questions / deferred (future agents)

- **Background-concurrency band** (faint shaded region spanning concurrent rows, and/or distinct
  bg-vs-fg arrow colors) — deliberately deferred; add only if the emergent spacing reads ambiguously.
- **Viewport culling / virtualization** — deferred to a perf-driven v2.
- **Live Tree mode** — deferred; snapshot + refresh is the v1 contract.
- **Exact metrics** (`BoxW/BoxH/HGap/VGap/Pad/ConnGap`, zoom step, ConnGap) are tunable during
  implementation; none were pinned in the design session.
