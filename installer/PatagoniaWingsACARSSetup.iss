; ============================================================================
; Patagonia Wings ACARS - Inno Setup Script
; One-click installer for Windows 10/11 + MSFS 2020/2024
; ============================================================================

#ifndef MyAppName
  #define MyAppName      "Patagonia Wings ACARS"
#endif
#ifndef MyAppVersion
  #define MyAppVersion   "3.1.5"
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
OutputBaseFilename=PatagoniaWingsACARSSetup-{#MyAppVersion}
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
Source: "..\PatagoniaWings.Acars.Master\bin\x64\Release\AutoUpdater.NET.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PatagoniaWings.Acars.Master\bin\x64\Release\PatagoniaWings.Acars.Master.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PatagoniaWings.Acars.SimConnect\AircraftProfiles.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PatagoniaWings.Acars.Master\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs
; Carpeta de imágenes de aeronaves (el usuario guarda PNG/JPG aquí; nombre = código ICAO, ej. A320.png)
Source: "..\PatagoniaWings.Acars.Master\Assets\Aircraft\.gitkeep"; DestDir: "{app}\Assets\Aircraft"; Flags: ignoreversion
Source: "..\PatagoniaWings.Acars.Master\Assets\Sounds\*"; DestDir: "{app}\Assets\Sounds"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: DirExists(ExpandConstant('{src}\\..\\PatagoniaWings.Acars.Master\\Assets\\Sounds'))
; MobiFlight WASM Module - se copia a temp, el codigo lo instala en Community
Source: "MobiFlightWasm\mobiflight-event-module\*"; DestDir: "{tmp}\mobiflight-event-module"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Patagonia Wings ACARS"; Filename: "{app}\{#MyAppExe}"; IconFilename: "{app}\{#MyAppExe}"
Name: "{group}\Desinstalar ACARS"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Patagonia Wings ACARS"; Filename: "{app}\{#MyAppExe}"; IconFilename: "{app}\{#MyAppExe}"; Tasks: desktopicon
Name: "{userstartup}\Patagonia Wings ACARS"; Filename: "{app}\{#MyAppExe}"; Tasks: startupicon

[Registry]
Root: HKCU; Subkey: "Software\PatagoniaWings\ACARS"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\PatagoniaWings\ACARS"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"

[Run]
; Modo interactivo: muestra checkbox "Iniciar ahora" al finalizar
Filename: "{app}\{#MyAppExe}"; Description: "Iniciar Patagonia Wings ACARS ahora"; Flags: nowait postinstall skipifsilent unchecked runascurrentuser
; Modo silencioso (actualizacion automatica): abre el ACARS automaticamente
Filename: "{app}\{#MyAppExe}"; Flags: nowait runascurrentuser; Check: WizardSilent

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

function FindMSFSCommunityFolder(): String;
var
  // Rutas posibles de la carpeta Community de MSFS 2020/2024
  Candidates: array of String;
  LocalAppData, AppData: String;
  I: Integer;
begin
  Result := '';
  LocalAppData := ExpandConstant('{localappdata}');
  AppData := ExpandConstant('{userappdata}');

  SetArrayLength(Candidates, 6);
  Candidates[0] := LocalAppData + '\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalCache\Packages\Community';
  Candidates[1] := AppData + '\Microsoft Flight Simulator\Packages\Community';
  Candidates[2] := LocalAppData + '\Packages\Microsoft.FlightDashboard_8wekyb3d8bbwe\LocalCache\Packages\Community';
  Candidates[3] := 'C:\MSFSPackages\Community';
  Candidates[4] := AppData + '\Microsoft Flight Simulator 2024\Packages\Community';
  Candidates[5] := LocalAppData + '\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\Packages\Community';

  for I := 0 to GetArrayLength(Candidates) - 1 do
  begin
    if DirExists(Candidates[I]) then
    begin
      Result := Candidates[I];
      Exit;
    end;
  end;
end;

procedure InstallMobiFlightWasm();
var
  CommunityFolder, WasmDest, WasmSrc: String;
begin
  CommunityFolder := FindMSFSCommunityFolder();

  if CommunityFolder = '' then
  begin
    MsgBox(
      'No se encontro la carpeta Community de MSFS.' + #13#10 + #13#10 +
      'El modulo MobiFlight WASM no fue instalado automaticamente.' + #13#10 +
      'Puedes instalarlo manualmente desde:' + #13#10 +
      'github.com/MobiFlight/MobiFlight-WASM-Module',
      mbInformation, MB_OK);
    Exit;
  end;

  WasmDest := CommunityFolder + '\mobiflight-event-module';
  WasmSrc  := ExpandConstant('{tmp}\mobiflight-event-module');

  if not DirExists(WasmDest) then
    CreateDir(WasmDest);

  // Copiar archivos del modulo WASM
  if not DirExists(WasmDest + '\modules') then
    CreateDir(WasmDest + '\modules');
  if not DirExists(WasmDest + '\ContentInfo') then
    CreateDir(WasmDest + '\ContentInfo');
  if not DirExists(WasmDest + '\ContentInfo\mobiflight-event-module') then
    CreateDir(WasmDest + '\ContentInfo\mobiflight-event-module');

  FileCopy(WasmSrc + '\modules\MobiFlightWasmModule.wasm', WasmDest + '\modules\MobiFlightWasmModule.wasm', False);
  FileCopy(WasmSrc + '\manifest.json',  WasmDest + '\manifest.json',  False);
  FileCopy(WasmSrc + '\layout.json',    WasmDest + '\layout.json',    False);
  FileCopy(WasmSrc + '\ContentInfo\mobiflight-event-module\Thumbnail.jpg',
           WasmDest + '\ContentInfo\mobiflight-event-module\Thumbnail.jpg', False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    InstallMobiFlightWasm();
end;
