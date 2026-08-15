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

[Types]
Name: "full"; Description: "{cm:TypeRecommended}"
Name: "custom"; Description: "{cm:TypeCustom}"; Flags: iscustom

[Components]
Name: "directml"; Description: "{cm:ComponentDirectML}"; Types: full custom; Flags: exclusive
Name: "cpu"; Description: "{cm:ComponentCpu}"; Types: custom; Flags: exclusive

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
chinesetrad.TypeRecommended=å»ºè­°ç DirectML å®è£
chinesetrad.TypeCustom=é¸æå·è¡éæ®µåä»¶
chinesetrad.ComponentDirectML=DirectML (NVIDIAãAMD æ Intel GPU)
chinesetrad.ComponentCpu=åé CPU
chinesetrad.TaskDesktopIcon=å»ºç«æ¡é¢æ·å¾
chinesetrad.TaskShortcuts=æ·å¾ï¼
chinesetrad.TaskDownloadModels=å®è£å¾ä¸è¼ç¸å®¹ç ONNX æ¨¡å
chinesetrad.TaskOptionalResources=é¸ç¨è³æºï¼
chinesetrad.DirectoryNotEmpty=é¸åçç®éåå«ä¸å±¬æ¼ VRC-Fisher çæªæ¡ãè«é¸æç©ºç®éæç¾æç VRC-Fisher å®è£ç®éã
chinesetrad.DirectoryNotWritable=ç®åä½¿ç¨èç¡æ³å¯«å¥é¸åçç®éãè«é¸æå¶ä»ç®éã
chinesetrad.ModelDownloadFailed=è»é«å·²å®è£ï¼ä½ç¡æ³ä¸è¼æ¨¡åãè«å¨å®è£å¾éåæ¨¡åé é¢éè©¦ãçµæä»£ç¢¼ï¼%1
japanese.TypeRecommended=æ¨å¥¨ã® DirectML ã¤ã³ã¹ãã¼ã«
japanese.TypeCustom=ã©ã³ã¿ã¤ã  ã³ã³ãã¼ãã³ããé¸æ
japanese.ComponentDirectML=DirectML (NVIDIAãAMD ã¾ãã¯ Intel GPU)
japanese.ComponentCpu=CPU ã®ã¿
japanese.TaskDesktopIcon=ãã¹ã¯ããã ã·ã§ã¼ãã«ãããä½æ
japanese.TaskShortcuts=ã·ã§ã¼ãã«ãã:
japanese.TaskDownloadModels=ã¤ã³ã¹ãã¼ã«å¾ã«äºææ§ã®ãã ONNX ã¢ãã«ããã¦ã³ã­ã¼ã
japanese.TaskOptionalResources=ãªãã·ã§ã³ ãªã½ã¼ã¹:
japanese.DirectoryNotEmpty=é¸æãããã£ã¬ã¯ããªã«ã¯ VRC-Fisher ã«å±ããªããã¡ã¤ã«ãå«ã¾ãã¦ãã¾ããç©ºã®ãã£ã¬ã¯ããªãã¾ãã¯æ¢å­ã® VRC-Fisher ã¤ã³ã¹ãã¼ã« ãã£ã¬ã¯ããªãé¸æãã¦ãã ããã
japanese.DirectoryNotWritable=é¸æãããã£ã¬ã¯ããªã¯ç¾å¨ã®ã¦ã¼ã¶ã¼ãæ¸ãè¾¼ãã¾ãããå¥ã®ãã£ã¬ã¯ããªãé¸æãã¦ãã ããã
japanese.ModelDownloadFailed=ã½ããã¦ã§ã¢ã¯ã¤ã³ã¹ãã¼ã«ããã¾ããããã¢ãã«ããã¦ã³ã­ã¼ãã§ãã¾ããã§ãããã¤ã³ã¹ãã¼ã«å¾ã«ã¢ãã« ãã¼ã¸ãéãã¦åè©¦è¡ãã¦ãã ãããçµäºã³ã¼ã: %1
korean.TypeRecommended=ê¶ì¥ DirectML ì¤ì¹
korean.TypeCustom=ë°íì êµ¬ì± ìì ì í
korean.ComponentDirectML=DirectML (NVIDIA, AMD ëë Intel GPU)
korean.ComponentCpu=CPU ì ì©
korean.TaskDesktopIcon=ë°í íë©´ ë°ë¡ ê°ê¸° ë§ë¤ê¸°
korean.TaskShortcuts=ë°ë¡ ê°ê¸°:
korean.TaskDownloadModels=ì¤ì¹ í í¸íëë ONNX ëª¨ë¸ ë¤ì´ë¡ë
korean.TaskOptionalResources=ì í ë¦¬ìì¤:
korean.DirectoryNotEmpty=ì íí ëë í°ë¦¬ì VRC-Fisherì ìíì§ ìë íì¼ì´ ììµëë¤. ë¹ ëë í°ë¦¬ ëë ê¸°ì¡´ VRC-Fisher ì¤ì¹ ëë í°ë¦¬ë¥¼ ì ííì¸ì.
korean.DirectoryNotWritable=ì íí ëë í°ë¦¬ë íì¬ ì¬ì©ìê° ì¸ ì ììµëë¤. ë¤ë¥¸ ëë í°ë¦¬ë¥¼ ì ííì¸ì.
korean.ModelDownloadFailed=ìíí¸ì¨ì´ê° ì¤ì¹ëìì§ë§ ëª¨ë¸ì ë¤ì´ë¡ëí  ì ììµëë¤. ì¤ì¹ í ëª¨ë¸ íì´ì§ë¥¼ ì´ì´ ë¤ì ìëíì¸ì. ì¢ë£ ì½ë: %1
spanish.TypeRecommended=InstalaciÃ³n recomendada de DirectML
spanish.TypeCustom=Elegir componente de runtime
spanish.ComponentDirectML=DirectML (GPU NVIDIA, AMD o Intel)
spanish.ComponentCpu=Solo CPU
spanish.TaskDesktopIcon=Crear un acceso directo en el escritorio
spanish.TaskShortcuts=Accesos directos:
spanish.TaskDownloadModels=Descargar modelos ONNX compatibles despuÃ©s de la instalaciÃ³n
spanish.TaskOptionalResources=Recursos opcionales:
spanish.DirectoryNotEmpty=El directorio seleccionado contiene archivos que no pertenecen a VRC-Fisher. Elija un directorio vacÃ­o o el directorio de instalaciÃ³n existente de VRC-Fisher.
spanish.DirectoryNotWritable=El directorio seleccionado no es escribible por el usuario actual. Elija otro directorio.
spanish.ModelDownloadFailed=El software se instalÃ³, pero no se pudieron descargar los modelos. Abra la pÃ¡gina de Modelos despuÃ©s de la instalaciÃ³n para reintentarlo. CÃ³digo de salida: %1
french.TypeRecommended=Installation recommandÃ©e de DirectML
french.TypeCustom=Choisir le composant d'exÃ©cution
french.ComponentDirectML=DirectML (GPU NVIDIA, AMD ou Intel)
french.ComponentCpu=CPU uniquement
french.TaskDesktopIcon=CrÃ©er un raccourci sur le bureau
french.TaskShortcuts=Raccourcis :
french.TaskDownloadModels=TÃ©lÃ©charger les modÃ¨les ONNX compatibles aprÃ¨s l'installation
french.TaskOptionalResources=Ressources facultatives :
french.DirectoryNotEmpty=Le dossier sÃ©lectionnÃ© contient des fichiers qui n'appartiennent pas Ã  VRC-Fisher. Choisissez un dossier vide ou le dossier d'installation existant de VRC-Fisher.
french.DirectoryNotWritable=Le dossier sÃ©lectionnÃ© n'est pas accessible en Ã©criture pour l'utilisateur actuel. Choisissez un autre dossier.
french.ModelDownloadFailed=Le logiciel a Ã©tÃ© installÃ©, mais les modÃ¨les n'ont pas pu Ãªtre tÃ©lÃ©chargÃ©s. Ouvrez la page ModÃ¨les aprÃ¨s l'installation pour rÃ©essayer. Code de sortie : %1
german.TypeRecommended=Empfohlene DirectML-Installation
german.TypeCustom=Laufzeitkomponente auswÃ¤hlen
german.ComponentDirectML=DirectML (NVIDIA-, AMD- oder Intel-GPU)
german.ComponentCpu=Nur CPU
german.TaskDesktopIcon=DesktopverknÃ¼pfung erstellen
german.TaskShortcuts=VerknÃ¼pfungen:
german.TaskDownloadModels=Kompatible ONNX-Modelle nach der Installation herunterladen
german.TaskOptionalResources=Optionale Ressourcen:
german.DirectoryNotEmpty=Das ausgewÃ¤hlte Verzeichnis enthÃ¤lt Dateien, die nicht zu VRC-Fisher gehÃ¶ren. WÃ¤hlen Sie ein leeres Verzeichnis oder das vorhandene VRC-Fisher-Installationsverzeichnis.
german.DirectoryNotWritable=Das ausgewÃ¤hlte Verzeichnis ist fÃ¼r den aktuellen Benutzer nicht schreibbar. WÃ¤hlen Sie ein anderes Verzeichnis.
german.ModelDownloadFailed=Die Software wurde installiert, aber die Modelle konnten nicht heruntergeladen werden. Ãffnen Sie nach der Installation die Seite âModelleâ, um es erneut zu versuchen. Exitcode: %1
brazilianportuguese.TypeRecommended=InstalaÃ§Ã£o recomendada do DirectML
brazilianportuguese.TypeCustom=Escolha o componente de runtime
brazilianportuguese.ComponentDirectML=DirectML (GPU NVIDIA, AMD ou Intel)
brazilianportuguese.ComponentCpu=Somente CPU
brazilianportuguese.TaskDesktopIcon=Criar atalho na Ã¡rea de trabalho
brazilianportuguese.TaskShortcuts=Atalhos:
brazilianportuguese.TaskDownloadModels=Baixar modelos ONNX compatÃ­veis apÃ³s a instalaÃ§Ã£o
brazilianportuguese.TaskOptionalResources=Recursos opcionais:
brazilianportuguese.DirectoryNotEmpty=O diretÃ³rio selecionado contÃ©m arquivos que nÃ£o pertencem ao VRC-Fisher. Escolha um diretÃ³rio vazio ou o diretÃ³rio de instalaÃ§Ã£o existente do VRC-Fisher.
brazilianportuguese.DirectoryNotWritable=O diretÃ³rio selecionado nÃ£o Ã© gravÃ¡vel pelo usuÃ¡rio atual. Escolha outro diretÃ³rio.
brazilianportuguese.ModelDownloadFailed=O software foi instalado, mas os modelos nÃ£o puderam ser baixados. Abra a pÃ¡gina de Modelos apÃ³s a instalaÃ§Ã£o para tentar novamente. CÃ³digo de saÃ­da: %1
russian.TypeRecommended=Ð ÐµÐºÐ¾Ð¼ÐµÐ½Ð´ÑÐµÐ¼Ð°Ñ ÑÑÑÐ°Ð½Ð¾Ð²ÐºÐ° DirectML
russian.TypeCustom=ÐÑÐ±ÐµÑÐ¸ÑÐµ ÐºÐ¾Ð¼Ð¿Ð¾Ð½ÐµÐ½Ñ ÑÑÐµÐ´Ñ Ð²ÑÐ¿Ð¾Ð»Ð½ÐµÐ½Ð¸Ñ
russian.ComponentDirectML=DirectML (GPU NVIDIA, AMD Ð¸Ð»Ð¸ Intel)
russian.ComponentCpu=Ð¢Ð¾Ð»ÑÐºÐ¾ CPU
russian.TaskDesktopIcon=Ð¡Ð¾Ð·Ð´Ð°ÑÑ ÑÑÐ»ÑÐº Ð½Ð° ÑÐ°Ð±Ð¾ÑÐµÐ¼ ÑÑÐ¾Ð»Ðµ
russian.TaskShortcuts=Ð¯ÑÐ»ÑÐºÐ¸:
russian.TaskDownloadModels=Ð¡ÐºÐ°ÑÐ°ÑÑ ÑÐ¾Ð²Ð¼ÐµÑÑÐ¸Ð¼ÑÐµ Ð¼Ð¾Ð´ÐµÐ»Ð¸ ONNX Ð¿Ð¾ÑÐ»Ðµ ÑÑÑÐ°Ð½Ð¾Ð²ÐºÐ¸
russian.TaskOptionalResources=ÐÐµÐ¾Ð±ÑÐ·Ð°ÑÐµÐ»ÑÐ½ÑÐµ ÑÐµÑÑÑÑÑ:
russian.DirectoryNotEmpty=ÐÑÐ±ÑÐ°Ð½Ð½ÑÐ¹ ÐºÐ°ÑÐ°Ð»Ð¾Ð³ ÑÐ¾Ð´ÐµÑÐ¶Ð¸Ñ ÑÐ°Ð¹Ð»Ñ, Ð½Ðµ Ð¾ÑÐ½Ð¾ÑÑÑÐ¸ÐµÑÑ Ðº VRC-Fisher. ÐÑÐ±ÐµÑÐ¸ÑÐµ Ð¿ÑÑÑÐ¾Ð¹ ÐºÐ°ÑÐ°Ð»Ð¾Ð³ Ð¸Ð»Ð¸ ÑÑÑÐµÑÑÐ²ÑÑÑÐ¸Ð¹ ÐºÐ°ÑÐ°Ð»Ð¾Ð³ ÑÑÑÐ°Ð½Ð¾Ð²ÐºÐ¸ VRC-Fisher.
russian.DirectoryNotWritable=ÐÑÐ±ÑÐ°Ð½Ð½ÑÐ¹ ÐºÐ°ÑÐ°Ð»Ð¾Ð³ Ð½ÐµÐ´Ð¾ÑÑÑÐ¿ÐµÐ½ Ð´Ð»Ñ Ð·Ð°Ð¿Ð¸ÑÐ¸ ÑÐµÐºÑÑÐ¸Ð¼ Ð¿Ð¾Ð»ÑÐ·Ð¾Ð²Ð°ÑÐµÐ»ÐµÐ¼. ÐÑÐ±ÐµÑÐ¸ÑÐµ Ð´ÑÑÐ³Ð¾Ð¹ ÐºÐ°ÑÐ°Ð»Ð¾Ð³.
russian.ModelDownloadFailed=ÐÑÐ¾Ð³ÑÐ°Ð¼Ð¼Ð° Ð±ÑÐ»Ð° ÑÑÑÐ°Ð½Ð¾Ð²Ð»ÐµÐ½Ð°, Ð½Ð¾ Ð½Ðµ ÑÐ´Ð°Ð»Ð¾ÑÑ Ð·Ð°Ð³ÑÑÐ·Ð¸ÑÑ Ð¼Ð¾Ð´ÐµÐ»Ð¸. ÐÑÐºÑÐ¾Ð¹ÑÐµ ÑÑÑÐ°Ð½Ð¸ÑÑ Â«ÐÐ¾Ð´ÐµÐ»Ð¸Â» Ð¿Ð¾ÑÐ»Ðµ ÑÑÑÐ°Ð½Ð¾Ð²ÐºÐ¸, ÑÑÐ¾Ð±Ñ Ð¿Ð¾Ð²ÑÐ¾ÑÐ¸ÑÑ Ð¿Ð¾Ð¿ÑÑÐºÑ. ÐÐ¾Ð´ Ð²ÑÑÐ¾Ð´Ð°: %1
italian.TypeRecommended=Installazione consigliata di DirectML
italian.TypeCustom=Scegli il componente runtime
italian.ComponentDirectML=DirectML (GPU NVIDIA, AMD o Intel)
italian.ComponentCpu=Solo CPU
italian.TaskDesktopIcon=Crea un collegamento sul desktop
italian.TaskShortcuts=Collegamenti:
italian.TaskDownloadModels=Scarica i modelli ONNX compatibili dopo l'installazione
italian.TaskOptionalResources=Risorse facoltative:
italian.DirectoryNotEmpty=La directory selezionata contiene file che non appartengono a VRC-Fisher. Scegli una directory vuota o la directory di installazione esistente di VRC-Fisher.
italian.DirectoryNotWritable=La directory selezionata non Ã¨ scrivibile dall'utente corrente. Scegli un'altra directory.
italian.ModelDownloadFailed=Il software Ã¨ stato installato, ma non Ã¨ stato possibile scaricare i modelli. Apri la pagina Modelli dopo l'installazione per riprovare. Codice di uscita: %1
polish.TypeRecommended=Zalecana instalacja DirectML
polish.TypeCustom=Wybierz komponent Årodowiska uruchomieniowego
polish.ComponentDirectML=DirectML (NVIDIA, AMD lub Intel GPU)
polish.ComponentCpu=Tylko CPU
polish.TaskDesktopIcon=UtwÃ³rz skrÃ³t na pulpicie
polish.TaskShortcuts=SkrÃ³ty:
polish.TaskDownloadModels=Pobierz zgodne modele ONNX po instalacji
polish.TaskOptionalResources=Opcjonalne zasoby:
polish.DirectoryNotEmpty=Wybrany katalog zawiera pliki, ktÃ³re nie naleÅ¼Ä do VRC-Fisher. Wybierz pusty katalog lub istniejÄcy katalog instalacyjny VRC-Fisher.
polish.DirectoryNotWritable=Wybrany katalog nie jest zapisywalny przez bieÅ¼Äcego uÅ¼ytkownika. Wybierz inny katalog.
polish.ModelDownloadFailed=Oprogramowanie zostaÅo zainstalowane, ale nie moÅ¼na byÅo pobraÄ modeli. OtwÃ³rz stronÄ Modele po instalacji, aby sprÃ³bowaÄ ponownie. Kod wyjÅcia: %1
turkish.TypeRecommended=Ãnerilen DirectML kurulumu
turkish.TypeCustom=ÃalÄ±Åma zamanÄ± bileÅenini seÃ§in
turkish.ComponentDirectML=DirectML (NVIDIA, AMD veya Intel GPU)
turkish.ComponentCpu=YalnÄ±zca CPU
turkish.TaskDesktopIcon=MasaÃ¼stÃ¼ kÄ±sayolu oluÅtur
turkish.TaskShortcuts=KÄ±sayollar:
turkish.TaskDownloadModels=Kurulumdan sonra uyumlu ONNX modellerini indir
turkish.TaskOptionalResources=Ä°steÄe baÄlÄ± kaynaklar:
turkish.DirectoryNotEmpty=SeÃ§ilen dizin VRC-Fisher'a ait olmayan dosyalar iÃ§eriyor. BoÅ bir dizin veya mevcut VRC-Fisher kurulum dizinini seÃ§in.
turkish.DirectoryNotWritable=SeÃ§ilen dizin geÃ§erli kullanÄ±cÄ± tarafÄ±ndan yazÄ±labilir deÄil. BaÅka bir dizin seÃ§in.
turkish.ModelDownloadFailed=YazÄ±lÄ±m kuruldu, ancak modeller indirilemedi. Kurulumdan sonra tekrar denemek iÃ§in Modeller sayfasÄ±nÄ± aÃ§Ä±n. ÃÄ±kÄ±Å kodu: %1
dutch.TypeRecommended=Aanbevolen DirectML-installatie
dutch.TypeCustom=Kies runtime-component
dutch.ComponentDirectML=DirectML (NVIDIA, AMD of Intel GPU)
dutch.ComponentCpu=Alleen CPU
dutch.TaskDesktopIcon=Bureaubladsnelkoppeling maken
dutch.TaskShortcuts=Snelkoppelingen:
dutch.TaskDownloadModels=Download compatibele ONNX-modellen na installatie
dutch.TaskOptionalResources=Optionele bronnen:
dutch.DirectoryNotEmpty=De geselecteerde map bevat bestanden die niet bij VRC-Fisher horen. Kies een lege map of de bestaande VRC-Fisher-installatiemap.
dutch.DirectoryNotWritable=De geselecteerde map is niet schrijfbaar door de huidige gebruiker. Kies een andere map.
dutch.ModelDownloadFailed=De software is geÃ¯nstalleerd, maar de modellen konden niet worden gedownload. Open na installatie de pagina Modellen om het opnieuw te proberen. Exitcode: %1
czech.TypeRecommended=DoporuÄenÃ¡ instalace DirectML
czech.TypeCustom=Vyberte bÄhovou komponentu
czech.ComponentDirectML=DirectML (GPU NVIDIA, AMD nebo Intel)
czech.ComponentCpu=Pouze CPU
czech.TaskDesktopIcon=VytvoÅit zÃ¡stupce na ploÅ¡e
czech.TaskShortcuts=ZÃ¡stupci:
czech.TaskDownloadModels=Po instalaci stÃ¡hnout kompatibilnÃ­ modely ONNX
czech.TaskOptionalResources=VolitelnÃ© zdroje:
czech.DirectoryNotEmpty=VybranÃ¡ sloÅ¾ka obsahuje soubory, kterÃ© nepatÅÃ­ do VRC-Fisher. Vyberte prÃ¡zdnou sloÅ¾ku nebo existujÃ­cÃ­ instalaÄnÃ­ sloÅ¾ku VRC-Fisher.
czech.DirectoryNotWritable=VybranÃ¡ sloÅ¾ka nenÃ­ pro aktuÃ¡lnÃ­ho uÅ¾ivatele zapisovatelnÃ¡. Vyberte jinou sloÅ¾ku.
czech.ModelDownloadFailed=Software byl nainstalovÃ¡n, ale modely se nepodaÅilo stÃ¡hnout. Po instalaci otevÅete strÃ¡nku modelÅ¯ a zkuste to znovu. KÃ³d ukonÄenÃ­: %1
hungarian.TypeRecommended=AjÃ¡nlott DirectML telepÃ­tÃ©s
hungarian.TypeCustom=FutÃ¡sidejÅ± Ã¶sszetevÅ kivÃ¡lasztÃ¡sa
hungarian.ComponentDirectML=DirectML (NVIDIA, AMD vagy Intel GPU)
hungarian.ComponentCpu=Csak CPU
hungarian.TaskDesktopIcon=Asztali parancsikon lÃ©trehozÃ¡sa
hungarian.TaskShortcuts=Parancsikonok:
hungarian.TaskDownloadModels=Kompatibilis ONNX-modellek letÃ¶ltÃ©se a telepÃ­tÃ©s utÃ¡n
hungarian.TaskOptionalResources=OpcionÃ¡lis erÅforrÃ¡sok:
hungarian.DirectoryNotEmpty=A kivÃ¡lasztott mappa olyan fÃ¡jlokat tartalmaz, amelyek nem a VRC-Fisherhez tartoznak. VÃ¡lasszon Ã¼res mappÃ¡t vagy a meglÃ©vÅ VRC-Fisher telepÃ­tÃ©si mappÃ¡t.
hungarian.DirectoryNotWritable=A kivÃ¡lasztott mappÃ¡ba az aktuÃ¡lis felhasznÃ¡lÃ³ nem Ã­rhat. VÃ¡lasszon mÃ¡sik mappÃ¡t.
hungarian.ModelDownloadFailed=A szoftver telepÃ­tve lett, de a modelleket nem sikerÃ¼lt letÃ¶lteni. A telepÃ­tÃ©s utÃ¡n nyissa meg a Modellek oldalt az ÃºjraprÃ³bÃ¡lkozÃ¡shoz. KilÃ©pÃ©si kÃ³d: %1
ukrainian.TypeRecommended=Ð ÐµÐºÐ¾Ð¼ÐµÐ½Ð´Ð¾Ð²Ð°Ð½Ðµ Ð²ÑÑÐ°Ð½Ð¾Ð²Ð»ÐµÐ½Ð½Ñ DirectML
ukrainian.TypeCustom=ÐÐ¸Ð±ÑÑ ÐºÐ¾Ð¼Ð¿Ð¾Ð½ÐµÐ½ÑÐ° ÑÐµÑÐµÐ´Ð¾Ð²Ð¸ÑÐ° Ð²Ð¸ÐºÐ¾Ð½Ð°Ð½Ð½Ñ
ukrainian.ComponentDirectML=DirectML (GPU NVIDIA, AMD Ð°Ð±Ð¾ Intel)
ukrainian.ComponentCpu=Ð¢ÑÐ»ÑÐºÐ¸ CPU
ukrainian.TaskDesktopIcon=Ð¡ÑÐ²Ð¾ÑÐ¸ÑÐ¸ ÑÑÐ»Ð¸Ðº Ð½Ð° ÑÐ¾Ð±Ð¾ÑÐ¾Ð¼Ñ ÑÑÐ¾Ð»Ñ
ukrainian.TaskShortcuts=Ð¯ÑÐ»Ð¸ÐºÐ¸:
ukrainian.TaskDownloadModels=ÐÐ°Ð²Ð°Ð½ÑÐ°Ð¶Ð¸ÑÐ¸ ÑÑÐ¼ÑÑÐ½Ñ Ð¼Ð¾Ð´ÐµÐ»Ñ ONNX Ð¿ÑÑÐ»Ñ Ð²ÑÑÐ°Ð½Ð¾Ð²Ð»ÐµÐ½Ð½Ñ
ukrainian.TaskOptionalResources=ÐÐ¾Ð´Ð°ÑÐºÐ¾Ð²Ñ ÑÐµÑÑÑÑÐ¸:
ukrainian.DirectoryNotEmpty=ÐÐ¸Ð±ÑÐ°Ð½Ð° Ð¿Ð°Ð¿ÐºÐ° Ð¼ÑÑÑÐ¸ÑÑ ÑÐ°Ð¹Ð»Ð¸, ÑÐºÑ Ð½Ðµ Ð½Ð°Ð»ÐµÐ¶Ð°ÑÑ VRC-Fisher. ÐÐ¸Ð±ÐµÑÑÑÑ Ð¿Ð¾ÑÐ¾Ð¶Ð½Ñ Ð¿Ð°Ð¿ÐºÑ Ð°Ð±Ð¾ Ð½Ð°ÑÐ²Ð½Ñ Ð¿Ð°Ð¿ÐºÑ Ð²ÑÑÐ°Ð½Ð¾Ð²Ð»ÐµÐ½Ð½Ñ VRC-Fisher.
ukrainian.DirectoryNotWritable=ÐÐ¸Ð±ÑÐ°Ð½Ð° Ð¿Ð°Ð¿ÐºÐ° Ð½ÐµÐ´Ð¾ÑÑÑÐ¿Ð½Ð° Ð´Ð»Ñ Ð·Ð°Ð¿Ð¸ÑÑ Ð¿Ð¾ÑÐ¾ÑÐ½Ð¾Ð¼Ñ ÐºÐ¾ÑÐ¸ÑÑÑÐ²Ð°ÑÐµÐ²Ñ. ÐÐ¸Ð±ÐµÑÑÑÑ ÑÐ½ÑÑ Ð¿Ð°Ð¿ÐºÑ.
ukrainian.ModelDownloadFailed=ÐÑÐ¾Ð³ÑÐ°Ð¼Ð½Ðµ Ð·Ð°Ð±ÐµÐ·Ð¿ÐµÑÐµÐ½Ð½Ñ Ð²ÑÑÐ°Ð½Ð¾Ð²Ð»ÐµÐ½Ð¾, Ð°Ð»Ðµ Ð¼Ð¾Ð´ÐµÐ»Ñ Ð½Ðµ Ð²Ð´Ð°Ð»Ð¾ÑÑ Ð·Ð°Ð²Ð°Ð½ÑÐ°Ð¶Ð¸ÑÐ¸. ÐÑÑÐ»Ñ Ð²ÑÑÐ°Ð½Ð¾Ð²Ð»ÐµÐ½Ð½Ñ Ð²ÑÐ´ÐºÑÐ¸Ð¹ÑÐµ ÑÑÐ¾ÑÑÐ½ÐºÑ Ð¼Ð¾Ð´ÐµÐ»ÐµÐ¹, ÑÐ¾Ð± Ð¿Ð¾Ð²ÑÐ¾ÑÐ¸ÑÐ¸ ÑÐ¿ÑÐ¾Ð±Ñ. ÐÐ¾Ð´ Ð·Ð°Ð²ÐµÑÑÐµÐ½Ð½Ñ: %1
thai.TypeRecommended=à¹à¸à¸°à¸à¸³à¸à¸²à¸£à¸à¸´à¸à¸à¸±à¹à¸ DirectML
thai.TypeCustom=à¹à¸¥à¸·à¸­à¸à¸ªà¹à¸§à¸à¸à¸£à¸°à¸à¸­à¸à¸£à¸±à¸à¹à¸à¸¡à¹
thai.ComponentDirectML=DirectML (NVIDIA, AMD à¸«à¸£à¸·à¸­ Intel GPU)
thai.ComponentCpu=CPU à¹à¸à¹à¸²à¸à¸±à¹à¸
thai.TaskDesktopIcon=à¸ªà¸£à¹à¸²à¸à¸à¸²à¸à¸¥à¸±à¸à¸à¸à¹à¸à¸ªà¸à¹à¸à¹à¸­à¸
thai.TaskShortcuts=à¸à¸²à¸à¸¥à¸±à¸:
thai.TaskDownloadModels=à¸à¸²à¸§à¸à¹à¹à¸«à¸¥à¸à¹à¸¡à¹à¸à¸¥ ONNX à¸à¸µà¹à¹à¸à¹à¸²à¸à¸±à¸à¹à¸à¹à¸«à¸¥à¸±à¸à¸à¸²à¸£à¸à¸´à¸à¸à¸±à¹à¸
thai.TaskOptionalResources=à¸à¸£à¸±à¸à¸¢à¸²à¸à¸£à¹à¸à¸´à¹à¸¡à¹à¸à¸´à¸¡:
thai.DirectoryNotEmpty=à¹à¸à¹à¸£à¸à¸à¸­à¸£à¸µà¸à¸µà¹à¹à¸¥à¸·à¸­à¸à¸¡à¸µà¹à¸à¸¥à¹à¸à¸µà¹à¹à¸¡à¹à¹à¸à¹à¸à¸­à¸ VRC-Fisher à¸à¸£à¸¸à¸à¸²à¹à¸¥à¸·à¸­à¸à¹à¸à¹à¸£à¸à¸à¸­à¸£à¸µà¸à¸µà¹à¸§à¹à¸²à¸à¸«à¸£à¸·à¸­à¹à¸à¹à¸£à¸à¸à¸­à¸£à¸µà¸à¸²à¸£à¸à¸´à¸à¸à¸±à¹à¸ VRC-Fisher à¸à¸µà¹à¸¡à¸µà¸­à¸¢à¸¹à¹
thai.DirectoryNotWritable=à¹à¸à¹à¸£à¸à¸à¸­à¸£à¸µà¸à¸µà¹à¹à¸¥à¸·à¸­à¸à¹à¸¡à¹à¸ªà¸²à¸¡à¸²à¸£à¸à¹à¸à¸µà¸¢à¸à¹à¸à¹à¹à¸à¸¢à¸à¸¹à¹à¹à¸à¹à¸à¸±à¸à¸à¸¸à¸à¸±à¸ à¸à¸£à¸¸à¸à¸²à¹à¸¥à¸·à¸­à¸à¹à¸à¹à¸£à¸à¸à¸­à¸£à¸µà¸­à¸·à¹à¸
thai.ModelDownloadFailed=à¸à¸´à¸à¸à¸±à¹à¸à¸à¸­à¸à¸à¹à¹à¸§à¸£à¹à¹à¸¥à¹à¸§ à¹à¸à¹à¹à¸¡à¹à¸ªà¸²à¸¡à¸²à¸£à¸à¸à¸²à¸§à¸à¹à¹à¸«à¸¥à¸à¹à¸¡à¹à¸à¸¥à¹à¸à¹ à¹à¸à¸´à¸à¸«à¸à¹à¸² Models à¸«à¸¥à¸±à¸à¸à¸²à¸£à¸à¸´à¸à¸à¸±à¹à¸à¹à¸à¸·à¹à¸­à¸¥à¸­à¸à¸­à¸µà¸à¸à¸£à¸±à¹à¸ à¸£à¸«à¸±à¸ªà¸­à¸­à¸: %1
swedish.TypeRecommended=Rekommenderad DirectML-installation
swedish.TypeCustom=VÃ¤lj runtime-komponent
swedish.ComponentDirectML=DirectML (NVIDIA, AMD eller Intel GPU)
swedish.ComponentCpu=Endast CPU
swedish.TaskDesktopIcon=Skapa en genvÃ¤g pÃ¥ skrivbordet
swedish.TaskShortcuts=GenvÃ¤gar:
swedish.TaskDownloadModels=Ladda ner kompatibla ONNX-modeller efter installationen
swedish.TaskOptionalResources=Valfria resurser:
swedish.DirectoryNotEmpty=Den valda mappen innehÃ¥ller filer som inte tillhÃ¶r VRC-Fisher. VÃ¤lj en tom mapp eller den befintliga installationsmappen fÃ¶r VRC-Fisher.
swedish.DirectoryNotWritable=Den valda mappen Ã¤r inte skrivbar av den aktuella anvÃ¤ndaren. VÃ¤lj en annan mapp.
swedish.ModelDownloadFailed=Programvaran installerades, men modellerna kunde inte laddas ner. Ãppna sidan Models efter installationen fÃ¶r att fÃ¶rsÃ¶ka igen. UtgÃ¥ngskod: %1
finnish.TypeRecommended=Suositeltu DirectML-asennus
finnish.TypeCustom=Valitse suorituksenaikainen komponentti
finnish.ComponentDirectML=DirectML (NVIDIA, AMD tai Intel GPU)
finnish.ComponentCpu=Vain CPU
finnish.TaskDesktopIcon=Luo tyÃ¶pÃ¶ydÃ¤n pikakuvake
finnish.TaskShortcuts=Pikakuvakkeet:
finnish.TaskDownloadModels=Lataa yhteensopivat ONNX-mallit asennuksen jÃ¤lkeen
finnish.TaskOptionalResources=Valinnaiset resurssit:
finnish.DirectoryNotEmpty=Valittu kansio sisÃ¤ltÃ¤Ã¤ tiedostoja, jotka eivÃ¤t kuulu VRC-Fisherille. Valitse tyhjÃ¤ kansio tai olemassa oleva VRC-Fisherin asennuskansio.
finnish.DirectoryNotWritable=Nykyinen kÃ¤yttÃ¤jÃ¤ ei voi kirjoittaa valittuun kansioon. Valitse toinen kansio.
finnish.ModelDownloadFailed=Ohjelmisto asennettiin, mutta malleja ei voitu ladata. Avaa Models-sivu asennuksen jÃ¤lkeen yrittÃ¤Ã¤ksesi uudelleen. Poistumiskoodi: %1

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
