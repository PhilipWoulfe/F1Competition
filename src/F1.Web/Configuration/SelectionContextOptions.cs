namespace F1.Web.Configuration;

public sealed class SelectionContextOptions
{
    public const string SectionName = "SelectionContext";

    public List<SelectionContextOption> Options { get; set; } = [];
}

public sealed class SelectionContextOption
{
    public string CompetitionSlug { get; set; } = string.Empty;

    public string CompetitionLabel { get; set; } = string.Empty;

    public int Season { get; set; }

    public int DefaultRound { get; set; } = 1;

    public string ContextKey => $"{CompetitionSlug}:{Season}";

    public string DisplayLabel => $"{GetCompetitionLabel()} {Season}";

    public string GetCompetitionLabel()
    {
        return string.IsNullOrWhiteSpace(CompetitionLabel) ? CompetitionSlug : CompetitionLabel;
    }
}