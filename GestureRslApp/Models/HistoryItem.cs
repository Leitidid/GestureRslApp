namespace GestureRslApp.Models;

public class HistoryItem
{
    public string Text { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public int ConfidencePercent { get; set; }
    public GestureType Type { get; set; }
    public bool IsWord { get; set; }

    public string TimeDisplay => Timestamp.ToString("HH:mm:ss");
    public string TypeLabel => IsWord ? "Слово" : "Буква";
    public Color TypeColor => Type == GestureType.Dynamic
        ? Color.FromArgb("#E53935")
        : Color.FromArgb("#6750A4");
}
