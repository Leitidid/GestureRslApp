using Camera.MAUI;
using GestureRslApp.Models;
using GestureRslApp.Services;
#if ANDROID
using Android.Media;
#endif
namespace GestureRslApp.Pages;

public partial class CameraPage : ContentPage
{
    private readonly HandGestureRecognizer _staticRecognizer;
    private readonly DynamicGestureRecognizer _dynamicRecognizer;
    private readonly HistoryService _historyService;

    private CancellationTokenSource? _frameTokenSource;
    private const int FrameIntervalMs = 200;

    private bool _isDynamicMode = false;
    private bool _isRecording = false;
    private bool _isCountingDown = false;
    private const int RecordingFrames = DynamicGestureRecognizer.SequenceLength;

    private GestureResult? _lastResult;

    public CameraPage(HandGestureRecognizer staticRecognizer,
                      DynamicGestureRecognizer dynamicRecognizer,
                      HistoryService historyService)
    {
        InitializeComponent();
        _staticRecognizer = staticRecognizer;
        _dynamicRecognizer = dynamicRecognizer;
        _historyService = historyService;
        UpdateModeUI();
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
            await _staticRecognizer.InitializeAsync();
            await _dynamicRecognizer.InitializeAsync();
            LoadingOverlay.IsVisible = false;
        }
        catch (Exception ex)
        {
            LoadingOverlay.IsVisible = false;
            await DisplayAlert("Ошибка загрузки", ex.Message, "ОК");
            return;
        }

        await StartCameraAsync();
        _frameTokenSource = new CancellationTokenSource();
        _ = ProcessFramesLoop(_frameTokenSource.Token);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _frameTokenSource?.Cancel();
        _ = CameraViewControl.StopCameraAsync();
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

    private async Task ProcessFramesLoop(CancellationToken token)
    {
        await Task.Delay(1000, token);

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FrameIntervalMs, token);

                var stream = CameraViewControl.SnapShotStream;
                if (stream == null || stream.Length == 0) continue;

                byte[] jpegBytes;
                using (var ms = new MemoryStream())
                {
                    await stream.CopyToAsync(ms);
                    jpegBytes = ms.ToArray();
                }
                if (jpegBytes.Length == 0) continue;

                float[]? landmarks = _staticRecognizer.ExtractLandmarks(jpegBytes);

                GestureResult result;
                if (!_isDynamicMode)
                {
                    result = landmarks != null
                        ? _staticRecognizer.ClassifyLandmarks(landmarks)
                        : GestureResult.NoHand;
                }
                else
                {
                    result = ProcessDynamicFrame(landmarks);
                }

                _lastResult = result.HandDetected ? result : null;
                await MainThread.InvokeOnMainThreadAsync(() => UpdateUI(result));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraPage] {ex.Message}");
            }
        }
    }

    private GestureResult ProcessDynamicFrame(float[]? landmarks)
    {
        if (!_isRecording) return GestureResult.NoHand;

        _dynamicRecognizer.AddFrame(landmarks);
        int buffered = _dynamicRecognizer.BufferedFrames;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            RecordingStatusLabel.Text = $"Запись {buffered}/{RecordingFrames}...";
        });

        if (_dynamicRecognizer.IsReady)
        {
            GestureResult res = _dynamicRecognizer.Classify();
            _dynamicRecognizer.ClearBuffer();
            _isRecording = false;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                RecordingOverlay.IsVisible = false;
                RecordButton.IsVisible = true;
                RecordButton.IsEnabled = true;
                RecordButton.Text = "Записать жест";
            });

            return res;
        }

        return GestureResult.NoHand;
    }

    private void UpdateUI(GestureResult result)
    {
        bool show = result.HandDetected && result.Letter != "—";
        SaveToHistoryBtn.IsVisible = show;

        if (!result.HandDetected)
        {
            LetterLabel.Text = "—";
            StatusLabel.Text = _isDynamicMode ? "Нажмите «Записать жест»" : "Рука не найдена";
            ConfidenceBar.Progress = 0;
            ConfidenceLabel.Text = "0%";
            LetterLabel.TextColor = GetRes<Color>("SecondaryText");
            return;
        }

        LetterLabel.Text = result.Letter;
        LetterLabel.TextColor = GetRes<Color>("Primary");

        string tag = result.Type == GestureType.Dynamic ? " (динам.)" : string.Empty;
        StatusLabel.Text = result.Confidence >= 0.75f
            ? $"Распознано{tag}"
            : $"Уточняется{tag}...";

        ConfidenceBar.Progress = result.Confidence;
        ConfidenceLabel.Text = $"{result.ConfidencePercent}%";
        ConfidenceBar.ProgressColor = result.Confidence >= 0.75f
            ? GetRes<Color>("SuccessColor")
            : GetRes<Color>("WarningColor");
    }

    private void OnStaticModeClicked(object sender, EventArgs e)
    {
        _isDynamicMode = false;
        _dynamicRecognizer.ClearBuffer();
        _isRecording = false;
        UpdateModeUI();
    }

    private void OnDynamicModeClicked(object sender, EventArgs e)
    {
        _isDynamicMode = true;
        UpdateModeUI();
    }

    private void UpdateModeUI()
    {
        Color activeBg = GetRes<Color>("Primary");
        Color inactiveBg = Color.FromArgb("#40FFFFFF");

        BtnStatic.BackgroundColor = _isDynamicMode ? inactiveBg : activeBg;
        BtnStatic.TextColor = Colors.White;
        BtnDynamic.BackgroundColor = _isDynamicMode ? activeBg : inactiveBg;
        BtnDynamic.TextColor = Colors.White;

        RecordButton.IsVisible = _isDynamicMode;
        HintLabel.Text = _isDynamicMode
            ? "Нажмите «Записать» и покажите жест"
            : "Поместите руку в рамку";

        LetterLabel.Text = "—";
        StatusLabel.Text = _isDynamicMode ? "Нажмите «Записать жест»" : "Ожидание жеста...";
        ConfidenceBar.Progress = 0;
        ConfidenceLabel.Text = "0%";
        SaveToHistoryBtn.IsVisible = false;
    }

    private async void OnRecordClicked(object sender, EventArgs e)
    {
        if (_isRecording || _isCountingDown) return;

        _isCountingDown = true;
        RecordButton.IsEnabled = false;
        _dynamicRecognizer.ClearBuffer();
        RecordingOverlay.IsVisible = true;
        RecordButton.IsVisible = false;

        for (int i = 3; i >= 1; i--)
        {
            RecordingCountdownLabel.Text = i.ToString();
            RecordingStatusLabel.Text = "Приготовьтесь...";
            await Task.Delay(800);
        }

        RecordingCountdownLabel.Text = "●";
        RecordingStatusLabel.Text = $"Запись 0/{RecordingFrames}...";
        _isCountingDown = false;
        _isRecording = true;
    }

    private async void OnSaveToHistoryClicked(object sender, EventArgs e)
    {
        if (_lastResult == null) return;
        await _historyService.AddAsync(_lastResult);
        SaveToHistoryBtn.Text = "Сохранено ✓";
        SaveToHistoryBtn.IsEnabled = false;
        await Task.Delay(1200);
        SaveToHistoryBtn.Text = "Сохранить в историю";
        SaveToHistoryBtn.IsEnabled = true;
    }

    private static T GetRes<T>(string key)
    {
        if (Application.Current!.Resources.TryGetValue(key, out object? v) && v is T typed)
            return typed;
        return default!;
    }
    // ─── Переключение режимов ──────────────────────────────────────────

    private bool _isRealTimeMode = true;

    private void OnRealTimeClicked(object sender, EventArgs e)
    {
        _isRealTimeMode = true;
        UpdateModeButtons();
        _dynamicRecognizer.ClearBuffer();
        _isRecording = false;
        _frameTokenSource?.Cancel();
        _frameTokenSource = new CancellationTokenSource();
        _ = ProcessFramesLoop(_frameTokenSource.Token);
    }

    private async void OnPhotoClicked(object sender, EventArgs e)
    {
        // 🔥 1. Останавливаем обработку кадров с камеры
        _frameTokenSource?.Cancel();
        _isRealTimeMode = false;
        UpdateModeButtons();

        // 🔥 2. Небольшая задержка чтобы камера успела остановиться
        await Task.Delay(200);

        try
        {
            StatusLabel.Text = "Выберите фото...";

            var result = await MediaPicker.PickPhotoAsync(new MediaPickerOptions
            {
                Title = "Выберите фото с жестом"
            });

            if (result == null)
            {
                StatusLabel.Text = "Ожидание жеста...";
                return;
            }

            StatusLabel.Text = "Обработка...";

            // 🔥 3. Читаем фото в память
            using var stream = await result.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            byte[] jpegBytes = ms.ToArray();

            // 🔥 4. Тяжёлые вычисления — в отдельном потоке!
            var gestureResult = await Task.Run(() =>
            {
                var landmarks = _staticRecognizer.ExtractLandmarks(jpegBytes);
                if (landmarks == null) return GestureResult.NoHand;
                return _staticRecognizer.ClassifyLandmarks(landmarks);
            });

            // 🔥 5. Обновляем UI в главном потоке
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                UpdateUI(gestureResult);
                if (gestureResult.HandDetected)
                {
                    _lastResult = gestureResult;
                    _ = _historyService.AddAsync(gestureResult);
                }
                StatusLabel.Text = "Готово! Нажмите 📹 для возврата к камере";
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Ошибка", $"Не удалось обработать фото: {ex.Message}", "ОК");
            StatusLabel.Text = "Ожидание жеста...";
        }
    }

    private async void OnVideoClicked(object sender, EventArgs e)
    {
        // 🔥 1. Останавливаем камеру
        _frameTokenSource?.Cancel();
        _isRealTimeMode = false;
        UpdateModeButtons();
        await Task.Delay(200);

        try
        {
            StatusLabel.Text = "Выберите видео...";

            var result = await MediaPicker.PickVideoAsync(new MediaPickerOptions
            {
                Title = "Выберите видео с жестом"
            });

            if (result == null)
            {
                StatusLabel.Text = "Ожидание жеста...";
                return;
            }

            StatusLabel.Text = "Извлечение кадра...";

            byte[]? frameBytes = null;

#if ANDROID
        // 🔥 2. Извлекаем кадр на отдельном потоке
        frameBytes = await Task.Run(() => 
        {
            var retriever = new Android.Media.MediaMetadataRetriever();
            try
            {
                retriever.SetDataSource(result.FullPath);
                var frame = retriever.GetFrameAtTime(0);
                
                if (frame != null)
                {
                    using var ms = new MemoryStream();
                    frame.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg, 90, ms);
                    return ms.ToArray();
                }
                return null;
            }
            finally
            {
                retriever.Release();
            }
        });
#endif

            if (frameBytes == null || frameBytes.Length == 0)
            {
                await DisplayAlert("Ошибка", "Не удалось извлечь кадр.", "ОК");
                StatusLabel.Text = "Ожидание жеста...";
                return;
            }

            StatusLabel.Text = "Распознавание...";

            // 🔥 3. Распознавание — тоже в фоне
            var gestureResult = await Task.Run(() =>
            {
                var landmarks = _staticRecognizer.ExtractLandmarks(frameBytes);
                if (landmarks == null) return GestureResult.NoHand;
                return _staticRecognizer.ClassifyLandmarks(landmarks);
            });

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                UpdateUI(gestureResult);
                if (gestureResult.HandDetected)
                {
                    _lastResult = gestureResult;
                    _ = _historyService.AddAsync(gestureResult);
                }
                StatusLabel.Text = "Готово! Нажмите 📹 для возврата к камере";
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Ошибка", $"Не удалось обработать видео: {ex.Message}", "ОК");
            StatusLabel.Text = "Ожидание жеста...";
        }
    }

    private void UpdateModeButtons()
    {
        Color activeBg = GetRes<Color>("Primary");
        Color inactiveBg = Color.FromArgb("#40FFFFFF");

        // Обновляем кнопки если они существуют
        if (BtnRealTime != null)
            BtnRealTime.BackgroundColor = _isRealTimeMode ? activeBg : inactiveBg;

        if (BtnPhoto != null)
            BtnPhoto.BackgroundColor = !_isRealTimeMode ? activeBg : inactiveBg;

        if (BtnVideo != null)
            BtnVideo.BackgroundColor = !_isRealTimeMode ? activeBg : inactiveBg;

        StatusLabel.Text = _isRealTimeMode
            ? "Ожидание жеста..."
            : "Режим загрузки фото/видео";
    }
}