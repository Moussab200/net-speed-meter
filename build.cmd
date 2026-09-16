@echo off
REM Build Net Speed Meter using the C# compiler shipped with Windows - no SDK needed
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo [!] csc.exe not found - .NET Framework missing
  exit /b 1
)

cd /d "%~dp0"
set ICON=
if exist icon.ico set ICON=/win32icon:icon.ico

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /win32manifest:app.manifest %ICON% ^
  /reference:System.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /out:NetSpeedMeter.exe NetSpeedMeter.cs SpeedTest.cs

if errorlevel 1 (
  echo [!] BUILD FAILED
  exit /b 1
)
echo [ok] Built NetSpeedMeter.exe
endlocal
