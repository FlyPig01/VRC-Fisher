using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using VrcFisher.Core;

namespace VrcFisher.Desktop;

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

    public static string Provider(string provider) =>
        provider == "Unavailable" ? Get("Unavailable") : provider;

    public static string RuntimeStatus(RuntimeStatus status) => status.Code switch
    {
        RuntimeMessageCode.ModelsUnavailable => Get("RuntimeModelsUnavailable"),
        RuntimeMessageCode.ModelsRequired => Get("RuntimeModelsRequired"),
        RuntimeMessageCode.AutomaticNotAllowed => Get("RuntimeAutomaticNotAllowed"),
        RuntimeMessageCode.AutomaticStarted => Get("RuntimeAutomaticStarted"),
        RuntimeMessageCode.ObservationStarted => Get("RuntimeObservationStarted"),
        RuntimeMessageCode.DetectionStopped => Format("RuntimeDetectionStopped", status.Detail ?? Get("UnknownError")),
        RuntimeMessageCode.Stopped => Get("RuntimeStopped"),
        RuntimeMessageCode.CaptureStopped => Get("RuntimeCaptureStopped"),
        RuntimeMessageCode.FrameStale => Get("RuntimeFrameStale"),
        RuntimeMessageCode.TargetNotForeground => Get("RuntimeTargetNotForeground"),
        RuntimeMessageCode.OutputContractUnverified => Get("RuntimeOutputContractUnverified"),
        RuntimeMessageCode.InferenceFailed => Format("RuntimeInferenceFailed", status.Detail ?? Get("UnknownError")),
        RuntimeMessageCode.StateMachineDecision => StateDecision(status.Detail),
        _ => Get("UnknownStatus")
    };

    private static string StateDecision(string? reason) => reason switch
    {
        "cast" => Get("DecisionCast"),
        "bite confirmed" => Get("DecisionBiteConfirmed"),
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
