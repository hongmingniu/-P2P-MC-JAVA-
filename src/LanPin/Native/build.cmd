@echo off
setlocal
set "LANPIN_VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%LANPIN_VSWHERE%" exit /b 1
for /f "usebackq delims=" %%i in (`"%LANPIN_VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "LANPIN_VS=%%i"
if not defined LANPIN_VS exit /b 1
call "%LANPIN_VS%\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b 1
if not exist "%~dp0build" mkdir "%~dp0build"
cl /nologo /std:c++17 /EHsc /O2 /DNDEBUG /LD /MT /Brepro "%~dp0backdrop.cpp" /Fo"%~dp0build\backdrop.obj" /Fe"%~dp0build\LanPin.Backdrop.dll" /link /Brepro windowsapp.lib CoreMessaging.lib
exit /b %errorlevel%
