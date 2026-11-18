; Установщик «Чистая папка» (Inno Setup 6)
; Сборка: powershell -ExecutionPolicy Bypass -File tools\build.ps1
; Тихое обновление из программы: CleanFolder-Setup-X.Y.Z.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1 [/RELAUNCH=1]

#define AppName "Чистая папка"
#define AppVersion "2.0.0"
#define AppExe "CleanFolder.exe"
#define BuildDir "..\src\CleanFolder\bin\Release\net48"

[Setup]
AppId={{3F7C2A91-5D4E-4B8A-9C61-7E2F0B1D8A44}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=worldruschel
AppPublisherURL=https://github.com/worldruschel/clean-folder
AppSupportURL=https://github.com/worldruschel/clean-folder/issues
AppUpdatesURL=https://github.com/worldruschel/clean-folder/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\CleanFolder
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
DisableReadyPage=yes
; Ставится без прав администратора – в папку пользователя
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=CleanFolder-Setup-{#AppVersion}
SetupIconFile=..\assets\cleanfolder.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=force
RestartApplications=no

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "{#BuildDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\{#AppExe}.config"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Ярлык на рабочем столе создаётся всегда
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Навести порядок в папке"
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Удалить «{#AppName}»"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "Запустить «{#AppName}»"; Flags: postinstall nowait skipifsilent
; после тихого обновления из программы – открыть её снова
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: ShouldRelaunch

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{localappdata}\CleanFolder"

[Registry]
Root: HKCU; Subkey: "Software\CleanFolder"; Flags: uninsdeletekey
; пункт «Навести порядок» в Проводнике создаёт сама программа (настройка), здесь – только удаление
Root: HKCU; Subkey: "Software\Classes\Directory\shell\CleanFolder"; Flags: uninsdeletekey dontcreatekey
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\CleanFolder"; Flags: uninsdeletekey dontcreatekey
Root: HKCU; Subkey: "Software\Classes\Drive\shell\CleanFolder"; Flags: uninsdeletekey dontcreatekey

[Code]
function ShouldRelaunch: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:RELAUNCH|0}') = '1');
end;
