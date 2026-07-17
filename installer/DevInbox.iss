; Instalador do DevInbox.
; Se o .NET 10 Desktop Runtime não estiver presente, o próprio instalador
; baixa o pacote oficial da Microsoft e o instala em modo silencioso.

#define MyAppName "DevInbox"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Rob Silva"
#define MyAppExeName "DevInbox.exe"
#define PublishDir "..\src\DevInbox.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"

[Setup]
AppId={{B7E5D1F4-3C9A-4A14-9E1B-6F2D8C7A5E90}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/RobsonSilvaVR
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Sem admin instala por usuário (sem UAC); o diálogo permite escolher por máquina.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=output
OutputBaseFilename=DevInbox-Setup-{#MyAppVersion}
SetupIconFile=..\src\DevInbox.App\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Pede para fechar o app em atualização/desinstalação (novo mutex e o da era GitHubChecker).
AppMutex=Local\DevInbox.SingleInstance,Local\GitHubChecker.SingleInstance

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Restos da era "GitHub PR Checker" em upgrades sobre a instalação antiga.
Type: files; Name: "{app}\GitHubChecker.exe"
Type: files; Name: "{autoprograms}\GitHub PR Checker.lnk"
Type: files; Name: "{autodesktop}\GitHub PR Checker.lnk"

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\e_sqlite3.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\audio\*"; DestDir: "{app}\audio"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir o {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;
  NeedsRuntime: Boolean;

function IsDotNet10DesktopInstalled: Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

procedure InitializeWizard;
begin
  NeedsRuntime := not IsDotNet10DesktopInstalled;
  if NeedsRuntime then
    DownloadPage := CreateDownloadPage(
      'Dependência necessária',
      'Baixando o .NET 10 Desktop Runtime da Microsoft…', nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if NeedsRuntime and (CurPageID = wpReady) then
  begin
    DownloadPage.Clear;
    DownloadPage.Add('https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe',
      'windowsdesktop-runtime.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        if not DownloadPage.AbortedByUser then
          SuppressibleMsgBox(
            'Não foi possível baixar o .NET 10 Desktop Runtime. ' +
            'A instalação continuará, mas o aplicativo só abrirá depois que você instalar o runtime ' +
            '(winget install Microsoft.DotNet.DesktopRuntime.10).',
            mbError, MB_OK, IDOK);
        exit;
      end;

      if Exec(ExpandConstant('{tmp}\windowsdesktop-runtime.exe'),
        '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
      begin
        if ResultCode <> 0 then
          Log('Instalador do runtime terminou com código ' + IntToStr(ResultCode));
      end
      else
        SuppressibleMsgBox(
          'Não foi possível executar o instalador do .NET 10 Desktop Runtime (código ' +
          IntToStr(ResultCode) + ').', mbError, MB_OK, IDOK);
    finally
      DownloadPage.Hide;
    end;
  end;
end;
