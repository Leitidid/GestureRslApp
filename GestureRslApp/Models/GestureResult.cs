namespace GestureRslApp.Models;

/// <summary>
/// Результат распознавания одного жеста.
/// </summary>
public class GestureResult
{
    /// <summary>Распознанная буква (например "А", "Б").</summary>
    public string Letter { get; init; } = string.Empty;

    /// <summary>Уверенность модели от 0.0 до 1.0.</summary>
    public float Confidence { get; init; }

    /// <summary>Уверенность в процентах для отображения (0–100).</summary>
    public int ConfidencePercent => (int)(Confidence * 100);

    /// <summary>Была ли рука найдена на кадре.</summary>
    public bool HandDetected { get; init; }

    /// <summary>Пустой результат когда рука не найдена.</summary>
    public static GestureResult NoHand => new()
    {
        Letter = "—",
        Confidence = 0f,
        HandDetected = false
    };
}
