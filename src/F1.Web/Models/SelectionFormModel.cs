namespace F1.Web.Models;

public class SelectionFormModel
{
    public List<string> SelectedDriverIds { get; } = [];
    public BetType SelectedBetType { get; set; } = BetType.Regular;

    public void EnsureSelectionCount(int selectionCount)
    {
        if (selectionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selectionCount));
        }

        while (SelectedDriverIds.Count < selectionCount)
        {
            SelectedDriverIds.Add(string.Empty);
        }

        while (SelectedDriverIds.Count > selectionCount)
        {
            SelectedDriverIds.RemoveAt(SelectedDriverIds.Count - 1);
        }
    }
}