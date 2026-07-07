using System.Text.Json;
using System.Text.RegularExpressions;
using F1.DataSyncWorker.Models;
using F1.DataSyncWorker.Options;
using F1.Core.Models;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace F1.DataSyncWorker.Services;

public sealed partial class MigrationRaceSelectionParser : IMigrationRaceSelectionParser
{
    private const string SectionTypeRacePick = "RacePick";
    private const string SectionTypeSeasonQuestionPrediction = "SeasonQuestionPrediction";
    private const string SectionTypeHeader = "Header";
    private const string ActualSubject = "ACTUAL";
    private static readonly Dictionary<string, string?> TokenAliasDictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MAX"] = "VER",
        ["HULK"] = "HUL",
        ["BEAR MAN"] = "BEA",
        ["BEAR"] = "BEA",
        ["BORT"] = "BOR",
        ["LEEC"] = "LEC",
        ["NONE"] = null,
        ["NOT"] = null
    };

    private readonly IDbContextFactory<F1DbContext> _dbContextFactory;
    private readonly MigrationImportOptions _importOptions;

    public MigrationRaceSelectionParser(
        IDbContextFactory<F1DbContext> dbContextFactory,
        IOptions<MigrationImportOptions> importOptions)
    {
        _dbContextFactory = dbContextFactory;
        _importOptions = importOptions.Value;
    }

    public MigrationRaceSelectionParser(IDbContextFactory<F1DbContext> dbContextFactory)
        : this(dbContextFactory, Microsoft.Extensions.Options.Options.Create(new MigrationImportOptions()))
    {
    }

    public async Task<MigrationRaceSelectionParseResult> ParseAndPersistAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var sourceFilePath = await dbContext.MigrationImportRuns
            .Where(x => x.Id == runId)
            .Select(x => x.SourceFilePath)
            .SingleOrDefaultAsync(cancellationToken);
        var usePhil2025SequenceMapping = !string.IsNullOrWhiteSpace(sourceFilePath) &&
            MigrationPhil2025CsvContractPolicy.AppliesTo(sourceFilePath);

        var stagedRows = await dbContext.MigrationImportRawRows
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RowNumber)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var headerRow = stagedRows.FirstOrDefault(x => string.Equals(x.SectionType, SectionTypeHeader, StringComparison.Ordinal));
        var participants = ResolveParticipants(headerRow?.RawPayload);
        var preseasonParticipants = usePhil2025SequenceMapping
            ? MigrationPhil2025CsvContractPolicy.ParticipantColumns.ToList()
            : participants;

        var preseasonAnswers = ParsePreseasonQuestionAnswers(runId, stagedRows, preseasonParticipants, usePhil2025SequenceMapping);
        var genericPreseason = preseasonAnswers.Count == 0
            ? null
            : await BuildGenericPreseasonQuestionDataAsync(
                dbContext,
                runId,
                stagedRows,
                preseasonParticipants,
                usePhil2025SequenceMapping,
                cancellationToken);

        if (participants.Count == 0)
        {
            if (preseasonAnswers.Count > 0)
            {
                dbContext.MigrationImportPreseasonAnswers.RemoveRange(
                    dbContext.MigrationImportPreseasonAnswers.Where(x => x.ImportRunId == runId));
                if (genericPreseason is not null)
                {
                    dbContext.QuestionAnswers.RemoveRange(dbContext.QuestionAnswers.Where(x => x.ImportRunId == runId));
                    dbContext.QuestionActuals.RemoveRange(dbContext.QuestionActuals.Where(x => x.ImportRunId == runId));
                }
                await dbContext.SaveChangesAsync(cancellationToken);

                await dbContext.MigrationImportPreseasonAnswers.AddRangeAsync(preseasonAnswers, cancellationToken);
                if (genericPreseason is not null)
                {
                    var templateIds = await UpsertQuestionTemplatesAsync(dbContext, genericPreseason.Templates, cancellationToken);
                    ApplyTemplateIds(genericPreseason, templateIds);
                    await dbContext.QuestionAnswers.AddRangeAsync(genericPreseason.Answers, cancellationToken);
                    await dbContext.QuestionActuals.AddRangeAsync(genericPreseason.Actuals, cancellationToken);
                }
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return new MigrationRaceSelectionParseResult(
                SelectionCount: 0,
                UnresolvedTokenCount: 0,
                PreseasonAnswerCount: preseasonAnswers.Count);
        }

        var raceRows = stagedRows.Where(x => string.Equals(x.SectionType, SectionTypeRacePick, StringComparison.Ordinal));
        var selections = new List<MigrationImportRaceSelectionEntity>();
        var unresolvedTokens = new List<MigrationImportUnresolvedTokenEntity>();
        string? currentRaceCode = null;
        string? currentCanonicalRaceCode = null;
        var raceSequence = 0;
        var createdAtUtc = DateTime.UtcNow;

        foreach (var row in raceRows)
        {
            var columns = CsvLineParser.Parse(row.RawPayload);
            if (columns.Count == 0)
            {
                continue;
            }

            var label = columns[0].Trim();
            if (!TryResolveRace(label, ref currentRaceCode, out var raceCode, out var pickType))
            {
                continue;
            }

            if (string.Equals(pickType, "1", StringComparison.OrdinalIgnoreCase))
            {
                raceSequence++;
                currentCanonicalRaceCode = usePhil2025SequenceMapping
                    ? MigrationPhil2025RaceSequenceMapper.TryResolveCircuitId(raceSequence) ?? raceCode
                    : raceCode;
            }

            var persistedRaceCode = usePhil2025SequenceMapping
                ? currentCanonicalRaceCode ?? raceCode
                : raceCode;

            var participantValues = columns.Skip(1).Take(participants.Count).ToArray();
            for (var index = 0; index < participants.Count; index++)
            {
                var rawValue = index < participantValues.Length ? participantValues[index] : string.Empty;
                var normalization = NormalizeSelection(rawValue, pickType, usePhil2025SequenceMapping);
                selections.Add(new MigrationImportRaceSelectionEntity
                {
                    ImportRunId = runId,
                    RowNumber = row.RowNumber,
                    RaceCode = persistedRaceCode,
                    PickType = pickType,
                    Subject = participants[index],
                    RawValue = string.IsNullOrWhiteSpace(rawValue) ? null : rawValue.Trim(),
                    NormalizedValue = normalization.NormalizedValue,
                    IsActualOutcome = false
                });

                if (normalization.UnresolvedTokens.Count > 0)
                {
                    unresolvedTokens.AddRange(normalization.UnresolvedTokens.Select(unresolvedToken => new MigrationImportUnresolvedTokenEntity
                    {
                        ImportRunId = runId,
                        RowNumber = row.RowNumber,
                        RaceCode = persistedRaceCode,
                        PickType = pickType,
                        Subject = participants[index],
                        RawToken = unresolvedToken,
                        CreatedAtUtc = createdAtUtc
                    }));
                }
            }

            var actualRaw = columns.Skip(1 + participants.Count).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            var actualNormalization = NormalizeSelection(actualRaw, pickType, usePhil2025SequenceMapping);
            selections.Add(new MigrationImportRaceSelectionEntity
            {
                ImportRunId = runId,
                RowNumber = row.RowNumber,
                RaceCode = persistedRaceCode,
                PickType = pickType,
                Subject = ActualSubject,
                RawValue = string.IsNullOrWhiteSpace(actualRaw) ? null : actualRaw.Trim(),
                NormalizedValue = actualNormalization.NormalizedValue,
                IsActualOutcome = true
            });

            if (actualNormalization.UnresolvedTokens.Count > 0)
            {
                unresolvedTokens.AddRange(actualNormalization.UnresolvedTokens.Select(unresolvedToken => new MigrationImportUnresolvedTokenEntity
                {
                    ImportRunId = runId,
                    RowNumber = row.RowNumber,
                    RaceCode = persistedRaceCode,
                    PickType = pickType,
                    Subject = ActualSubject,
                    RawToken = unresolvedToken,
                    CreatedAtUtc = createdAtUtc
                }));
            }
        }

        if (selections.Count == 0 && preseasonAnswers.Count == 0)
        {
            return new MigrationRaceSelectionParseResult(
                SelectionCount: 0,
                UnresolvedTokenCount: 0,
                PreseasonAnswerCount: preseasonAnswers.Count);
        }

        dbContext.MigrationImportRaceSelections.RemoveRange(
            dbContext.MigrationImportRaceSelections.Where(x => x.ImportRunId == runId));
        dbContext.MigrationImportPreseasonAnswers.RemoveRange(
            dbContext.MigrationImportPreseasonAnswers.Where(x => x.ImportRunId == runId));
        dbContext.QuestionAnswers.RemoveRange(
            dbContext.QuestionAnswers.Where(x => x.ImportRunId == runId));
        dbContext.QuestionActuals.RemoveRange(
            dbContext.QuestionActuals.Where(x => x.ImportRunId == runId));
        dbContext.MigrationImportUnresolvedTokens.RemoveRange(
            dbContext.MigrationImportUnresolvedTokens.Where(x => x.ImportRunId == runId));
        await dbContext.SaveChangesAsync(cancellationToken);

        await dbContext.MigrationImportRaceSelections.AddRangeAsync(selections, cancellationToken);
        if (preseasonAnswers.Count > 0)
        {
            await dbContext.MigrationImportPreseasonAnswers.AddRangeAsync(preseasonAnswers, cancellationToken);
            if (genericPreseason is not null)
            {
                var templateIds = await UpsertQuestionTemplatesAsync(dbContext, genericPreseason.Templates, cancellationToken);
                ApplyTemplateIds(genericPreseason, templateIds);
                await dbContext.QuestionAnswers.AddRangeAsync(genericPreseason.Answers, cancellationToken);
                await dbContext.QuestionActuals.AddRangeAsync(genericPreseason.Actuals, cancellationToken);
            }
        }
        if (unresolvedTokens.Count > 0)
        {
            await dbContext.MigrationImportUnresolvedTokens.AddRangeAsync(unresolvedTokens, cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        return new MigrationRaceSelectionParseResult(
            SelectionCount: selections.Count,
            UnresolvedTokenCount: unresolvedTokens.Count,
            PreseasonAnswerCount: preseasonAnswers.Count);
    }

    private async Task<GenericPreseasonQuestionData?> BuildGenericPreseasonQuestionDataAsync(
        F1DbContext dbContext,
        Guid runId,
        IReadOnlyCollection<MigrationImportRawRowEntity> stagedRows,
        IReadOnlyList<string> participants,
        bool usePhil2025Contract,
        CancellationToken cancellationToken)
    {
        var preseasonRows = stagedRows
            .Where(x => string.Equals(x.SectionType, SectionTypeSeasonQuestionPrediction, StringComparison.Ordinal))
            .OrderBy(x => x.RowNumber)
            .ToList();

        if (preseasonRows.Count == 0)
        {
            return null;
        }

        var competitionIds = await dbContext.Competitions
            .Where(x => x.Year == _importOptions.Season)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (competitionIds.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one competition for season '{_importOptions.Season}' when persisting question templates, but found {competitionIds.Count}.");
        }

        var competitionId = competitionIds[0];
        var templateKeys = preseasonRows.Select(row => $"PRE-{row.RowNumber:D3}").ToArray();
        var existingTemplateIds = await dbContext.QuestionTemplates
            .Where(x => x.CompetitionId == competitionId && x.Season == _importOptions.Season && templateKeys.Contains(x.QuestionId))
            .ToDictionaryAsync(x => x.QuestionId, x => x.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var now = DateTime.UtcNow;
        var templates = new List<QuestionTemplateEntity>();
        var answers = new List<QuestionAnswerEntity>();
        var actuals = new List<QuestionActualEntity>();

        foreach (var row in preseasonRows)
        {
            var columns = CsvLineParser.Parse(row.RawPayload);
            if (columns.Count == 0)
            {
                continue;
            }

            var questionText = columns[0].Trim();
            if (string.IsNullOrWhiteSpace(questionText))
            {
                continue;
            }

            var questionId = $"PRE-{row.RowNumber:D3}";
            templates.Add(new QuestionTemplateEntity
            {
                Id = existingTemplateIds.TryGetValue(questionId, out var existingTemplateId) ? existingTemplateId : 0,
                CompetitionId = competitionId,
                Season = _importOptions.Season,
                QuestionId = questionId,
                Category = QuestionCategory.Preseason,
                Prompt = questionText,
                OptionsJson = null,
                Status = QuestionTemplateStatus.Published,
                SortOrder = row.RowNumber,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

            var participantStartIndex = usePhil2025Contract
                ? MigrationPhil2025CsvContractPolicy.ParticipantStartColumnIndex
                : 1;

            for (var index = 0; index < participants.Count; index++)
            {
                var columnIndex = participantStartIndex + index;
                var raw = columnIndex < columns.Count ? columns[columnIndex] : null;
                var normalization = NormalizePreseasonAnswer(raw, isActualOutcome: false);
                answers.Add(new QuestionAnswerEntity
                {
                    ImportRunId = runId,
                    QuestionTemplateId = existingTemplateIds.TryGetValue(questionId, out var templateId) ? templateId : 0,
                    ParticipantId = participants[index],
                    ImportedAnswer = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim(),
                    NormalizedAnswer = normalization.NormalizedValue,
                    SourceRow = row.RowNumber,
                    SourceColumn = columnIndex + 1,
                    RecordedAtUtc = now
                });
            }

            string? actualRaw;
            var actualColumnIndex = -1;
            if (usePhil2025Contract)
            {
                actualColumnIndex = MigrationPhil2025CsvContractPolicy.ActualAnswerColumnIndex;
                actualRaw = actualColumnIndex < columns.Count ? columns[actualColumnIndex] : null;
            }
            else
            {
                actualColumnIndex = -1;
                for (var index = 1 + participants.Count; index < columns.Count; index++)
                {
                    if (!string.IsNullOrWhiteSpace(columns[index]))
                    {
                        actualColumnIndex = index;
                        break;
                    }
                }

                actualRaw = actualColumnIndex >= 0 ? columns[actualColumnIndex] : null;
            }

            var actualNormalization = NormalizePreseasonAnswer(actualRaw, isActualOutcome: true);
            actuals.Add(new QuestionActualEntity
            {
                ImportRunId = runId,
                QuestionTemplateId = existingTemplateIds.TryGetValue(questionId, out var actualTemplateId) ? actualTemplateId : 0,
                ActualAnswer = string.IsNullOrWhiteSpace(actualRaw) ? null : actualRaw.Trim(),
                NormalizedAnswer = actualNormalization.NormalizedValue,
                SourceRow = row.RowNumber,
                SourceColumn = actualColumnIndex >= 0 ? actualColumnIndex + 1 : 0,
                NormalizationDiagnosticsJson = actualNormalization.Diagnostics.Count == 0
                    ? null
                    : JsonSerializer.Serialize(actualNormalization.Diagnostics),
                RecordedAtUtc = now
            });
        }

        return templates.Count == 0
            ? null
            : new GenericPreseasonQuestionData(templates, answers, actuals);
    }

    private static async Task<Dictionary<string, long>> UpsertQuestionTemplatesAsync(
        F1DbContext dbContext,
        IReadOnlyList<QuestionTemplateEntity> templates,
        CancellationToken cancellationToken)
    {
        if (templates.Count == 0)
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        var byQuestionId = templates
            .GroupBy(x => x.QuestionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);

        var competitionId = templates[0].CompetitionId;
        var season = templates[0].Season;
        var existing = await dbContext.QuestionTemplates
            .Where(x => x.CompetitionId == competitionId && x.Season == season && byQuestionId.Keys.Contains(x.QuestionId))
            .ToDictionaryAsync(x => x.QuestionId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var item in byQuestionId.Values)
        {
            if (!existing.TryGetValue(item.QuestionId, out var entity))
            {
                entity = new QuestionTemplateEntity
                {
                    CompetitionId = item.CompetitionId,
                    Season = item.Season,
                    QuestionId = item.QuestionId,
                    CreatedAtUtc = item.CreatedAtUtc
                };
                dbContext.QuestionTemplates.Add(entity);
            }

            entity.Category = item.Category;
            entity.Prompt = item.Prompt;
            entity.OptionsJson = item.OptionsJson;
            entity.Status = item.Status;
            entity.SortOrder = item.SortOrder;
            entity.UpdatedAtUtc = item.UpdatedAtUtc;
            if (entity.CreatedAtUtc == default)
            {
                entity.CreatedAtUtc = item.CreatedAtUtc;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var persistedIds = await dbContext.QuestionTemplates
            .Where(x => x.CompetitionId == competitionId && x.Season == season && byQuestionId.Keys.Contains(x.QuestionId))
            .ToDictionaryAsync(x => x.QuestionId, x => x.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var template in templates)
        {
            template.Id = persistedIds[template.QuestionId];
        }

        return persistedIds;
    }

    private static void ApplyTemplateIds(GenericPreseasonQuestionData genericPreseason, IReadOnlyDictionary<string, long> templateIds)
    {
        foreach (var answer in genericPreseason.Answers)
        {
            answer.QuestionTemplateId = templateIds[$"PRE-{answer.SourceRow:D3}"];
        }

        foreach (var actual in genericPreseason.Actuals)
        {
            actual.QuestionTemplateId = templateIds[$"PRE-{actual.SourceRow:D3}"];
        }
    }

    private static List<MigrationImportPreseasonAnswerEntity> ParsePreseasonQuestionAnswers(
        Guid runId,
        IReadOnlyCollection<MigrationImportRawRowEntity> stagedRows,
        IReadOnlyList<string> participants,
        bool usePhil2025Contract)
    {
        if (participants.Count == 0)
        {
            return [];
        }

        var preseasonRows = stagedRows
            .Where(x => string.Equals(x.SectionType, SectionTypeSeasonQuestionPrediction, StringComparison.Ordinal))
            .OrderBy(x => x.RowNumber)
            .ToList();

        if (preseasonRows.Count == 0)
        {
            return [];
        }

        var parsed = new List<MigrationImportPreseasonAnswerEntity>();

        foreach (var row in preseasonRows)
        {
            var columns = CsvLineParser.Parse(row.RawPayload);
            if (columns.Count == 0)
            {
                continue;
            }

            var questionText = columns[0].Trim();
            if (string.IsNullOrWhiteSpace(questionText))
            {
                continue;
            }

            var questionKey = $"PRE-{row.RowNumber:D3}";
            var participantStartIndex = usePhil2025Contract
                ? MigrationPhil2025CsvContractPolicy.ParticipantStartColumnIndex
                : 1;

            for (var index = 0; index < participants.Count; index++)
            {
                var columnIndex = participantStartIndex + index;
                var raw = columnIndex < columns.Count ? columns[columnIndex] : null;
                parsed.Add(new MigrationImportPreseasonAnswerEntity
                {
                    ImportRunId = runId,
                    RowNumber = row.RowNumber,
                    QuestionKey = questionKey,
                    QuestionText = questionText,
                    Subject = participants[index],
                    RawAnswer = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim(),
                    NormalizedAnswer = NormalizePreseasonAnswer(raw, isActualOutcome: false).NormalizedValue,
                    IsActualOutcome = false
                });
            }

            string? actualRaw;
            if (usePhil2025Contract)
            {
                actualRaw = MigrationPhil2025CsvContractPolicy.ActualAnswerColumnIndex < columns.Count
                    ? columns[MigrationPhil2025CsvContractPolicy.ActualAnswerColumnIndex]
                    : null;
            }
            else
            {
                actualRaw = columns.Skip(1 + participants.Count).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            }

            parsed.Add(new MigrationImportPreseasonAnswerEntity
            {
                ImportRunId = runId,
                RowNumber = row.RowNumber,
                QuestionKey = questionKey,
                QuestionText = questionText,
                Subject = ActualSubject,
                RawAnswer = string.IsNullOrWhiteSpace(actualRaw) ? null : actualRaw.Trim(),
                NormalizedAnswer = NormalizePreseasonAnswer(actualRaw, isActualOutcome: true).NormalizedValue,
                IsActualOutcome = true
            });
        }

        return parsed;
    }

    private static List<string> ResolveParticipants(string? headerPayload)
    {
        if (string.IsNullOrWhiteSpace(headerPayload))
        {
            return [];
        }

        var columns = CsvLineParser.Parse(headerPayload);
        if (columns.Count < 2)
        {
            return [];
        }

        var participants = new List<string>();
        foreach (var token in columns.Skip(1))
        {
            var trimmed = token.Trim();
            if (trimmed.Length == 0)
            {
                break;
            }

            participants.Add(trimmed);
        }

        return participants;
    }

    private static bool TryResolveRace(string label, ref string? currentRaceCode, out string raceCode, out string pickType)
    {
        raceCode = string.Empty;
        pickType = string.Empty;

        if (label.Length == 0)
        {
            return false;
        }

        var normalizedLabel = label.Trim();
        if (normalizedLabel.StartsWith("L-", StringComparison.OrdinalIgnoreCase))
        {
            normalizedLabel = normalizedLabel[2..];
        }

        if (normalizedLabel.Equals("DNF", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(currentRaceCode))
            {
                return false;
            }

            raceCode = currentRaceCode;
            pickType = "DNF";
            return true;
        }

        if (normalizedLabel.Contains("HUMBUG", StringComparison.OrdinalIgnoreCase))
        {
            var humbugMatch = GenericRacePrefixRegex().Match(normalizedLabel);
            if (!humbugMatch.Success)
            {
                return false;
            }

            raceCode = RaceCodeNormalizer.NormalizeRaceCode(humbugMatch.Groups[1].Value);
            pickType = "DNF";
            currentRaceCode = raceCode;
            return true;
        }

        var match = RaceLabelRegex().Match(normalizedLabel);
        if (!match.Success)
        {
            var genericRaceMatch = GenericRacePrefixRegex().Match(normalizedLabel);
            if (!genericRaceMatch.Success)
            {
                return false;
            }

            raceCode = RaceCodeNormalizer.NormalizeRaceCode(genericRaceMatch.Groups[1].Value);
            pickType = "DNF";
            currentRaceCode = raceCode;
            return true;
        }

        raceCode = RaceCodeNormalizer.NormalizeRaceCode(match.Groups[1].Value);
        pickType = match.Groups[2].Value.ToUpperInvariant();
        currentRaceCode = raceCode;
        return true;
    }

    private static NormalizationResult NormalizeSelection(string? rawValue, string pickType, bool applyPhil2025TokenCorrections)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return new NormalizationResult(NormalizedValue: null, UnresolvedTokens: []);
        }

        var normalized = rawValue.Trim();
        var lookupToken = NormalizeTokenLookup(rawValue);

        if (lookupToken.Length == 0)
        {
            return new NormalizationResult(NormalizedValue: null, UnresolvedTokens: []);
        }

        // The Phil 2025 source contains podium typos where NOT was intended to be NOR.
        if (applyPhil2025TokenCorrections && PodiumPickTypes.Contains(pickType) &&
            string.Equals(lookupToken, "NOT", StringComparison.OrdinalIgnoreCase))
        {
            return new NormalizationResult(NormalizedValue: "NOR", UnresolvedTokens: []);
        }

        // DNF and ACTUAL DNF values can be comma/space-separated token sets.
        if (string.Equals(pickType, "DNF", StringComparison.OrdinalIgnoreCase) && LooksLikeMultiTokenDnf(rawValue))
        {
            var resolvedTokens = new List<string>();
            var unresolvedTokens = new List<string>();

            foreach (var token in DnfTokenSplitRegex().Split(lookupToken).Where(token => token.Length > 0))
            {
                if (TokenAliasDictionary.TryGetValue(token, out var mappedToken))
                {
                    if (!string.IsNullOrWhiteSpace(mappedToken))
                    {
                        resolvedTokens.Add(mappedToken);
                    }

                    continue;
                }

                if (CanonicalTokenRegex().IsMatch(token))
                {
                    resolvedTokens.Add(token);
                    continue;
                }

                unresolvedTokens.Add(token);
            }

            var normalizedDnf = resolvedTokens.Count == 0 ? null : string.Join(" ", resolvedTokens);
            return new NormalizationResult(NormalizedValue: normalizedDnf, UnresolvedTokens: unresolvedTokens);
        }

        if (TokenAliasDictionary.TryGetValue(lookupToken, out var mappedSingleToken))
        {
            return new NormalizationResult(NormalizedValue: mappedSingleToken, UnresolvedTokens: []);
        }

        if (CanonicalTokenRegex().IsMatch(lookupToken))
        {
            return new NormalizationResult(NormalizedValue: lookupToken, UnresolvedTokens: []);
        }

        return new NormalizationResult(NormalizedValue: normalized, UnresolvedTokens: [normalized]);
    }

    private static bool LooksLikeMultiTokenDnf(string rawValue)
    {
        return rawValue.Contains(',') || DnfTokenSplitRegex().Split(rawValue.Trim()).Length > 1;
    }

    private static string NormalizeTokenLookup(string rawValue)
    {
        return MultiWhitespaceRegex().Replace(rawValue.Trim().ToUpperInvariant(), " ");
    }

    private static PreseasonNormalizationResult NormalizePreseasonAnswer(string? rawAnswer, bool isActualOutcome)
    {
        if (string.IsNullOrWhiteSpace(rawAnswer))
        {
            return new PreseasonNormalizationResult(null, ["NULL_OR_WHITESPACE"]);
        }

        var normalized = MultiWhitespaceRegex().Replace(rawAnswer.Trim(), " ");
        if (string.Equals(normalized, "NONE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "NOT", StringComparison.OrdinalIgnoreCase))
        {
            return new PreseasonNormalizationResult(null, ["NULL_EQUIVALENT_TOKEN"]);
        }

        if (!isActualOutcome || !PreseasonDelimitedAnswerRegex().IsMatch(normalized))
        {
            var diagnostics = HasUnsupportedAnswerShape(normalized)
                ? new[] { "UNSUPPORTED_TOKEN_SHAPE_PRESERVED" }
                : Array.Empty<string>();
            return new PreseasonNormalizationResult(normalized, diagnostics);
        }

        var tokens = PreseasonDelimitedAnswerRegex()
            .Split(normalized)
            .Select(token => MultiWhitespaceRegex().Replace(token.Trim(), " "))
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Where(token =>
                !string.Equals(token, "NONE", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(token, "NOT", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (tokens.Length == 0)
        {
            return new PreseasonNormalizationResult(null, ["DELIMITED_ACTUAL_NORMALIZED_TO_NULL"]);
        }

        return new PreseasonNormalizationResult(
            tokens.Length == 1 ? tokens[0] : string.Join(" | ", tokens),
            ["MULTI_TOKEN_ACTUAL_NORMALIZED"]);
    }

    private static bool HasUnsupportedAnswerShape(string normalized)
    {
        return normalized.Any(character =>
            !(char.IsLetterOrDigit(character) || char.IsWhiteSpace(character) || character is '-' or '_' or '.' or '/'));
    }

    [GeneratedRegex("^([A-Za-z][A-Za-z\\s]{2,})-(1|2|3|DNF)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RaceLabelRegex();

    [GeneratedRegex("^([A-Za-z][A-Za-z\\s]{2,})-.+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex GenericRacePrefixRegex();

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.Compiled)]
    private static partial Regex CanonicalTokenRegex();

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex MultiWhitespaceRegex();

    [GeneratedRegex("[\\s,;/]+", RegexOptions.Compiled)]
    private static partial Regex DnfTokenSplitRegex();

    [GeneratedRegex("[|,;/]+", RegexOptions.Compiled)]
    private static partial Regex PreseasonDelimitedAnswerRegex();

    private static readonly HashSet<string> PodiumPickTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "1",
        "2",
        "3"
    };

    private readonly record struct NormalizationResult(string? NormalizedValue, IReadOnlyList<string> UnresolvedTokens);

    private readonly record struct PreseasonNormalizationResult(string? NormalizedValue, IReadOnlyList<string> Diagnostics);

    private sealed record GenericPreseasonQuestionData(
        IReadOnlyList<QuestionTemplateEntity> Templates,
        IReadOnlyList<QuestionAnswerEntity> Answers,
        IReadOnlyList<QuestionActualEntity> Actuals);
}