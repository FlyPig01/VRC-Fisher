using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Inference;

public sealed class OnnxRuntimeDetector : IDetector, IDisposable
{
    public const string LocatorModel = "locator.onnx";
    public const string MinigameModel = "minigame.onnx";

    private readonly InferenceSession _locator;
    private readonly InferenceSession _minigame;
    private readonly string _locatorInput;
    private readonly string _minigameInput;
    private readonly float _confidenceThreshold;
    private readonly float _iouThreshold;
    private readonly int _inputSize;
    private readonly IReadOnlyList<string> _locatorClasses = ["prompt", "fishing_ui_group", "success", "failure"];
    private readonly IReadOnlyList<string> _minigameClasses = ["rail", "control_bar", "target", "progress_bar"];

    public OnnxRuntimeDetector(
        string modelsDirectory,
        ExecutionDevice device,
        float confidenceThreshold = 0.35f,
        float iouThreshold = 0.45f,
        int inputSize = 640)
    {
        var locatorPath = Path.Combine(modelsDirectory, LocatorModel);
        var minigamePath = Path.Combine(modelsDirectory, MinigameModel);
        if (!File.Exists(locatorPath) || !File.Exists(minigamePath))
            throw new FileNotFoundException("两个 ONNX 模型必须同时存在");

        var sessions = CreateSessions(locatorPath, minigamePath, device);
        _locator = sessions.Locator;
        _minigame = sessions.Minigame;
        _locatorInput = _locator.InputMetadata.Keys.Single();
        _minigameInput = _minigame.InputMetadata.Keys.Single();
        _confidenceThreshold = confidenceThreshold;
        _iouThreshold = iouThreshold;
        if (inputSize is < 32 or > 2048)
            throw new ArgumentOutOfRangeException(nameof(inputSize));
        _inputSize = inputSize;
        Provider = sessions.Provider;
    }

    public string Provider { get; }
    public bool IsReady => true;
    public bool CanProduceDecisions => true;
    public static bool SupportsDirectML
    {
        get
        {
#if VRC_DIRECTML
            return true;
#else
            return false;
#endif
        }
    }

    public DetectionObservation Detect(CapturedFrameEventArgs frame)
    {
        // The detector deliberately refuses to infer from an empty capture.
        if (frame.Width <= 0 || frame.Height <= 0 || frame.BgraPixels.IsEmpty)
            throw new InvalidDataException("捕获帧为空");

        using var locator = Run(_locator, _locatorInput, frame, _inputSize, out var locatorTransform);
        var locatorDetections = Decode(locator, _locatorClasses, _confidenceThreshold, _iouThreshold, locatorTransform);
        var prompt = BestBox(locatorDetections, "prompt");
        var fishingUi = BestBox(locatorDetections, "fishing_ui_group");
        var success = BestBox(locatorDetections, "success");
        var failure = BestBox(locatorDetections, "failure");
        if (fishingUi is null)
            return new DetectionObservation(frame.FrameNumber, frame.CapturedAt, Prompt: prompt, Success: success, Failure: failure);

        var crop = Crop(frame, fishingUi.Value, 0.08f);
        using var minigame = Run(_minigame, _minigameInput, crop.Frame, _inputSize, out var minigameTransform);
        var localDetections = Decode(minigame, _minigameClasses, _confidenceThreshold, _iouThreshold, minigameTransform);
        var rail = BestBox(localDetections, "rail");
        var control = BestBox(localDetections, "control_bar");
        var target = BestBox(localDetections, "target");
        var progress = BestBox(localDetections, "progress_bar");
        var targetY = RelativeCenter(rail, target);
        var controlTop = RelativeEdge(rail, control, top: true);
        var controlBottom = RelativeEdge(rail, control, top: false);
        var progressNorm = rail is not null && progress is not null
            ? Math.Clamp(progress.Value.Height / MathF.Max(1, rail.Value.Height), 0, 1)
            : (float?)null;
        return new DetectionObservation(
            frame.FrameNumber,
            frame.CapturedAt,
            FishingUi: fishingUi,
            Prompt: prompt,
            Success: success,
            Failure: failure,
            Rail: ToGlobal(rail, crop.OffsetX, crop.OffsetY),
            ControlBar: ToGlobal(control, crop.OffsetX, crop.OffsetY),
            Target: ToGlobal(target, crop.OffsetX, crop.OffsetY),
            ProgressBar: ToGlobal(progress, crop.OffsetX, crop.OffsetY),
            TargetYNorm: targetY,
            ControlTopNorm: controlTop,
            ControlBottomNorm: controlBottom,
            ProgressNorm: progressNorm);
    }

    public void Dispose()
    {
        _minigame.Dispose();
        _locator.Dispose();
    }

    private static (InferenceSession Locator, InferenceSession Minigame, string Provider) CreateSessions(
        string locatorPath,
        string minigamePath,
        ExecutionDevice device)
    {
        if (device == ExecutionDevice.Gpu)
        {
#if VRC_DIRECTML
            return CreatePair(locatorPath, minigamePath, useDirectMl: true, "DmlExecutionProvider");
#else
            throw new PlatformNotSupportedException("当前构建未包含 DirectML Provider");
#endif
        }

        if (device == ExecutionDevice.Cpu)
            return CreatePair(locatorPath, minigamePath, useDirectMl: false, "CPUExecutionProvider");

#if VRC_DIRECTML
        try
        {
            return CreatePair(locatorPath, minigamePath, useDirectMl: true, "DmlExecutionProvider");
        }
        catch (Exception error) when (error is OnnxRuntimeException or DllNotFoundException or BadImageFormatException)
        {
            return CreatePair(locatorPath, minigamePath, useDirectMl: false, "CPUExecutionProvider");
        }
#else
        return CreatePair(locatorPath, minigamePath, useDirectMl: false, "CPUExecutionProvider");
#endif
    }

    private static (InferenceSession Locator, InferenceSession Minigame, string Provider) CreatePair(
        string locatorPath,
        string minigamePath,
        bool useDirectMl,
        string provider)
    {
        var locator = new InferenceSession(locatorPath, CreateOptions(useDirectMl));
        try
        {
            var minigame = new InferenceSession(minigamePath, CreateOptions(useDirectMl));
            return (locator, minigame, provider);
        }
        catch
        {
            locator.Dispose();
            throw;
        }
    }

    private static SessionOptions CreateOptions(bool useDirectMl)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
            InterOpNumThreads = 1
        };
        if (useDirectMl)
        {
#if VRC_DIRECTML
            options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            options.EnableMemoryPattern = false;
            options.AppendExecutionProvider_DML(0);
#else
            throw new PlatformNotSupportedException("当前构建未包含 DirectML Provider");
#endif
        }
        else
        {
            options.AppendExecutionProvider_CPU();
        }
        return options;
    }

    private static IDisposableReadOnlyCollection<DisposableNamedOnnxValue> Run(
        InferenceSession session,
        string inputName,
        CapturedFrameEventArgs frame,
        int inputSize,
        out LetterboxTransform transform)
    {
        var tensor = ToTensor(frame, inputSize, out transform);
        return session.Run([NamedOnnxValue.CreateFromTensor(inputName, tensor)]);
    }

    private static DenseTensor<float> ToTensor(
        CapturedFrameEventArgs frame,
        int size,
        out LetterboxTransform transform)
    {
        var tensor = new DenseTensor<float>([1, 3, size, size]);
        var source = frame.BgraPixels.Span;
        var scale = MathF.Min((float)size / frame.Width, (float)size / frame.Height);
        var resizedWidth = Math.Max(1, (int)MathF.Round(frame.Width * scale));
        var resizedHeight = Math.Max(1, (int)MathF.Round(frame.Height * scale));
        var offsetX = (size - resizedWidth) / 2;
        var offsetY = (size - resizedHeight) / 2;
        transform = new LetterboxTransform(scale, offsetX, offsetY, size, size, frame.Width, frame.Height);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            tensor[0, 0, y, x] = 114f / 255f;
            tensor[0, 1, y, x] = 114f / 255f;
            tensor[0, 2, y, x] = 114f / 255f;
        }
        for (var y = 0; y < size; y++)
        {
            if (y < offsetY || y >= offsetY + resizedHeight) continue;
            var sourceY = Math.Min(frame.Height - 1, (int)((y - offsetY) / scale));
            for (var x = 0; x < size; x++)
            {
                if (x < offsetX || x >= offsetX + resizedWidth) continue;
                var sourceX = Math.Min(frame.Width - 1, (int)((x - offsetX) / scale));
                var index = (sourceY * frame.Width + sourceX) * 4;
                if (index + 2 >= source.Length) continue;
                tensor[0, 0, y, x] = source[index + 2] / 255f;
                tensor[0, 1, y, x] = source[index + 1] / 255f;
                tensor[0, 2, y, x] = source[index] / 255f;
            }
        }
        return tensor;
    }

    private static IReadOnlyList<YoloDetection> Decode(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs,
        IReadOnlyList<string> classes,
        float confidenceThreshold,
        float iouThreshold,
        LetterboxTransform transform)
    {
        var output = outputs.FirstOrDefault() ?? throw new InvalidDataException("模型没有输出");
        return YoloPostprocessor.Decode(output.AsTensor<float>(), classes, confidenceThreshold, iouThreshold, transform);
    }

    private static BoundingBox? BestBox(IReadOnlyList<YoloDetection> detections, string className) =>
        detections.FirstOrDefault(item => string.Equals(item.ClassName, className, StringComparison.Ordinal))?.Box;

    private static float? RelativeCenter(BoundingBox? rail, BoundingBox? target)
    {
        if (rail is null || target is null) return null;
        return Math.Clamp((target.Value.CenterY - rail.Value.Top) / MathF.Max(1, rail.Value.Height), 0, 1);
    }

    private static float? RelativeEdge(BoundingBox? rail, BoundingBox? control, bool top)
    {
        if (rail is null || control is null) return null;
        var value = top ? control.Value.Top : control.Value.Bottom;
        return Math.Clamp((value - rail.Value.Top) / MathF.Max(1, rail.Value.Height), 0, 1);
    }

    private static BoundingBox? ToGlobal(BoundingBox? box, int offsetX, int offsetY) =>
        box is null ? null : new BoundingBox(box.Value.Left + offsetX, box.Value.Top + offsetY,
            box.Value.Right + offsetX, box.Value.Bottom + offsetY);

    private static (CapturedFrameEventArgs Frame, int OffsetX, int OffsetY) Crop(
        CapturedFrameEventArgs source,
        BoundingBox box,
        float padding)
    {
        var padX = (int)MathF.Round(box.Width * padding);
        var padY = (int)MathF.Round(box.Height * padding);
        var left = Math.Clamp((int)MathF.Floor(box.Left) - padX, 0, source.Width - 1);
        var top = Math.Clamp((int)MathF.Floor(box.Top) - padY, 0, source.Height - 1);
        var right = Math.Clamp((int)MathF.Ceiling(box.Right) + padX, left + 1, source.Width);
        var bottom = Math.Clamp((int)MathF.Ceiling(box.Bottom) + padY, top + 1, source.Height);
        var width = right - left;
        var height = bottom - top;
        var pixels = new byte[width * height * 4];
        var input = source.BgraPixels.Span;
        for (var y = 0; y < height; y++)
            input.Slice(((top + y) * source.Width + left) * 4, width * 4)
                .CopyTo(pixels.AsSpan(y * width * 4, width * 4));
        return (new CapturedFrameEventArgs(source.FrameNumber, source.CapturedAt, pixels, width, height), left, top);
    }
}
