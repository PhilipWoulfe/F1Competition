using System.Text.Json;
using F1.Web.Configuration;
using F1.Web.Models;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace F1.Web.Services;

public sealed record StoredSelectionContext(string CompetitionSlug, int Season);

public interface ISelectionContextStore
{
    Task<StoredSelectionContext?> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(StoredSelectionContext context, CancellationToken cancellationToken = default);
}

public interface ISelectionContextService
{
    IReadOnlyList<SelectionContextOption> GetAvailableContexts();

    SelectionContextOption GetDefaultContext();

    Task<SelectionContextOption> GetRestoredOrDefaultAsync(CancellationToken cancellationToken = default);

    SelectionContextOption? ResolveContext(RaceSelectionContext routeContext, string? resolvedRaceId = null);

    string BuildSelectionPath(SelectionContextOption context);

    Task SaveLastUsedAsync(SelectionContextOption context, CancellationToken cancellationToken = default);
}

public sealed class BrowserSelectionContextStore(IJSRuntime jsRuntime) : ISelectionContextStore
{
    private const string StorageKey = "f1.selection.last-context";

    public async Task<StoredSelectionContext?> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var rawValue = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", cancellationToken, StorageKey);
            return string.IsNullOrWhiteSpace(rawValue)
                ? null
                : JsonSerializer.Deserialize<StoredSelectionContext>(rawValue);
        }
        catch (JSException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(StoredSelectionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var rawValue = JsonSerializer.Serialize(context);
            await jsRuntime.InvokeVoidAsync("localStorage.setItem", cancellationToken, StorageKey, rawValue);
        }
        catch (JSException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}

public sealed class SelectionContextService(IOptions<SelectionContextOptions> options, ISelectionContextStore store) : ISelectionContextService
{
    private readonly IReadOnlyList<SelectionContextOption> availableContexts = BuildContexts(options.Value.Options);

    public IReadOnlyList<SelectionContextOption> GetAvailableContexts() => availableContexts;

    public SelectionContextOption GetDefaultContext()
    {
        return availableContexts.FirstOrDefault(context =>
                   string.Equals(context.CompetitionSlug, SelectionDefaults.DefaultCompetitionSlug, StringComparison.Ordinal)
                   && context.Season == SelectionDefaults.DefaultSeason)
               ?? availableContexts.FirstOrDefault()
               ?? new SelectionContextOption
               {
                   CompetitionSlug = SelectionDefaults.DefaultCompetitionSlug,
                   CompetitionLabel = "Main",
                   Season = SelectionDefaults.DefaultSeason,
                   DefaultRound = SelectionDefaults.DefaultRound
               };
    }

    public async Task<SelectionContextOption> GetRestoredOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        var storedContext = await store.GetAsync(cancellationToken);
        if (storedContext is not null)
        {
            var restoredContext = TryFind(storedContext.CompetitionSlug, storedContext.Season);
            if (restoredContext is not null)
            {
                return restoredContext;
            }
        }

        return GetDefaultContext();
    }

    public SelectionContextOption? ResolveContext(RaceSelectionContext routeContext, string? resolvedRaceId = null)
    {
        if (routeContext.Lookup is not null)
        {
            return TryFind(routeContext.Lookup.CompetitionSlug, routeContext.Lookup.Season);
        }

        var raceId = string.IsNullOrWhiteSpace(resolvedRaceId) ? routeContext.RaceId : resolvedRaceId;
        if (string.IsNullOrWhiteSpace(raceId))
        {
            return null;
        }

        return availableContexts.FirstOrDefault(context =>
            raceId.StartsWith($"{context.CompetitionSlug}-{context.Season}-", StringComparison.Ordinal));
    }

    public string BuildSelectionPath(SelectionContextOption context)
    {
        return $"/selection/{context.CompetitionSlug}/{context.Season}/round/{context.DefaultRound}";
    }

    public Task SaveLastUsedAsync(SelectionContextOption context, CancellationToken cancellationToken = default)
    {
        return store.SaveAsync(new StoredSelectionContext(context.CompetitionSlug, context.Season), cancellationToken);
    }

    private SelectionContextOption? TryFind(string competitionSlug, int season)
    {
        return availableContexts.FirstOrDefault(context =>
            string.Equals(context.CompetitionSlug, competitionSlug, StringComparison.Ordinal)
            && context.Season == season);
    }

    private static IReadOnlyList<SelectionContextOption> BuildContexts(IEnumerable<SelectionContextOption> configuredContexts)
    {
        return configuredContexts
            .Where(context => !string.IsNullOrWhiteSpace(context.CompetitionSlug) && context.Season > 0)
            .Select(context => new SelectionContextOption
            {
                CompetitionSlug = context.CompetitionSlug.Trim().ToLowerInvariant(),
                CompetitionLabel = context.GetCompetitionLabel().Trim(),
                Season = context.Season,
                DefaultRound = context.DefaultRound > 0 ? context.DefaultRound : 1
            })
            .GroupBy(context => context.ContextKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(context => context.GetCompetitionLabel(), StringComparer.Ordinal)
            .ThenByDescending(context => context.Season)
            .ToArray();
    }
}