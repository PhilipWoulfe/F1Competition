namespace F1.Web.Configuration;

public sealed class PostLoginRoutingOptions
{
    public const string SectionName = "PostLoginRouting";

    public string AdminLandingPath { get; set; } = "/admin/migration-runs";

    public string AuthenticatedUserLandingPath { get; set; } = "/results";

    public string FallbackPath { get; set; } = "/results";
}