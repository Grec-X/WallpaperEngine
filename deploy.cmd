@echo off
rem ============================================================
rem Wallpaper Engine deploy script: build + install + launch on device
rem Usage: deploy.cmd [Debug or Release]
rem NOTE: runs through ASCII junction G:\WE (see build.cmd note)
rem ============================================================
setlocal
set "PROJ_ROOT=G:\WE\"
set "DOTNET_ROOT=%PROJ_ROOT%tools\dotnet"
set "JAVA_HOME=%PROJ_ROOT%tools\jdk\jdk-17.0.20.1+1"
set "PATH=%DOTNET_ROOT%;%JAVA_HOME%\bin;%PATH%"
set "ADB=%PROJ_ROOT%tools\AndroidSDK\platform-tools\adb.exe"

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"

cd /d "%PROJ_ROOT%src\WallpaperEngine.App" || exit /b 1
dotnet build -t:Install -c %CONFIG%
if errorlevel 1 exit /b 1

rem monkey 启动主入口，免去 crc 类名耦合
%ADB% shell monkey -p com.wallpaperengine.app 1
