@echo off
setlocal EnableExtensions
cd /d "%~dp0"

echo === Compilando Factix.exe ===
dotnet build src\Factix.App\Factix.App.csproj -c Release
if errorlevel 1 goto :fail

echo === Compilando Factix.Setup.exe ===
dotnet build src\Factix.Setup\Factix.Setup.csproj -c Release
if errorlevel 1 goto :fail

if not exist "src\Factix.App\bin\Release\net48\Factix.exe" (
  echo No se encontro src\Factix.App\bin\Release\net48\Factix.exe
  goto :fail
)

echo === Creando artifacts\Factix ===
if not exist artifacts\Factix mkdir artifacts\Factix
copy /Y "src\Factix.App\bin\Release\net48\Factix.exe" "artifacts\Factix\"
copy /Y "src\Factix.App\bin\Release\net48\Factix.exe.config" "artifacts\Factix\" >nul 2>&1
copy /Y "src\Factix.App\bin\Release\net48\*.dll" "artifacts\Factix\"
copy /Y "src\Factix.Setup\bin\Release\net48\Factix.Setup.exe" "artifacts\Factix\"
copy /Y "src\Factix.Setup\bin\Release\net48\Factix.Setup.exe.config" "artifacts\Factix\" >nul 2>&1

echo === Compilando factixpp.dll ===
call src\Factix.PrintProcessor\build.bat
if errorlevel 1 goto :nativefail
copy /Y "src\Factix.PrintProcessor\factixpp.dll" "artifacts\Factix\"
echo.
echo Carpeta lista: %cd%\artifacts\Factix
echo Ejecuta Factix.Setup.exe /install como administrador y luego Factix.exe
goto :end

:nativefail
echo.
echo La aplicacion quedo en artifacts\Factix, pero falta factixpp.dll.
echo Sin esa DLL el instalador no puede crear la impresora virtual.
echo Instala Build Tools de Visual Studio 2022 con "Desarrollo de escritorio con C++" y vuelve a ejecutar este archivo.
goto :end

:fail
echo.
echo La compilacion de .NET se detuvo. Revisa el mensaje de arriba.
echo Si dotnet no se reconoce, instala el SDK de .NET 8.

:end
echo.
pause
endlocal
