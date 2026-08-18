using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using VrcFisher.Core;

namespace VrcFisher.Desktop.Localization;

internal static class UiStrings
{
    private static readonly object Sync = new();
    private static ResourceManager? _manager;
    private static ResourceMap? _resources;
    private static ResourceContext? _context;
    private static CultureInfo _culture = CultureInfo.GetCultureInfo("en-US");

    public static void Configure(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = culture.Name;
        var manager = new ResourceManager();
        var context = manager.CreateResourceContext();
        context.QualifierValues["Language"] = culture.Name;
        var resources = manager.MainResourceMap.GetSubtree("Resources");

        lock (Sync)
        {
            _culture = culture;
            _manager = manager;
            _context = context;
            _resources = resources;
        }

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    public static string Get(string key)
    {
        lock (Sync)
        {
            if (_resources is null || _context is null)
                throw new InvalidOperationException("Application language resources are not initialized.");

            return _resources.GetValue(key, _context).ValueAsString;
        }
    }

    public static string Format(string key, params object[] values)
    {
        lock (Sync)
            return string.Format(_culture, Get(key), values);
    }

    public static string Phase(FishingPhase phase) => Get($"Phase{phase}");

    public static string OverlayStage(FishingPhase phase) => phase switch
    {
        FishingPhase.Idle or FishingPhase.Casting => Get("OverlayStageCasting"),
        FishingPhase.WaitingForBite => Get("OverlayStageWaitingForBite"),
        FishingPhase.Hooking => Get("PhaseHooking"),
        FishingPhase.Minigame => Get("OverlayStageFightingFish"),
        FishingPhase.Reeling or FishingPhase.Loot => Get("OverlayStageReeling"),
        FishingPhase.Recovery => Get("PhaseRecovery"),
        _ => Get("RuntimeStopped")
    };

    public static string Provider(string provider) =>
        provider == "Unavailable" ? Get("Unavailable") : provider;

    public static string Device(ExecutionDevice device) => device switch
    {
        ExecutionDevice.Cpu => Get("DeviceCpu"),
        ExecutionDevice.Gpu => Get("DeviceGpu"),
        _ => Get("DeviceAuto")
    };

    public static string RuntimeStatus(RuntimeStatus status) => status.Code switch
    {
        RuntimeMessageCode.VrChatNotRunning => Get("RuntimeVrChatNotRunning"),
        RuntimeMessageCode.StartTargetNotForeground => Get("RuntimeStartTargetNotForeground"),
        RuntimeMessageCode.HotkeyRegistrationFailed => Format("RuntimeHotkeyRegistrationFailed", status.Detail ?? "F8"),
        RuntimeMessageCode.OverlayUnavailable => Get("RuntimeOverlayUnavailable"),
        RuntimeMessageCode.UnexpectedFailure => Format("RuntimeUnexpectedFailure", status.Detail ?? Get("UnknownError")),
        RuntimeMessageCode.ModelsUnavailable => Get("RuntimeModelsUnavailable"),
        RuntimeMessageCode.ModelsRequired => Get("RuntimeModelsRequired"),
        RuntimeMessageCode.AutomaticNotAllowed => Get("RuntimeAutomaticNotAllowed"),
        RuntimeMessageCode.Starting => Get("RuntimeStarting"),
        RuntimeMessageCode.AutomaticStarted => Get("RuntimeAutomaticStarted"),
        RuntimeMessageCode.Stopping => Get("RuntimeStopping"),
        RuntimeMessageCode.DetectionStopped => Format("RuntimeDetectionStopped", status.Detail ?? Get("UnknownError")),
        RuntimeMessageCode.Stopped => Get("RuntimeStopped"),
        RuntimeMessageCode.CaptureStopped => Get("RuntimeCaptureStopped"),
        RuntimeMessageCode.FrameStale => Get("RuntimeFrameStale"),
        RuntimeMessageCode.TargetNotForeground => Get("RuntimeTargetStopped"),
        RuntimeMessageCode.OutputContractUnverified => Get("RuntimeOutputContractUnverified"),
        RuntimeMessageCode.InferenceFailed => Format("RuntimeInferenceFailed", status.Detail ?? Get("UnknownError")),
        RuntimeMessageCode.InputFailed => Format("RuntimeUnexpectedFailure", status.Detail ?? Get("UnknownError")),
        RuntimeMessageCode.StateMachineDecision => StateDecision(status.Detail),
        _ => Get("UnknownStatus")
    };

    public static UiRuntimeNotice? RuntimeNotice(RuntimeStatus status) => status.Code switch
    {
        RuntimeMessageCode.VrChatNotRunning => ErrorNotice("AlertVrChatNotRunningTitle", "AlertVrChatNotRunningMessage"),
        RuntimeMessageCode.StartTargetNotForeground => ErrorNotice("AlertVrChatBackgroundTitle", "AlertVrChatBackgroundMessage"),
        RuntimeMessageCode.HotkeyRegistrationFailed => new UiRuntimeNotice(
            Get("AlertHotkeyFailedTitle"),
            Format("AlertHotkeyFailedMessage", status.Detail ?? "F8"),
            UiNoticeSeverity.Error),
        RuntimeMessageCode.OverlayUnavailable =>
            ErrorNotice("AlertOverlayFailedTitle", "AlertOverlayFailedMessage"),
        RuntimeMessageCode.UnexpectedFailure => new UiRuntimeNotice(
            Get("AlertUnexpectedFailureTitle"),
            Format("AlertUnexpectedFailureMessage", status.Detail ?? Get("UnknownError")),
            UiNoticeSeverity.Error),
        RuntimeMessageCode.ModelsRequired or RuntimeMessageCode.ModelsUnavailable =>
            ErrorNotice("AlertModelsMissingTitle", "AlertModelsMissingMessage"),
        RuntimeMessageCode.AutomaticNotAllowed =>
            ErrorNotice("AlertModelsUnapprovedTitle", "AlertModelsUnapprovedMessage"),
        RuntimeMessageCode.DetectionStopped => new UiRuntimeNotice(
            Get("AlertDetectionStoppedTitle"),
            Format("AlertDetectionStoppedMessage", status.Detail ?? Get("UnknownError")),
            UiNoticeSeverity.Error),
        RuntimeMessageCode.CaptureStopped => ErrorNotice("AlertCaptureStoppedTitle", "AlertCaptureStoppedMessage"),
        RuntimeMessageCode.FrameStale => ErrorNotice("AlertFrameStaleTitle", "AlertFrameStaleMessage"),
        RuntimeMessageCode.TargetNotForeground => WarningNotice("AlertTargetStoppedTitle", "AlertTargetStoppedMessage"),
        RuntimeMessageCode.OutputContractUnverified =>
            ErrorNotice("AlertModelContractTitle", "AlertModelContractMessage"),
        RuntimeMessageCode.InferenceFailed => new UiRuntimeNotice(
            Get("AlertInferenceFailedTitle"),
            Format("AlertInferenceFailedMessage", status.Detail ?? Get("UnknownError")),
            UiNoticeSeverity.Error),
        RuntimeMessageCode.InputFailed => new UiRuntimeNotice(
            Get("AlertUnexpectedFailureTitle"),
            Format("AlertUnexpectedFailureMessage", status.Detail ?? Get("UnknownError")),
            UiNoticeSeverity.Error),
        _ => null
    };

    private static UiRuntimeNotice ErrorNotice(string titleKey, string messageKey) =>
        new(Get(titleKey), Get(messageKey), UiNoticeSeverity.Error);

    private static UiRuntimeNotice WarningNotice(string titleKey, string messageKey) =>
        new(Get(titleKey), Get(messageKey), UiNoticeSeverity.Warning);

    private static string StateDecision(string? reason) => reason switch
    {
        "cast" => Get("DecisionCast"),
        "bite confirmed" => Get("DecisionBiteConfirmed"),
        "bite fallback" => Get("DecisionBiteFallback"),
        "bite fallback recovery" => Get("DecisionFailureDetected"),
        "bite timeout" => Get("DecisionBiteTimeout"),
        "minigame confirmed" => Get("DecisionMinigameConfirmed"),
        "minigame did not start" => Get("DecisionMinigameDidNotStart"),
        "success confirmed" => Get("DecisionSuccessConfirmed"),
        "minigame ended" => Get("DecisionMinigameEnded"),
        "minigame timeout" => Get("DecisionMinigameTimeout"),
        "follow target" => Get("DecisionFollowTarget"),
        "reel and collect" => Get("DecisionReelAndCollect"),
        "next cycle" => Get("DecisionNextCycle"),
        "loot timeout" => Get("DecisionLootTimeout"),
        "recovery complete" => Get("DecisionRecoveryComplete"),
        "failure detected" => Get("DecisionFailureDetected"),
        "stop requested" => Get("DecisionStopRequested"),
        "waiting" => Get("DecisionWaiting"),
        _ => Get("UnknownStatus")
    };
}

internal enum UiNoticeSeverity
{
    Warning,
    Error
}

internal sealed record UiRuntimeNotice(string Title, string Message, UiNoticeSeverity Severity);
