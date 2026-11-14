; ── Open Forge — інсталятор (Inno Setup 6) ────────────────────────────────────
; Збирається на GitHub Actions: iscc /DAppVersion=1.1.0 installer.iss
; Файл має бути в UTF-8 з BOM, інакше українські рядки зіпсуються.

#ifndef AppVersion
  #define AppVersion "dev"
#endif
#define AppName "Open Forge"
#define AppPublisher "Dmytro Brychnyk"
#define AppURL "https://github.com/Dimonchikk/Open-Forge"
#define AppExeName "OpenForge.exe"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
VersionInfoVersion={#AppVersion}
; Куди встановлювати: користувач обирає папку сам
DefaultDirName={autopf}\{#AppName}
DisableDirPage=no
UsePreviousAppDir=yes
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; «Лише для мене» (без прав адміністратора) або «Для всіх користувачів»
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0
SetupIconFile=assets\icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
OutputDir=dist
OutputBaseFilename=OpenForge-setup

[Languages]
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Створити ярлик на робочому столі"; GroupDescription: "Ярлики:"

[Files]
Source: "dist\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Видалити {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Запустити {#AppName}"; Flags: nowait postinstall skipifsilent
; оновлення в один клік запускає інсталятор у тихому режимі — після нього відкриваємо програму знову
Filename: "{app}\{#AppExeName}"; Flags: nowait runasoriginaluser; Check: WizardSilent

[Code]
// Під час видалення через «Програми й компоненти» питаємо про налаштування.
// Якщо видалення запустили з самої програми (тихий режим), вона вже вирішила це сама.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
    if MsgBox('Видалити також налаштування Open Forge?' + #13#10 +
              'Завантажені моделі Ollama це не зачепить.',
              mbConfirmation, MB_YESNO) = IDYES then
      DelTree(ExpandConstant('{userappdata}\OpenForge'), True, True, True);
end;
