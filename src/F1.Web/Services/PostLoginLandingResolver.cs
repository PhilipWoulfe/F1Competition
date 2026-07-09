using F1.Web.Configuration;
using F1.Web.Models;
using Microsoft.Extensions.Options;

namespace F1.Web.Services;

public interface IPostLoginLandingResolver
{
    string Resolve(User? user);
}

public sealed class PostLoginLandingResolver(IOptions<PostLoginRoutingOptions> options) : IPostLoginLandingResolver
{
    private const string DefaultFallbackPath = "/results";

    public string Resolve(User? user)
    {
        var routingOptions = options.Value;
        var fallbackPath = NormalizePath(routingOptions.FallbackPath, DefaultFallbackPath);

        if (user?.IsAdmin == true)
        {
            return NormalizePath(routingOptions.AdminLandingPath, fallbackPath);
        }

        if (user?.IsAuthenticated == true || !string.IsNullOrWhiteSpace(user?.Email))
        {
            return NormalizePath(routingOptions.AuthenticatedUserLandingPath, fallbackPath);
        }

        return fallbackPath;
    }

    private static string NormalizePath(string? configuredPath, string fallbackPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return fallbackPath;
        }

        var normalizedPath = configuredPath.Trim();
        if (!normalizedPath.StartsWith('/'))
        {
            normalizedPath = $"/{normalizedPath}";
        }

        if (normalizedPath.Length > 1)
        {
            normalizedPath = normalizedPath.TrimEnd('/');
        }

        return normalizedPath;
    }
}