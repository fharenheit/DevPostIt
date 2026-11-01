# Hunmin Notes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows Markdown note application with a central manager and independent desktop sticky-note windows, preserving `.md` as the source of truth.

**Architecture:** WPF hosts desktop windows and system integration; a versioned WebView2 bridge hosts the TypeScript Markdown editor. `NoteService` serializes writes to Markdown files, updates a rebuildable SQLite index, and reconciles external file changes.

**Tech Stack:** C#, .NET, WPF, WebView2, TypeScript, Vite, SQLite/FTS5, xUnit, Playwright (editor tests).

**Spec:** `docs/superpowers/specs/2026-11-01-hunmin-notes-design.md` (approved design; copy from `DevPostIt_Detailed_Design_v0.1.md` before beginning).

## Global Constraints

- Windows 10/11, C# WPF + WebView2.
- Central manager plus multiple independent sticky windows; at most one sticky window per note in MVP.
- WYSIWYG and Markdown source modes; verify unsupported Markdown is never silently lost.
- `.md` files are authoritative for note content and portable metadata; SQLite stores index/UI state.
- No user accounts, cloud sync backend, mobile or AI features in initial release.
- No copying source code or embedded assets from the legacy app; REA output is observational evidence only.
- Use latest supported .NET LTS and pinned dependencies as verified on development machine; lock versions in checked-in manifests.
- Every implementation task uses TDD and ends with relevant tests and a commit. Do not claim test success without executing on Windows.

## Review Focus

1. Korean IME composition across mode switches: no duplicated or dropped Hangul (Task 3).
2. External edit during local unsaved edits: preserve both versions and present a conflict (Task 7).
3. Markdown with unsupported HTML/extension: source remains byte-identical unless explicitly edited (Task 3).
4. Monitor removed or DPI altered: sticky window returns inside visible work area (Task 9).
5. Crash/low disk during save: previous valid file survives and unsaved draft is recoverable (Task 6).

---

## Repository layout

```text
docs/superpowers/specs/2026-11-01-hunmin-notes-design.md
docs/superpowers/plans/2026-11-01-hunmin-notes.md
docs/research/REA_INVESTIGATION.md
DevPostIt.sln
src/DevPostIt.App/{App.xaml,App.xaml.cs,MainWindow.xaml,MainWindow.xaml.cs}
src/DevPostIt.App/Windows/{StickyWindow.xaml,StickyWindow.xaml.cs,WindowPlacement.cs}
src/DevPostIt.Core/{Note.cs,NoteService.cs,INoteRepository.cs,NoteChange.cs}
src/DevPostIt.Storage/{MarkdownNoteRepository.cs,FrontmatterCodec.cs,SqliteNoteIndex.cs,FileReconciler.cs,AtomicFileWriter.cs}
src/DevPostIt.EditorHost/{EditorBridge.cs,EditorMessage.cs}
editor-web/src/{main.ts,editor.ts,bridge.ts,markdown.ts}
tests/DevPostIt.Core.Tests/
tests/DevPostIt.Storage.Tests/
tests/DevPostIt.App.Tests/
editor-web/tests/
```

## Task 0: REA investigation and evidence baseline (independent research track)

**Files:** Create `docs/research/REA_INVESTIGATION.md`, `docs/research/BEHAVIOR_MATRIX.md`, `docs/research/EVIDENCE_INDEX.md`.

**Interfaces:** Produces verified acceptance scenarios, not production code. Runs only if an authorized original executable and a disposable Windows VM are provided.

- [ ] Record original version, SHA-256, install directory, architecture and VM snapshot ID; define a clean set of test notes.
- [ ] Observe menus, shortcuts, tray, sticky window move/resize/topmost, close versus delete, restart, multi-monitor behavior; store reproduction steps and evidence IDs.
- [ ] Record discovered storage location, changes during save/restart, and unverified assumptions in separate columns.
- [ ] Convert each verified behavior to `Given/When/Then` cases in `BEHAVIOR_MATRIX.md`.
- [ ] Review for copyrighted code/assets and avoid incorporating them in the new product.
- [ ] Commit `docs/research/*` with `docs: record REA behavior baseline`.

## Task 1: Windows solution and headless domain

**Files:** Create solution, `src/DevPostIt.Core/Note.cs`, `NoteChange.cs`, `INoteRepository.cs`, `tests/DevPostIt.Core.Tests/NoteTests.cs`; include project config and minimal WPF startup.

**Interfaces:** `record Note(Guid Id, string RelativePath, string Title, string Markdown, IReadOnlyList<string> Tags, DateTimeOffset Created, DateTimeOffset Updated)`; `record NoteChange(Guid NoteId, long Revision, string Markdown)`; `INoteRepository.GetAsync(Guid,CancellationToken)`, `SaveAsync(Note,CancellationToken)`.

- [ ] Write failing `NoteTests`: new note gets distinct stable ID; title and tags round-trip in domain; empty body allowed.
- [ ] Run `dotnet test DevPostIt.sln --filter FullyQualifiedName~NoteTests`; expect FAIL.
- [ ] Implement domain records/interfaces and solution projects; no persistence in Core.
- [ ] Run same test; expect PASS. Build WPF `dotnet build DevPostIt.sln` on Windows; expect success.
- [ ] Commit `feat: establish WPF solution and note domain`.

## Task 2: Markdown/frontmatter codec

**Files:** Create `src/DevPostIt.Storage/FrontmatterCodec.cs`, `MarkdownNoteRepository.cs`; test `tests/DevPostIt.Storage.Tests/FrontmatterCodecTests.cs`.

**Interfaces:** `FrontmatterCodec.Parse(string raw, string relativePath) -> Note`; `FrontmatterCodec.Serialize(Note note) -> string`; `MarkdownNoteRepository : INoteRepository`.

- [ ] Write failing tests: `id/title/created/updated/tags` round-trip; Unicode Korean titles; CRLF input; YAML quoting; missing ID import assigns ID only on explicit import; duplicate ID does not overwrite existing note.
- [ ] Run `dotnet test ... --filter FullyQualifiedName~FrontmatterCodecTests`; expect FAIL.
- [ ] Implement codec and repository with strict root-relative path normalization and path traversal rejection.
- [ ] Run tests; expect PASS.
- [ ] Commit `feat: parse and serialize Markdown notes`.

## Task 3: Web editor proof-of-concept and Markdown integrity gate

**Files:** Create `editor-web/src/markdown.ts`, `editor.ts`, `main.ts`; `editor-web/tests/roundtrip.spec.ts`, `ime.spec.ts`; document engine comparison in `docs/research/EDITOR_SPIKE.md`.

**Interfaces:** `Editor.load(noteId: string, markdown: string, revision: number): void`; `Editor.setMode(mode: 'wysiwyg'|'source'): void`; `Editor.getDraft(): {markdown:string, revision:number, dirty:boolean}`.

- [ ] Write failing editor tests: open/close without edit returns exactly the same Markdown bytes; source ↔ visual mode without edit preserves unsupported HTML/extension verbatim; edited heading/list/checkbox/table/code keeps syntax and meaning; Korean IME composition survives switching after composition end.
- [ ] Run `npm test`; expect FAIL (record exact command in `editor-web/package.json`).
- [ ] Spike ProseMirror/TipTap and Milkdown candidates with matching fixtures; select engine with documented failure matrix and lock package versions.
- [ ] Implement supported editor modes; keep original raw buffer until a material edit is committed, warn and prevent lossy visual edits for unsupported structures.
- [ ] Run Playwright tests and `npm run build`; expect PASS. Capture any residual unsupported syntax as a blocking gate.
- [ ] Commit `feat: add loss-aware Markdown editor`.

## Task 4: Versioned WebView2 message bridge

**Files:** Create `src/DevPostIt.EditorHost/EditorMessage.cs`, `EditorBridge.cs`, `editor-web/src/bridge.ts`, `tests/DevPostIt.App.Tests/EditorBridgeTests.cs`.

**Interfaces:** `EditorMessage(int ProtocolVersion, string Type, string RequestId, Guid NoteId, long Revision, JsonElement Payload)`; `EditorBridge.HandleAsync(EditorMessage,CancellationToken)`; protocol v1 `editor.ready`, `note.load`, `note.changed`, `note.saveRequested`, `note.saved`, `note.conflict`.

- [ ] Test invalid version, wrong note ID, stale revision, oversized payload, unrecognized message and untrusted navigation are rejected; valid `note.changed` emits a typed `NoteChange`.
- [ ] Run `dotnet test ... --filter FullyQualifiedName~EditorBridgeTests`; expect FAIL.
- [ ] Implement strict JSON validation, local WebView2 virtual host resource mapping, deny navigation away from bundled editor, no direct JS filesystem writes.
- [ ] Run tests and a manual WPF ↔ editor smoke test; expect PASS.
- [ ] Commit `feat: connect WebView2 editor with validated messages`.

## Task 5: SQLite metadata, index and rebuild

**Files:** Create `src/DevPostIt.Storage/SqliteNoteIndex.cs`, `tests/DevPostIt.Storage.Tests/SqliteNoteIndexTests.cs`.

**Interfaces:** `UpsertAsync(Note,CancellationToken)`, `RemoveAsync(Guid,CancellationToken)`, `SearchAsync(string,int,CancellationToken) -> IReadOnlyList<Guid>`, `RebuildAsync(IAsyncEnumerable<Note>,CancellationToken)`.

- [ ] Write failing tests for insert/update/delete/search, unique note IDs, corrupt/empty index rebuilding from files, Korean keyword search fixture, and search injection input treated as a query string.
- [ ] Run selected Storage tests; expect FAIL.
- [ ] Implement SQLite schema/migrations and FTS5; benchmark Hangul partial-word queries and document tokenizer limitations before choosing trigram/fallback.
- [ ] Run tests; expect PASS; use in-memory or disposable SQLite DB only.
- [ ] Commit `feat: index Markdown notes with SQLite`.

## Task 6: Autosave, atomic writer and crash recovery

**Files:** Create `src/DevPostIt.Storage/AtomicFileWriter.cs`, `src/DevPostIt.Core/NoteService.cs`, tests `AtomicFileWriterTests.cs`, `NoteServiceTests.cs`.

**Interfaces:** `AtomicFileWriter.WriteAsync(string path,string content,CancellationToken)`; `NoteService.OpenAsync(Guid,CancellationToken)`, `ApplyChangeAsync(NoteChange,CancellationToken)`, `FlushAsync(Guid,CancellationToken)`.

- [ ] Write failing tests: debounce coalesces rapid drafts, per-note serialized revisions, failed replace preserves previous file, disk-full mock preserves draft recovery state, forced restart restores draft from recovery journal.
- [ ] Run focused tests; expect FAIL.
- [ ] Implement debounce (initial 500 ms), temp-write/flush/replace, journal, save-success-before-index-update ordering; expose errors without discarding in-memory draft.
- [ ] Run tests plus forced-termination integration test on disposable files; expect PASS.
- [ ] Commit `feat: add durable autosave and recovery`.

## Task 7: External file watcher and conflict resolution

**Files:** Create `src/DevPostIt.Storage/FileReconciler.cs`, tests `FileReconcilerTests.cs`.

**Interfaces:** `FileReconciler.ReconcileAsync(CancellationToken) -> IReadOnlyList<ExternalFileChange>`; `NoteService.ResolveConflictAsync(Guid,string strategy,CancellationToken)` where strategy is `keep-local-copy` or `reload-disk` (never silent overwrite).

- [ ] Write failing tests: external modify with clean draft reloads; external modify with dirty draft produces conflict and preserves both variants; file rename updates path; duplicate watcher events idempotent; missed event detected by periodic hash-based scan; duplicate ID flagged.
- [ ] Run focused tests; expect FAIL.
- [ ] Implement watcher as hint plus periodic scan and SHA-256 comparison; create sidecar conflict copy with unique suffix rather than overwriting.
- [ ] Run tests; expect PASS.
- [ ] Commit `feat: reconcile external Markdown file edits`.

## Task 8: Central manager window

**Files:** Modify `src/DevPostIt.App/MainWindow.xaml{,.cs}`; create `ViewModels/MainViewModel.cs`; test `MainViewModelTests.cs`.

**Interfaces:** `MainViewModel.CreateNoteAsync()`, `SearchAsync(string query)`, `OpenNoteAsync(Guid)`, `MoveNoteAsync(Guid,string targetFolder)`, `DeleteNoteAsync(Guid)` and `RestoreNoteAsync(Guid)`.

- [ ] Write failing view-model tests for create/search/folder move/favorite/delete-to-trash/restore, keyboard selection, empty state, and filename collision.
- [ ] Run tests; expect FAIL.
- [ ] Implement three-column layout, list filters and edit-host selection; persist folder moves through repository; never delete from disk without recoverable trash workflow.
- [ ] Run tests and manual high-DPI UI smoke test; expect PASS.
- [ ] Commit `feat: build central note manager`.

## Task 9: Sticky windows and system tray

**Files:** Create `Windows/StickyWindow.xaml{,.cs}`, `Windows/WindowPlacement.cs`, `Services/StickyWindowManager.cs`, `Services/TrayService.cs`; test `WindowPlacementTests.cs`, `StickyWindowManagerTests.cs`.

**Interfaces:** `StickyWindowManager.Show(Guid noteId)`, `Close(Guid noteId)`, `ShowAll()`, `HideAll()`; `WindowPlacement.ClampToVisibleArea(Rect requested,IReadOnlyList<Rect> workAreas) -> Rect`.

- [ ] Write failing tests: one window per note, close does not delete, topmost toggle, opacity range, persisted geometry, disconnected monitor and mixed DPI reposition into visible working area.
- [ ] Run focused tests; expect FAIL.
- [ ] Implement WPF sticky windows, tray actions, per-note geometry and topmost state; lazy-load WebView2 to control window memory overhead.
- [ ] Run tests and manual two-monitor scenario; expect PASS.
- [ ] Commit `feat: add independent sticky windows and tray`.

## Task 10: Assets, export, packaging and release gate

**Files:** Create `src/DevPostIt.Storage/AssetStore.cs`, `docs/RELEASE_CHECKLIST.md`, `tests/DevPostIt.Storage.Tests/AssetStoreTests.cs`, packaging configuration.

**Interfaces:** `AssetStore.ImportAsync(Guid noteId, Stream source, string originalName,CancellationToken) -> string` returns safe relative Markdown link; export preserves note and its referenced local assets.

- [ ] Write failing tests: pasted image stored below `assets/<note-id>/`, unsafe filename sanitized, path traversal blocked, identical image deduplication within a note, export links remain valid.
- [ ] Run focused tests; expect FAIL.
- [ ] Implement asset import and export, bundle WebView2 editor, select documented WebView2 runtime delivery and installer strategy.
- [ ] Run all `dotnet test`, editor `npm test`, `npm run build`, package install/update/uninstall smoke tests; expect PASS and user notes preserved.
- [ ] Commit `feat: package offline-first Hunmin Notes MVP`.

## Codex handoff instructions

1. Create a Windows Git repository for this project. Copy the approved design and this plan into the exact `docs/superpowers/specs/` and `docs/superpowers/plans/` paths above.
2. Open Codex in that repository, not in a directory containing the original proprietary binaries. Keep REA evidence in a separately controlled `research` location if licensing requires.
3. First ask Codex to inspect toolchain (`dotnet --info`, Node/npm, WebView2 runtime), verify target frameworks/dependency versions from official sources and record pins. Do not silently choose preview versions.
4. Start with Task 0 when original EXE is available; otherwise execute Tasks 1–4 for P0, then stop for a Markdown editor gate review.
5. For each task follow the five checkbox steps, show actual failing/passing test output, preserve the worktree, and commit only task-owned files.
6. Request confirmation after the P0 editor gate and again before packaging for release.

### First Codex prompt

```text
Read docs/superpowers/specs/2026-11-01-hunmin-notes-design.md and
docs/superpowers/plans/2026-11-01-hunmin-notes.md completely.
This is a Windows WPF + WebView2 Markdown sticky-note application.
Follow the plan task-by-task using TDD, minimal implementation, actual test runs,
and scoped Git commits. Do not start all tasks at once.
Before Task 1, report Windows SDK/.NET/Node/WebView2 availability, existing repo
state, pinned dependency choices, and blockers. Do not alter unrelated files.
If the original 훈민메모패드 EXE is absent, defer Task 0 rather than invent findings.
Start Task 1 only after confirming the repository has the approved design and plan.
```

## Execution status

This is a plan, not a claim that code has been written or tests have passed. The next human gate is review of this plan and availability of a Windows development environment.
