@echo off
setlocal EnableExtensions
cd /d "%~dp0"

where cl >nul 2>&1
if not errorlevel 1 goto :compile

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" goto :nocl

set "VSINSTALL="
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%i"
if not defined VSINSTALL goto :nocl
if not exist "%VSINSTALL%\VC\Auxiliary\Build\vcvars64.bat" goto :nocl

echo Preparando el compilador de C++ de 64 bits...
call "%VSINSTALL%\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 goto :nocl
where cl >nul 2>&1
if errorlevel 1 goto :nocl

:compile
cl /nologo /LD /O2 /W3 /MT /DUNICODE /D_UNICODE /DWINVER=0x0601 /D_WIN32_WINNT=0x0601 factixpp.cpp /link /DEF:factixpp.def /OUT:factixpp.dll kernel32.lib user32.lib gdi32.lib winspool.lib advapi32.lib shell32.lib ole32.lib
if errorlevel 1 exit /b 1
echo Listo: %cd%\factixpp.dll
exit /b 0

:nocl
echo No esta el compilador de C++ de 64 bits ^(cl.exe^).
echo Instala Build Tools de Visual Studio 2022 con la carga "Desarrollo de escritorio con C++".
exit /b 1
