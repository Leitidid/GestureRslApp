using GestureRslApp.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;
using System.Linq;
using System.Text.Json;

namespace GestureRslApp.Services;

/// <summary>
/// Сервис распознавания жестов РЖЯ.
/// Загружает ДВЕ модели:
///   1. static_gesture_model.onnx  — статические буквы (А Б В ... Я, 27 классов)
///   2. dynamic_gesture_model.onnx — динамические буквы + слова (З Й К Ц Щ Ь + МАМА ПАПА ПРИВЕТ ПОКА)
///
/// Пайплайн каждого кадра:
///   1. DecodeAndResize  → SKBitmap 256×256
///   2. RunLandmarkModel → 63 координаты (hand_landmark_full.onnx, КОД БЕЗ ИЗМЕНЕНИЙ)
///   3. SubtractWristAnchor → нормализованные координаты
///   4. RunStaticModel  → StaticResult (всегда, каждый кадр)
///   5. FrameBuffer.Add(coords) → буфер 30 кадров
///   6. Если буфер заполнен → RunDynamicModel → DynamicResult
///   7. Возвращаем DynamicResult если он свежий и уверенный, иначе StaticResult.
///
/// Необходимые файлы в Resources/Raw/:
///   hand_landmark_full.onnx
///   static_gesture_model.onnx   + static_gesture_classes.json
///   dynamic_gesture_model.onnx  + dynamic_gesture_classes.json
/// </summary>
public class HandGestureRecognizer : IDisposable
{
    // ─── LANDMARK-МОДЕЛЬ (КОД НЕ ИЗМЕНЁН — НЕ ТРОГАТЬ) ──────────────────────

    private const int    InputImageSize        = 256;
    private const string LandmarkInputName     = "inputs:0";
    private const string LandmarkOutputCoords  = "Identity_2:0";   // float32[1,63]
    private const string LandmarkOutputScore   = "Identity:0";     // float32[1,1,1,1]
    private const float  HandPresenceThreshold = 0.5f;

    // ─── ONNX-сессии ──────────────────────────────────────────────────────────

    private InferenceSession? _landmarkSession;
    private InferenceSession? _staticSession;
    private InferenceSession? _dynamicSession;

    // ─── Классы моделей ───────────────────────────────────────────────────────

    private string[]? _staticClasses;    // ["А", "Б", ..., "Я"]
    private string[]? _dynamicClasses;   // ["З", "Й", ..., "ПОКА"]

    // ─── Набор слов (для IsWord) ──────────────────────────────────────────────

    private static readonly HashSet<string> _words =
        new(StringComparer.OrdinalIgnoreCase) { "МАМА", "ПАПА", "ПРИВЕТ", "ПОКА" };

    // ─── Буфер кадров для динамической модели ────────────────────────────────

    private const int DynamicSeqLen = 30;          // Длина последовательности
    private const int DynamicRunEveryFrames = 10;  // Запускаем динамику каждые N кадров
    private const float DynamicMinConfidence = 0.65f;
    private const int DynamicResultExpiryMs = 2500;  // Показываем результат N мс

    private readonly float[][] _frameBuffer = new float[DynamicSeqLen][];
    private int  _bufferHead  = 0;    // Индекс для записи (циклический)
    private int  _bufferFill  = 0;    // Сколько кадров записано (0..DynamicSeqLen)
    private int  _framesSinceDynRun = 0;

    private GestureResult? _lastDynamicResult;
    private DateTime _dynamicResultTimestamp = DateTime.MinValue;

    private bool _isInitialized;

    // ─── Инициализация ────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        System.Diagnostics.Debug.WriteLine("[Recognizer] Инициализация...");

        string landmarkPath = await ExtractAssetToCache("hand_landmark_full.onnx");
        string staticPath   = await ExtractAssetToCache("static_gesture_model.onnx");
        string dynamicPath  = await ExtractAssetToCache("dynamic_gesture_model.onnx");

        string staticJson  = await ReadAssetTextAsync("static_gesture_classes.json");
        string dynamicJson = await ReadAssetTextAsync("dynamic_gesture_classes.json");

        var opts = new SessionOptions();
        opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

        _landmarkSession = new InferenceSession(landmarkPath, opts);
        _staticSession   = new InferenceSession(staticPath,   opts);
        _dynamicSession  = new InferenceSession(dynamicPath,  opts);

        // Логирование имён входов/выходов landmark-модели (для отладки)
        System.Diagnostics.Debug.WriteLine("[Recognizer] Landmark inputs:");
        foreach (var kv in _landmarkSession.InputMetadata)
            System.Diagnostics.Debug.WriteLine($"  '{kv.Key}'");
        System.Diagnostics.Debug.WriteLine("[Recognizer] Landmark outputs:");
        foreach (var kv in _landmarkSession.OutputMetadata)
            System.Diagnostics.Debug.WriteLine($"  '{kv.Key}'");

        _staticClasses  = JsonSerializer.Deserialize<string[]>(staticJson)!;
        _dynamicClasses = JsonSerializer.Deserialize<string[]>(dynamicJson)!;

        System.Diagnostics.Debug.WriteLine(
            $"[Recognizer] Статических классов: {_staticClasses.Length}");
        System.Diagnostics.Debug.WriteLine(
            $"[Recognizer] Динамических классов: {_dynamicClasses.Length}");

        _isInitialized = true;
        System.Diagnostics.Debug.WriteLine("[Recognizer] Готово ✓");
    }

    // ─── Основной метод (вызывается из CameraPage каждые 200мс) ──────────────

    public GestureResult Recognize(byte[] jpegBytes)
    {
        if (!_isInitialized || jpegBytes is not { Length: > 0 })
            return GestureResult.NoHand;

        try
        {
            // ── 1. Декодируем и ресайзим изображение ──────────────────────────
            using SKBitmap? resized = DecodeAndResize(jpegBytes);
            if (resized == null) return GestureResult.NoHand;

            // ── 2. Landmark-модель → координаты 21 точки ──────────────────────
            float[]? rawLandmarks = RunLandmarkModel(resized);
            if (rawLandmarks == null) return GestureResult.NoHand;

            // ── 3. Нормализация координат (вычитаем запястье как якорь) ───────
            float[] anchored = SubtractWristAnchor(rawLandmarks);

            // ── 4. Статическая модель → результат для текущего кадра ──────────
            GestureResult staticResult = RunStaticModel(anchored);

            // ── 5. Добавляем кадр в буфер для динамической модели ─────────────
            AddToBuffer(anchored);
            _framesSinceDynRun++;

            // ── 6. Динамическая модель (каждые DynamicRunEveryFrames кадров) ───
            if (_bufferFill >= DynamicSeqLen && _framesSinceDynRun >= DynamicRunEveryFrames)
            {
                _framesSinceDynRun = 0;
                GestureResult? dynResult = RunDynamicModel();
                if (dynResult != null && dynResult.Confidence >= DynamicMinConfidence)
                {
                    _lastDynamicResult    = dynResult;
                    _dynamicResultTimestamp = DateTime.Now;
                    System.Diagnostics.Debug.WriteLine(
                        $"[Dynamic] {dynResult.Letter} ({dynResult.ConfidencePercent}%)");
                }
            }

            // ── 7. Возвращаем динамический результат если он свежий ───────────
            if (_lastDynamicResult != null)
            {
                var elapsed = (DateTime.Now - _dynamicResultTimestamp).TotalMilliseconds;
                if (elapsed < DynamicResultExpiryMs)
                    return _lastDynamicResult;
            }

            return staticResult;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Recognizer] Ошибка: {ex.Message}");
            return GestureResult.NoHand;
        }
    }

    // ─── Предобработка ────────────────────────────────────────────────────────
    // (КОД НЕ ИЗМЕНЁН — идентичен рабочей версии)

    private static SKBitmap? DecodeAndResize(byte[] jpegBytes)
    {
        using SKData data = SKData.CreateCopy(jpegBytes);
        using SKBitmap? src = SKBitmap.Decode(data);
        if (src == null) return null;

        int side = (int)(Math.Min(src.Width, src.Height) * 0.85f);
        int left = (src.Width  - side) / 2;
        int top  = (src.Height - side) / 2;
        var srcRect = new SKRectI(left, top, left + side, top + side);

        var dst = new SKBitmap(InputImageSize, InputImageSize, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(dst);
        canvas.DrawBitmap(src, srcRect, new SKRect(0, 0, InputImageSize, InputImageSize));
        return dst;
    }

    // ─── Landmark-инференс ────────────────────────────────────────────────────
    // (КОД НЕ ИЗМЕНЁН — идентичен рабочей версии)

    private float[]? RunLandmarkModel(SKBitmap bitmap)
    {
        int h = bitmap.Height, w = bitmap.Width;
        var tensor = new DenseTensor<float>(new[] { 1, h, w, 3 });
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            SKColor px = bitmap.GetPixel(x, y);
            tensor[0, y, x, 0] = px.Red   / 255f;
            tensor[0, y, x, 1] = px.Green / 255f;
            tensor[0, y, x, 2] = px.Blue  / 255f;
        }

        var inputs = new[] { NamedOnnxValue.CreateFromTensor(LandmarkInputName, tensor) };
        using var outputs = _landmarkSession!.Run(inputs);

        System.Diagnostics.Debug.WriteLine($"[Landmark] Outputs: {outputs.Count}");
        foreach (var o in outputs)
        {
            var dims = string.Join("×", o.AsTensor<float>().Dimensions.ToArray());
            System.Diagnostics.Debug.WriteLine($"  '{o.Name}': [{dims}]");
        }

        var scoreTensor = outputs.First(o => o.Name == LandmarkOutputScore).AsTensor<float>();
        float score = scoreTensor[0, 0, 0, 0];   // Форма [1,1,1,1]
        System.Diagnostics.Debug.WriteLine($"[Landmark] Score: {score:F3}");

        if (score < HandPresenceThreshold) return null;

        var coordTensor = outputs.First(o => o.Name == LandmarkOutputCoords).AsTensor<float>();
        float[] landmarks = new float[63];
        for (int i = 0; i < 63; i++)
            landmarks[i] = coordTensor[0, i];   // Форма [1,63]

        return landmarks;
    }

    // ─── Нормализация ────────────────────────────────────────────────────────
    // (КОД НЕ ИЗМЕНЁН)

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

    // ─── Статическая классификация ────────────────────────────────────────────

    private GestureResult RunStaticModel(float[] anchored)
    {
        var tensor = new DenseTensor<float>(anchored, new[] { 1, 63 });
        string inName = _staticSession!.InputMetadata.Keys.First();
        var inputs = new[] { NamedOnnxValue.CreateFromTensor(inName, tensor) };
        using var outputs = _staticSession.Run(inputs);

        var logitsTensor = outputs.First().AsTensor<float>();
        int n = _staticClasses!.Length;
        float[] logits = new float[n];
        for (int i = 0; i < n; i++)
            logits[i] = logitsTensor[0, i];

        return BuildResult(logits, _staticClasses, GestureType.Static);
    }

    // ─── Буфер кадров для динамической модели ────────────────────────────────

    private void AddToBuffer(float[] landmarks)
    {
        // Копируем координаты в буфер (избегаем утечки если массивы переиспользуются)
        _frameBuffer[_bufferHead] = (float[])landmarks.Clone();
        _bufferHead = (_bufferHead + 1) % DynamicSeqLen;
        if (_bufferFill < DynamicSeqLen) _bufferFill++;
    }

    /// <summary>Возвращает буфер в хронологическом порядке как [30, 63] = 1890 float.</summary>
    private float[] GetSequenceFlat()
    {
        float[] seq = new float[DynamicSeqLen * 63];
        // Читаем начиная с самого старого кадра
        int readHead = _bufferFill < DynamicSeqLen ? 0 : _bufferHead;
        for (int i = 0; i < DynamicSeqLen; i++)
        {
            int idx = (readHead + i) % DynamicSeqLen;
            if (_frameBuffer[idx] != null)
                Array.Copy(_frameBuffer[idx], 0, seq, i * 63, 63);
        }
        return seq;
    }

    // ─── Динамическая классификация ───────────────────────────────────────────

    private GestureResult? RunDynamicModel()
    {
        if (_dynamicSession == null || _dynamicClasses == null) return null;

        try
        {
            float[] seqFlat = GetSequenceFlat();  // [1890]
            // Модель ожидает [1, 30, 63]
            var tensor = new DenseTensor<float>(seqFlat, new[] { 1, DynamicSeqLen, 63 });
            string inName = _dynamicSession.InputMetadata.Keys.First();
            var inputs = new[] { NamedOnnxValue.CreateFromTensor(inName, tensor) };
            using var outputs = _dynamicSession.Run(inputs);

            var logitsTensor = outputs.First().AsTensor<float>();
            int n = _dynamicClasses.Length;
            float[] logits = new float[n];
            for (int i = 0; i < n; i++)
                logits[i] = logitsTensor[0, i];

            return BuildResult(logits, _dynamicClasses, GestureType.Dynamic);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Dynamic] Ошибка: {ex.Message}");
            return null;
        }
    }

    // ─── Softmax + argmax ─────────────────────────────────────────────────────

    private static GestureResult BuildResult(float[] logits, string[] classes, GestureType type)
    {
        float max = logits.Max();
        float[] ex = logits.Select(l => MathF.Exp(l - max)).ToArray();
        float sum = ex.Sum();
        float[] probs = ex.Select(e => e / sum).ToArray();

        int best = Array.IndexOf(probs, probs.Max());
        string letter = classes[best];

        return new GestureResult
        {
            Letter = letter,
            Confidence = probs[best],
            HandDetected = true,
            Type = type,
            // 🔥 IsWord вычисляется автоматически — НЕ присваиваем!
        };
    }

    // ─── Загрузка ресурсов ────────────────────────────────────────────────────

    private static async Task<string> ExtractAssetToCache(string filename)
    {
        string cachePath = Path.Combine(FileSystem.CacheDirectory, filename);
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

    // ─── IDisposable ──────────────────────────────────────────────────────────
    // 🔥 ДОБАВЛЕНО: Публичные методы для CameraPage

    public float[]? ExtractLandmarks(byte[] jpegBytes)
    {
        using SKBitmap? resized = DecodeAndResize(jpegBytes);
        if (resized == null) return null;

        float[]? rawLandmarks = RunLandmarkModel(resized);
        if (rawLandmarks == null) return null;

        return SubtractWristAnchor(rawLandmarks);
    }

    public GestureResult ClassifyLandmarks(float[] anchored)
    {
        return RunStaticModel(anchored);
    }
    public void Dispose()
    {
        _landmarkSession?.Dispose();
        _staticSession?.Dispose();
        _dynamicSession?.Dispose();
        GC.SuppressFinalize(this);
    }
}
