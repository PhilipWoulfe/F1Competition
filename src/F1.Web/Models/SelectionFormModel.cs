namespace F1.Web.Models;

public class SelectionFormModel
{
    public List<string> SelectedDriverIds { get; } = ["", "", "", "", ""];
    public BetType SelectedBetType { get; set; } = BetType.Regular;
}