@echo off
setlocal
cd /d "%~dp0"
dotnet build src\Factix.App\Factix.App.csproj -c Release
if errorlevel 1 exit /b 1
dotnet build src\Factix.Setup\Factix.Setup.csproj -c Release
if errorlevel 1 exit /b 1
call src\Factix.PrintProcessor\build.bat
if errorlevel 1 exit /b 1
if not exist artifacts\Factix mkdir artifacts\Factix
copy /Y src\Factix.App\bin\Release\net48\Factix.exe artifacts\Factix\
copy /Y src\Factix.App\bin\Release\net48\Factix.exe.config artifacts\Factix\ >nul 2>&1
copy /Y src\Factix.App\bin\Release\net48\*.dll artifacts\Factix\
copy /Y src\Factix.Setup\bin\Release\net48\Factix.Setup.exe artifacts\Factix\
copy /Y src\Factix.Setup\bin\Release\net48\Factix.Setup.exe.config artifacts\Factix\ >nul 2>&1
copy /Y src\Factix.PrintProcessor\factixpp.dll artifacts\Factix\
echo Carpeta lista: %cd%\artifacts\Factix
echo Ejecuta Factix.Setup.exe /install como administrador y luego Factix.exe
