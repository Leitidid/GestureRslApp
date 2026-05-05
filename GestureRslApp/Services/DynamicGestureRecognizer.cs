using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text.Json;
using GestureRslApp.Models;

namespace GestureRslApp.Services;

public class DynamicGestureRecognizer
{
    public const int SequenceLength = 30;
    private readonly Queue<float[]> _buffer = new();
    private InferenceSession? _session;
    private string[]? _labels;

    public async Task InitializeAsync()
    {
        try
        {
            // 🔥 Загружаем модель из ресурсов
            var modelBytes = await FileSystem.OpenAppPackageFileAsync("dynamic_gesture_model.onnx");
            using var ms = new MemoryStream();
            await modelBytes.CopyToAsync(ms);
            _session = new InferenceSession(ms.ToArray());

            // 🔥 Загружаем JSON из ресурсов (не из AppDataDirectory!)
            var jsonStream = await FileSystem.OpenAppPackageFileAsync("dynamic_gesture_classes.json");
            using var reader = new StreamReader(jsonStream);
            var json = await reader.ReadToEndAsync();

            try
            {
                _labels = JsonSerializer.Deserialize<string[]>(json);
            }
            catch
            {
                var meta = JsonSerializer.Deserialize<DynamicMeta>(json);
                _labels = meta?.Classes ?? Array.Empty<string>();
            }

            System.Diagnostics.Debug.WriteLine($"[Dynamic] Загружено {_labels?.Length ?? 0} классов");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Dynamic] Ошибка инициализации: {ex.Message}");
            throw;
        }
    }

    public void AddFrame(float[]? landmarks)
    {
        if (landmarks == null)
        {
            _buffer.Enqueue(new float[63]);
        }
        else
        {
            _buffer.Enqueue(landmarks);
        }

        if (_buffer.Count > SequenceLength)
            _buffer.Dequeue();
    }

    public int BufferedFrames => _buffer.Count;
    public bool IsReady => _buffer.Count >= SequenceLength;

    public void ClearBuffer()
    {
        _buffer.Clear();
    }

    public GestureResult Classify()
    {
        if (_buffer.Count < SequenceLength || _session == null || _labels == null)
            return GestureResult.NoHand;

        try
        {
            var tensor = new DenseTensor<float>(new[] { 1, SequenceLength, 63 });
            int t = 0;
            foreach (var frame in _buffer)
            {
                for (int i = 0; i < 63; i++)
                    tensor[0, t, i] = frame[i];
                t++;
            }

            var inputs = new[] { NamedOnnxValue.CreateFromTensor("input", tensor) };
            using var outputs = _session.Run(inputs);
            var scores = outputs.First().AsEnumerable<float>().ToArray();

            int maxIndex = Array.IndexOf(scores, scores.Max());

            return new GestureResult
            {
                Letter = _labels[maxIndex],
                Confidence = scores[maxIndex],
                HandDetected = true,
                Type = GestureType.Dynamic,
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Dynamic] Ошибка классификации: {ex.Message}");
            return GestureResult.NoHand;
        }
    }
}

public class DynamicMeta
{
    public string[] Classes { get; set; } = Array.Empty<string>();
    public int SequenceLength { get; set; }
    public int Features { get; set; }
    public string ModelType { get; set; } = string.Empty;
}