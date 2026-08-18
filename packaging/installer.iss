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
LanguageDetectionMethod=uilanguage
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseName}
SetupIconFile={#SourcePath}\..\assets\branding\VRC-Fisher.ico
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

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "{#SourcePath}\languages\ChineseSimplified.isl"
Name: "chinesetrad"; MessagesFile: "{#SourcePath}\languages\ChineseTraditional.isl"
Name: "japanese"; MessagesFile: "{#SourcePath}\languages\Japanese.isl"
Name: "korean"; MessagesFile: "{#SourcePath}\languages\Korean.isl"
Name: "spanish"; MessagesFile: "{#SourcePath}\languages\Spanish.isl"
Name: "french"; MessagesFile: "{#SourcePath}\languages\French.isl"
Name: "german"; MessagesFile: "{#SourcePath}\languages\German.isl"
Name: "brazilianportuguese"; MessagesFile: "{#SourcePath}\languages\BrazilianPortuguese.isl"
Name: "russian"; MessagesFile: "{#SourcePath}\languages\Russian.isl"
Name: "italian"; MessagesFile: "{#SourcePath}\languages\Italian.isl"
Name: "polish"; MessagesFile: "{#SourcePath}\languages\Polish.isl"
Name: "turkish"; MessagesFile: "{#SourcePath}\languages\Turkish.isl"
Name: "dutch"; MessagesFile: "{#SourcePath}\languages\Dutch.isl"
Name: "czech"; MessagesFile: "{#SourcePath}\languages\Czech.isl"
Name: "hungarian"; MessagesFile: "{#SourcePath}\languages\Hungarian.isl"
Name: "ukrainian"; MessagesFile: "{#SourcePath}\languages\Ukrainian.isl"
Name: "thai"; MessagesFile: "{#SourcePath}\languages\Thai.isl"
Name: "swedish"; MessagesFile: "{#SourcePath}\languages\Swedish.isl"
Name: "finnish"; MessagesFile: "{#SourcePath}\languages\Finnish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:TaskDesktopIcon}"; GroupDescription: "{cm:TaskShortcuts}"; Flags: unchecked

[CustomMessages]
english.TaskDesktopIcon=Create a desktop shortcut
english.TaskShortcuts=Shortcuts:
english.DirectoryNotEmpty=The selected directory contains files that do not belong to VRC-Fisher. Choose an empty directory or the existing VRC-Fisher installation directory.
english.DirectoryNotWritable=The selected directory is not writable by the current user. Choose another directory.
chinesesimp.TaskDesktopIcon=创建桌面快捷方式
chinesesimp.TaskShortcuts=快捷方式：
chinesesimp.DirectoryNotEmpty=所选目录中含有不属于 VRC-Fisher 的文件。请选择空目录或现有 VRC-Fisher 安装目录。
chinesesimp.DirectoryNotWritable=当前用户无法写入所选目录，请选择其他目录。
chinesetrad.TaskDesktopIcon=建立桌面捷徑
chinesetrad.TaskShortcuts=捷徑：
chinesetrad.DirectoryNotEmpty=選取的目錄包含不屬於 VRC-Fisher 的檔案。請選擇空目錄或現有的 VRC-Fisher 安裝目錄。
chinesetrad.DirectoryNotWritable=目前使用者無法寫入選取的目錄。請選擇其他目錄。
japanese.TaskDesktopIcon=デスクトップ ショートカットを作成
japanese.TaskShortcuts=ショートカット:
japanese.DirectoryNotEmpty=選択したディレクトリには VRC-Fisher に属さないファイルが含まれています。空のディレクトリ、または既存の VRC-Fisher インストール ディレクトリを選択してください。
japanese.DirectoryNotWritable=選択したディレクトリは現在のユーザーが書き込めません。別のディレクトリを選択してください。
korean.TaskDesktopIcon=바탕 화면 바로 가기 만들기
korean.TaskShortcuts=바로 가기:
korean.DirectoryNotEmpty=선택한 디렉터리에 VRC-Fisher에 속하지 않는 파일이 있습니다. 빈 디렉터리 또는 기존 VRC-Fisher 설치 디렉터리를 선택하세요.
korean.DirectoryNotWritable=선택한 디렉터리는 현재 사용자가 쓸 수 없습니다. 다른 디렉터리를 선택하세요.
spanish.TaskDesktopIcon=Crear un acceso directo en el escritorio
spanish.TaskShortcuts=Accesos directos:
spanish.DirectoryNotEmpty=El directorio seleccionado contiene archivos que no pertenecen a VRC-Fisher. Elija un directorio vacío o el directorio de instalación existente de VRC-Fisher.
spanish.DirectoryNotWritable=El directorio seleccionado no es escribible por el usuario actual. Elija otro directorio.
french.TaskDesktopIcon=Créer un raccourci sur le bureau
french.TaskShortcuts=Raccourcis :
french.DirectoryNotEmpty=Le dossier sélectionné contient des fichiers qui n'appartiennent pas à VRC-Fisher. Choisissez un dossier vide ou le dossier d'installation existant de VRC-Fisher.
french.DirectoryNotWritable=Le dossier sélectionné n'est pas accessible en écriture pour l'utilisateur actuel. Choisissez un autre dossier.
german.TaskDesktopIcon=Desktopverknüpfung erstellen
german.TaskShortcuts=Verknüpfungen:
german.DirectoryNotEmpty=Das ausgewählte Verzeichnis enthält Dateien, die nicht zu VRC-Fisher gehören. Wählen Sie ein leeres Verzeichnis oder das vorhandene VRC-Fisher-Installationsverzeichnis.
german.DirectoryNotWritable=Das ausgewählte Verzeichnis ist für den aktuellen Benutzer nicht schreibbar. Wählen Sie ein anderes Verzeichnis.
brazilianportuguese.TaskDesktopIcon=Criar atalho na área de trabalho
brazilianportuguese.TaskShortcuts=Atalhos:
brazilianportuguese.DirectoryNotEmpty=O diretório selecionado contém arquivos que não pertencem ao VRC-Fisher. Escolha um diretório vazio ou o diretório de instalação existente do VRC-Fisher.
brazilianportuguese.DirectoryNotWritable=O diretório selecionado não é gravável pelo usuário atual. Escolha outro diretório.
russian.TaskDesktopIcon=Создать ярлык на рабочем столе
russian.TaskShortcuts=Ярлыки:
russian.DirectoryNotEmpty=Выбранный каталог содержит файлы, не относящиеся к VRC-Fisher. Выберите пустой каталог или существующий каталог установки VRC-Fisher.
russian.DirectoryNotWritable=Выбранный каталог недоступен для записи текущим пользователем. Выберите другой каталог.
italian.TaskDesktopIcon=Crea un collegamento sul desktop
italian.TaskShortcuts=Collegamenti:
italian.DirectoryNotEmpty=La directory selezionata contiene file che non appartengono a VRC-Fisher. Scegli una directory vuota o la directory di installazione esistente di VRC-Fisher.
italian.DirectoryNotWritable=La directory selezionata non è scrivibile dall'utente corrente. Scegli un'altra directory.
polish.TaskDesktopIcon=Utwórz skrót na pulpicie
polish.TaskShortcuts=Skróty:
polish.DirectoryNotEmpty=Wybrany katalog zawiera pliki, które nie należą do VRC-Fisher. Wybierz pusty katalog lub istniejący katalog instalacyjny VRC-Fisher.
polish.DirectoryNotWritable=Wybrany katalog nie jest zapisywalny przez bieżącego użytkownika. Wybierz inny katalog.
turkish.TaskDesktopIcon=Masaüstü kısayolu oluştur
turkish.TaskShortcuts=Kısayollar:
turkish.DirectoryNotEmpty=Seçilen dizin VRC-Fisher'a ait olmayan dosyalar içeriyor. Boş bir dizin veya mevcut VRC-Fisher kurulum dizinini seçin.
turkish.DirectoryNotWritable=Seçilen dizin geçerli kullanıcı tarafından yazılabilir değil. Başka bir dizin seçin.
dutch.TaskDesktopIcon=Bureaubladsnelkoppeling maken
dutch.TaskShortcuts=Snelkoppelingen:
dutch.DirectoryNotEmpty=De geselecteerde map bevat bestanden die niet bij VRC-Fisher horen. Kies een lege map of de bestaande VRC-Fisher-installatiemap.
dutch.DirectoryNotWritable=De geselecteerde map is niet schrijfbaar door de huidige gebruiker. Kies een andere map.
czech.TaskDesktopIcon=Vytvořit zástupce na ploše
czech.TaskShortcuts=Zástupci:
czech.DirectoryNotEmpty=Vybraná složka obsahuje soubory, které nepatří do VRC-Fisher. Vyberte prázdnou složku nebo existující instalační složku VRC-Fisher.
czech.DirectoryNotWritable=Vybraná složka není pro aktuálního uživatele zapisovatelná. Vyberte jinou složku.
hungarian.TaskDesktopIcon=Asztali parancsikon létrehozása
hungarian.TaskShortcuts=Parancsikonok:
hungarian.DirectoryNotEmpty=A kiválasztott mappa olyan fájlokat tartalmaz, amelyek nem a VRC-Fisherhez tartoznak. Válasszon üres mappát vagy a meglévő VRC-Fisher telepítési mappát.
hungarian.DirectoryNotWritable=A kiválasztott mappába az aktuális felhasználó nem írhat. Válasszon másik mappát.
ukrainian.TaskDesktopIcon=Створити ярлик на робочому столі
ukrainian.TaskShortcuts=Ярлики:
ukrainian.DirectoryNotEmpty=Вибрана папка містить файли, які не належать VRC-Fisher. Виберіть порожню папку або наявну папку встановлення VRC-Fisher.
ukrainian.DirectoryNotWritable=Вибрана папка недоступна для запису поточному користувачеві. Виберіть іншу папку.
thai.TaskDesktopIcon=สร้างทางลัดบนเดสก์ท็อป
thai.TaskShortcuts=ทางลัด:
thai.DirectoryNotEmpty=ไดเรกทอรีที่เลือกมีไฟล์ที่ไม่ใช่ของ VRC-Fisher กรุณาเลือกไดเรกทอรีที่ว่างหรือไดเรกทอรีการติดตั้ง VRC-Fisher ที่มีอยู่
thai.DirectoryNotWritable=ไดเรกทอรีที่เลือกไม่สามารถเขียนได้โดยผู้ใช้ปัจจุบัน กรุณาเลือกไดเรกทอรีอื่น
swedish.TaskDesktopIcon=Skapa en genväg på skrivbordet
swedish.TaskShortcuts=Genvägar:
swedish.DirectoryNotEmpty=Den valda mappen innehåller filer som inte tillhör VRC-Fisher. Välj en tom mapp eller den befintliga installationsmappen för VRC-Fisher.
swedish.DirectoryNotWritable=Den valda mappen är inte skrivbar av den aktuella användaren. Välj en annan mapp.
finnish.TaskDesktopIcon=Luo työpöydän pikakuvake
finnish.TaskShortcuts=Pikakuvakkeet:
finnish.DirectoryNotEmpty=Valittu kansio sisältää tiedostoja, jotka eivät kuulu VRC-Fisherille. Valitse tyhjä kansio tai olemassa oleva VRC-Fisherin asennuskansio.
finnish.DirectoryNotWritable=Nykyinen käyttäjä ei voi kirjoittaa valittuun kansioon. Valitse toinen kansio.

[InstallDelete]
Type: filesandordirs; Name: "{app}\program"
Type: filesandordirs; Name: "{app}\licenses"

[Files]
Source: "{#SourceDir}\release.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\USER_GUIDE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\program\*"; DestDir: "{app}\program"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\VRC-Fisher"; Filename: "{app}\program\VrcFisher.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\VRC-Fisher"; Filename: "{app}\program\VrcFisher.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[UninstallDelete]
Type: filesandordirs; Name: "{app}\program"
Type: filesandordirs; Name: "{app}\config"
Type: filesandordirs; Name: "{app}\models"
Type: filesandordirs; Name: "{app}\downloads"
Type: filesandordirs; Name: "{app}\logs"
Type: filesandordirs; Name: "{app}\licenses"
Type: files; Name: "{app}\release.json"
Type: files; Name: "{app}\USER_GUIDE.md"
Type: files; Name: "{app}\LICENSE"
Type: files; Name: "{app}\THIRD_PARTY_NOTICES.md"
Type: dirifempty; Name: "{app}"

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

function ApplicationLanguageCode(): String;
begin
  if ActiveLanguage = 'chinesesimp' then Result := 'zh-CN'
  else if ActiveLanguage = 'chinesetrad' then Result := 'zh-TW'
  else if ActiveLanguage = 'japanese' then Result := 'ja-JP'
  else if ActiveLanguage = 'korean' then Result := 'ko-KR'
  else if ActiveLanguage = 'spanish' then Result := 'es-ES'
  else if ActiveLanguage = 'french' then Result := 'fr-FR'
  else if ActiveLanguage = 'german' then Result := 'de-DE'
  else if ActiveLanguage = 'brazilianportuguese' then Result := 'pt-BR'
  else if ActiveLanguage = 'russian' then Result := 'ru-RU'
  else if ActiveLanguage = 'italian' then Result := 'it-IT'
  else if ActiveLanguage = 'polish' then Result := 'pl-PL'
  else if ActiveLanguage = 'turkish' then Result := 'tr-TR'
  else if ActiveLanguage = 'dutch' then Result := 'nl-NL'
  else if ActiveLanguage = 'czech' then Result := 'cs-CZ'
  else if ActiveLanguage = 'hungarian' then Result := 'hu-HU'
  else if ActiveLanguage = 'ukrainian' then Result := 'uk-UA'
  else if ActiveLanguage = 'thai' then Result := 'th-TH'
  else if ActiveLanguage = 'swedish' then Result := 'sv-SE'
  else if ActiveLanguage = 'finnish' then Result := 'fi-FI'
  else Result := 'en-US';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  LanguageCode: String;
begin
  if CurStep <> ssPostInstall then exit;
  LanguageCode := ApplicationLanguageCode();
  ForceDirectories(ExpandConstant('{app}\config'));
  SaveStringToFile(ExpandConstant('{app}\config\installer-language.ini'), LanguageCode, False);
end;
