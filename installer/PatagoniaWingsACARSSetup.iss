; ============================================================================
; Patagonia Wings ACARS - Inno Setup Script
; One-click installer for Windows 10/11 + MSFS 2020/2024
; ============================================================================

#ifndef MyAppName
  #define MyAppName      "Patagonia Wings ACARS"
#endif
#ifndef MyAppVersion
  #define MyAppVersion   "2.0.12"
#endif
#ifndef MyAppPublisher
  #define MyAppPublisher "Patagonia Wings Virtual Airline"
#endif
#ifndef MyAppURL
  #define MyAppURL       "https://www.patagoniaw.com"
#endif
#ifndef MyAppExe
  #define MyAppExe       "PatagoniaWings.Acars.Master.exe"
#endif
#ifndef MyAppId
  #define MyAppId        "{{A3F72C1E-88B4-4D2A-9E1F-7C3B5A2D8F46}"
#endif

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\PatagoniaWings\ACARS
DefaultGroupName=Patagonia Wings
DisableProgramGroupPage=yes
AllowNoIcons=no
OutputDir=..\release
OutputBaseFilename=PatagoniaWingsACARSSetup-2.0.12
SetupIconFile=..\PatagoniaWings.Acars.Master\Assets\patagonia-logo.ico
UninstallDisplayIcon={app}\{#MyAppExe}
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
WizardSizePercent=120
WizardImageFile=compiler:WizModernImage.bmp
WizardSmallImageFile=compiler:WizModernSmallImage.bmp
MinVersion=10.0.17763
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ShowLanguageDialog=no
CloseApplications=yes
RestartApplications=no
CreateUninstallRegKey=yes
Uninstallable=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: unchecked
Name: "startupicon"; Description: "Iniciar con Windows"; GroupDescription: "Inicio automatico:"; Flags: unchecked

[Files]
Source: "..\PatagoniaWings.Acars.Master\bin\x64\Release\{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PatagoniaWings.Acars.Master\bin\x64\Release\PatagoniaWings.Acars.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PatagoniaWings.Acars.Master\bin\x64\Release\PatagoniaWings.Acars.SimConnect.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Libs\SimConnect.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PatagoniaWings.Acars.Master\bin\x64\Release\Microsoft.FlightSimulator.SimConnect.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PatagoniaWings.Acars.Master\bin\x64\Release\fsuipcClient.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PatagoniaWings.Acars.Master\bin\x64\Release\PatagoniaWings.Acars.Master.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PatagoniaWings.Acars.Master\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\PatagoniaWings.Acars.Master\Assets\Sounds\*"; DestDir: "{app}\Assets\Sounds"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: DirExists(ExpandConstant('{src}\\..\\PatagoniaWings.Acars.Master\\Assets\\Sounds'))

[Icons]
Name: "{group}\Patagonia Wings ACARS"; Filename: "{app}\{#MyAppExe}"; IconFilename: "{app}\{#MyAppExe}"
Name: "{group}\Desinstalar ACARS"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Patagonia Wings ACARS"; Filename: "{app}\{#MyAppExe}"; IconFilename: "{app}\{#MyAppExe}"; Tasks: desktopicon
Name: "{userstartup}\Patagonia Wings ACARS"; Filename: "{app}\{#MyAppExe}"; Tasks: startupicon

[Registry]
Root: HKCU; Subkey: "Software\PatagoniaWings\ACARS"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\PatagoniaWings\ACARS"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Iniciar Patagonia Wings ACARS ahora"; Flags: nowait postinstall skipifsilent unchecked

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\PatagoniaWings\Acars\logs"

[Code]
function IsDotNet481Installed(): Boolean;
var
  ReleaseKey: Cardinal;
begin
  Result := RegQueryDWordValue(
    HKLM,
    'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
    'Release',
    ReleaseKey
  ) and (ReleaseKey >= 533320);
end;

function InitializeSetup(): Boolean;
var
  OpenResultCode: Integer;
begin
  Result := True;

  if not IsDotNet481Installed() then
  begin
    if MsgBox(
      '.NET Framework 4.8.1 no esta instalado.' + #13#10 + #13#10 +
      'Patagonia Wings ACARS requiere .NET Framework 4.8.1 para funcionar.' + #13#10 +
      'Haz clic en Si para abrir la pagina de descarga de Microsoft,' + #13#10 +
      'instalarlo y luego volver a ejecutar este instalador.' + #13#10 + #13#10 +
      'Abrir la pagina de descarga ahora?',
      mbConfirmation,
      MB_YESNO
    ) = IDYES then
    begin
      ShellExec(
        'open',
        'https://dotnet.microsoft.com/en-us/download/dotnet-framework/net481',
        '',
        '',
        SW_SHOW,
        ewNoWait,
        OpenResultCode
      );
    end;

    Result := False;
  end;
end;

function GetWelcomeMessage(Param: String): String;
begin
  Result :=
    'Bienvenido al instalador de Patagonia Wings ACARS v{#MyAppVersion}.' + #13#10 + #13#10 +
    'Este asistente instalara el cliente ACARS en tu equipo.' + #13#10 + #13#10 +
    'Asegurate de tener MSFS 2020 o 2024 instalado antes de continuar.';
end;
