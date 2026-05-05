namespace GestureRslApp.Models;

public class HistoryEntry
{
    public string Gesture { get; set; } = string.Empty;
    public int ConfidencePercent { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsDynamic { get; set; }

    // Для совместимости с биндингом
    public string Text => Gesture;
    public string TimeDisplay => Timestamp.ToString("HH:mm:ss");
    public string TypeLabel => IsDynamic ? "Динамический" : "Статический";
}