@echo off
rem ============================================================
rem Wallpaper Engine build script (all tools on G drive, project-local)
rem Usage: build.cmd [Debug or Release]
rem NOTE: build runs through ASCII junction G:\WE to avoid
rem       aapt2 non-ASCII path issues (see stage-2/3 logs)
rem ============================================================
setlocal
set "PROJ_ROOT=G:\WE\"
set "DOTNET_ROOT=%PROJ_ROOT%tools\dotnet"
set "JAVA_HOME=%PROJ_ROOT%tools\jdk\jdk-17.0.20.1+1"
set "PATH=%DOTNET_ROOT%;%JAVA_HOME%\bin;%PATH%"

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"

cd /d "%PROJ_ROOT%src\WallpaperEngine.App" || exit /b 1
dotnet build -c %CONFIG%
