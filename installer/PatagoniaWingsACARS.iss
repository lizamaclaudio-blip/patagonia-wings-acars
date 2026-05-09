#define MyAppName "Patagonia Wings ACARS"
#define MyAppVersion "7.0.21"
#define MyAppPublisher "Patagonia Wings"
#define MyAppExeName "PatagoniaWings.Acars.Master.exe"

[Setup]
AppId={{A6E7F4E2-6C2A-4C9E-9A1B-5FD05A7A7020}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Patagonia Wings ACARS
DefaultGroupName=Patagonia Wings ACARS
DisableProgramGroupPage=yes
OutputDir=..\Releases
OutputBaseFilename=PatagoniaWingsACARSSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: unchecked

[Files]












Source: "..\PatagoniaWings.Acars.Master\bin\x64\Release\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Patagonia Wings ACARS"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Patagonia Wings ACARS"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Iniciar Patagonia Wings ACARS"; Flags: nowait postinstall skipifsilent
