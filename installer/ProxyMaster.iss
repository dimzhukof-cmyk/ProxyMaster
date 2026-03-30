#define AppName      "ProxyMaster"
#define AppVersion   "1.0.0"
#define AppPublisher "Dmitry Zhukov"
#define AppURL       "https://github.com/dimzhukof-cmyk/ProxyMaster"
#define AppExeName   "ProxyMaster.exe"
#define SrcDir       "..\bin\Release\net8.0-windows"

[Setup]
AppId={{F3A7B2C1-9D4E-4F8A-B6C3-1E2D5A7F9B0C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
OutputDir=..\output
OutputBaseFilename=ProxyMaster-{#AppVersion}-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#AppExeName}
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon";   Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SrcDir}\{#AppExeName}";    DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\ProxyMaster.dll";  DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\WinDivert.dll";    DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\WinDivert64.sys";  DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}";           Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";     Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Устанавливаем .NET 8 Desktop Runtime если нужно (скачан во время установки)
Filename: "{tmp}\dotnet-runtime.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing .NET 8 Desktop Runtime..."; Flags: waituntilterminated skipifdoesntexist
; Запускаем приложение после установки
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /F /IM {#AppExeName}"; Flags: runhidden; RunOnceId: "KillApp"

[Code]

// URL официального установщика .NET 8.0 Desktop Runtime x64
const
  DotNetUrl = 'https://download.visualstudio.microsoft.com/download/pr/a1f9b848-5d8b-4a44-8f7d-60bd7c19d64a/90de863cb7e85b44e9d95a9a1e79b854/windowsdesktop-runtime-8.0.15-win-x64.exe';

function IsDotNet8Installed(): Boolean;
var
  Key: String;
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  Key := 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  if RegGetValueNames(HKLM, Key, Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Copy(Names[I], 1, 2) = '8.' then
      begin
        Result := True;
        Exit;
      end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  DownloadPage: TDownloadWizardPage;
begin
  Result := '';
  if IsDotNet8Installed() then Exit;

  DownloadPage := CreateDownloadPage(
    'Downloading .NET 8 Desktop Runtime',
    'This may take a moment. Please wait...',
    nil);

  DownloadPage.Clear;
  DownloadPage.Add(DotNetUrl, 'dotnet-runtime.exe', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
    except
      Result := 'Failed to download .NET 8 Desktop Runtime.' + #13#10 +
                'Please install it manually from:' + #13#10 +
                'https://dotnet.microsoft.com/download/dotnet/8.0';
    end;
  finally
    DownloadPage.Hide;
  end;
end;
