@echo off
REM Run the Net Speed Meter test suite (compiles the REAL app sources)
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" ( echo [!] csc.exe not found & exit /b 1 )

cd /d "%~dp0"

REM /main picks the test entry point; the app's own Main stays untouched
"%CSC%" /nologo /target:exe /platform:anycpu /warn:4 /main:NetSpeedMeter.TestEntry ^
  /reference:System.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /out:"%TEMP%\nsm-tests.exe" NetSpeedMeter.cs SpeedTest.cs Tests.cs TestEntry.cs

if errorlevel 1 ( echo [!] TEST BUILD FAILED & exit /b 1 )

"%TEMP%\nsm-tests.exe"
set RESULT=%ERRORLEVEL%
del "%TEMP%\nsm-tests.exe" >nul 2>&1
exit /b %RESULT%
