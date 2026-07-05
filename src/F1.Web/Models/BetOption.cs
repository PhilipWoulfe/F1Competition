namespace F1.Web.Models;

public class BetOption
{
    public BetType BetType { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool IsAvailable { get; set; } = true;
}