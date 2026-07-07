# Question Category Onboarding

Use this checklist when adding a new generic question category to the migration and admin framework.

1. Add the category enum value in `F1.Core/Models/QuestionCategory.cs`.
2. Decide the template scope and canonical `QuestionId` shape. Reuse competition-season scope unless the category needs a stricter key.
3. Extend the parser or adapter boundary so source rows persist `QuestionTemplates`, `QuestionAnswers`, and `QuestionActuals` with row and column provenance.
4. Normalize actual answers before scoring and store any malformed-token diagnostics on `QuestionActuals.NormalizationDiagnosticsJson`.
5. Implement `IQuestionScoringStrategy` for the new category and register it in the worker DI container.
6. If imported points exist for the category, map them into `QuestionScores.ImportedPoints` and preserve `DeltaPoints` plus `ReasonCode`.
7. Add contract tests for parser mapping, scoring behavior, and missing-strategy fallback.
8. Add one extensibility test proving the new category works without changing `MigrationScoreRecalculator` orchestration.
9. If the category needs admin review or export behavior, extend the admin query surface without adding category-specific branching to the core worker pipeline.

Reference implementation:

- `PreseasonQuestionScoringStrategy` shows a category strategy that reuses the shared orchestration path.
- `QuestionFrameworkExtensibilityTests` shows the minimum harness for a mock category.

Automated guardrails:

- `QuestionFrameworkExtensibilityTests.RecalculateAndPersistAsync_WhenCustomCategoryStrategyIsRegistered_ScoresWithoutCoreOrchestratorChanges` verifies a mock category can run end-to-end through shared orchestration.
- `QuestionFrameworkExtensibilityTests.CalculateGenericQuestionScores_ShouldUseStrategyRegistry_WithoutCategorySpecificBranching` guards the core generic scorer against category-specific branching.
- `QuestionFrameworkExtensibilityTests.QuestionCategoryOnboardingDoc_ShouldContainRequiredOnboardingSteps` fails when required onboarding checklist guidance drifts.