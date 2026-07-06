# Expected Variance Governance Runbook

## Purpose
Define how operators author, review, approve, expire, and remove expected variance rules used during migration reconciliation.

This runbook applies to expected variance metadata only. It must never rewrite imported points, calculated points, or deltas.

## Scope
- In scope: Rule lifecycle management for expected variance classification in migration reconciliation.
- Out of scope: Changing scoring logic, mutating source CSV files, or overriding canonical reconciliation values.

## Current Implementation Snapshot
- Rule shape is defined by `MigrationExpectedVarianceRule` in `src/F1.DataSyncWorker/Services/MigrationExpectedVarianceRule.cs`.
- Rule matching is executed by `MigrationExpectedVarianceClassifier` in `src/F1.DataSyncWorker/Services/MigrationExpectedVarianceClassifier.cs`.
- Classification metadata is persisted on pick and race diff rows (`IsExpectedVariance`, `ExpectedVarianceReasonCode`, `ExpectedVarianceRuleId`) and remains additive.
- `MigrationReconciliationService` applies classification only when `delta != 0`.
- Source-of-truth manifest is `data/imports/phil-2025/expected-variance-rules.json` loaded by `FileBackedMigrationExpectedVarianceRuleCatalog`.
- Promotion is controlled via `MigrationExpectedVariance__RuleManifestPath` and `MigrationExpectedVariance__Enabled`.
- Audit trail is emitted per run with environment, ruleset id/version/checksum, and active rule count.

## Rule Record Template
Every rule proposal must include these fields before approval:

| Field | Required | Notes |
|---|---|---|
| `RuleId` | Yes | Stable, unique, lowercase kebab-case. Example: `phil-2025-aus-p1-legacy-cell-b11` |
| `ReasonCode` | Yes | Existing reason taxonomy value (for example `KNOWN_LEGACY_POINTS_ERROR`) |
| `Subject` | Optional | Exact match, case-insensitive |
| `RaceCode` | Optional | Exact match, case-insensitive |
| `PickType` | Optional | Exact match, case-insensitive |
| `ImportedSourcePattern` | Optional | Supports `*` and `?` wildcards |
| `CalculatedSourcePattern` | Optional | Supports `*` and `?` wildcards |
| Owner | Yes | Named owner (team or person) responsible for maintenance |
| Rationale | Yes | Plain-language reason this is expected, not a defect |
| Traceable Reference | Yes | Link to issue, run ID, incident, or evidence note |
| Approved By | Yes | Reviewer(s) granting approval |
| Approved On (UTC) | Yes | Approval timestamp |
| Effective From | Yes | Date/time rule is valid from |
| Expires On | Yes | Date/time rule must be reviewed or removed |

## Authoring Policy
1. Confirm candidate diff is non-zero and reproducible across reruns for the same source hash.
2. Confirm discrepancy is a known legacy anomaly, not a regression in scoring or parsing logic.
3. Draft the narrowest possible rule. Prefer specific `Subject`, `RaceCode`, and `PickType` before using source patterns.
4. Avoid broad wildcard-only patterns unless supported by a documented incident class and explicit approval.
5. Add or update tests proving:
   - The intended mismatch is classified as expected.
   - Adjacent unmatched rows remain unexpected.
6. Record owner, rationale, and traceable reference in the PR description and rule comment block or companion doc note.

## Review and Approval Policy
Minimum approval required before merge:
- One code owner for migration reconciliation.
- One data/operator reviewer familiar with the source spreadsheet anomalies.

Review checklist:
- Rule key is unique and deterministic.
- Matching scope is as narrow as possible.
- Test coverage includes positive and negative cases.
- No mutation of imported/calculated/delta values.
- `ExpectedVarianceReasonCode` and `ExpectedVarianceRuleId` are populated only when matched.
- Evidence link (run ID, issue, or incident) is present.
- Expiration date is set.

## Expiration and Renewal Policy
- Default expiration: 90 days from approval, unless an issue-specific date is required.
- Renewal requires a fresh review and updated evidence reference.
- Expired rules are treated as invalid until re-approved and reintroduced.

Weekly operator task:
1. List rules expiring within 14 days.
2. Validate whether source anomaly still exists.
3. Renew with updated rationale and reference, or remove.

## Validation Workflow (Post-Approval)
1. Run migration import in dry-run mode with the target source file.
2. Capture run ID from logs/API.
3. Validate output via admin endpoints:
   - `GET /admin/migration-runs/{runId}?expectedStatus=all`
   - `GET /admin/migration-runs/{runId}?expectedStatus=expected`
   - `GET /admin/migration-runs/{runId}?expectedStatus=unexpected`
4. Confirm:
   - Total variance remains unchanged.
   - Unexpected-only totals change only for intended rows.
   - Expected rows include both reason code and rule ID.
5. Export for audit when needed:
   - `GET /admin/migration-runs/{runId}/exports/pick-diffs?format=csv&expectedStatus=all`

## Environment Promotion Workflow
1. Update the manifest file in source control with approved rules and increment `ruleSetVersion`.
2. Open PR with owner, rationale, and evidence references for each new or changed rule.
3. Merge to dev deployment branch and run a dry-run import in dev.
4. Verify audit logs include expected ruleset metadata for the dev run.
5. Promote the same manifest revision to test, then prod (no manual data rewrite required).
6. In each environment, verify run logs report the same `RuleSetChecksum` for the promoted revision.

Environment targeting:
- Use optional `targetEnvironments` per rule to constrain rule activation.
- Omit `targetEnvironments` for rules that must apply in every environment.

## Rollback and Removal Procedure
Use this when a rule is incorrect, over-broad, or no longer needed.

1. Disable or remove the rule from the active rule catalog source.
2. Re-run reconciliation for the affected import run(s).
3. Compare `expectedStatus=unexpected` output before and after rollback.
4. Confirm only expected classification changed; imported/calculated/delta values must remain identical.
5. Record rollback in the linked issue/incident with:
   - Removed `RuleId`
   - Timestamp
   - Operator
   - Reason for removal
   - Validation evidence (run IDs and export artifacts)

Emergency rollback target:
- If multiple rules are suspect, revert to an empty catalog and rerun classification to restore baseline unexpected-only reporting.

## Onboarding Checklist
- Read this runbook and GH-270 Story 4-7 scope.
- Pair once with a maintainer to author one rule proposal.
- Run dry-run import and verify expected/unexpected filtering via admin API.
- Demonstrate rollback of a test rule in a non-production environment.
- Confirm understanding that expected variance metadata is additive only.

## Definition of Ready for a New Rule
- Reproducible mismatch identified.
- Evidence attached (run ID and source references).
- Owner, rationale, and expiration provided.
- Positive and negative tests prepared.

## Definition of Done for a Rule Change
- Approved by required reviewers.
- Tests pass.
- Validation workflow completed with evidence.
- Expiration tracked.
- Rollback plan documented.
