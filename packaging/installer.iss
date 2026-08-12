#ifndef AppVersion
  #error AppVersion must be provided with /DAppVersion=x.y.z
#endif
#ifndef AppVariant
  #error AppVariant must be provided with /DAppVariant=CPU or DirectML
#endif
#ifndef OutputBaseName
  #error OutputBaseName must be provided with /DOutputBaseName=name
#endif
#ifndef SourceDir
  #error SourceDir must be provided with /DSourceDir=path
#endif
#ifndef OutputDir
  #error OutputDir must be provided with /DOutputDir=path
#endif

[Setup]
AppId={{B881EE62-C588-41D0-A661-83D3EA19DC19}
AppName=VRC-Fisher
AppVerName=VRC-Fisher {#AppVersion} ({#AppVariant})
AppVersion={#AppVersion}
AppPublisher=VRC-Fisher
DefaultDirName={src}\VRC-Fisher
DefaultGroupName=VRC-Fisher
DisableDirPage=no
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseName}
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\vrc-fisher.exe
VersionInfoVersion={#AppVersion}
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "downloadmodels"; Description: "Download the latest compatible ONNX models after installation"; GroupDescription: "Optional resources:"; Flags: unchecked

[InstallDelete]
Type: files; Name: "{app}\vrc-fisher.exe"
Type: filesandordirs; Name: "{app}\_internal"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\VRC-Fisher"; Filename: "{app}\vrc-fisher.exe"; WorkingDir: "{app}"
Name: "{group}\Model status"; Filename: "{app}\vrc-fisher.exe"; Parameters: "models status"; WorkingDir: "{app}"
Name: "{autodesktop}\VRC-Fisher"; Filename: "{app}\vrc-fisher.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\vrc-fisher.exe"; Parameters: "models install"; WorkingDir: "{app}"; Description: "Download the latest compatible ONNX models"; Tasks: downloadmodels; Flags: postinstall nowait skipifsilent unchecked

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
