@echo off
rem Compile Tomee et son installateur avec le compilateur C# fourni avec Windows (.NET Framework 4.8).
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Compilateur C# introuvable. Installez .NET Framework 4.8.
  exit /b 1
)

if not exist assets\tomee.ico (
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-icon.ps1 || exit /b 1
)
if not exist bin mkdir bin

set "OPTS=/nologo /platform:anycpu /optimize+ /utf8output /codepage:65001 /win32icon:assets\tomee.ico /win32manifest:src\app.manifest"

echo Application...
"%CSC%" %OPTS% /target:winexe /unsafe /out:bin\Tomee.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  src\*.cs
if errorlevel 1 exit /b 1

echo Installateur...
"%CSC%" %OPTS% /target:winexe /out:bin\Tomee-Setup.exe /resource:bin\Tomee.exe,Tomee.App.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Microsoft.CSharp.dll ^
  installer\Setup.cs src\AppInfo.cs
if errorlevel 1 exit /b 1

echo.
echo OK : bin\Tomee.exe
echo OK : bin\Tomee-Setup.exe  (installateur a distribuer)
