using System.Net.Http.Json;

namespace F1.Web.Services;

public interface IAppInfoService
{
    Task<string> GetShortVersionAsync(CancellationToken cancellationToken = default);
}

public sealed class AppInfoService(IHttpClientFactory httpClientFactory) : IAppInfoService
{
    public const string HostClientName = "F1Host";

    public async Task<string> GetShortVersionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var localClient = httpClientFactory.CreateClient(HostClientName);
            var versionModel = await localClient.GetFromJsonAsync<VersionModel>("version.json", cancellationToken);
            if (string.IsNullOrWhiteSpace(versionModel?.Version))
            {
                return "N/A";
            }

            return versionModel.Version[..Math.Min(7, versionModel.Version.Length)];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return "Error";
        }
    }

    private sealed class VersionModel
    {
        public string? Version { get; set; }
    }
}