@echo off
setlocal
cd /d "%~dp0"
where cl >nul 2>&1
if errorlevel 1 (
  echo Abre "x64 Native Tools Command Prompt for VS" y vuelve a ejecutar build.bat
  exit /b 1
)
cl /nologo /LD /O2 /W3 /MT /DUNICODE /D_UNICODE /DWINVER=0x0601 /D_WIN32_WINNT=0x0601 factixpp.cpp /link /DEF:factixpp.def /OUT:factixpp.dll kernel32.lib user32.lib gdi32.lib winspool.lib advapi32.lib shell32.lib ole32.lib
if errorlevel 1 exit /b 1
echo Listo: %cd%\factixpp.dll
