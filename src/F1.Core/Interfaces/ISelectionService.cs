using F1.Core.Dtos;
using F1.Core.Models;

namespace F1.Core.Interfaces;

public interface ISelectionService
{
    Task<Selection?> GetSelectionAsync(string raceId, string userId);
    Task<IReadOnlyList<CurrentSelectionDto>> GetCurrentSelectionsAsync(string raceId, string userId);
    Task<Selection> UpsertSelectionAsync(string raceId, string userId, SelectionSubmissionDto submission);
    int CalculateScore(BetType betType, bool isPerfectTopFive, int basePoints, bool submittedBeforePreQualyDeadline);
    Task<RaceConfigDto?> GetRaceConfigAsync(string raceId);
}
