using Camera.MAUI;
using GestureRslApp.Models;
using GestureRslApp.Services;

namespace GestureRslApp.Pages;

public partial class CameraPage : ContentPage
{
    private readonly HandGestureRecognizer _recognizer;

    // Флаг: идёт ли сейчас обработка кадра
    // Защита от накопления кадров в очереди (дроп лишних)
    private bool _isProcessingFrame;

    // Счётчик пропускаемых кадров: обрабатываем каждый N-й кадр
    // (для плавности UI — не нагружаем CPU каждым кадром)
    private int _frameCounter;
    private const int ProcessEveryNthFrame = 5;

    public CameraPage(HandGestureRecognizer recognizer)
    {
        InitializeComponent();
        _recognizer = recognizer;
    }

    // ─── Жизненный цикл страницы ──────────────────────────────────────────

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Запрашиваем разрешение на камеру при первом открытии
        var status = await Permissions.RequestAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
        {
            await DisplayAlert("Нет доступа", "Приложению нужен доступ к камере.", "ОК");
            return;
        }

        // Инициализируем ONNX-модели (только один раз)
        try
        {
            await _recognizer.InitializeAsync();
            LoadingOverlay.IsVisible = false;
        }
        catch (Exception ex)
        {
            LoadingOverlay.IsVisible = false;
            await DisplayAlert("Ошибка", $"Не удалось загрузить модель:\n{ex.Message}", "ОК");
            return;
        }

        // Запускаем камеру
        await StartCameraAsync();
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await StopCameraAsync();
    }

    // ─── Управление камерой ───────────────────────────────────────────────

    private async Task StartCameraAsync()
    {
        CameraViewControl.Camera = CameraViewControl.Cameras.FirstOrDefault(
            c => c.Position == CameraPosition.Back)
            ?? CameraViewControl.Cameras.FirstOrDefault();

        if (CameraViewControl.Camera == null) return;

        // Настройки камеры
        CameraViewControl.BarCodeDetectionEnabled = false;
        await CameraViewControl.StopCameraAsync();
        await CameraViewControl.StartCameraAsync();
    }

    private async Task StopCameraAsync()
    {
        await CameraViewControl.StopCameraAsync();
    }

    // ─── Обработка кадров с камеры ────────────────────────────────────────

    /// <summary>
    /// Вызывается Camera.MAUI каждый раз когда готов новый кадр.
    /// Запускаем распознавание в фоне чтобы не блокировать UI-поток.
    /// </summary>
    private void OnCameraFrameReady(object sender, EventArgs e)
    {
        _frameCounter++;

        // Пропускаем кадры для снижения нагрузки на CPU
        if (_frameCounter % ProcessEveryNthFrame != 0) return;

        // Если предыдущий кадр ещё обрабатывается — пропускаем этот
        if (_isProcessingFrame) return;
        _isProcessingFrame = true;

        // Запускаем обработку в фоновом потоке
        Task.Run(async () =>
        {
            try
            {
                // 🔥 Получаем кадр как Stream из Camera.MAUI
                using Stream? photoStream = await CameraViewControl.TakePhotoAsync();
                if (photoStream == null) return;

                // 🔥 Конвертируем Stream → byte[]
                using var memoryStream = new MemoryStream();
                await photoStream.CopyToAsync(memoryStream);
                byte[] jpegBytes = memoryStream.ToArray();

                if (jpegBytes.Length == 0) return;

                // Запускаем распознавание
                GestureResult result = _recognizer.Recognize(jpegBytes);

                // Обновляем UI строго в главном потоке
                await MainThread.InvokeOnMainThreadAsync(() => UpdateUI(result));
            }
            catch (Exception ex)
            {
                // Логируем ошибку (можно добавить серую зону для отладки)
                System.Diagnostics.Debug.WriteLine($"[CameraPage] Error: {ex.Message}");
            }
            finally
            {
                _isProcessingFrame = false;
            }
        });
    }

    // ─── Обновление интерфейса ────────────────────────────────────────────

    /// <summary>
    /// Обновляет букву, процент уверенности и прогресс-бар на экране.
    /// </summary>
    private void UpdateUI(GestureResult result)
    {
        if (!result.HandDetected)
        {
            LetterLabel.Text      = "—";
            StatusLabel.Text      = "Рука не найдена";
            ConfidenceBar.Progress = 0;
            ConfidenceLabel.Text  = "0%";
            LetterLabel.TextColor = GetResource<Color>("SecondaryText");
            return;
        }

        // Выводим букву
        LetterLabel.Text      = result.Letter;
        LetterLabel.TextColor = GetResource<Color>("Primary");

        // Подпись под буквой
        StatusLabel.Text = result.Confidence >= 0.8f
            ? $"Буква распознана"
            : "Уточняется...";

        // Прогресс-бар уверенности
        ConfidenceBar.Progress = result.Confidence;
        ConfidenceLabel.Text   = $"{result.ConfidencePercent}%";

        // Меняем цвет прогресс-бара: зелёный если >80%, жёлтый иначе
        ConfidenceBar.ProgressColor = result.Confidence >= 0.8f
            ? GetResource<Color>("SuccessColor")
            : GetResource<Color>("WarningColor");
    }

    /// <summary>Хелпер для получения ресурса из словаря по ключу.</summary>
    private static T GetResource<T>(string key)
    {
        if (Application.Current!.Resources.TryGetValue(key, out object? value) && value is T typed)
            return typed;
        return default!;
    }
}
