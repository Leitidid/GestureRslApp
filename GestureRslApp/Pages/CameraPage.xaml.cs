using Camera.MAUI;
using GestureRslApp.Models;
using GestureRslApp.Services;

namespace GestureRslApp.Pages;

public partial class CameraPage : ContentPage
{
    private readonly HandGestureRecognizer _recognizer;
    private CancellationTokenSource? _frameTokenSource;
    private const int FrameIntervalMs = 200; // Обработка каждые 200мс = 5 FPS

    public CameraPage(HandGestureRecognizer recognizer)
    {
        InitializeComponent();
        _recognizer = recognizer;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var status = await Permissions.RequestAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
        {
            await DisplayAlert("Нет доступа", "Приложению нужен доступ к камере.", "ОК");
            return;
        }

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

        await StartCameraAsync();

        // 🔥 Запускаем обработку кадров через таймер
        _frameTokenSource = new CancellationTokenSource();
        _ = ProcessFramesLoop(_frameTokenSource.Token);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _frameTokenSource?.Cancel();
        StopCameraAsync();
    }

    private async Task StartCameraAsync()
    {
        CameraViewControl.Camera = CameraViewControl.Cameras.FirstOrDefault(
            c => c.Position == CameraPosition.Back)
            ?? CameraViewControl.Cameras.FirstOrDefault();

        if (CameraViewControl.Camera == null) return;

        CameraViewControl.BarCodeDetectionEnabled = false;
        await CameraViewControl.StopCameraAsync();
        await CameraViewControl.StartCameraAsync();
    }

    private async Task StopCameraAsync()
    {
        await CameraViewControl.StopCameraAsync();
    }

    // 🔥 Новый метод: обработка кадров через таймер
    // 🔥 Новый метод: обработка кадров через таймер
    private async Task ProcessFramesLoop(CancellationToken token)
    {
        await Task.Delay(1000, token);

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FrameIntervalMs, token);

                // 🔥 Получаем кадр через SnapShotStream
                // 🔥 Получаем кадр через SnapShotStream
                var stream = CameraViewControl.SnapShotStream;
                if (stream == null)
                {
                    System.Diagnostics.Debug.WriteLine("[DEBUG] SnapShotStream is NULL");
                    continue;
                }

                // 🔥 Читаем длину ДО копирования
                long length = stream.Length;
                if (length == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[DEBUG] SnapShotStream is EMPTY");
                    continue;
                }

                System.Diagnostics.Debug.WriteLine($"[DEBUG] Frame: {length} bytes");

                // 🔥 Копируем данные
                byte[] jpegBytes;
                using (var ms = new MemoryStream())
                {
                    await stream.CopyToAsync(ms);
                    jpegBytes = ms.ToArray();
                }

                if (jpegBytes.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[DEBUG] JPEG bytes are EMPTY");
                    continue;
                }

                // Распознавание
                GestureResult result = _recognizer.Recognize(jpegBytes);

                // Обновление UI
                await MainThread.InvokeOnMainThreadAsync(() => UpdateUI(result));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] {ex.Message}");
            }
        }
    }
    private void UpdateUI(GestureResult result)
    {
        if (!result.HandDetected)
        {
            LetterLabel.Text = "—";
            StatusLabel.Text = "Рука не найдена";
            ConfidenceBar.Progress = 0;
            ConfidenceLabel.Text = "0%";
            LetterLabel.TextColor = GetResource<Color>("SecondaryText");
            return;
        }

        LetterLabel.Text = result.Letter;
        LetterLabel.TextColor = GetResource<Color>("Primary");
        StatusLabel.Text = result.Confidence >= 0.8f ? "Буква распознана" : "Уточняется...";
        ConfidenceBar.Progress = result.Confidence;
        ConfidenceLabel.Text = $"{result.ConfidencePercent}%";
        ConfidenceBar.ProgressColor = result.Confidence >= 0.8f
            ? GetResource<Color>("SuccessColor")
            : GetResource<Color>("WarningColor");
    }

    private static T GetResource<T>(string key)
    {
        if (Application.Current!.Resources.TryGetValue(key, out object? value) && value is T typed)
            return typed;
        return default!;
    }
}