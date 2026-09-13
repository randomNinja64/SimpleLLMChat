@echo off
setlocal EnableExtensions
set ROOT=%~dp0
set OUT=%ROOT%SimpleLLMChat

:: Find MSBuild
for /f "usebackq delims=" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set MSBUILD=%%i
if not defined MSBUILD (echo ERROR: MSBuild not found. & exit /b 1)

:: Build solution
echo Building solution ...
"%MSBUILD%" "%ROOT%SimpleLLMChat.sln" /p:Configuration=Release /m /v:minimal || exit /b %ERRORLEVEL%
echo.

:: Stage output
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%"

copy /Y "%ROOT%SimpleLLMChatCLI\bin\Release\SimpleLLMChatCLI.exe" "%OUT%\" >nul
copy /Y "%ROOT%SimpleLLMChatGUI\bin\Release\SimpleLLMChatGUI.exe" "%OUT%\" >nul
copy /Y "%ROOT%SimpleLLMChatCLI\bin\Release\Newtonsoft.Json.dll"  "%OUT%\" >nul

for %%T in (FileTools PythonTools ShellTools WebTools MemoryTools SkillTools DesktopTools) do (
    mkdir "%OUT%\tools\%%T"
    copy /Y "%ROOT%Tools\%%T\bin\Release\%%T.exe"           "%OUT%\tools\%%T\" >nul
    copy /Y "%ROOT%Tools\%%T\bin\Release\%%T.json"          "%OUT%\tools\%%T\" >nul
    copy /Y "%ROOT%Tools\%%T\bin\Release\Newtonsoft.Json.dll" "%OUT%\tools\%%T\" >nul
)

:: Optional deps from deps\
echo Packaging optional dependencies ...
call :CopyIfExists "%ROOT%deps\7za.exe" "%OUT%\tools\FileTools\7za.exe"
call :CopyIfExists "%ROOT%deps\curl.exe" "%OUT%\tools\WebTools\curl.exe"
call :CopyIfExists "%ROOT%deps\curl-ca-bundle.crt" "%OUT%\tools\WebTools\curl-ca-bundle.crt"
call :CopyIfExists "%ROOT%deps\yt-dlp.exe" "%OUT%\tools\WebTools\yt-dlp.exe"

echo.
echo Done: %OUT%
exit /b 0

:CopyIfExists
if exist "%~1" (
  copy /Y "%~1" "%~2" >nul
  echo   packaged %~nx2
)
exit /b 0
