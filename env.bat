@echo off
rem Redirect dotnet user config and NuGet cache to D drive
set "DOTNET_CLI_HOME=D:\MP3player\.dotnet-cli"
set "NUGET_PACKAGES=D:\MP3player\.nuget"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "DOTNET_NOLOGO=1"
set "PATH=D:\MP3player\.dotnet;%PATH%"
