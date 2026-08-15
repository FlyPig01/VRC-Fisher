using VrcFisher.Application;
using VrcFisher.Core;

namespace VrcFisher.Desktop.Contracts;

internal interface IDesktopPageContext
{
    IRuntimeController Runtime { get; }
    IModelCatalog Models { get; }
    ModelDownloadCoordinator ModelDownloads { get; }
    ICaptureTargetState Capture { get; }
    AppOptions Options { get; }
    string SoftwareRoot { get; }
    bool SupportsGpu { get; }
    Task<HardwareSnapshot> Hardware { get; }

    Task SaveOptionsAsync(AppOptions options);
    Task ChangeLanguageAsync(string language);
    Task ChangeDeviceAsync(ExecutionDevice device);
    Task ChangeHotkeyAsync(string hotkey);
    void OpenModelsFolder();
    void OpenSoftwareRoot();
}

internal interface ICaptureTargetState
{
    bool IsConfigured { get; }
    string TargetName { get; }
    event EventHandler? TargetChanged;
}
