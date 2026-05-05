using System.Text.Json;
using GestureRslApp.Models;

namespace GestureRslApp.Services;

public class HistoryService
{
    private const string FileName = "gesture_history.json";
    private const int MaxEntries = 200;

    private static string FilePath =>
        Path.Combine(FileSystem.AppDataDirectory, FileName);

    private List<HistoryEntry> _entries = new();

    // 🔥 ДОБАВЛЕНО: Синглтон
    private static HistoryService? _instance;
    public static HistoryService Instance => _instance ??= new HistoryService();

    public event Action? ItemAdded;

    public IReadOnlyList<HistoryEntry> Items => _entries;

    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                _entries = new List<HistoryEntry>();
                return;
            }

            string json = await File.ReadAllTextAsync(FilePath);
            _entries = JsonSerializer.Deserialize<List<HistoryEntry>>(json)
                       ?? new List<HistoryEntry>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HistoryService] Ошибка загрузки: {ex.Message}");
            _entries = new List<HistoryEntry>();
        }
    }

    public async Task AddAsync(GestureResult result)
    {
        if (!result.HandDetected || result.Letter == "—") return;

        var entry = new HistoryEntry
        {
            Gesture = result.Letter,
            ConfidencePercent = result.ConfidencePercent,
            Timestamp = DateTime.Now,
            IsDynamic = result.Type == GestureType.Dynamic,
        };

        _entries.Insert(0, entry);

        if (_entries.Count > MaxEntries)
            _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);

        await SaveAsync();
        ItemAdded?.Invoke();
    }

    //public async Task ClearAsync()
    //{
    //    _entries.Clear();
    //    await SaveAsync();
    //    ItemAdded?.Invoke();
    //}
    // Добавь этот метод в класс HistoryService
    public void Clear()
    {
        _entries.Clear();
        // Сохраняем синхронно чтобы не было async void
        Task.Run(async () => await SaveAsync()).Wait();
        ItemAdded?.Invoke();
    }
    private async Task SaveAsync()
    {
        try
        {
            var opts = new JsonSerializerOptions { WriteIndented = false };
            string json = JsonSerializer.Serialize(_entries, opts);
            await File.WriteAllTextAsync(FilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HistoryService] Ошибка сохранения: {ex.Message}");
        }
    }
}