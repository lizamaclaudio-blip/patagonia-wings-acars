@echo off
echo ===== Building Patagonia Wings ACARS v3.1.0 =====
set MSBUILD="C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
set PROJ="C:\Users\lizam\Desktop\ACARS NUEVO\PatagoniaWings.Acars.Master\PatagoniaWings.Acars.Master.csproj"
set ISCC="C:\Users\lizam\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
set ISS="C:\Users\lizam\Desktop\ACARS NUEVO\installer\PatagoniaWingsACARSSetup.iss"
set RELEASE="C:\Users\lizam\Desktop\ACARS NUEVO\release"

echo [1/3] Compiling Release x64...
%MSBUILD% %PROJ% /p:Configuration=Release /p:Platform=x64 /t:Rebuild /m /v:minimal
if %ERRORLEVEL% NEQ 0 (
    echo BUILD FAILED with code %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)
echo Build OK.

echo [2/3] Creating release folder...
if not exist %RELEASE% mkdir %RELEASE%

echo [3/3] Running Inno Setup...
%ISCC% %ISS%
if %ERRORLEVEL% NEQ 0 (
    echo INNO SETUP FAILED with code %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)
echo Installer created successfully.

echo ===== Done! =====
dir %RELEASE%\PatagoniaWingsACARSSetup-3.1.0.exe
