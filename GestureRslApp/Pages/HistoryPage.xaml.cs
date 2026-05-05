using GestureRslApp.Services;

namespace GestureRslApp.Pages;

public partial class HistoryPage : ContentPage
{
    public HistoryPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Подписываемся на обновления истории
        HistoryService.Instance.ItemAdded += OnHistoryUpdated;

        // Отображаем текущее состояние
        RefreshList();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Отписываемся чтобы не было утечек памяти
        HistoryService.Instance.ItemAdded -= OnHistoryUpdated;
    }

    /// <summary>
    /// Вызывается из HistoryService (фоновый поток) при добавлении записи.
    /// Переключаемся на главный поток для обновления UI.
    /// </summary>
    private void OnHistoryUpdated()
    {
        MainThread.BeginInvokeOnMainThread(RefreshList);
    }

    /// <summary>Обновляет список и счётчик.</summary>
    private void RefreshList()
    {
        var items = HistoryService.Instance.Items;
        int count = items.Count;

        // Переключаем между заглушкой и списком
        EmptyView.IsVisible   = count == 0;
        HistoryList.IsVisible = count > 0;

        // Обновляем счётчик
        CountLabel.Text = count == 0
            ? "Нет записей"
            : $"{count} {GetCountLabel(count)}";

        // Биндим список
        if (count > 0)
            HistoryList.ItemsSource = items.ToList();
    }

    /// <summary>Склонение слова "запись".</summary>
    private static string GetCountLabel(int count)
    {
        int mod10 = count % 10;
        int mod100 = count % 100;
        if (mod100 >= 11 && mod100 <= 14) return "записей";
        return mod10 switch { 1 => "запись", 2 or 3 or 4 => "записи", _ => "записей" };
    }

    /// <summary>Очищает историю по нажатию кнопки.</summary>
    private async void OnClearClicked(object sender, EventArgs e)
    {
        bool confirmed = await DisplayAlert(
            "Очистить историю",
            "Все записи будут удалены. Продолжить?",
            "Очистить",
            "Отмена");

        if (confirmed)
        {
            HistoryService.Instance.Clear();
            RefreshList();
        }
    }
}
