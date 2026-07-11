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
    private const string Philip2025CompetitionName = "Philip 2025";
    private const string SectionTypeRacePick = "RacePick";
    private const string SectionTypeSeasonQuestionPrediction = "SeasonQuestionPrediction";
    private const string SectionTypeHeader = "Header";
    private const string ActualSubject = "ACTUAL";
    private const int DefaultPhilH2hPointsForCorrectPick = 1;
    private const int DefaultDaveH2hPointsForCorrectPick = 5;
    private const int DefaultRaceBonusPointsForCorrectPick = 20;
    private const string DaveRacesFile = "races.csv";
    private const string DaveBonusFile = "bonus.csv";
    private const string DaveBonusAnswersFile = "bonusAnswers.csv";
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

    private static readonly Dictionary<string, string?> QuestionTokenAliasDictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        ["YES"] = "YES",
        ["Y"] = "YES",
        ["TRUE"] = "YES",
        ["T"] = "YES",
        ["1"] = "YES",
        ["NO"] = "NO",
        ["N"] = "NO",
        ["FALSE"] = "NO",
        ["F"] = "NO",
        ["0"] = "NO",
        ["MAX VERSTAPPEN"] = "VER",
        ["VERSTAPPEN"] = "VER",
        ["LEWIS HAMILTON"] = "HAM",
        ["HAMILTON"] = "HAM",
        ["CHARLES LECLERC"] = "LEC",
        ["LECLERC"] = "LEC",
        ["LANDO NORRIS"] = "NOR",
        ["NORRIS"] = "NOR",
        ["GEORGE RUSSELL"] = "RUS",
        ["RUSSELL"] = "RUS",
        ["OSCAR PIASTRI"] = "PIA",
        ["PIASTRI"] = "PIA",
        ["CARLOS SAINZ"] = "SAI",
        ["SAINZ"] = "SAI",
        ["FERNANDO ALONSO"] = "ALO",
        ["ALONSO"] = "ALO",
        ["LANCE STROLL"] = "STR",
        ["STROLL"] = "STR",
        ["PIERRE GASLY"] = "GAS",
        ["GASLY"] = "GAS",
        ["ESTEBAN OCON"] = "OCO",
        ["OCON"] = "OCO",
        ["ALEX ALBON"] = "ALB",
        ["ALBON"] = "ALB",
        ["YUKI TSUNODA"] = "TSU",
        ["TSUNODA"] = "TSU",
        ["NICO HULKENBERG"] = "HUL",
        ["HULKENBERG"] = "HUL",
        ["DANIEL RICCIARDO"] = "RIC",
        ["RICCIARDO"] = "RIC",
        ["VALTTERI BOTTAS"] = "BOT",
        ["BOTTAS"] = "BOT",
        ["ZHOU GUANYU"] = "ZHO",
        ["GUANYU ZHOU"] = "ZHO",
        ["ZHOU"] = "ZHO",
        ["KEVIN MAGNUSSEN"] = "MAG",
        ["MAGNUSSEN"] = "MAG",
        ["OLIVER BEARMAN"] = "BEA",
        ["BEARMAN"] = "BEA",
        ["SERGIO PEREZ"] = "PER",
        ["PEREZ"] = "PER",
        ["FRANCO COLAPINTO"] = "COL",
        ["COLAPINTO"] = "COL",
        ["JACK DOOHAN"] = "DOO",
        ["DOOHAN"] = "DOO",
        ["GABRIEL BORTOLETO"] = "BOR",
        ["BORTOLETO"] = "BOR",
        ["ISACK HADJAR"] = "HAD",
        ["HADJAR"] = "HAD",
        ["LIAM LAWSON"] = "LAW",
        ["LAWSON"] = "LAW",
        ["KIMI ANTONELLI"] = "ANT",
        ["ANTONELLI"] = "ANT",
        ["NONE"] = null,
        ["NOT"] = null
    };

    private static readonly Dictionary<string, string> JolpicaDriverIdByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ALB"] = "albon",
        ["ALO"] = "alonso",
        ["ANT"] = "antonelli",
        ["BEA"] = "bearman",
        ["BOR"] = "bortoleto",
        ["BOT"] = "bottas",
        ["COL"] = "colapinto",
        ["DOO"] = "doohan",
        ["GAS"] = "gasly",
        ["HAD"] = "hadjar",
        ["HAM"] = "hamilton",
        ["HUL"] = "hulkenberg",
        ["LAW"] = "lawson",
        ["LEC"] = "leclerc",
        ["LIN"] = "lindblad",
        ["MAG"] = "magnussen",
        ["NOR"] = "norris",
        ["OCO"] = "ocon",
        ["PER"] = "perez",
        ["PIA"] = "piastri",
        ["RIC"] = "ricciardo",
        ["RUS"] = "russell",
        ["SAI"] = "sainz",
        ["STR"] = "stroll",
        ["TSU"] = "tsunoda",
        ["VER"] = "max_verstappen",
        ["ZHO"] = "zhou"
    };

    private static readonly Dictionary<string, string> JolpicaConstructorIdByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ALPINE"] = "alpine",
        ["ALPINE F1 TEAM"] = "alpine",
        ["AMR"] = "aston_martin",
        ["ASTON MARTIN"] = "aston_martin",
        ["ASTON MARTIN F1 TEAM"] = "aston_martin",
        ["FER"] = "ferrari",
        ["FERRARI"] = "ferrari",
        ["HAAS"] = "haas",
        ["HAAS F1 TEAM"] = "haas",
        ["MCL"] = "mclaren",
        ["MCLAREN"] = "mclaren",
        ["MERCEDES"] = "mercedes",
        ["RB"] = "rb",
        ["RBPT"] = "red_bull",
        ["RB F1 TEAM"] = "rb",
        ["RACING BULLS"] = "rb",
        ["RED BULL"] = "red_bull",
        ["RED BULL RACING"] = "red_bull",
        ["SAUBER"] = "sauber",
        ["WILLIAMS"] = "williams"
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
        var sourceProfile = MigrationSourceProfileResolver.Resolve(sourceFilePath ?? string.Empty);
        var usePhil2025SequenceMapping = sourceProfile == MigrationSourceProfile.Phil2025Csv;

        if (sourceProfile == MigrationSourceProfile.Dave2025Package)
        {
            return await ParseDave2025PackageAsync(runId, dbContext, cancellationToken);
        }

        var stagedRows = await dbContext.MigrationImportRawRows
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RowNumber)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var driverIdByCode = await dbContext.Drivers
            .Where(x => !string.IsNullOrWhiteSpace(x.Code) && !string.IsNullOrWhiteSpace(x.DriverId))
            .ToDictionaryAsync(
                x => x.Code!.Trim().ToUpperInvariant(),
                x => x.DriverId!.Trim().ToLowerInvariant(),
                cancellationToken);

        var headerRow = stagedRows.FirstOrDefault(x => string.Equals(x.SectionType, SectionTypeHeader, StringComparison.Ordinal));
        var participants = ResolveParticipants(headerRow?.RawPayload);
        var preseasonParticipants = usePhil2025SequenceMapping
            ? MigrationPhil2025CsvContractPolicy.ParticipantColumns.ToList()
            : participants;

        var preseasonAnswers = ParsePreseasonQuestionAnswers(runId, stagedRows, preseasonParticipants, usePhil2025SequenceMapping, driverIdByCode);
        var genericQuestions = await BuildGenericQuestionDataAsync(
            dbContext,
            runId,
            stagedRows,
            preseasonParticipants,
            usePhil2025SequenceMapping,
            driverIdByCode,
            cancellationToken);

        if (participants.Count == 0)
        {
            if (preseasonAnswers.Count > 0)
            {
                dbContext.MigrationImportPreseasonAnswers.RemoveRange(
                    dbContext.MigrationImportPreseasonAnswers.Where(x => x.ImportRunId == runId));
                await dbContext.SaveChangesAsync(cancellationToken);

                await dbContext.MigrationImportPreseasonAnswers.AddRangeAsync(preseasonAnswers, cancellationToken);
                if (genericQuestions is not null)
                {
                    var templateIds = await UpsertQuestionTemplatesAsync(dbContext, genericQuestions.Templates, cancellationToken);
                    var materialized = ApplyTemplateIds(genericQuestions, templateIds);
                    var templateIdSet = templateIds.Values.Distinct().ToArray();
                    dbContext.QuestionAnswers.RemoveRange(dbContext.QuestionAnswers.Where(x => templateIdSet.Contains(x.QuestionTemplateId)));
                    dbContext.QuestionActuals.RemoveRange(dbContext.QuestionActuals.Where(x => templateIdSet.Contains(x.QuestionTemplateId)));
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await dbContext.QuestionAnswers.AddRangeAsync(materialized.Answers, cancellationToken);
                    await dbContext.QuestionActuals.AddRangeAsync(materialized.Actuals, cancellationToken);
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
                var mappedSelectionValue = MapSelectionNormalizedValueToDriverIds(normalization.NormalizedValue, pickType, driverIdByCode);
                selections.Add(new MigrationImportRaceSelectionEntity
                {
                    ImportRunId = runId,
                    RowNumber = row.RowNumber,
                    RaceCode = persistedRaceCode,
                    PickType = pickType,
                    Subject = participants[index],
                    RawValue = string.IsNullOrWhiteSpace(rawValue) ? null : rawValue.Trim(),
                    NormalizedValue = mappedSelectionValue,
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
            var mappedActualSelectionValue = MapSelectionNormalizedValueToDriverIds(actualNormalization.NormalizedValue, pickType, driverIdByCode);
            selections.Add(new MigrationImportRaceSelectionEntity
            {
                ImportRunId = runId,
                RowNumber = row.RowNumber,
                RaceCode = persistedRaceCode,
                PickType = pickType,
                Subject = ActualSubject,
                RawValue = string.IsNullOrWhiteSpace(actualRaw) ? null : actualRaw.Trim(),
                NormalizedValue = mappedActualSelectionValue,
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

        if (selections.Count == 0 && preseasonAnswers.Count == 0 && genericQuestions is null)
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
        dbContext.MigrationImportUnresolvedTokens.RemoveRange(
            dbContext.MigrationImportUnresolvedTokens.Where(x => x.ImportRunId == runId));
        await dbContext.SaveChangesAsync(cancellationToken);

        await dbContext.MigrationImportRaceSelections.AddRangeAsync(selections, cancellationToken);
        if (preseasonAnswers.Count > 0)
        {
            await dbContext.MigrationImportPreseasonAnswers.AddRangeAsync(preseasonAnswers, cancellationToken);
        }

        if (genericQuestions is not null)
        {
            var templateIds = await UpsertQuestionTemplatesAsync(dbContext, genericQuestions.Templates, cancellationToken);
            var materialized = ApplyTemplateIds(genericQuestions, templateIds);
            var templateIdSet = templateIds.Values.Distinct().ToArray();
            dbContext.QuestionAnswers.RemoveRange(dbContext.QuestionAnswers.Where(x => templateIdSet.Contains(x.QuestionTemplateId)));
            dbContext.QuestionActuals.RemoveRange(dbContext.QuestionActuals.Where(x => templateIdSet.Contains(x.QuestionTemplateId)));
            await dbContext.SaveChangesAsync(cancellationToken);
            await dbContext.QuestionAnswers.AddRangeAsync(materialized.Answers, cancellationToken);
            await dbContext.QuestionActuals.AddRangeAsync(materialized.Actuals, cancellationToken);
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

    private async Task<MigrationRaceSelectionParseResult> ParseDave2025PackageAsync(
        Guid runId,
        F1DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var stagedRows = await dbContext.MigrationImportRawRows
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.SourceFileName)
            .ThenBy(x => x.RowNumber)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var driverIdByCode = await dbContext.Drivers
            .Where(x => !string.IsNullOrWhiteSpace(x.Code) && !string.IsNullOrWhiteSpace(x.DriverId))
            .ToDictionaryAsync(
                x => x.Code!.Trim().ToUpperInvariant(),
                x => x.DriverId!.Trim().ToLowerInvariant(),
                cancellationToken);

        var raceRows = GetRowsForSourceFile(stagedRows, DaveRacesFile);
        var bonusRows = GetRowsForSourceFile(stagedRows, DaveBonusFile);
        var bonusAnswerRows = GetRowsForSourceFile(stagedRows, DaveBonusAnswersFile);

        var unresolvedTokens = new List<MigrationImportUnresolvedTokenEntity>();
        var raceSelections = ParseDaveRaceSelections(runId, raceRows, driverIdByCode, unresolvedTokens);
        var preseasonAnswers = ParseDavePreseasonAnswers(runId, bonusRows, bonusAnswerRows, driverIdByCode, unresolvedTokens);
        var genericQuestions = await BuildDaveRaceQuestionDataAsync(
            dbContext,
            raceSelections,
            driverIdByCode,
            cancellationToken);

        dbContext.MigrationImportRaceSelections.RemoveRange(
            dbContext.MigrationImportRaceSelections.Where(x => x.ImportRunId == runId));
        dbContext.MigrationImportPreseasonAnswers.RemoveRange(
            dbContext.MigrationImportPreseasonAnswers.Where(x => x.ImportRunId == runId));
        dbContext.MigrationImportUnresolvedTokens.RemoveRange(
            dbContext.MigrationImportUnresolvedTokens.Where(x => x.ImportRunId == runId));
        await dbContext.SaveChangesAsync(cancellationToken);

        if (raceSelections.Count > 0)
        {
            await dbContext.MigrationImportRaceSelections.AddRangeAsync(raceSelections, cancellationToken);
        }

        if (preseasonAnswers.Count > 0)
        {
            await dbContext.MigrationImportPreseasonAnswers.AddRangeAsync(preseasonAnswers, cancellationToken);
        }

        if (genericQuestions is not null)
        {
            var templateIds = await UpsertQuestionTemplatesAsync(dbContext, genericQuestions.Templates, cancellationToken);
            var materialized = ApplyTemplateIds(genericQuestions, templateIds);
            var templateIdSet = templateIds.Values.Distinct().ToArray();
            dbContext.QuestionAnswers.RemoveRange(dbContext.QuestionAnswers.Where(x => templateIdSet.Contains(x.QuestionTemplateId)));
            dbContext.QuestionActuals.RemoveRange(dbContext.QuestionActuals.Where(x => templateIdSet.Contains(x.QuestionTemplateId)));
            await dbContext.SaveChangesAsync(cancellationToken);
            await dbContext.QuestionAnswers.AddRangeAsync(materialized.Answers, cancellationToken);
            await dbContext.QuestionActuals.AddRangeAsync(materialized.Actuals, cancellationToken);
        }

        if (unresolvedTokens.Count > 0)
        {
            await dbContext.MigrationImportUnresolvedTokens.AddRangeAsync(unresolvedTokens, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new MigrationRaceSelectionParseResult(
            SelectionCount: raceSelections.Count,
            UnresolvedTokenCount: unresolvedTokens.Count,
            PreseasonAnswerCount: preseasonAnswers.Count);
    }

    private async Task<GenericQuestionData?> BuildDaveRaceQuestionDataAsync(
        F1DbContext dbContext,
        IReadOnlyList<MigrationImportRaceSelectionEntity> raceSelections,
        IReadOnlyDictionary<string, string> driverIdByCode,
        CancellationToken cancellationToken)
    {
        if (raceSelections.Count == 0)
        {
            return null;
        }

        var participants = raceSelections
            .Where(x => !x.IsActualOutcome && !string.Equals(x.Subject, ActualSubject, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Subject)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (participants.Count == 0)
        {
            return null;
        }

        var competitionId = await ResolveTargetCompetitionIdAsync(
            dbContext,
            participants,
            usePhil2025Contract: false,
            cancellationToken);

        if (!competitionId.HasValue)
        {
            return null;
        }

        var questionPickRows = raceSelections
            .Where(x => IsDaveRaceQuestionPickType(x.PickType))
            .OrderBy(x => x.RaceCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.PickType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.RowNumber)
            .ThenBy(x => x.Subject, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (questionPickRows.Count == 0)
        {
            return null;
        }

        var templates = new List<QuestionTemplateEntity>();
        var answers = new List<PendingQuestionAnswer>();
        var actuals = new List<PendingQuestionActual>();
        var now = DateTime.UtcNow;

        foreach (var questionGroup in questionPickRows.GroupBy(x => new { x.RaceCode, PickType = x.PickType.ToUpperInvariant() }))
        {
            var category = string.Equals(questionGroup.Key.PickType, "H2H", StringComparison.OrdinalIgnoreCase)
                ? QuestionCategory.H2H
                : QuestionCategory.RaceBonus;

            var questionId = category == QuestionCategory.H2H
                ? $"H2H-{questionGroup.Key.RaceCode.ToUpperInvariant()}"
                : $"RB-{questionGroup.Key.RaceCode.ToUpperInvariant()}-{questionGroup.Key.PickType}";

            var prompt = $"{questionGroup.Key.RaceCode.ToUpperInvariant()} {questionGroup.Key.PickType}";
            var optionsJson = category == QuestionCategory.H2H
                ? BuildDaveH2hOptionsJson(questionGroup.ToList(), driverIdByCode, DefaultDaveH2hPointsForCorrectPick)
                : BuildRaceBonusOptionsJson(prompt, DefaultRaceBonusPointsForCorrectPick);

            templates.Add(new QuestionTemplateEntity
            {
                CompetitionId = competitionId.Value,
                Season = _importOptions.Season,
                QuestionId = questionId,
                Category = category,
                Prompt = prompt,
                OptionsJson = optionsJson,
                Status = QuestionTemplateStatus.Published,
                SortOrder = ResolveDaveRaceQuestionSortOrder(questionGroup.Key.RaceCode, questionGroup.Key.PickType),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

            foreach (var participant in questionGroup
                         .Where(x => !x.IsActualOutcome && !string.Equals(x.Subject, ActualSubject, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(x => x.Subject, StringComparer.OrdinalIgnoreCase))
            {
                var rawValue = !string.IsNullOrWhiteSpace(participant.RawValue)
                    ? participant.RawValue
                    : participant.NormalizedValue;
                var normalization = NormalizeQuestionAnswer(rawValue, isActualOutcome: false, category, driverIdByCode);
                answers.Add(new PendingQuestionAnswer(
                    QuestionId: questionId,
                    ParticipantId: participant.Subject,
                    ImportedAnswer: normalization.NormalizedValue,
                    OverrideAnswer: null,
                    RecordedAtUtc: now));
            }

            var actualRow = questionGroup
                .Where(x => x.IsActualOutcome || string.Equals(x.Subject, ActualSubject, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.RowNumber)
                .FirstOrDefault();

            if (actualRow is not null)
            {
                var rawActual = !string.IsNullOrWhiteSpace(actualRow.RawValue)
                    ? actualRow.RawValue
                    : actualRow.NormalizedValue;
                var actualNormalization = NormalizeQuestionAnswer(rawActual, isActualOutcome: true, category, driverIdByCode);
                actuals.Add(new PendingQuestionActual(
                    QuestionId: questionId,
                    ImportedAnswer: actualNormalization.NormalizedValue,
                    OverrideAnswer: null,
                    RecordedAtUtc: now));
            }
        }

        return templates.Count == 0
            ? null
            : new GenericQuestionData(templates, answers, actuals);
    }

    private static IReadOnlyList<MigrationImportRawRowEntity> GetRowsForSourceFile(
        IReadOnlyList<MigrationImportRawRowEntity> stagedRows,
        string sourceFileName)
    {
        return stagedRows
            .Where(x => string.Equals(x.SourceFileName, sourceFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.RowNumber)
            .ToList();
    }

    private static List<MigrationImportRaceSelectionEntity> ParseDaveRaceSelections(
        Guid runId,
        IReadOnlyList<MigrationImportRawRowEntity> raceRows,
        IReadOnlyDictionary<string, string> driverIdByCode,
        List<MigrationImportUnresolvedTokenEntity> unresolvedTokens)
    {
        if (raceRows.Count == 0)
        {
            return [];
        }

        var headerColumns = CsvLineParser.Parse(raceRows[0].RawPayload);
        if (headerColumns.Count == 0)
        {
            return [];
        }

        var raceColumns = new List<(int ColumnIndex, int RaceNumber, string PickType)>();
        for (var columnIndex = 0; columnIndex < headerColumns.Count; columnIndex++)
        {
            var token = headerColumns[columnIndex].Trim();
            var match = DaveRaceColumnRegex().Match(token);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var raceNumber))
            {
                continue;
            }

            raceColumns.Add((columnIndex, raceNumber, match.Groups[2].Value.ToUpperInvariant()));
        }

        if (raceColumns.Count == 0)
        {
            return [];
        }

        var selections = new List<MigrationImportRaceSelectionEntity>();
        var createdAtUtc = DateTime.UtcNow;

        foreach (var row in raceRows.Skip(1))
        {
            var columns = CsvLineParser.Parse(row.RawPayload);
            if (columns.Count == 0)
            {
                continue;
            }

            var name = columns[0].Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var isActualOutcome = string.Equals(name, "_Result", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(name, "Result", StringComparison.OrdinalIgnoreCase);
            var subject = isActualOutcome ? ActualSubject : name;

            foreach (var mappedColumn in raceColumns)
            {
                var rawValue = mappedColumn.ColumnIndex < columns.Count ? columns[mappedColumn.ColumnIndex] : null;
                var raceCode = $"R{mappedColumn.RaceNumber:D2}";
                var pickType = mappedColumn.PickType;

                string? normalizedValue;
                IReadOnlyList<string> unresolved;

                if (pickType is "1" or "2" or "3" or "DNF")
                {
                    var normalization = NormalizeSelection(rawValue, pickType, applyPhil2025TokenCorrections: false);
                    normalizedValue = MapSelectionNormalizedValueToDriverIds(normalization.NormalizedValue, pickType, driverIdByCode);
                    unresolved = normalization.UnresolvedTokens;
                }
                else if (pickType == "PQ")
                {
                    normalizedValue = string.IsNullOrWhiteSpace(rawValue) ? null : NormalizeTokenLookup(rawValue);
                    unresolved = Array.Empty<string>();
                }
                else
                {
                    normalizedValue = string.IsNullOrWhiteSpace(rawValue)
                        ? null
                        : MultiWhitespaceRegex().Replace(rawValue.Trim(), " ");
                    unresolved = Array.Empty<string>();
                }

                selections.Add(new MigrationImportRaceSelectionEntity
                {
                    ImportRunId = runId,
                    RowNumber = row.RowNumber,
                    RaceCode = raceCode,
                    PickType = pickType,
                    Subject = subject,
                    RawValue = string.IsNullOrWhiteSpace(rawValue) ? null : rawValue.Trim(),
                    NormalizedValue = normalizedValue,
                    IsActualOutcome = isActualOutcome
                });

                if (unresolved.Count == 0)
                {
                    continue;
                }

                unresolvedTokens.AddRange(unresolved.Select(unresolvedToken => new MigrationImportUnresolvedTokenEntity
                {
                    ImportRunId = runId,
                    RowNumber = row.RowNumber,
                    RaceCode = raceCode,
                    PickType = pickType,
                    Subject = subject,
                    RawToken = unresolvedToken,
                    CreatedAtUtc = createdAtUtc
                }));
            }
        }

        return selections;
    }

    private static List<MigrationImportPreseasonAnswerEntity> ParseDavePreseasonAnswers(
        Guid runId,
        IReadOnlyList<MigrationImportRawRowEntity> bonusRows,
        IReadOnlyList<MigrationImportRawRowEntity> bonusAnswerRows,
        IReadOnlyDictionary<string, string> driverIdByCode,
        List<MigrationImportUnresolvedTokenEntity> unresolvedTokens)
    {
        if (bonusRows.Count == 0)
        {
            return [];
        }

        var bonusHeader = CsvLineParser.Parse(bonusRows[0].RawPayload);
        if (bonusHeader.Count < 2)
        {
            return [];
        }

        var participants = bonusHeader
            .Skip(1)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        if (participants.Count == 0)
        {
            return [];
        }

        var createdAtUtc = DateTime.UtcNow;
        var answersByQuestionKey = BuildDaveAnswerKeyMap(runId, bonusAnswerRows, unresolvedTokens, createdAtUtc);

        var parsed = new List<MigrationImportPreseasonAnswerEntity>();
        var questionOrdinal = 0;
        var seenQuestionKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in bonusRows.Skip(1))
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

            questionOrdinal++;
            var questionKey = $"PRE-{questionOrdinal:D3}";
            var lookupKey = NormalizeQuestionLookupKey(questionText);
            if (seenQuestionKeys.TryGetValue(lookupKey, out var firstRow))
            {
                unresolvedTokens.Add(new MigrationImportUnresolvedTokenEntity
                {
                    ImportRunId = runId,
                    RowNumber = row.RowNumber,
                    RaceCode = "PRESEASON",
                    PickType = "QUESTION_KEY",
                    Subject = ActualSubject,
                    RawToken = $"Normalized question-key collision in bonus.csv for '{lookupKey}' (first seen at row {firstRow})",
                    CreatedAtUtc = createdAtUtc
                });
            }
            else
            {
                seenQuestionKeys[lookupKey] = row.RowNumber;
            }

            for (var index = 0; index < participants.Count; index++)
            {
                var columnIndex = index + 1;
                var raw = columnIndex < columns.Count ? columns[columnIndex] : null;
                var normalized = NormalizePreseasonAnswer(raw, isActualOutcome: false, driverIdByCode);
                parsed.Add(new MigrationImportPreseasonAnswerEntity
                {
                    ImportRunId = runId,
                    RowNumber = row.RowNumber,
                    QuestionKey = questionKey,
                    QuestionText = questionText,
                    Subject = participants[index],
                    RawAnswer = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim(),
                    NormalizedAnswer = normalized.NormalizedValue,
                    NormalizedAnswerBoolean = ToNullableBoolean(normalized.NormalizedValue),
                    IsActualOutcome = false
                });
            }

            var hasActualEntry = answersByQuestionKey.TryGetValue(lookupKey, out var actualEntry);
            if (!hasActualEntry)
            {
                unresolvedTokens.Add(new MigrationImportUnresolvedTokenEntity
                {
                    ImportRunId = runId,
                    RowNumber = row.RowNumber,
                    RaceCode = "PRESEASON",
                    PickType = "QUESTION_KEY",
                    Subject = ActualSubject,
                    RawToken = $"No matching actual answer for question key '{lookupKey}'",
                    CreatedAtUtc = createdAtUtc
                });
            }

            var rawActualAnswer = hasActualEntry ? actualEntry.Answer : null;
            var normalizedActual = NormalizePreseasonAnswer(rawActualAnswer, isActualOutcome: true, driverIdByCode);
            parsed.Add(new MigrationImportPreseasonAnswerEntity
            {
                ImportRunId = runId,
                RowNumber = row.RowNumber,
                QuestionKey = questionKey,
                QuestionText = questionText,
                Subject = ActualSubject,
                RawAnswer = rawActualAnswer,
                NormalizedAnswer = normalizedActual.NormalizedValue,
                NormalizedAnswerBoolean = ToNullableBoolean(normalizedActual.NormalizedValue),
                IsActualOutcome = true
            });
        }

        return parsed;
    }

    private static Dictionary<string, (string QuestionText, string? Answer, int RowNumber)> BuildDaveAnswerKeyMap(
        Guid runId,
        IReadOnlyList<MigrationImportRawRowEntity> bonusAnswerRows,
        List<MigrationImportUnresolvedTokenEntity> unresolvedTokens,
        DateTime createdAtUtc)
    {
        var map = new Dictionary<string, (string QuestionText, string? Answer, int RowNumber)>(StringComparer.OrdinalIgnoreCase);
        if (bonusAnswerRows.Count == 0)
        {
            return map;
        }

        foreach (var row in bonusAnswerRows.Skip(1))
        {
            var columns = CsvLineParser.Parse(row.RawPayload);
            if (columns.Count < 2)
            {
                continue;
            }

            var questionText = columns[0].Trim();
            if (string.IsNullOrWhiteSpace(questionText))
            {
                continue;
            }

            var lookupKey = NormalizeQuestionLookupKey(questionText);
            var answer = columns[1].Trim();

            if (map.ContainsKey(lookupKey))
            {
                unresolvedTokens.Add(new MigrationImportUnresolvedTokenEntity
                {
                    ImportRunId = runId,
                    RowNumber = row.RowNumber,
                    RaceCode = "PRESEASON",
                    PickType = "QUESTION_KEY",
                    Subject = ActualSubject,
                    RawToken = $"Question-key collision for '{lookupKey}'",
                    CreatedAtUtc = createdAtUtc
                });
            }

            map[lookupKey] = (questionText, string.IsNullOrWhiteSpace(answer) ? null : answer, row.RowNumber);
        }

        return map;
    }

    private static string NormalizeQuestionLookupKey(string questionText)
    {
        var normalized = MultiWhitespaceRegex().Replace(questionText.Trim().ToLowerInvariant(), " ");
        return QuestionLookupKeyRegex().Replace(normalized, string.Empty);
    }

    private async Task<GenericQuestionData?> BuildGenericQuestionDataAsync(
        F1DbContext dbContext,
        Guid runId,
        IReadOnlyCollection<MigrationImportRawRowEntity> stagedRows,
        IReadOnlyList<string> participants,
        bool usePhil2025Contract,
        IReadOnlyDictionary<string, string> driverIdByCode,
        CancellationToken cancellationToken)
    {
        var questionRows = stagedRows
            .Where(x => string.Equals(x.SectionType, SectionTypeSeasonQuestionPrediction, StringComparison.Ordinal))
            .OrderBy(x => x.RowNumber)
            .ToList();

        if (questionRows.Count == 0)
        {
            return null;
        }

        var competitionId = await ResolveTargetCompetitionIdAsync(
            dbContext,
            participants,
            usePhil2025Contract,
            cancellationToken);

        if (!competitionId.HasValue)
        {
            return null;
        }

        var templateKeys = questionRows.Select(row => ResolveQuestionId(row.RowNumber, row.RawPayload, usePhil2025Contract)).ToArray();
        var existingTemplateIds = await dbContext.QuestionTemplates
            .Where(x => x.CompetitionId == competitionId.Value && x.Season == _importOptions.Season && templateKeys.Contains(x.QuestionId))
            .ToDictionaryAsync(x => x.QuestionId, x => x.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var now = DateTime.UtcNow;
        var templates = new List<QuestionTemplateEntity>();
        var answers = new List<PendingQuestionAnswer>();
        var actuals = new List<PendingQuestionActual>();

        foreach (var row in questionRows)
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

            var questionId = ResolveQuestionId(row.RowNumber, row.RawPayload, usePhil2025Contract);
            var category = ResolveQuestionCategory(row.RowNumber, row.RawPayload, usePhil2025Contract);
            var defaultH2hPoints = usePhil2025Contract
                ? DefaultPhilH2hPointsForCorrectPick
                : DefaultDaveH2hPointsForCorrectPick;
            var optionsJson = category == QuestionCategory.H2H
                ? BuildH2hOptionsJson(questionText, columns, participants, usePhil2025Contract, driverIdByCode, defaultH2hPoints)
                : category == QuestionCategory.RaceBonus
                    ? BuildRaceBonusOptionsJson(questionText, DefaultRaceBonusPointsForCorrectPick)
                : null;

            templates.Add(new QuestionTemplateEntity
            {
                Id = existingTemplateIds.TryGetValue(questionId, out var existingTemplateId) ? existingTemplateId : 0,
                CompetitionId = competitionId.Value,
                Season = _importOptions.Season,
                QuestionId = questionId,
                Category = category,
                Prompt = questionText,
                OptionsJson = optionsJson,
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
                var normalization = NormalizeQuestionAnswer(raw, isActualOutcome: false, category, driverIdByCode);
                answers.Add(new PendingQuestionAnswer(
                    QuestionId: questionId,
                    ParticipantId: participants[index],
                    ImportedAnswer: normalization.NormalizedValue,
                    OverrideAnswer: null,
                    RecordedAtUtc: now));
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

            var actualNormalization = NormalizeQuestionAnswer(actualRaw, isActualOutcome: true, category, driverIdByCode);
            actuals.Add(new PendingQuestionActual(
                QuestionId: questionId,
                ImportedAnswer: actualNormalization.NormalizedValue,
                OverrideAnswer: null,
                RecordedAtUtc: now));
        }

        return templates.Count == 0
            ? null
            : new GenericQuestionData(templates, answers, actuals);
    }

    private async Task<int?> ResolveTargetCompetitionIdAsync(
        F1DbContext dbContext,
        IReadOnlyList<string> participants,
        bool usePhil2025Contract,
        CancellationToken cancellationToken)
    {
        var competitions = await dbContext.Competitions
            .Where(x => x.Year == _importOptions.Season)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Name, x.Description })
            .ToListAsync(cancellationToken);

        if (competitions.Count == 0)
        {
            return null;
        }

        if (competitions.Count == 1)
        {
            return competitions[0].Id;
        }

        var shouldPreferPhilipCompetition = usePhil2025Contract ||
            participants.Any(x => string.Equals(x, "Philip", StringComparison.OrdinalIgnoreCase));

        if (shouldPreferPhilipCompetition)
        {
            var exactPhilipCompetition = competitions.FirstOrDefault(x =>
                string.Equals(x.Name, Philip2025CompetitionName, StringComparison.OrdinalIgnoreCase));

            if (exactPhilipCompetition is not null)
            {
                return exactPhilipCompetition.Id;
            }

            var philipCompetition = competitions.FirstOrDefault(x =>
                x.Name.Contains("Philip", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains("Phil", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(x.Description) &&
                 (x.Description.Contains("Philip", StringComparison.OrdinalIgnoreCase) ||
                  x.Description.Contains("Phil", StringComparison.OrdinalIgnoreCase))));

            if (philipCompetition is not null)
            {
                return philipCompetition.Id;
            }
        }

        var mainCompetition = competitions.FirstOrDefault(x =>
            x.Name.Contains("Main", StringComparison.OrdinalIgnoreCase));

        return (mainCompetition ?? competitions[0]).Id;
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

    private static MaterializedGenericQuestionData ApplyTemplateIds(GenericQuestionData genericQuestions, IReadOnlyDictionary<string, long> templateIds)
    {
        var answers = genericQuestions.Answers
            .Select(answer => new QuestionAnswerEntity
            {
                QuestionTemplateId = templateIds[answer.QuestionId],
                ParticipantId = answer.ParticipantId,
                ImportedAnswer = answer.ImportedAnswer,
                OverrideAnswer = answer.OverrideAnswer,
                RecordedAtUtc = answer.RecordedAtUtc
            })
            .ToList();

        var actuals = genericQuestions.Actuals
            .Select(actual => new QuestionActualEntity
            {
                QuestionTemplateId = templateIds[actual.QuestionId],
                ImportedAnswer = actual.ImportedAnswer,
                OverrideAnswer = actual.OverrideAnswer,
                RecordedAtUtc = actual.RecordedAtUtc
            })
            .ToList();

        return new MaterializedGenericQuestionData(answers, actuals);
    }

    private static List<MigrationImportPreseasonAnswerEntity> ParsePreseasonQuestionAnswers(
        Guid runId,
        IReadOnlyCollection<MigrationImportRawRowEntity> stagedRows,
        IReadOnlyList<string> participants,
        bool usePhil2025Contract,
        IReadOnlyDictionary<string, string> driverIdByCode)
    {
        if (participants.Count == 0)
        {
            return [];
        }

        var preseasonRows = stagedRows
            .Where(x => string.Equals(x.SectionType, SectionTypeSeasonQuestionPrediction, StringComparison.Ordinal))
            .Where(x => ResolveQuestionCategory(x.RowNumber, x.RawPayload, usePhil2025Contract) == QuestionCategory.Preseason)
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
                var normalized = NormalizePreseasonAnswer(raw, isActualOutcome: false, driverIdByCode);
                parsed.Add(new MigrationImportPreseasonAnswerEntity
                {
                    ImportRunId = runId,
                    RowNumber = row.RowNumber,
                    QuestionKey = questionKey,
                    QuestionText = questionText,
                    Subject = participants[index],
                    RawAnswer = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim(),
                    NormalizedAnswer = normalized.NormalizedValue,
                    NormalizedAnswerBoolean = ToNullableBoolean(normalized.NormalizedValue),
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

            var normalizedActual = NormalizePreseasonAnswer(actualRaw, isActualOutcome: true, driverIdByCode);
            parsed.Add(new MigrationImportPreseasonAnswerEntity
            {
                ImportRunId = runId,
                RowNumber = row.RowNumber,
                QuestionKey = questionKey,
                QuestionText = questionText,
                Subject = ActualSubject,
                RawAnswer = string.IsNullOrWhiteSpace(actualRaw) ? null : actualRaw.Trim(),
                NormalizedAnswer = normalizedActual.NormalizedValue,
                NormalizedAnswerBoolean = ToNullableBoolean(normalizedActual.NormalizedValue),
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

    private static PreseasonNormalizationResult NormalizeQuestionAnswer(
        string? rawAnswer,
        bool isActualOutcome,
        QuestionCategory category,
        IReadOnlyDictionary<string, string> driverIdByCode)
    {
        return category == QuestionCategory.H2H
            ? NormalizeH2hAnswer(rawAnswer, driverIdByCode)
            : NormalizePreseasonAnswer(rawAnswer, isActualOutcome, driverIdByCode);
    }

    private static PreseasonNormalizationResult NormalizeH2hAnswer(string? rawAnswer, IReadOnlyDictionary<string, string> driverIdByCode)
    {
        if (string.IsNullOrWhiteSpace(rawAnswer))
        {
            return new PreseasonNormalizationResult(null, ["NULL_OR_WHITESPACE"]);
        }

        var lookupToken = NormalizeTokenLookup(rawAnswer);
        if (QuestionTokenAliasDictionary.TryGetValue(lookupToken, out var mappedQuestionToken))
        {
            return new PreseasonNormalizationResult(MapDriverCodeToId(mappedQuestionToken, driverIdByCode), []);
        }

        if (TokenAliasDictionary.TryGetValue(lookupToken, out var mappedToken))
        {
            return new PreseasonNormalizationResult(MapDriverCodeToId(mappedToken, driverIdByCode), []);
        }

        if (CanonicalTokenRegex().IsMatch(lookupToken))
        {
            return new PreseasonNormalizationResult(MapDriverCodeToId(lookupToken, driverIdByCode), []);
        }

        var normalized = MultiWhitespaceRegex().Replace(rawAnswer.Trim(), " ");
        return new PreseasonNormalizationResult(normalized, ["H2H_UNSUPPORTED_TOKEN_SHAPE_PRESERVED"]);
    }

    private static QuestionCategory ResolveQuestionCategory(int rowNumber, string rawPayload, bool usePhil2025Contract)
    {
        var columns = CsvLineParser.Parse(rawPayload);
        if (columns.Count == 0)
        {
            return QuestionCategory.Preseason;
        }

        var prompt = columns[0].Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return QuestionCategory.Preseason;
        }

        if (H2hPromptRegex().IsMatch(prompt))
        {
            return QuestionCategory.H2H;
        }

        if (usePhil2025Contract &&
            rowNumber >= MigrationPhil2025CsvContractPolicy.PreseasonQuestionStartRow &&
            rowNumber <= MigrationPhil2025CsvContractPolicy.PreseasonQuestionEndRow)
        {
            return QuestionCategory.Preseason;
        }

        if (RaceBonusPromptRegex().IsMatch(prompt))
        {
            return QuestionCategory.RaceBonus;
        }

        return QuestionCategory.Preseason;
    }

    private static string ResolveQuestionId(int rowNumber, string rawPayload, bool usePhil2025Contract)
    {
        var category = ResolveQuestionCategory(rowNumber, rawPayload, usePhil2025Contract);
        return category switch
        {
            QuestionCategory.H2H => $"H2H-{rowNumber:D3}",
            _ => $"PRE-{rowNumber:D3}"
        };
    }

    private static string? BuildH2hOptionsJson(
        string questionText,
        IReadOnlyList<string> columns,
        IReadOnlyList<string> participants,
        bool usePhil2025Contract,
        IReadOnlyDictionary<string, string> driverIdByCode,
        int pointsForCorrectPick)
    {
        var driverCandidates = ExtractH2hCandidatesFromPrompt(questionText, driverIdByCode);
        if (driverCandidates.Count < 2)
        {
            var participantStartIndex = usePhil2025Contract
                ? MigrationPhil2025CsvContractPolicy.ParticipantStartColumnIndex
                : 1;

            var fallbackCandidates = new List<string>();
            for (var index = 0; index < participants.Count; index++)
            {
                var columnIndex = participantStartIndex + index;
                var rawAnswer = columnIndex < columns.Count ? columns[columnIndex] : null;
                var normalized = NormalizeH2hAnswer(rawAnswer, driverIdByCode).NormalizedValue;
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    fallbackCandidates.Add(normalized);
                }
            }

            var actualColumnIndex = usePhil2025Contract
                ? MigrationPhil2025CsvContractPolicy.ActualAnswerColumnIndex
                : columns.Count - 1;
            var actualRaw = actualColumnIndex >= 0 && actualColumnIndex < columns.Count
                ? columns[actualColumnIndex]
                : null;
            var actualNormalized = NormalizeH2hAnswer(actualRaw, driverIdByCode).NormalizedValue;
            if (!string.IsNullOrWhiteSpace(actualNormalized))
            {
                fallbackCandidates.Add(actualNormalized);
            }

            foreach (var candidate in fallbackCandidates)
            {
                if (!driverCandidates.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    driverCandidates.Add(candidate);
                }

                if (driverCandidates.Count == 2)
                {
                    break;
                }
            }
        }

        if (driverCandidates.Count < 2)
        {
            return null;
        }

        var options = new H2hQuestionTemplateOptions
        {
            LeftDriverId = driverCandidates[0],
            RightDriverId = driverCandidates[1],
            PointsForCorrectPick = pointsForCorrectPick
        };

        return JsonSerializer.Serialize(options);
    }

    private static string BuildRaceBonusOptionsJson(string questionText, int pointsForCorrectPick)
    {
        if (questionText.Contains("SAU", StringComparison.OrdinalIgnoreCase) &&
            questionText.Contains("GAP", StringComparison.OrdinalIgnoreCase))
        {
            var formulaOptions = new RaceBonusQuestionTemplateOptions
            {
                Mode = "FormulaMaxMinusGap",
                PointsForCorrectPick = pointsForCorrectPick,
                FormulaMaxPoints = pointsForCorrectPick,
                FormulaPenaltyPerUnit = 1m
            };

            return JsonSerializer.Serialize(formulaOptions);
        }

        if (questionText.Contains("MON", StringComparison.OrdinalIgnoreCase) ||
            questionText.Contains("GBR", StringComparison.OrdinalIgnoreCase) ||
            questionText.Contains("+/-", StringComparison.OrdinalIgnoreCase))
        {
            var toleranceOptions = new RaceBonusQuestionTemplateOptions
            {
                Mode = "Tolerance",
                PointsForCorrectPick = pointsForCorrectPick,
                Tolerance = 1m
            };

            return JsonSerializer.Serialize(toleranceOptions);
        }

        var exactOptions = new RaceBonusQuestionTemplateOptions
        {
            Mode = "Exact",
            PointsForCorrectPick = pointsForCorrectPick
        };

        return JsonSerializer.Serialize(exactOptions);
    }

    private static string? BuildDaveH2hOptionsJson(
        IReadOnlyList<MigrationImportRaceSelectionEntity> questionRows,
        IReadOnlyDictionary<string, string> driverIdByCode,
        int pointsForCorrectPick)
    {
        var candidates = new List<string>();

        foreach (var row in questionRows
                     .OrderBy(x => x.IsActualOutcome)
                     .ThenBy(x => x.RowNumber)
                     .ThenBy(x => x.Subject, StringComparer.OrdinalIgnoreCase))
        {
            var raw = !string.IsNullOrWhiteSpace(row.RawValue)
                ? row.RawValue
                : row.NormalizedValue;
            var normalized = NormalizeH2hAnswer(raw, driverIdByCode).NormalizedValue;

            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (!candidates.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(normalized);
            }

            if (candidates.Count == 2)
            {
                break;
            }
        }

        if (candidates.Count < 2)
        {
            return null;
        }

        var options = new H2hQuestionTemplateOptions
        {
            LeftDriverId = candidates[0],
            RightDriverId = candidates[1],
            PointsForCorrectPick = pointsForCorrectPick
        };

        return JsonSerializer.Serialize(options);
    }

    private static bool IsDaveRaceQuestionPickType(string pickType)
    {
        return string.Equals(pickType, "H2H", StringComparison.OrdinalIgnoreCase) ||
               pickType.StartsWith("BQ", StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolveDaveRaceQuestionSortOrder(string raceCode, string pickType)
    {
        var raceOrder = 999;
        if (raceCode.Length > 1 && raceCode.StartsWith("R", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(raceCode[1..], out var parsedRaceOrder))
        {
            raceOrder = parsedRaceOrder;
        }

        var pickOrder = string.Equals(pickType, "H2H", StringComparison.OrdinalIgnoreCase)
            ? 1
            : string.Equals(pickType, "BQ1", StringComparison.OrdinalIgnoreCase)
                ? 2
                : string.Equals(pickType, "BQ2", StringComparison.OrdinalIgnoreCase)
                    ? 3
                    : 9;

        return (raceOrder * 10) + pickOrder;
    }

    private static List<string> ExtractH2hCandidatesFromPrompt(string questionText, IReadOnlyDictionary<string, string> driverIdByCode)
    {
        var candidates = new List<string>();
        foreach (Match match in H2hDriverTokenRegex().Matches(questionText))
        {
            var token = match.Value;
            var normalized = NormalizeH2hAnswer(token, driverIdByCode).NormalizedValue;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (!candidates.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(normalized);
            }

            if (candidates.Count == 2)
            {
                break;
            }
        }

        return candidates;
    }

    private static PreseasonNormalizationResult NormalizePreseasonAnswer(
        string? rawAnswer,
        bool isActualOutcome,
        IReadOnlyDictionary<string, string> driverIdByCode)
    {
        if (string.IsNullOrWhiteSpace(rawAnswer))
        {
            return new PreseasonNormalizationResult(null, ["NULL_OR_WHITESPACE"]);
        }

        var normalized = MultiWhitespaceRegex().Replace(rawAnswer.Trim(), " ");
        var mappedAtomic = NormalizeQuestionToken(normalized, driverIdByCode);
        if (mappedAtomic is null)
        {
            var lookupToken = NormalizeTokenLookup(normalized);
            if (QuestionTokenAliasDictionary.ContainsKey(lookupToken) || TokenAliasDictionary.ContainsKey(lookupToken))
            {
                return new PreseasonNormalizationResult(null, ["NULL_EQUIVALENT_TOKEN"]);
            }
        }
        else if (!string.IsNullOrWhiteSpace(mappedAtomic))
        {
            normalized = mappedAtomic;
        }

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
            .Select(token => NormalizeQuestionToken(token, driverIdByCode) ?? string.Empty)
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

    [GeneratedRegex("(head\\s*[- ]?to\\s*[- ]?head|h2h)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex H2hPromptRegex();

    [GeneratedRegex("(dnf|fastest\\s*lap|bonus)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RaceBonusPromptRegex();

    [GeneratedRegex("\\b[A-Za-z]{3}\\b", RegexOptions.Compiled)]
    private static partial Regex H2hDriverTokenRegex();

    [GeneratedRegex("^Race(\\d+)-(PQ|1|2|3|DNF|H2H|BQ1|BQ2)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DaveRaceColumnRegex();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex QuestionLookupKeyRegex();

    private static readonly HashSet<string> PodiumPickTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "1",
        "2",
        "3"
    };

    private readonly record struct NormalizationResult(string? NormalizedValue, IReadOnlyList<string> UnresolvedTokens);

    private readonly record struct PreseasonNormalizationResult(string? NormalizedValue, IReadOnlyList<string> Diagnostics);

    private static string? NormalizeQuestionToken(string? token, IReadOnlyDictionary<string, string> driverIdByCode)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var lookupToken = NormalizeTokenLookup(token);
        if (JolpicaConstructorIdByName.TryGetValue(lookupToken, out var mappedConstructorId))
        {
            return mappedConstructorId;
        }

        if (QuestionTokenAliasDictionary.TryGetValue(lookupToken, out var mappedToken))
        {
            return MapDriverCodeToId(mappedToken, driverIdByCode);
        }

        if (TokenAliasDictionary.TryGetValue(lookupToken, out var mappedSelectionToken))
        {
            return MapDriverCodeToId(mappedSelectionToken, driverIdByCode);
        }

        if (CanonicalTokenRegex().IsMatch(lookupToken))
        {
            return MapDriverCodeToId(lookupToken, driverIdByCode);
        }

        return MultiWhitespaceRegex().Replace(token.Trim(), " ");
    }

    private static string? MapDriverCodeToId(string? value, IReadOnlyDictionary<string, string> driverIdByCode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var token = value.Trim();
        if (token.Length != 3)
        {
            return token;
        }

        var code = token.ToUpperInvariant();
        if (driverIdByCode.TryGetValue(code, out var mappedDriverIdFromDb))
        {
            return mappedDriverIdFromDb;
        }

        return JolpicaDriverIdByCode.TryGetValue(code, out var mappedDriverId)
            ? mappedDriverId
            : token;
    }

    private static string? MapSelectionNormalizedValueToDriverIds(
        string? normalizedValue,
        string pickType,
        IReadOnlyDictionary<string, string> driverIdByCode)
    {
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return normalizedValue;
        }

        var trimmed = normalizedValue.Trim();
        if (!string.Equals(pickType, "DNF", StringComparison.OrdinalIgnoreCase))
        {
            return MapDriverCodeToId(trimmed, driverIdByCode);
        }

        var mappedTokens = trimmed
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => MapDriverCodeToId(token, driverIdByCode) ?? token)
            .ToArray();

        return mappedTokens.Length == 0 ? null : string.Join(" ", mappedTokens);
    }

    private static bool? ToNullableBoolean(string? normalizedValue)
    {
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return null;
        }

        var token = normalizedValue.Trim().ToUpperInvariant();
        if (token is "YES" or "TRUE")
        {
            return true;
        }

        if (token is "NO" or "FALSE")
        {
            return false;
        }

        return null;
    }

    private sealed record GenericQuestionData(
        IReadOnlyList<QuestionTemplateEntity> Templates,
        IReadOnlyList<PendingQuestionAnswer> Answers,
        IReadOnlyList<PendingQuestionActual> Actuals);

    private sealed record PendingQuestionAnswer(
        string QuestionId,
        string ParticipantId,
        string? ImportedAnswer,
        string? OverrideAnswer,
        DateTime RecordedAtUtc);

    private sealed record PendingQuestionActual(
        string QuestionId,
        string? ImportedAnswer,
        string? OverrideAnswer,
        DateTime RecordedAtUtc);

    private sealed record MaterializedGenericQuestionData(
        IReadOnlyList<QuestionAnswerEntity> Answers,
        IReadOnlyList<QuestionActualEntity> Actuals);
}