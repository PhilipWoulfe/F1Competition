using F1.Web.Models;

namespace F1.Web.Services.Api;

public interface ISelectionApiService
{
    Task<RaceConfig> GetConfigAsync(string raceId, CancellationToken cancellationToken = default);
    Task<Selection?> GetMineAsync(string raceId, CancellationToken cancellationToken = default);
    Task<CurrentSelectionItem[]> GetCurrentAsync(string raceId, CancellationToken cancellationToken = default);
    Task<Selection> SaveMineAsync(string raceId, SelectionSubmission submission, CancellationToken cancellationToken = default);
}
