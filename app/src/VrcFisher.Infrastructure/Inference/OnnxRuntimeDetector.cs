using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Inference;

public sealed class OnnxRuntimeDetector : IDetector, IDisposable
{
    public const string LocatorModel = "locator.onnx";
    public const string MinigameModel = "minigame.onnx";
    public const int ExpectedLocatorInputSize = 960;
    public const int ExpectedMinigameInputSize = 640;
    private readonly InferenceSession _locator;
    private readonly InferenceSession _minigame;
    private readonly string _locatorInput;
    private readonly string _minigameInput;
    private readonly float _confidenceThreshold;
    private readonly float _iouThreshold;
    private readonly int _locatorInputSize;
    private readonly int _minigameInputSize;
    private readonly DenseTensor<float> _locatorTensor;
    private readonly DenseTensor<float> _minigameTensor;
    private readonly NamedOnnxValue[] _locatorInputs;
    private readonly NamedOnnxValue[] _minigameInputs;
    private readonly IReadOnlyList<string> _locatorClasses = ["bite_indicator", "minigame_panel"];
    private readonly IReadOnlyList<string> _minigameClasses = ["catch_zone", "moving_target"];
    private ResizeMap? _locatorResizeMap;
    private ResizeMap? _minigameResizeMap;
    private YoloDetection? _cachedPanel;
    private DateTimeOffset _lastLocatorAt = DateTimeOffset.MinValue;

    public OnnxRuntimeDetector(
        string modelsDirectory,
        ExecutionDevice device,
        float confidenceThreshold = 0.35f,
        float iouThreshold = 0.45f)
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
        _locatorInputSize = ReadSquareInputSize(
            _locator,
            _locatorInput,
            LocatorModel,
            ExpectedLocatorInputSize);
        _minigameInputSize = ReadSquareInputSize(
            _minigame,
            _minigameInput,
            MinigameModel,
            ExpectedMinigameInputSize);
        _locatorTensor = new DenseTensor<float>([1, 3, _locatorInputSize, _locatorInputSize]);
        _minigameTensor = new DenseTensor<float>([1, 3, _minigameInputSize, _minigameInputSize]);
        _locatorInputs = [NamedOnnxValue.CreateFromTensor(_locatorInput, _locatorTensor)];
        _minigameInputs = [NamedOnnxValue.CreateFromTensor(_minigameInput, _minigameTensor)];
        _confidenceThreshold = confidenceThreshold;
        _iouThreshold = iouThreshold;
        Provider = sessions.Provider;
    }

    public string Provider { get; }
    public bool IsReady => true;
    public bool CanProduceDecisions => true;
    public bool HasCachedPanel => _cachedPanel is not null;
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

    public DetectionResult Detect(
        CapturedFrameEventArgs frame,
        FishingPhase phase,
        TimeSpan minigamePanelRecheckInterval,
        bool includeVisualization = false)
    {
        // The detector deliberately refuses to infer from an empty capture.
        if (frame.Width <= 0 || frame.Height <= 0 || frame.BgraPixels.IsEmpty)
            throw new InvalidDataException("捕获帧为空");

        if (phase is not (FishingPhase.Hooking or FishingPhase.Minigame))
            _cachedPanel = null;

        if (phase == FishingPhase.Minigame
            && _cachedPanel is not null
            && frame.CapturedAt - _lastLocatorAt < minigamePanelRecheckInterval)
        {
            var minigame = DetectMinigame(frame, _cachedPanel, biteIndicator: null, includeVisualization);
            return new DetectionResult(
                minigame.Observation,
                InferenceWorkload.CachedMinigame,
                includeVisualization ? CreateVisualization(frame, minigame.Visuals) : null);
        }

        var (biteIndicator, detectedPanel) = DetectLocator(frame);
        _lastLocatorAt = frame.CapturedAt;
        if (detectedPanel is null)
        {
            _cachedPanel = null;
            return new DetectionResult(
                new DetectionObservation(frame.FrameNumber, frame.CapturedAt, BiteIndicator: biteIndicator?.Box),
                InferenceWorkload.Locator,
                includeVisualization ? CreateVisualization(frame, Compact(biteIndicator)) : null);
        }

        // The panel is stable for one minigame. Keep the first confirmed crop
        // so locator jitter does not move the control coordinate system.
        _cachedPanel ??= detectedPanel;
        var detectedMinigame = DetectMinigame(frame, _cachedPanel, biteIndicator, includeVisualization);
        return new DetectionResult(
            detectedMinigame.Observation,
            InferenceWorkload.LocatorAndMinigame,
            includeVisualization ? CreateVisualization(frame, detectedMinigame.Visuals) : null);
    }

    private (YoloDetection? BiteIndicator, YoloDetection? MinigamePanel) DetectLocator(
        CapturedFrameEventArgs frame)
    {
        var region = PixelRegion.Full(frame);
        using var locator = Run(
            _locator,
            _locatorInputs,
            _locatorTensor,
            region,
            _locatorInputSize,
            ref _locatorResizeMap,
            out var locatorTransform);
        var detections = Decode(locator, _locatorClasses, _confidenceThreshold, _iouThreshold, locatorTransform);
        return (
            BestDetection(detections, "bite_indicator"),
            BestDetection(detections, "minigame_panel"));
    }

    private (DetectionObservation Observation, IReadOnlyList<YoloDetection> Visuals) DetectMinigame(
        CapturedFrameEventArgs frame,
        YoloDetection minigamePanel,
        YoloDetection? biteIndicator,
        bool includeVisualization)
    {
        var crop = PixelRegion.Crop(frame, minigamePanel.Box, 0.08f);
        using var minigame = Run(
            _minigame,
            _minigameInputs,
            _minigameTensor,
            crop,
            _minigameInputSize,
            ref _minigameResizeMap,
            out var minigameTransform);
        var localDetections = Decode(minigame, _minigameClasses, _confidenceThreshold, _iouThreshold, minigameTransform);
        var localCatchZone = BestDetection(localDetections, "catch_zone");
        var localMovingTarget = BestDetection(localDetections, "moving_target");
        var targetY = RelativeCenter(localCatchZone?.Box, localMovingTarget?.Box);
        var controlTop = localCatchZone is null ? (float?)null : 0f;
        var controlBottom = localCatchZone is null ? (float?)null : 1f;
        var catchZone = ToGlobal(localCatchZone, crop.OriginX, crop.OriginY);
        var movingTarget = ToGlobal(localMovingTarget, crop.OriginX, crop.OriginY);
        var observation = new DetectionObservation(
            frame.FrameNumber,
            frame.CapturedAt,
            BiteIndicator: biteIndicator?.Box,
            MinigamePanel: minigamePanel.Box,
            CatchZone: catchZone?.Box,
            MovingTarget: movingTarget?.Box,
            MovingTargetYNorm: targetY,
            CatchZoneTopNorm: controlTop,
            CatchZoneBottomNorm: controlBottom);
        return (
            observation,
            includeVisualization
                ? Compact(biteIndicator, minigamePanel, catchZone, movingTarget)
                : []);
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
        IReadOnlyCollection<NamedOnnxValue> inputs,
        DenseTensor<float> tensor,
        PixelRegion region,
        int inputSize,
        ref ResizeMap? resizeMap,
        out LetterboxTransform transform)
    {
        FillTensor(region, inputSize, tensor, ref resizeMap, out transform);
        return session.Run(inputs);
    }

    private static int ReadSquareInputSize(
        InferenceSession session,
        string inputName,
        string modelName,
        int expectedSize)
    {
        var dimensions = session.InputMetadata[inputName].Dimensions;
        if (dimensions.Length != 4
            || dimensions[0] is not (1 or -1)
            || dimensions[1] != 3
            || dimensions[2] <= 0
            || dimensions[2] != dimensions[3]
            || dimensions[2] is < 32 or > 2048)
        {
            throw new InvalidDataException(
                $"{modelName} 必须使用 [1,3,H,W] 的静态方形输入，实际为 [{string.Join(',', dimensions)}]");
        }
        if (dimensions[2] != expectedSize)
        {
            throw new InvalidDataException(
                $"{modelName} 输入必须为 {expectedSize} x {expectedSize}，实际为 {dimensions[2]} x {dimensions[3]}");
        }
        return dimensions[2];
    }

    private static void FillTensor(
        PixelRegion region,
        int size,
        DenseTensor<float> tensor,
        ref ResizeMap? cachedMap,
        out LetterboxTransform transform)
    {
        var map = cachedMap;
        if (map is null || !map.Matches(region.Width, region.Height, size))
        {
            map = ResizeMap.Create(region.Width, region.Height, size);
            cachedMap = map;
        }

        transform = new LetterboxTransform(
            map.Scale,
            map.OffsetX,
            map.OffsetY,
            size,
            size,
            region.Width,
            region.Height);

        const float inverse255 = 1f / 255f;
        var destination = tensor.Buffer.Span;
        destination.Fill(114f * inverse255);
        var source = region.Pixels.Span;
        var planeLength = size * size;

        for (var destinationY = 0; destinationY < map.ResizedHeight; destinationY++)
        {
            var topRow = (region.OriginY + map.Y0[destinationY]) * region.Stride;
            var bottomRow = (region.OriginY + map.Y1[destinationY]) * region.Stride;
            var yWeight = map.YWeight[destinationY];
            var outputRow = (map.OffsetY + destinationY) * size + map.OffsetX;

            for (var destinationX = 0; destinationX < map.ResizedWidth; destinationX++)
            {
                var left = region.OriginX + map.X0[destinationX];
                var right = region.OriginX + map.X1[destinationX];
                var xWeight = map.XWeight[destinationX];
                var topLeft = (topRow + left) * 4;
                var topRight = (topRow + right) * 4;
                var bottomLeft = (bottomRow + left) * 4;
                var bottomRight = (bottomRow + right) * 4;

                var topR = source[topLeft + 2] + (source[topRight + 2] - source[topLeft + 2]) * xWeight;
                var bottomR = source[bottomLeft + 2] + (source[bottomRight + 2] - source[bottomLeft + 2]) * xWeight;
                var topG = source[topLeft + 1] + (source[topRight + 1] - source[topLeft + 1]) * xWeight;
                var bottomG = source[bottomLeft + 1] + (source[bottomRight + 1] - source[bottomLeft + 1]) * xWeight;
                var topB = source[topLeft] + (source[topRight] - source[topLeft]) * xWeight;
                var bottomB = source[bottomLeft] + (source[bottomRight] - source[bottomLeft]) * xWeight;
                var outputIndex = outputRow + destinationX;

                destination[outputIndex] = (topR + (bottomR - topR) * yWeight) * inverse255;
                destination[planeLength + outputIndex] = (topG + (bottomG - topG) * yWeight) * inverse255;
                destination[planeLength * 2 + outputIndex] = (topB + (bottomB - topB) * yWeight) * inverse255;
            }
        }
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

    private static YoloDetection? BestDetection(IReadOnlyList<YoloDetection> detections, string className) =>
        detections.FirstOrDefault(item => string.Equals(item.ClassName, className, StringComparison.Ordinal));

    private static float? RelativeCenter(BoundingBox? zone, BoundingBox? target)
    {
        if (zone is null || target is null) return null;
        return Math.Clamp((target.Value.CenterY - zone.Value.Top) / MathF.Max(1, zone.Value.Height), 0, 1);
    }

    private static YoloDetection? ToGlobal(YoloDetection? detection, int offsetX, int offsetY)
    {
        if (detection is null) return null;
        var box = detection.Box;
        return detection with
        {
            Box = new BoundingBox(
                box.Left + offsetX,
                box.Top + offsetY,
                box.Right + offsetX,
                box.Bottom + offsetY)
        };
    }

    private static IReadOnlyList<YoloDetection> Compact(params YoloDetection?[] detections) =>
        detections.Where(item => item is not null).Cast<YoloDetection>().ToArray();

    private static DetectionVisualizationFrame CreateVisualization(
        CapturedFrameEventArgs frame,
        IReadOnlyList<YoloDetection> detections) =>
        new(
            frame.FrameNumber,
            frame.CapturedAt,
            frame.Width,
            frame.Height,
            detections.Select(item => new DetectionVisual(item.ClassName, item.Confidence, item.Box)).ToArray());

    private sealed class ResizeMap
    {
        private ResizeMap(
            int sourceWidth,
            int sourceHeight,
            int inputSize,
            float scale,
            int resizedWidth,
            int resizedHeight,
            int offsetX,
            int offsetY,
            int[] x0,
            int[] x1,
            float[] xWeight,
            int[] y0,
            int[] y1,
            float[] yWeight)
        {
            SourceWidth = sourceWidth;
            SourceHeight = sourceHeight;
            InputSize = inputSize;
            Scale = scale;
            ResizedWidth = resizedWidth;
            ResizedHeight = resizedHeight;
            OffsetX = offsetX;
            OffsetY = offsetY;
            X0 = x0;
            X1 = x1;
            XWeight = xWeight;
            Y0 = y0;
            Y1 = y1;
            YWeight = yWeight;
        }

        public int SourceWidth { get; }
        public int SourceHeight { get; }
        public int InputSize { get; }
        public float Scale { get; }
        public int ResizedWidth { get; }
        public int ResizedHeight { get; }
        public int OffsetX { get; }
        public int OffsetY { get; }
        public int[] X0 { get; }
        public int[] X1 { get; }
        public float[] XWeight { get; }
        public int[] Y0 { get; }
        public int[] Y1 { get; }
        public float[] YWeight { get; }

        public bool Matches(int width, int height, int inputSize) =>
            SourceWidth == width && SourceHeight == height && InputSize == inputSize;

        public static ResizeMap Create(int width, int height, int inputSize)
        {
            var scale = MathF.Min((float)inputSize / width, (float)inputSize / height);
            var resizedWidth = Math.Max(1, (int)MathF.Round(width * scale));
            var resizedHeight = Math.Max(1, (int)MathF.Round(height * scale));
            var x0 = new int[resizedWidth];
            var x1 = new int[resizedWidth];
            var xWeight = new float[resizedWidth];
            var y0 = new int[resizedHeight];
            var y1 = new int[resizedHeight];
            var yWeight = new float[resizedHeight];
            FillAxis(resizedWidth, width, scale, x0, x1, xWeight);
            FillAxis(resizedHeight, height, scale, y0, y1, yWeight);
            return new ResizeMap(
                width,
                height,
                inputSize,
                scale,
                resizedWidth,
                resizedHeight,
                (inputSize - resizedWidth) / 2,
                (inputSize - resizedHeight) / 2,
                x0,
                x1,
                xWeight,
                y0,
                y1,
                yWeight);
        }

        private static void FillAxis(
            int destinationLength,
            int sourceLength,
            float scale,
            int[] lower,
            int[] upper,
            float[] weight)
        {
            for (var destination = 0; destination < destinationLength; destination++)
            {
                var source = (destination + 0.5f) / scale - 0.5f;
                var floor = MathF.Floor(source);
                lower[destination] = Math.Clamp((int)floor, 0, sourceLength - 1);
                upper[destination] = Math.Min(sourceLength - 1, lower[destination] + 1);
                weight[destination] = Math.Clamp(source - floor, 0, 1);
            }
        }
    }

    private readonly record struct PixelRegion(
        ReadOnlyMemory<byte> Pixels,
        int Width,
        int Height,
        int Stride,
        int OriginX,
        int OriginY)
    {
        public static PixelRegion Full(CapturedFrameEventArgs frame) =>
            new(frame.BgraPixels, frame.Width, frame.Height, frame.Width, 0, 0);

        public static PixelRegion Crop(CapturedFrameEventArgs frame, BoundingBox box, float padding)
        {
            var padX = (int)MathF.Round(box.Width * padding);
            var padY = (int)MathF.Round(box.Height * padding);
            var left = Math.Clamp((int)MathF.Floor(box.Left) - padX, 0, frame.Width - 1);
            var top = Math.Clamp((int)MathF.Floor(box.Top) - padY, 0, frame.Height - 1);
            var right = Math.Clamp((int)MathF.Ceiling(box.Right) + padX, left + 1, frame.Width);
            var bottom = Math.Clamp((int)MathF.Ceiling(box.Bottom) + padY, top + 1, frame.Height);
            return new PixelRegion(frame.BgraPixels, right - left, bottom - top, frame.Width, left, top);
        }
    }
}
