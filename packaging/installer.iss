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
; This reserve covers the two ONNX files and model metadata downloaded by the optional task.
ExtraDiskSpaceRequired=25165824
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
Name: "downloadmodels"; Description: "{cm:TaskDownloadModels}"; GroupDescription: "{cm:TaskOptionalResources}"; Flags: unchecked

[CustomMessages]
english.TaskDesktopIcon=Create a desktop shortcut
english.TaskShortcuts=Shortcuts:
english.TaskDownloadModels=Download models after installation
english.TaskOptionalResources=Optional resources:
english.ModelDownloadInProgress=Downloading models. Slow connections may take several minutes; please keep this window open.
english.DirectoryNotEmpty=The selected directory contains files that do not belong to VRC-Fisher. Choose an empty directory or the existing VRC-Fisher installation directory.
english.DirectoryNotWritable=The selected directory is not writable by the current user. Choose another directory.
english.ModelDownloadFailed=The software was installed, but the models could not be downloaded. Open the Models page after installation to retry. Exit code: %1
chinesesimp.TaskDesktopIcon=创建桌面快捷方式
chinesesimp.TaskShortcuts=快捷方式：
chinesesimp.TaskDownloadModels=安装后下载模型
chinesesimp.TaskOptionalResources=可选资源：
chinesesimp.ModelDownloadInProgress=正在下载模型。网络较慢时可能需要几分钟，请保持此窗口打开。
chinesesimp.DirectoryNotEmpty=所选目录中含有不属于 VRC-Fisher 的文件。请选择空目录或现有 VRC-Fisher 安装目录。
chinesesimp.DirectoryNotWritable=当前用户无法写入所选目录，请选择其他目录。
chinesesimp.ModelDownloadFailed=软件已安装，但模型下载失败。请在安装完成后打开“模型”页面重试。退出代码：%1
chinesetrad.TaskDesktopIcon=建立桌面捷徑
chinesetrad.TaskShortcuts=捷徑：
chinesetrad.TaskDownloadModels=安裝後下載模型
chinesetrad.TaskOptionalResources=選用資源：
chinesetrad.ModelDownloadInProgress=正在下載模型。網路較慢時可能需要幾分鐘，請保持此視窗開啟。
chinesetrad.DirectoryNotEmpty=選取的目錄包含不屬於 VRC-Fisher 的檔案。請選擇空目錄或現有的 VRC-Fisher 安裝目錄。
chinesetrad.DirectoryNotWritable=目前使用者無法寫入選取的目錄。請選擇其他目錄。
chinesetrad.ModelDownloadFailed=軟體已安裝，但無法下載模型。請在安裝後開啟模型頁面重試。結束代碼：%1
japanese.TaskDesktopIcon=デスクトップ ショートカットを作成
japanese.TaskShortcuts=ショートカット:
japanese.TaskDownloadModels=インストール後にモデルをダウンロード
japanese.TaskOptionalResources=オプション リソース:
japanese.ModelDownloadInProgress=モデルをダウンロードしています。接続が遅い場合は数分かかることがあります。このウィンドウを閉じないでください。
japanese.DirectoryNotEmpty=選択したディレクトリには VRC-Fisher に属さないファイルが含まれています。空のディレクトリ、または既存の VRC-Fisher インストール ディレクトリを選択してください。
japanese.DirectoryNotWritable=選択したディレクトリは現在のユーザーが書き込めません。別のディレクトリを選択してください。
japanese.ModelDownloadFailed=ソフトウェアはインストールされましたが、モデルをダウンロードできませんでした。インストール後にモデル ページを開いて再試行してください。終了コード: %1
korean.TaskDesktopIcon=바탕 화면 바로 가기 만들기
korean.TaskShortcuts=바로 가기:
korean.TaskDownloadModels=설치 후 모델 다운로드
korean.TaskOptionalResources=선택 리소스:
korean.ModelDownloadInProgress=모델을 다운로드하는 중입니다. 연결이 느리면 몇 분 정도 걸릴 수 있으니 이 창을 닫지 마세요.
korean.DirectoryNotEmpty=선택한 디렉터리에 VRC-Fisher에 속하지 않는 파일이 있습니다. 빈 디렉터리 또는 기존 VRC-Fisher 설치 디렉터리를 선택하세요.
korean.DirectoryNotWritable=선택한 디렉터리는 현재 사용자가 쓸 수 없습니다. 다른 디렉터리를 선택하세요.
korean.ModelDownloadFailed=소프트웨어가 설치되었지만 모델을 다운로드할 수 없습니다. 설치 후 모델 페이지를 열어 다시 시도하세요. 종료 코드: %1
spanish.TaskDesktopIcon=Crear un acceso directo en el escritorio
spanish.TaskShortcuts=Accesos directos:
spanish.TaskDownloadModels=Descargar modelos después de instalar
spanish.TaskOptionalResources=Recursos opcionales:
spanish.ModelDownloadInProgress=Descargando modelos. Las conexiones lentas pueden tardar varios minutos; mantenga esta ventana abierta.
spanish.DirectoryNotEmpty=El directorio seleccionado contiene archivos que no pertenecen a VRC-Fisher. Elija un directorio vacío o el directorio de instalación existente de VRC-Fisher.
spanish.DirectoryNotWritable=El directorio seleccionado no es escribible por el usuario actual. Elija otro directorio.
spanish.ModelDownloadFailed=El software se instaló, pero no se pudieron descargar los modelos. Abra la página de Modelos después de la instalación para reintentarlo. Código de salida: %1
french.TaskDesktopIcon=Créer un raccourci sur le bureau
french.TaskShortcuts=Raccourcis :
french.TaskDownloadModels=Télécharger les modèles après l'installation
french.TaskOptionalResources=Ressources facultatives :
french.ModelDownloadInProgress=Téléchargement des modèles. Une connexion lente peut prendre plusieurs minutes ; gardez cette fenêtre ouverte.
french.DirectoryNotEmpty=Le dossier sélectionné contient des fichiers qui n'appartiennent pas à VRC-Fisher. Choisissez un dossier vide ou le dossier d'installation existant de VRC-Fisher.
french.DirectoryNotWritable=Le dossier sélectionné n'est pas accessible en écriture pour l'utilisateur actuel. Choisissez un autre dossier.
french.ModelDownloadFailed=Le logiciel a été installé, mais les modèles n'ont pas pu être téléchargés. Ouvrez la page Modèles après l'installation pour réessayer. Code de sortie : %1
german.TaskDesktopIcon=Desktopverknüpfung erstellen
german.TaskShortcuts=Verknüpfungen:
german.TaskDownloadModels=Modelle nach der Installation herunterladen
german.TaskOptionalResources=Optionale Ressourcen:
german.ModelDownloadInProgress=Modelle werden heruntergeladen. Bei einer langsamen Verbindung kann dies mehrere Minuten dauern. Lassen Sie dieses Fenster geöffnet.
german.DirectoryNotEmpty=Das ausgewählte Verzeichnis enthält Dateien, die nicht zu VRC-Fisher gehören. Wählen Sie ein leeres Verzeichnis oder das vorhandene VRC-Fisher-Installationsverzeichnis.
german.DirectoryNotWritable=Das ausgewählte Verzeichnis ist für den aktuellen Benutzer nicht schreibbar. Wählen Sie ein anderes Verzeichnis.
german.ModelDownloadFailed=Die Software wurde installiert, aber die Modelle konnten nicht heruntergeladen werden. Öffnen Sie nach der Installation die Seite „Modelle“, um es erneut zu versuchen. Exitcode: %1
brazilianportuguese.TaskDesktopIcon=Criar atalho na área de trabalho
brazilianportuguese.TaskShortcuts=Atalhos:
brazilianportuguese.TaskDownloadModels=Baixar modelos após a instalação
brazilianportuguese.TaskOptionalResources=Recursos opcionais:
brazilianportuguese.ModelDownloadInProgress=Baixando modelos. Conexões lentas podem levar vários minutos; mantenha esta janela aberta.
brazilianportuguese.DirectoryNotEmpty=O diretório selecionado contém arquivos que não pertencem ao VRC-Fisher. Escolha um diretório vazio ou o diretório de instalação existente do VRC-Fisher.
brazilianportuguese.DirectoryNotWritable=O diretório selecionado não é gravável pelo usuário atual. Escolha outro diretório.
brazilianportuguese.ModelDownloadFailed=O software foi instalado, mas os modelos não puderam ser baixados. Abra a página de Modelos após a instalação para tentar novamente. Código de saída: %1
russian.TaskDesktopIcon=Создать ярлык на рабочем столе
russian.TaskShortcuts=Ярлыки:
russian.TaskDownloadModels=Скачать модели после установки
russian.TaskOptionalResources=Необязательные ресурсы:
russian.ModelDownloadInProgress=Загрузка моделей. При медленном соединении это может занять несколько минут. Не закрывайте это окно.
russian.DirectoryNotEmpty=Выбранный каталог содержит файлы, не относящиеся к VRC-Fisher. Выберите пустой каталог или существующий каталог установки VRC-Fisher.
russian.DirectoryNotWritable=Выбранный каталог недоступен для записи текущим пользователем. Выберите другой каталог.
russian.ModelDownloadFailed=Программа была установлена, но не удалось загрузить модели. Откройте страницу «Модели» после установки, чтобы повторить попытку. Код выхода: %1
italian.TaskDesktopIcon=Crea un collegamento sul desktop
italian.TaskShortcuts=Collegamenti:
italian.TaskDownloadModels=Scarica i modelli dopo l'installazione
italian.TaskOptionalResources=Risorse facoltative:
italian.ModelDownloadInProgress=Download dei modelli in corso. Con una connessione lenta possono servire alcuni minuti; non chiudere questa finestra.
italian.DirectoryNotEmpty=La directory selezionata contiene file che non appartengono a VRC-Fisher. Scegli una directory vuota o la directory di installazione esistente di VRC-Fisher.
italian.DirectoryNotWritable=La directory selezionata non è scrivibile dall'utente corrente. Scegli un'altra directory.
italian.ModelDownloadFailed=Il software è stato installato, ma non è stato possibile scaricare i modelli. Apri la pagina Modelli dopo l'installazione per riprovare. Codice di uscita: %1
polish.TaskDesktopIcon=Utwórz skrót na pulpicie
polish.TaskShortcuts=Skróty:
polish.TaskDownloadModels=Pobierz modele po instalacji
polish.TaskOptionalResources=Opcjonalne zasoby:
polish.ModelDownloadInProgress=Pobieranie modeli. Przy wolnym połączeniu może potrwać kilka minut; pozostaw to okno otwarte.
polish.DirectoryNotEmpty=Wybrany katalog zawiera pliki, które nie należą do VRC-Fisher. Wybierz pusty katalog lub istniejący katalog instalacyjny VRC-Fisher.
polish.DirectoryNotWritable=Wybrany katalog nie jest zapisywalny przez bieżącego użytkownika. Wybierz inny katalog.
polish.ModelDownloadFailed=Oprogramowanie zostało zainstalowane, ale nie można było pobrać modeli. Otwórz stronę Modele po instalacji, aby spróbować ponownie. Kod wyjścia: %1
turkish.TaskDesktopIcon=Masaüstü kısayolu oluştur
turkish.TaskShortcuts=Kısayollar:
turkish.TaskDownloadModels=Kurulumdan sonra modelleri indir
turkish.TaskOptionalResources=İsteğe bağlı kaynaklar:
turkish.ModelDownloadInProgress=Modeller indiriliyor. Yavaş bağlantılarda işlem birkaç dakika sürebilir; bu pencereyi açık tutun.
turkish.DirectoryNotEmpty=Seçilen dizin VRC-Fisher'a ait olmayan dosyalar içeriyor. Boş bir dizin veya mevcut VRC-Fisher kurulum dizinini seçin.
turkish.DirectoryNotWritable=Seçilen dizin geçerli kullanıcı tarafından yazılabilir değil. Başka bir dizin seçin.
turkish.ModelDownloadFailed=Yazılım kuruldu, ancak modeller indirilemedi. Kurulumdan sonra tekrar denemek için Modeller sayfasını açın. Çıkış kodu: %1
dutch.TaskDesktopIcon=Bureaubladsnelkoppeling maken
dutch.TaskShortcuts=Snelkoppelingen:
dutch.TaskDownloadModels=Modellen downloaden na installatie
dutch.TaskOptionalResources=Optionele bronnen:
dutch.ModelDownloadInProgress=Modellen worden gedownload. Een trage verbinding kan enkele minuten duren; laat dit venster open.
dutch.DirectoryNotEmpty=De geselecteerde map bevat bestanden die niet bij VRC-Fisher horen. Kies een lege map of de bestaande VRC-Fisher-installatiemap.
dutch.DirectoryNotWritable=De geselecteerde map is niet schrijfbaar door de huidige gebruiker. Kies een andere map.
dutch.ModelDownloadFailed=De software is geïnstalleerd, maar de modellen konden niet worden gedownload. Open na installatie de pagina Modellen om het opnieuw te proberen. Exitcode: %1
czech.TaskDesktopIcon=Vytvořit zástupce na ploše
czech.TaskShortcuts=Zástupci:
czech.TaskDownloadModels=Stáhnout modely po instalaci
czech.TaskOptionalResources=Volitelné zdroje:
czech.ModelDownloadInProgress=Probíhá stahování modelů. Pomalé připojení může trvat několik minut; ponechte toto okno otevřené.
czech.DirectoryNotEmpty=Vybraná složka obsahuje soubory, které nepatří do VRC-Fisher. Vyberte prázdnou složku nebo existující instalační složku VRC-Fisher.
czech.DirectoryNotWritable=Vybraná složka není pro aktuálního uživatele zapisovatelná. Vyberte jinou složku.
czech.ModelDownloadFailed=Software byl nainstalován, ale modely se nepodařilo stáhnout. Po instalaci otevřete stránku modelů a zkuste to znovu. Kód ukončení: %1
hungarian.TaskDesktopIcon=Asztali parancsikon létrehozása
hungarian.TaskShortcuts=Parancsikonok:
hungarian.TaskDownloadModels=Modellek letöltése telepítés után
hungarian.TaskOptionalResources=Opcionális erőforrások:
hungarian.ModelDownloadInProgress=Modellek letöltése folyamatban. Lassú kapcsolat esetén több percig tarthat; hagyja nyitva ezt az ablakot.
hungarian.DirectoryNotEmpty=A kiválasztott mappa olyan fájlokat tartalmaz, amelyek nem a VRC-Fisherhez tartoznak. Válasszon üres mappát vagy a meglévő VRC-Fisher telepítési mappát.
hungarian.DirectoryNotWritable=A kiválasztott mappába az aktuális felhasználó nem írhat. Válasszon másik mappát.
hungarian.ModelDownloadFailed=A szoftver telepítve lett, de a modelleket nem sikerült letölteni. A telepítés után nyissa meg a Modellek oldalt az újrapróbálkozáshoz. Kilépési kód: %1
ukrainian.TaskDesktopIcon=Створити ярлик на робочому столі
ukrainian.TaskShortcuts=Ярлики:
ukrainian.TaskDownloadModels=Завантажити моделі після встановлення
ukrainian.TaskOptionalResources=Додаткові ресурси:
ukrainian.ModelDownloadInProgress=Завантаження моделей. За повільного з’єднання це може тривати кілька хвилин. Не закривайте це вікно.
ukrainian.DirectoryNotEmpty=Вибрана папка містить файли, які не належать VRC-Fisher. Виберіть порожню папку або наявну папку встановлення VRC-Fisher.
ukrainian.DirectoryNotWritable=Вибрана папка недоступна для запису поточному користувачеві. Виберіть іншу папку.
ukrainian.ModelDownloadFailed=Програмне забезпечення встановлено, але моделі не вдалося завантажити. Після встановлення відкрийте сторінку моделей, щоб повторити спробу. Код завершення: %1
thai.TaskDesktopIcon=สร้างทางลัดบนเดสก์ท็อป
thai.TaskShortcuts=ทางลัด:
thai.TaskDownloadModels=ดาวน์โหลดโมเดลหลังการติดตั้ง
thai.TaskOptionalResources=ทรัพยากรเพิ่มเติม:
thai.ModelDownloadInProgress=กำลังดาวน์โหลดโมเดล หากการเชื่อมต่อช้าอาจใช้เวลาหลายนาที โปรดเปิดหน้าต่างนี้ไว้
thai.DirectoryNotEmpty=ไดเรกทอรีที่เลือกมีไฟล์ที่ไม่ใช่ของ VRC-Fisher กรุณาเลือกไดเรกทอรีที่ว่างหรือไดเรกทอรีการติดตั้ง VRC-Fisher ที่มีอยู่
thai.DirectoryNotWritable=ไดเรกทอรีที่เลือกไม่สามารถเขียนได้โดยผู้ใช้ปัจจุบัน กรุณาเลือกไดเรกทอรีอื่น
thai.ModelDownloadFailed=ติดตั้งซอฟต์แวร์แล้ว แต่ไม่สามารถดาวน์โหลดโมเดลได้ เปิดหน้า Models หลังการติดตั้งเพื่อลองอีกครั้ง รหัสออก: %1
swedish.TaskDesktopIcon=Skapa en genväg på skrivbordet
swedish.TaskShortcuts=Genvägar:
swedish.TaskDownloadModels=Ladda ner modeller efter installationen
swedish.TaskOptionalResources=Valfria resurser:
swedish.ModelDownloadInProgress=Laddar ner modeller. Långsamma anslutningar kan ta flera minuter; håll det här fönstret öppet.
swedish.DirectoryNotEmpty=Den valda mappen innehåller filer som inte tillhör VRC-Fisher. Välj en tom mapp eller den befintliga installationsmappen för VRC-Fisher.
swedish.DirectoryNotWritable=Den valda mappen är inte skrivbar av den aktuella användaren. Välj en annan mapp.
swedish.ModelDownloadFailed=Programvaran installerades, men modellerna kunde inte laddas ner. Öppna sidan Models efter installationen för att försöka igen. Utgångskod: %1
finnish.TaskDesktopIcon=Luo työpöydän pikakuvake
finnish.TaskShortcuts=Pikakuvakkeet:
finnish.TaskDownloadModels=Lataa mallit asennuksen jälkeen
finnish.TaskOptionalResources=Valinnaiset resurssit:
finnish.ModelDownloadInProgress=Ladataan malleja. Hidas yhteys voi kestää useita minuutteja; pidä tämä ikkuna avoinna.
finnish.DirectoryNotEmpty=Valittu kansio sisältää tiedostoja, jotka eivät kuulu VRC-Fisherille. Valitse tyhjä kansio tai olemassa oleva VRC-Fisherin asennuskansio.
finnish.DirectoryNotWritable=Nykyinen käyttäjä ei voi kirjoittaa valittuun kansioon. Valitse toinen kansio.
finnish.ModelDownloadFailed=Ohjelmisto asennettiin, mutta malleja ei voitu ladata. Avaa Models-sivu asennuksen jälkeen yrittääksesi uudelleen. Poistumiskoodi: %1

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
  ResultCode: Integer;
  Executed: Boolean;
begin
  if CurStep <> ssPostInstall then exit;
  LanguageCode := ApplicationLanguageCode();
  ForceDirectories(ExpandConstant('{app}\config'));
  SaveStringToFile(ExpandConstant('{app}\config\installer-language.ini'), LanguageCode, False);

  if WizardIsTaskSelected('downloadmodels') then
  begin
    WizardForm.StatusLabel.Caption := CustomMessage('ModelDownloadInProgress');
    WizardForm.ProgressGauge.Style := npbstMarquee;
    ResultCode := -1;
    try
      Executed := Exec(
        ExpandConstant('{app}\program\VrcFisher.exe'),
        '--download-models --non-interactive',
        ExpandConstant('{app}'),
        SW_HIDE,
        ewWaitUntilTerminated,
        ResultCode);
    finally
      WizardForm.ProgressGauge.Style := npbstNormal;
      WizardForm.ProgressGauge.Position := 100;
      WizardForm.StatusLabel.Caption := '';
    end;
    if (not Executed) or (ResultCode <> 0) then
      MsgBox(FmtMessage(CustomMessage('ModelDownloadFailed'), [IntToStr(ResultCode)]), mbError, MB_OK);
  end;
end;
