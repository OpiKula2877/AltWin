; Inno Setup script for Alt Account Switcher. Built by ..\build.ps1 -Installer
; Installs machine-wide, adds ONE machine-wide autostart value (HKLM Run) so the app starts in every account,
; and on uninstall removes that value, every app-created folder permission and every AltAccounts_* HKCU Run value.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{6F1C2A57-3B8E-4C7A-9E11-2D5A1F0B7C42}
AppName=Alt Account Switcher
AppVersion={#AppVersion}
AppVerName=Alt Account Switcher {#AppVersion}
AppPublisher=OpiKula2877
AppPublisherURL=https://github.com/OpiKula2877/AltWin
AppSupportURL=https://github.com/OpiKula2877/AltWin/issues
DefaultDirName={autopf}\AltAccounts
DefaultGroupName=Alt Account Switcher
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=AltWin-Setup-{#AppVersion}
SetupIconFile=..\src\AltAccounts.App\Assets\AltAccounts.ico
UninstallDisplayIcon={app}\AltAccounts.exe
UninstallDisplayName=Alt Account Switcher
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=no

[Files]
Source: "..\artifacts\publish\AltAccounts.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Alt Account Switcher"; Filename: "{app}\AltAccounts.exe"
Name: "{autoprograms}\Alt Account Switcher Settings"; Filename: "{app}\AltAccounts.exe"; Parameters: "--settings"

[Registry]
; One value for all accounts. Removed on uninstall.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AltAccounts"; ValueData: """{app}\AltAccounts.exe"""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\AltAccounts.exe"; Parameters: "--init-programdata"; StatusMsg: "Preparing shared settings…"; Flags: runhidden waituntilterminated
Filename: "{app}\AltAccounts.exe"; Description: "Start Alt Account Switcher now"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; Stop the app in every signed-in account first, so its file can be removed.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM AltAccounts.exe"; Flags: runhidden waituntilterminated; RunOnceId: "StopApp"
Filename: "{app}\AltAccounts.exe"; Parameters: "--uninstall-cleanup {code:CleanupArgs}"; Flags: runhidden waituntilterminated; RunOnceId: "Cleanup"

[Code]
var
  DeleteConfig: Boolean;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  { Upgrade: stop running copies in all accounts. They start again at the next sign-in. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM AltAccounts.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  DeleteConfig := False;
  if not UninstallSilent then
    DeleteConfig := MsgBox('Also delete the shared settings in C:\ProgramData\AltAccounts?' + #13#10 + #13#10 +
      'Folder permissions the app added are removed either way. Choose No to keep the settings for a later reinstall.',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  Result := True;
end;

function CleanupArgs(Param: String): String;
begin
  if DeleteConfig then
    Result := '--delete-config'
  else
    Result := '';
end;
