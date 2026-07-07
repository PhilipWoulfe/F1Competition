# Epic #279: Migration Runs Command Center Overhaul

## Context and Current UX Analysis

Current migration review experience is functional but high-friction for operators handling repeated dry-run/write-run validation cycles.

Observed issues from the implemented page:

1. Overloaded single-screen layout:
- Run kickoff, run list filters, pagination, detail metrics, exports, and four large comparison sections compete for attention in one long scroll.
- Review flow is linear and heavy even when the operator needs only one section.

2. Slow review loop for run triage:
- Operators must open each run detail to see meaningful context.
- Summary list lacks high-signal cues for preseason health and run risk.

3. Filter fragmentation and context loss:
- Separate filter sets for race and preseason are not unified under a single review mode.
- Filter/sort state is not encoded in URL, reducing shareability and forcing repeated setup.

4. Detail-first navigation inefficiency:
- Comparisons are stacked vertically rather than grouped by tasks.
- Operators reviewing many runs must repeatedly scroll between sections.

5. Export discoverability and decision support gaps:
- Export actions are present but not grouped by review intent.
- No explicit pre-export context summary to help operators choose the right artifact quickly.

6. Visual hierarchy and accessibility limits:
- Dense data tables with limited semantic emphasis increase cognitive load.
- No explicit keyboard-first navigation model for high-frequency reviewer workflows.

## User Story

As an admin operator, I want a command-center style migration page so I can triage run health and complete preseason/race sign-off in minutes instead of scrolling through a long, dense page.

## Desired Outcome

Drastically improve review throughput, confidence, and consistency for migration sign-off.

## Acceptance Criteria

1. Information architecture overhaul:
- Replace the long-scroll detail layout with a two-pane command-center pattern:
  - Left pane: run list and triage filters.
  - Right pane: selected run review workspace.
- Right-pane workspace is tabbed by review tasks:
  - Overview
  - Preseason
  - Race Participants
  - Race Diffs
  - Pick Diffs
  - Exports

2. High-signal run triage experience:
- Run list rows include visual status chips for:
  - Run status
  - Unresolved token severity
  - Preseason status (parse/scoring/guard)
- Add quick health columns that avoid opening detail for obvious pass/fail triage.

3. Unified filtering and persistent shareable state:
- Introduce a single filter model for participant/reason/non-zero and expected-status with scoped applicability by tab.
- Persist active filters, selected tab, and selected run in query string.
- Reloading the page restores the same operator context.

4. Preseason-first review clarity:
- Preseason tab uses compact summary cards followed by question diff table with sticky header and fast client-side search.
- Non-zero-only toggle is prominent and defaults on for preseason question diffs.

5. Export workflow redesign:
- Exports tab groups artifacts by operator intent:
  - Sign-off package
  - Preseason reconciliation
  - Race reconciliation
- Each export action shows row counts and active filter context before download.

6. Performance and responsiveness:
- Initial page render for run list remains under 2 seconds for 100 rows on standard CI test environment.
- Changing selected run updates right pane without full page reflow.
- Mobile and tablet layouts preserve tab access and core actions.

7. Accessibility and keyboard operations:
- Full keyboard navigation for run selection, tab switching, and export triggers.
- ARIA labels and landmarks for panes/tabs/tables.
- Visible focus states and screen-reader friendly section summaries.

8. Backward compatibility:
- Existing API contracts remain compatible.
- Existing export endpoints continue to function.

## UX Success Metrics

Measure after rollout using telemetry and operator feedback:

1. Time to first meaningful review action (select run + open relevant tab) reduced by at least 40 percent.
2. Median time from page load to export download reduced by at least 30 percent.
3. Operator-reported usability score for migration review improves from baseline by at least 2 points on a 10-point scale.
4. Drop-off rate (open run detail without export or tab interaction) reduced by at least 25 percent.

## Out of Scope

1. Changes to scoring logic or reconciliation math.
2. Migration data model redesign.
3. Non-admin migration review experiences.

## Dependencies

1. Existing migration run detail API and export endpoints.
2. Story P8 metadata fields for preseason run health signals.
3. Story P9 preseason test strategy for CI gate coverage.

---

## Stories

### UX-1.1: Two-pane layout shell

**As an** admin operator, **I want** the migration runs page split into a left run-list pane and a right detail pane **so that** I no longer need to scroll back and forth between the list and the detail.

**Acceptance Criteria:**
- Page uses a Bootstrap two-column split (`col-md-4` / `col-md-8`) with a visible divider.
- Left column contains the filter controls, run list table, and pagination.
- Right column contains the run detail section (metrics, comparisons, exports) in its current vertical-scroll form — no tab switching yet.
- On mobile (`< md`) the columns stack full-width.
- All existing BUnit tests pass without modification.

**Out of scope:** status chips, tabs, URL state, accessibility attributes.

---

### UX-1.2: Status chips in run list rows

**As an** admin operator, **I want** each run row to show visual status chips **so that** I can triage obvious pass/fail runs without opening the detail pane.

**Acceptance Criteria:**
- Each run list row shows:
  - A status badge: Completed → green, Failed → red, Started → blue, Queued → secondary.
  - A mode badge: Dry-run → secondary, Write → info.
  - An unresolved-token severity badge when `UnresolvedTokenCount > 0`: 1–5 → warning, >5 → danger.
  - An unexpected-delta badge when `UnexpectedTotalDeltaPoints != 0` → warning.
- Plain text Status/Mode/Unresolved columns are replaced by the chips; the full Run Id, Started, and Finished columns remain.
- BUnit test added: a completed run with 3 unresolved tokens renders the correct badge classes.

**Out of scope:** row click-to-select, tab switching.

---

### UX-1.3: Tabbed right pane

**As an** admin operator, **I want** the run detail split into tabs **so that** I can jump directly to the section I need without scrolling.

**Acceptance Criteria:**
- Six tabs appear when a run is selected: Overview, Preseason, Race Participants, Race Diffs, Pick Diffs, Exports.
- Tab switching is Blazor-driven (CSS `active`/`show` class toggling); all tab content remains in the DOM so filters work regardless of active tab.
- Overview tab contains: metrics cards, unresolved token summary table, source file and checksum.
- Preseason tab contains: summary cards, preseason participant totals table, preseason question diffs table, preseason-specific filter bar.
- Race Participants tab contains: participant comparisons table.
- Race Diffs tab contains: race diffs table.
- Pick Diffs tab contains: pick diffs table.
- Exports tab contains: the existing flat export links (regrouping is done in UX-1.4).
- The in-page anchor links (`#preseason-comparisons`, `#participant-comparisons`, `#race-comparisons`, `#pick-comparisons`) are removed; BUnit tests that assert those anchors are updated to assert tab button labels instead.
- The unified filter bar (participant, race, reason, non-zero, variance) remains always-visible above the tab nav when a run is selected.
- `activeTab` defaults to `"overview"` and resets to `"overview"` when a new run is selected.

**Out of scope:** URL state persistence, export grouping, accessibility attributes.

---

### UX-1.4: Export tab with intent grouping

**As an** admin operator, **I want** export actions grouped by review intent **so that** I can find the right artifact quickly without reading every button label.

**Acceptance Criteria:**
- The Exports tab contains three titled intent cards:
  - **Sign-off Package** — participant diffs CSV/JSON and pick diffs CSV/JSON.
  - **Preseason Reconciliation** — preseason question diffs CSV/JSON and preseason participant diffs CSV/JSON.
  - **Race Reconciliation** — race participant diffs CSV/JSON.
- Each card has a one-sentence description of its purpose.
- All existing export endpoint URLs are unchanged.
- BUnit test updated: asserts the three card headings are present and the existing export link text appears within the correct card.

**Out of scope:** row counts per export, filter-context summaries.

---

### UX-1.5: Kickoff panel toggle and preseason non-zero default

**As an** admin operator, **I want** the kickoff form to be collapsible **and** the preseason non-zero filter to default on **so that** the page is less cluttered on arrival and preseason diffs show meaningful rows by default.

**Acceptance Criteria:**
- A "New run" button in the page header toggles the kickoff card open/closed. Default is open (matching current behaviour).
- When the button collapses the panel it reads "New run"; when expanded it reads "Hide kickoff".
- `preseasonNonZeroOnly` is reset to `true` when a run is selected (previously `false`).
- Preseason question diffs with `DeltaPoints == 0` are hidden on initial run selection; the toggle remains available to show them.
- Existing BUnit tests that interact with `#preseason-non-zero-only` pass; any assertion relying on zero-delta preseason rows appearing immediately after run selection is updated.

**Out of scope:** URL state, other filter defaults.

---

### UX-1.6: URL query-string state persistence

**As an** admin operator, **I want** the selected run, active tab, status filter, and page number persisted in the URL **so that** I can share or reload the page and return to the same context.

**Acceptance Criteria:**
- `[SupplyParameterFromQuery]` parameters: `run` (Guid string), `tab`, `page` (int), `status`.
- `OnInitializedAsync` reads these parameters and applies them before first render, then loads the selected run's detail if `run` is present.
- State is pushed to the URL (replace, not push) via `NavigationManager.GetUriWithQueryParameters` when: a run is selected, the active tab changes, filters are applied, or the page changes.
- Default values are omitted from the URL (e.g. `tab=overview` and `page=1` are not appended).
- Reloading the URL with `?run=<id>&tab=preseason` restores the selected run and opens the Preseason tab.
- BUnit tests are unaffected (bUnit's `FakeNavigationManager` satisfies `NavigateTo` without side effects).

**Out of scope:** browser back-button history management, filter values beyond status.

---

### UX-1.7: ARIA labels and keyboard navigation

**As an** admin operator using a keyboard or screen reader, **I want** the command-center layout to be fully navigable without a mouse **so that** high-frequency review workflows are accessible.

**Acceptance Criteria:**
- Tab nav: `role="tablist"` on the `<ul>`, `role="tab"` and `aria-selected` on each button, `aria-controls` pointing to the corresponding `tab-pane` id, `id` on each button matching the `aria-labelledby` of its pane.
- Run list: native `<table>` semantics are preserved; roving tabindex ensures only the selected row (or the first row when nothing is selected) has `tabindex="0"`, with all other rows at `tabindex="-1"`.
- Right pane container: `aria-live="polite"`.
- All filter `<input>` and `<select>` elements have an `aria-label`.
- All table headers have `scope="col"`.
- All export `<a>` elements have a descriptive `aria-label`.
- Focus states are not removed (no `outline: none` without a replacement).
- BUnit test added: assert `role="tablist"` exists and at least one `aria-selected="true"` tab is present after run selection.
