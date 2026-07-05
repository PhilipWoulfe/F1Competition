using F1.Core.Models;

namespace F1.Core.Dtos;

public class BetOptionDto
{
    public BetType BetType { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool IsAvailable { get; set; } = true;
}