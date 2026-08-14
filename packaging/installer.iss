#ifndef AppVersion
  #error AppVersion must be provided with /DAppVersion=x.y.z
#endif
#ifndef SourceDir
  #error SourceDir must be provided with /DSourceDir=path
#endif
#ifndef OutputDir
  #error OutputDir must be provided with /DOutputDir=path
#endif
#ifndef OutputBaseName
  #error OutputBaseName must be provided with /DOutputBaseName=name
#endif

[Setup]
AppId={{B881EE62-C588-41D0-A661-83D3EA19DC19}
AppName=VRC-Fisher
AppVerName=VRC-Fisher {#AppVersion}
AppVersion={#AppVersion}
AppPublisher=VRC-Fisher
DefaultDirName={code:DefaultInstallDir}
DefaultGroupName=VRC-Fisher
DisableDirPage=no
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousLanguage=yes
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseName}
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\program\VrcFisher.exe
VersionInfoVersion={#AppVersion}
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=VrcFisher.exe
RestartApplications=no

[Types]
Name: "full"; Description: "{cm:TypeRecommended}"
Name: "custom"; Description: "{cm:TypeCustom}"; Flags: iscustom

[Components]
Name: "directml"; Description: "{cm:ComponentDirectML}"; Types: full custom; Flags: exclusive
Name: "cpu"; Description: "{cm:ComponentCpu}"; Types: custom; Flags: exclusive

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "{#SourcePath}\languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:TaskDesktopIcon}"; GroupDescription: "{cm:TaskShortcuts}"; Flags: unchecked
Name: "downloadmodels"; Description: "{cm:TaskDownloadModels}"; GroupDescription: "{cm:TaskOptionalResources}"; Flags: unchecked

[CustomMessages]
english.TypeRecommended=Recommended DirectML installation
english.TypeCustom=Choose runtime component
english.ComponentDirectML=DirectML (NVIDIA, AMD or Intel GPU)
english.ComponentCpu=CPU-only
english.TaskDesktopIcon=Create a desktop shortcut
english.TaskShortcuts=Shortcuts:
english.TaskDownloadModels=Download compatible ONNX models after installation
english.TaskOptionalResources=Optional resources:
english.DirectoryNotEmpty=The selected directory contains files that do not belong to VRC-Fisher. Choose an empty directory or the existing VRC-Fisher installation directory.
english.DirectoryNotWritable=The selected directory is not writable by the current user. Choose another directory.
english.ModelDownloadFailed=The software was installed, but the models could not be downloaded. Open the Models page after installation to retry. Exit code: %1
chinesesimp.TypeRecommended=推荐的 DirectML 安装
chinesesimp.TypeCustom=选择运行组件
chinesesimp.ComponentDirectML=DirectML（NVIDIA、AMD 或 Intel GPU）
chinesesimp.ComponentCpu=仅 CPU
chinesesimp.TaskDesktopIcon=创建桌面快捷方式
chinesesimp.TaskShortcuts=快捷方式：
chinesesimp.TaskDownloadModels=安装后下载兼容的 ONNX 模型
chinesesimp.TaskOptionalResources=可选资源：
chinesesimp.DirectoryNotEmpty=所选目录中含有不属于 VRC-Fisher 的文件。请选择空目录或现有 VRC-Fisher 安装目录。
chinesesimp.DirectoryNotWritable=当前用户无法写入所选目录，请选择其他目录。
chinesesimp.ModelDownloadFailed=软件已安装，但模型下载失败。请在安装完成后打开“模型”页面重试。退出代码：%1

[InstallDelete]
Type: filesandordirs; Name: "{app}\program"
Type: filesandordirs; Name: "{app}\licenses"

[Files]
Source: "{#SourceDir}\release.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\USER_GUIDE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\cpu\*"; DestDir: "{app}\program"; Components: cpu; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\directml\*"; DestDir: "{app}\program"; Components: directml; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\VRC-Fisher"; Filename: "{app}\program\VrcFisher.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\VRC-Fisher"; Filename: "{app}\program\VrcFisher.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[UninstallDelete]
Type: filesandordirs; Name: "{app}\program"
Type: filesandordirs; Name: "{app}\config"
Type: filesandordirs; Name: "{app}\models"
Type: filesandordirs; Name: "{app}\downloads"
Type: filesandordirs; Name: "{app}\logs"
Type: filesandordirs; Name: "{app}\artifacts"
Type: files; Name: "{app}\release.json"
Type: files; Name: "{app}\USER_GUIDE.md"

[Code]
function DefaultInstallDir(Param: String): String;
begin
  { Runtime data is stored beside the program, so the default must be user-writable. }
  Result := ExpandConstant('{userdocs}\VRC-Fisher');
end;

function DirectoryHasEntries(DirectoryName: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(AddBackslash(DirectoryName) + '*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          Result := True;
          exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function DirectoryIsWritable(DirectoryName: String): Boolean;
var
  TestFile: String;
begin
  Result := ForceDirectories(DirectoryName);
  if not Result then exit;
  TestFile := AddBackslash(DirectoryName) + '.vrc-fisher-write-test';
  Result := SaveStringToFile(TestFile, 'test', False);
  if Result then DeleteFile(TestFile);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  InstallDirectory: String;
begin
  Result := True;
  if CurPageID <> wpSelectDir then exit;

  InstallDirectory := ExpandConstant('{app}');
  if DirExists(InstallDirectory) and
     DirectoryHasEntries(InstallDirectory) and
     (not FileExists(AddBackslash(InstallDirectory) + 'release.json') or
      not FileExists(AddBackslash(InstallDirectory) + 'program\VrcFisher.exe')) then
  begin
    MsgBox(CustomMessage('DirectoryNotEmpty'), mbError, MB_OK);
    Result := False;
    exit;
  end;

  if not DirectoryIsWritable(InstallDirectory) then
  begin
    MsgBox(CustomMessage('DirectoryNotWritable'), mbError, MB_OK);
    Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  LanguageCode: String;
  ResultCode: Integer;
  Executed: Boolean;
begin
  if CurStep <> ssPostInstall then exit;
  if ActiveLanguage = 'chinesesimp' then
    LanguageCode := 'zh-CN'
  else
    LanguageCode := 'en-US';
  ForceDirectories(ExpandConstant('{app}\config'));
  SaveStringToFile(ExpandConstant('{app}\config\installer-language.ini'), LanguageCode, False);

  if WizardIsTaskSelected('downloadmodels') and not WizardSilent then
  begin
    ResultCode := -1;
    Executed := Exec(
      ExpandConstant('{app}\program\VrcFisher.exe'),
      '--download-models --non-interactive',
      ExpandConstant('{app}'),
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode);
    if (not Executed) or (ResultCode <> 0) then
      MsgBox(FmtMessage(CustomMessage('ModelDownloadFailed'), [IntToStr(ResultCode)]), mbError, MB_OK);
  end;
end;
