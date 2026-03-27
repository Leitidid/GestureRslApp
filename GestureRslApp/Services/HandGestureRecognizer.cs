using System.Text.Json;
using GestureRslApp.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace GestureRslApp.Services;

/// <summary>
/// Главный сервис распознавания жестов РЖЯ.
///
/// Пайплайн обработки каждого кадра:
///   1. Получаем JPEG-байты с камеры.
///   2. Декодируем, вырезаем центр, ресайзим до 224×224.
///   3. Формируем тензор NHWC float32[1, 224, 224, 3] → hand_landmark_full.onnx.
///   4. Читаем hand presence score: если < 0.5 — рука не найдена.
///   5. Читаем 63 координаты (21 точка × x,y,z).
///   6. Нормализуем: вычитаем запястье (точка 0) — «якорь».
///   7. Применяем StandardScaler: (x - mean) / scale — параметры из JSON.
///   8. Подаём float[1, 63] в gesture_model.onnx → логиты.
///   9. Softmax + argmax → буква + уверенность.
///
/// Необходимые файлы в Resources/Raw/:
///   hand_landmark_full.onnx     — детектор ключевых точек руки (MediaPipe / PINTO)
///   gesture_model.onnx          — классификатор жестов (обучен нами)
///   gesture_model_meta.json     — порядок классов + параметры нормализации
///
/// Откуда взять hand_landmark_full.onnx:
///   https://github.com/PINTO0309/PINTO_model_zoo/tree/main/033_Hand_Detection_and_Tracking
///   Файл: hand_landmark_full_1x224x224.onnx → переименовать в hand_landmark_full.onnx
///   Проверить входы/выходы можно на https://netron.app
/// </summary>
public class HandGestureRecognizer : IDisposable
{
    // ─── Параметры модели hand_landmark_full.onnx (PINTO build) ──────────

    // Размер входного изображения
    private const int InputImageSize = 224;

    // Имена входа и выходов — можно проверить в Netron
    private const string LandmarkInputName    = "input_1";       // float32[1,224,224,3]
    private const string LandmarkOutputCoords = "Identity";      // float32[1,63]  — координаты
    private const string LandmarkOutputScore  = "Identity_1";    // float32[1,1]   — score руки

    // Минимальный score для признания что рука найдена
    private const float HandPresenceThreshold = 0.5f;

    // ─── ONNX-сессии ──────────────────────────────────────────────────────
    private InferenceSession? _landmarkSession;
    private InferenceSession? _gestureSession;

    // ─── Метаданные модели ────────────────────────────────────────────────
    private string[]? _classes;       // ["А", "Б", "В", "Г", "Д"]
    private float[]? _scalerMean;     // 63 числа
    private float[]? _scalerScale;    // 63 числа

    private bool _isInitialized;

    // ─── Инициализация ────────────────────────────────────────────────────

    /// <summary>
    /// Загружает ONNX-файлы и метаданные.
    /// Вызывать один раз при появлении CameraPage.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        // OnnxRuntime на Android требует путь к файлу, а не поток.
        // Копируем Assets в CacheDirectory.
        string landmarkPath = await ExtractAssetToCache("hand_landmark_full.onnx");
        string gesturePath  = await ExtractAssetToCache("gesture_model.onnx");
        string metaJson     = await ReadAssetTextAsync("gesture_model_meta.json");

        var opts = new SessionOptions();
        opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        // Раскомментировать для аппаратного ускорения на Android (опционально):
        // opts.AppendExecutionProvider_Nnapi();

        _landmarkSession = new InferenceSession(landmarkPath, opts);
        _gestureSession  = new InferenceSession(gesturePath,  opts);

        ParseMetadata(metaJson);
        _isInitialized = true;
    }

    // ─── Основной метод распознавания ─────────────────────────────────────

    /// <summary>
    /// Принимает JPEG-кадр с камеры, возвращает распознанный жест.
    /// Потокобезопасен — вызывается из фонового потока.
    /// </summary>
    public GestureResult Recognize(byte[] jpegBytes)
    {
        if (!_isInitialized || jpegBytes is not { Length: > 0 })
            return GestureResult.NoHand;

        try
        {
            // ── 1. Предобработка изображения ──────────────────────────────
            using SKBitmap? resized = DecodeAndResize(jpegBytes);
            if (resized == null) return GestureResult.NoHand;

            // ── 2. Детекция ключевых точек руки ───────────────────────────
            float[]? rawLandmarks = RunLandmarkModel(resized);
            if (rawLandmarks == null) return GestureResult.NoHand;

            // ── 3. Нормализация координат (вычитаем якорь = запястье) ─────
            float[] anchored = SubtractWristAnchor(rawLandmarks);

            // ── 4. StandardScaler: (x - mean) / scale ─────────────────────
            float[] scaled = ApplyScaler(anchored);

            // ── 5. Классификация жеста ────────────────────────────────────
            float[] logits = RunGestureModel(scaled);

            // ── 6. Softmax + argmax → результат ───────────────────────────
            return BuildResult(logits);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Recognizer] Ошибка: {ex.Message}");
            return GestureResult.NoHand;
        }
    }

    // ─── Предобработка ────────────────────────────────────────────────────

    /// <summary>
    /// Декодирует JPEG, вырезает центральный квадрат (85% меньшей стороны),
    /// ресайзит до 224×224. Возвращает null если декодирование не удалось.
    /// </summary>
    private static SKBitmap? DecodeAndResize(byte[] jpegBytes)
    {
        using SKData data = SKData.CreateCopy(jpegBytes);
        using SKBitmap? src = SKBitmap.Decode(data);
        if (src == null) return null;

        // Вырезаем центральный квадрат, чтобы рука не была деформирована
        int side = (int)(Math.Min(src.Width, src.Height) * 0.85f);
        int left = (src.Width  - side) / 2;
        int top  = (src.Height - side) / 2;
        var srcRect = new SKRectI(left, top, left + side, top + side);

        var dst    = new SKBitmap(InputImageSize, InputImageSize, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(dst);
        canvas.DrawBitmap(src, srcRect, new SKRect(0, 0, InputImageSize, InputImageSize));
        return dst;
    }

    // ─── Инференс: детектор ландмарков ────────────────────────────────────

    /// <summary>
    /// Запускает hand_landmark_full.onnx.
    /// Формат входа: NHWC float32[1, 224, 224, 3], значения [0, 1].
    /// Выходы:
    ///   "Identity"   — float32[1, 63] координаты
    ///   "Identity_1" — float32[1, 1]  score наличия руки
    /// Возвращает null если рука не найдена.
    /// </summary>
    private float[]? RunLandmarkModel(SKBitmap bitmap)
    {
        // Строим тензор NHWC [1, H, W, 3]
        int h = bitmap.Height, w = bitmap.Width;
        var tensor = new DenseTensor<float>(new[] { 1, h, w, 3 });

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                SKColor px = bitmap.GetPixel(x, y);
                tensor[0, y, x, 0] = px.Red   / 255f;
                tensor[0, y, x, 1] = px.Green / 255f;
                tensor[0, y, x, 2] = px.Blue  / 255f;
            }
        }

        var inputs = new[] { NamedOnnxValue.CreateFromTensor(LandmarkInputName, tensor) };
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs =
            _landmarkSession!.Run(inputs);

        // Читаем score руки
        var scoreTensor = outputs.First(o => o.Name == LandmarkOutputScore).AsTensor<float>();
        float score = scoreTensor[0, 0];
        if (score < HandPresenceThreshold) return null;

        // Читаем координаты 21 точки
        var coordTensor = outputs.First(o => o.Name == LandmarkOutputCoords).AsTensor<float>();
        float[] landmarks = new float[63];
        for (int i = 0; i < 63; i++)
            landmarks[i] = coordTensor[0, i];

        return landmarks;
    }

    // ─── Нормализация координат ───────────────────────────────────────────

    /// <summary>
    /// Вычитает координаты запястья (точка 0) из всех остальных точек.
    /// Делает признаки инвариантными к позиции руки на экране.
    /// Повторяет логику collect_from_images.py.
    /// </summary>
    private static float[] SubtractWristAnchor(float[] raw)
    {
        float ax = raw[0], ay = raw[1], az = raw[2];
        float[] result = new float[63];
        for (int i = 0; i < 21; i++)
        {
            result[i * 3]     = raw[i * 3]     - ax;
            result[i * 3 + 1] = raw[i * 3 + 1] - ay;
            result[i * 3 + 2] = raw[i * 3 + 2] - az;
        }
        return result;
    }

    // ─── StandardScaler ───────────────────────────────────────────────────

    /// <summary>
    /// Применяет нормализацию из gesture_model_meta.json:
    ///   scaled[i] = (x[i] - mean[i]) / scale[i]
    /// </summary>
    private float[] ApplyScaler(float[] landmarks)
    {
        float[] result = new float[63];
        for (int i = 0; i < 63; i++)
        {
            float s = _scalerScale![i];
            result[i] = s > 1e-8f ? (landmarks[i] - _scalerMean![i]) / s : 0f;
        }
        return result;
    }

    // ─── Инференс: классификатор жестов ──────────────────────────────────

    /// <summary>
    /// Запускает gesture_model.onnx.
    /// Вход:  float32[1, 63].
    /// Выход: float32[1, N_классов] — логиты.
    /// </summary>
    private float[] RunGestureModel(float[] scaled)
    {
        var tensor    = new DenseTensor<float>(scaled, new[] { 1, 63 });
        string inName = _gestureSession!.InputMetadata.Keys.First();

        var inputs = new[] { NamedOnnxValue.CreateFromTensor(inName, tensor) };
        using var outputs = _gestureSession.Run(inputs);

        var logitsTensor = outputs.First().AsTensor<float>();
        int n = _classes!.Length;
        float[] logits = new float[n];
        for (int i = 0; i < n; i++)
            logits[i] = logitsTensor[0, i];

        return logits;
    }

    // ─── Интерпретация результата ─────────────────────────────────────────

    /// <summary>
    /// Softmax над логитами, выбор класса с максимальной вероятностью.
    /// </summary>
    private GestureResult BuildResult(float[] logits)
    {
        // Softmax с вычитанием максимума для численной стабильности
        float max  = logits.Max();
        float[] ex = logits.Select(l => MathF.Exp(l - max)).ToArray();
        float sum  = ex.Sum();
        float[] probs = ex.Select(e => e / sum).ToArray();

        int best = Array.IndexOf(probs, probs.Max());

        return new GestureResult
        {
            Letter       = _classes![best],
            Confidence   = probs[best],
            HandDetected = true,
        };
    }

    // ─── Загрузка ресурсов ────────────────────────────────────────────────

    /// <summary>
    /// Копирует файл из MauiAssets (Resources/Raw/) в CacheDirectory.
    /// OnnxRuntime требует путь к файлу на диске, а не поток.
    /// </summary>
    private static async Task<string> ExtractAssetToCache(string filename)
    {
        string cachePath = Path.Combine(FileSystem.CacheDirectory, filename);

        // Всегда перезаписываем — чтобы обновлённая модель применялась сразу
        using Stream assetStream = await FileSystem.OpenAppPackageFileAsync(filename);
        using FileStream fileStream = File.Create(cachePath);
        await assetStream.CopyToAsync(fileStream);

        return cachePath;
    }

    private static async Task<string> ReadAssetTextAsync(string filename)
    {
        using Stream stream = await FileSystem.OpenAppPackageFileAsync(filename);
        using var reader    = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    // ─── Парсинг метаданных ───────────────────────────────────────────────

    private void ParseMetadata(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        // Порядок классов
        var arr = root.GetProperty("classes");
        _classes = new string[arr.GetArrayLength()];
        for (int i = 0; i < _classes.Length; i++)
            _classes[i] = arr[i].GetString()!;

        // Параметры нормализации
        var mean  = root.GetProperty("scaler_mean");
        var scale = root.GetProperty("scaler_scale");
        _scalerMean  = new float[63];
        _scalerScale = new float[63];
        for (int i = 0; i < 63; i++)
        {
            _scalerMean[i]  = mean[i].GetSingle();
            _scalerScale[i] = scale[i].GetSingle();
        }
    }

    // ─── IDisposable ──────────────────────────────────────────────────────

    public void Dispose()
    {
        _landmarkSession?.Dispose();
        _gestureSession?.Dispose();
        GC.SuppressFinalize(this);
    }
}
