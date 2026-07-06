# Story UX-1: Migration Runs Command Center Overhaul

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

## Implementation Notes

1. Start with UI composition refactor in the migration page:
- Break page into composable components:
  - Run list pane
  - Run health header
  - Tabbed workspace sections
  - Export action panel

2. Keep service/API integration stable while improving client orchestration.

3. Add lightweight telemetry events for:
- run_selected
- tab_changed
- filter_changed
- export_triggered

## Test Notes

1. Web tests:
- Add BUnit tests for tabbed layout rendering, query-string state restore, keyboard navigation paths, and export grouping labels.

2. API tests:
- Confirm no regression in run detail payload and export endpoint compatibility.

3. E2E tests:
- Validate full admin flow:
  - Load page
  - Select run
  - Open Preseason tab
  - Apply non-zero filter
  - Download preseason export

4. Performance checks:
- Add a simple automated timing assertion for run list render and run-switch interaction in CI-friendly conditions.

## Out of Scope

1. Changes to scoring logic or reconciliation math.
2. Migration data model redesign.
3. Non-admin migration review experiences.

## Dependencies

1. Existing migration run detail API and export endpoints.
2. Story P8 metadata fields for preseason run health signals.
3. Story P9 preseason test strategy for CI gate coverage.
