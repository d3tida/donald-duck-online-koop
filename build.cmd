@echo off
rem Baut build\DonaldOnline.exe mit dem C#-Compiler des .NET Framework 4.x.
rem "build.cmd test" baut zusaetzlich build\DonaldOnlineTest.exe (TESTAUTO).
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (echo csc.exe nicht gefunden: %CSC% & exit /b 1)
if not exist build mkdir build
set SRC=src\OnlineLauncher.cs src\OnlineSession.cs src\AppPaths.cs src\GameSetup.cs src\GameLocation.cs src\GameInstance.cs src\GameBridge.cs src\ReplicaFrame.cs src\InputGuard.cs src\InputExpressions.cs src\PortalGate.cs src\LevelExit.cs src\RoomWire.cs src\Tunnel.cs src\Respawn.cs src\NameTag.cs src\CoopLives.cs src\Spectator.cs src\WorldSync.cs src\VideoStream.cs src\TestDriver.cs
set OPT=/nologo /nowarn:4014,169,414,649 /target:winexe /platform:x86 /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /resource:resources\dgVoodoo.conf,DonaldOnline.dgVoodoo.conf /resource:resources\ubi.ini,DonaldOnline.ubi.ini
"%CSC%" %OPT% /out:build\DonaldOnline.exe %SRC% || exit /b 1
if /i "%1"=="test" "%CSC%" %OPT% /define:TESTAUTO /out:build\DonaldOnlineTest.exe %SRC% || exit /b 1
echo Fertig: build\DonaldOnline.exe
