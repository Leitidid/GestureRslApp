namespace GestureRslApp.Models;

/// <summary>Тип жеста — статичный или динамичный.</summary>
public enum GestureType { Static, Dynamic }

/// <summary>
/// Результат распознавания одного жеста.
/// Используется и для статичных букв, и для динамичных жестов/слов.
/// </summary>
public class GestureResult
{
    /// <summary>Распознанная буква или слово (например "А", "ПРИВЕТ").</summary>
    public string Letter { get; init; } = string.Empty;

    /// <summary>Уверенность модели от 0.0 до 1.0.</summary>
    public float Confidence { get; init; }

    /// <summary>Уверенность в процентах (0–100).</summary>
    public int ConfidencePercent => (int)(Confidence * 100);

    /// <summary>Была ли рука найдена на кадре.</summary>
    public bool HandDetected { get; init; }

    /// <summary>Статичный или динамичный жест.</summary>
    public GestureType Type { get; init; } = GestureType.Static;

    /// <summary>true — это слово, а не буква.</summary>
    public bool IsWord => Letter.ToUpper() is "МАМА" or "ПАПА" or "ПРИВЕТ" or "ПОКА";

    /// <summary>Пустой результат — рука не найдена.</summary>
    public static GestureResult NoHand => new()
    {
        Letter      = "—",
        Confidence  = 0f,
        HandDetected = false,
    };
}
