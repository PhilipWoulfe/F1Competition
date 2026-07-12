using F1.Core.Models;
using F1.DataSyncWorker.Models;
using F1.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace F1.DataSyncWorker.Services;

public static class MigrationCompetitionScopeResolver
{
    private const string PhilCompetitionName = "Philip 2025";
    private const string DaveCompetitionName = "Dave 2025";

    public static async Task<Competition?> ResolveCompetitionAsync(
        F1DbContext dbContext,
        int season,
        MigrationSourceProfile sourceProfile,
        IReadOnlyCollection<string> participants,
        CancellationToken cancellationToken)
    {
        var competitions = await dbContext.Competitions
            .Where(x => x.Year == season)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        if (competitions.Count == 0)
        {
            return null;
        }

        if (competitions.Count == 1)
        {
            return competitions[0];
        }

        var preferred = sourceProfile switch
        {
            MigrationSourceProfile.Phil2025Csv =>
                FindPhilCompetition(competitions),
            MigrationSourceProfile.Dave2025Package =>
                FindDaveCompetition(competitions),
            _ =>
                ResolveFallbackCompetition(competitions, participants)
        };

        return preferred ?? competitions[0];
    }

    public static async Task<Competition> ResolveOrCreateCompetitionAsync(
        F1DbContext dbContext,
        int season,
        MigrationSourceProfile sourceProfile,
        IReadOnlyCollection<string> participants,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveCompetitionAsync(dbContext, season, sourceProfile, participants, cancellationToken);
        if (resolved is not null)
        {
            return resolved;
        }

        var competitionName = sourceProfile switch
        {
            MigrationSourceProfile.Phil2025Csv => PhilCompetitionName,
            MigrationSourceProfile.Dave2025Package => DaveCompetitionName,
            _ => $"Migration Import {season}"
        };

        var competition = new Competition
        {
            Name = competitionName,
            Year = season,
            Description = sourceProfile == MigrationSourceProfile.Dave2025Package
                ? "Auto-created for Dave 2025 migration canonical write scope"
                : "Auto-created by migration canonical writer"
        };

        dbContext.Competitions.Add(competition);
        await dbContext.SaveChangesAsync(cancellationToken);
        return competition;
    }

    private static Competition? ResolveFallbackCompetition(IReadOnlyList<Competition> competitions, IReadOnlyCollection<string> participants)
    {
        if (participants.Any(x => string.Equals(x, "Philip", StringComparison.OrdinalIgnoreCase)))
        {
            var phil = FindPhilCompetition(competitions);
            if (phil is not null)
            {
                return phil;
            }
        }

        if (participants.Any(x => string.Equals(x, "Dave", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(x, "David", StringComparison.OrdinalIgnoreCase)))
        {
            var dave = FindDaveCompetition(competitions);
            if (dave is not null)
            {
                return dave;
            }
        }

        return competitions.FirstOrDefault(x =>
            x.Name.Contains("Main", StringComparison.OrdinalIgnoreCase));
    }

    private static Competition? FindPhilCompetition(IReadOnlyCollection<Competition> competitions)
    {
        var exact = competitions.FirstOrDefault(x =>
            string.Equals(x.Name, PhilCompetitionName, StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
        {
            return exact;
        }

        return competitions.FirstOrDefault(x =>
            x.Name.Contains("Philip", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Contains("Phil", StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(x.Description) &&
             (x.Description.Contains("Philip", StringComparison.OrdinalIgnoreCase) ||
              x.Description.Contains("Phil", StringComparison.OrdinalIgnoreCase))));
    }

    private static Competition? FindDaveCompetition(IReadOnlyCollection<Competition> competitions)
    {
        var exact = competitions.FirstOrDefault(x =>
            string.Equals(x.Name, DaveCompetitionName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.Name, "David 2025", StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
        {
            return exact;
        }

        return competitions.FirstOrDefault(x =>
            x.Name.Contains("Dave", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Contains("David", StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(x.Description) &&
             (x.Description.Contains("Dave", StringComparison.OrdinalIgnoreCase) ||
              x.Description.Contains("David", StringComparison.OrdinalIgnoreCase))));
    }
}
