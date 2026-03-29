; ProxyMaster — Inno Setup Script
; Компилировать: Inno Setup Compiler (бесплатно: jrsoftware.org/isinfo.php)
; Перед компиляцией запустить publish.ps1 — он создаст папку publish\ProxyMaster\

#define AppName      "ProxyMaster"
#define AppVersion   "1.0.0"
#define AppPublisher "Dmitry Zhukov"
#define AppExeName   "ProxyMaster.exe"
#define SourceDir    "publish\ProxyMaster"

[Setup]
AppId={{A7F3C2D1-8E4B-4F9A-BC12-3D5E6F7A8B9C}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisherURL=
AppSupportURL=
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=no
OutputDir=publish
OutputBaseFilename=ProxyMaster-{#AppVersion}-Setup
SetupIconFile=
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
MinVersion=10.0.17763
; Windows 10 1809+ (минимум для .NET 8)

[Languages]
Name: "russian";    MessagesFile: "compiler:Languages\Russian.isl"
Name: "english";    MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Files]
; Основной исполняемый файл
Source: "{#SourceDir}\{#AppExeName}";   DestDir: "{app}"; Flags: ignoreversion

; WinDivert — драйвер перехвата пакетов (ДОЛЖЕН лежать рядом с exe)
Source: "{#SourceDir}\WinDivert.dll";   DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\WinDivert64.sys"; DestDir: "{app}"; Flags: ignoreversion

; Прочие файлы из папки публикации (кроме уже перечисленных)
Source: "{#SourceDir}\*"; DestDir: "{app}"; \
    Excludes: "{#AppExeName},WinDivert.dll,WinDivert64.sys"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}";              Filename: "{app}\{#AppExeName}"
Name: "{group}\Удалить {#AppName}";      Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";        Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Запуск после установки (опционально)
Filename: "{app}\{#AppExeName}"; \
    Description: "Запустить {#AppName}"; \
    Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
; При удалении — остановить процесс, если запущен
Filename: "taskkill"; Parameters: "/F /IM {#AppExeName}"; Flags: runhidden; RunOnceId: "KillApp"

[Code]
// Проверка: не запущено ли приложение во время установки
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  // Остановить процесс если запущен
  Exec('taskkill', '/F /IM {#AppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;
