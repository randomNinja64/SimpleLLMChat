@echo off

setlocal EnableExtensions EnableDelayedExpansion

set ROOT=%~dp0

set FAILED=0

set TESTBUILD=%ROOT%Tests-Build



:: Find MSBuild

for /f "usebackq delims=" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set MSBUILD=%%i

if not defined MSBUILD (echo ERROR: MSBuild not found. & exit /b 1)



echo === Building product solution (Release) ===

"%MSBUILD%" "%ROOT%SimpleLLMChat.sln" /p:Configuration=Release /m /v:minimal || exit /b %ERRORLEVEL%

echo.



echo === Building test projects (Release) ===

set TESTS=%ROOT%Tests



call :BuildOne "%TESTS%\FileTools.Tests\FileTools.Tests.csproj" || set FAILED=1

call :BuildOne "%TESTS%\MemoryTools.Tests\MemoryTools.Tests.csproj" || set FAILED=1

call :BuildOne "%TESTS%\SkillTools.Tests\SkillTools.Tests.csproj" || set FAILED=1

call :BuildOne "%TESTS%\ShellTools.Tests\ShellTools.Tests.csproj" || set FAILED=1

call :BuildOne "%TESTS%\PythonTools.Tests\PythonTools.Tests.csproj" || set FAILED=1

call :BuildOne "%TESTS%\WebTools.Tests\WebTools.Tests.csproj" || set FAILED=1

call :BuildOne "%TESTS%\DesktopTools.Tests\DesktopTools.Fixture\DesktopTools.Fixture.csproj" || set FAILED=1

call :BuildOne "%TESTS%\DesktopTools.Tests\DesktopTools.Tests.csproj" || set FAILED=1

call :BuildOne "%TESTS%\SimpleLLMChatCLI.Tests\SimpleLLMChatCLI.Tests.csproj" || set FAILED=1

call :BuildOne "%TESTS%\SimpleLLMChatGUI.Tests\SimpleLLMChatGUI.Tests.csproj" || set FAILED=1



if !FAILED! neq 0 (

  echo ERROR: one or more test projects failed to build.

  exit /b 1

)



echo.

echo === Packaging product files beside test EXEs ===



call :CopyTool FileTools

call :CopyTool MemoryTools

call :CopyTool SkillTools

call :CopyTool ShellTools

call :CopyTool PythonTools

call :CopyTool WebTools

call :CopyTool DesktopTools



:: Optional deps from deps\ (or place them beside the matching tool EXE manually before run-tests.bat)

call :CopyIfExists "%ROOT%deps\7za.exe" "%TESTBUILD%\FileTools.Tests\7za.exe"

call :CopyIfExists "%ROOT%deps\grep.exe" "%TESTBUILD%\FileTools.Tests\grep.exe"

call :CopyIfExists "%ROOT%deps\libiconv2.dll" "%TESTBUILD%\FileTools.Tests\libiconv2.dll"

call :CopyIfExists "%ROOT%deps\libintl3.dll" "%TESTBUILD%\FileTools.Tests\libintl3.dll"

call :CopyIfExists "%ROOT%deps\pcre3.dll" "%TESTBUILD%\FileTools.Tests\pcre3.dll"

call :CopyIfExists "%ROOT%deps\regex2.dll" "%TESTBUILD%\FileTools.Tests\regex2.dll"

call :CopyIfExists "%ROOT%deps\curl.exe" "%TESTBUILD%\WebTools.Tests\curl.exe"

call :CopyIfExists "%ROOT%deps\curl-ca-bundle.crt" "%TESTBUILD%\WebTools.Tests\curl-ca-bundle.crt"

call :CopyIfExists "%ROOT%deps\yt-dlp.exe" "%TESTBUILD%\WebTools.Tests\yt-dlp.exe"



copy /Y "%TESTBUILD%\DesktopTools.Fixture\DesktopTools.Fixture.exe" "%TESTBUILD%\DesktopTools.Tests\" >nul



copy /Y "%ROOT%SimpleLLMChatCLI\bin\Release\SimpleLLMChatCLI.exe" "%TESTBUILD%\SimpleLLMChatCLI.Tests\" >nul

copy /Y "%ROOT%SimpleLLMChatCLI\bin\Release\Newtonsoft.Json.dll" "%TESTBUILD%\SimpleLLMChatCLI.Tests\" >nul

:: Production-like tools/ tree beside the CLI test host (Skip in tests if missing)

call :CopyToolToCliHost FileTools

call :CopyToolToCliHost MemoryTools

call :CopyToolToCliHost SkillTools

call :CopyToolToCliHost ShellTools

call :CopyToolToCliHost PythonTools

call :CopyToolToCliHost WebTools

call :CopyToolToCliHost DesktopTools



copy /Y "%ROOT%SimpleLLMChatGUI\bin\Release\SimpleLLMChatGUI.exe" "%TESTBUILD%\SimpleLLMChatGUI.Tests\" >nul

copy /Y "%ROOT%SimpleLLMChatCLI\bin\Release\SimpleLLMChatCLI.exe" "%TESTBUILD%\SimpleLLMChatGUI.Tests\" >nul

copy /Y "%ROOT%SimpleLLMChatCLI\bin\Release\Newtonsoft.Json.dll" "%TESTBUILD%\SimpleLLMChatGUI.Tests\" >nul

:: Intermediate obj/ is only needed during compile
if exist "%TESTBUILD%\obj" rmdir /s /q "%TESTBUILD%\obj"

echo.

echo Done. Output: %TESTBUILD%

echo Run run-tests.bat next.

exit /b 0



:BuildOne

echo Building %~1 ...

"%MSBUILD%" "%~1" /p:Configuration=Release /v:minimal

exit /b %ERRORLEVEL%



:CopyTool

set T=%~1

set SRC=%ROOT%Tools\%T%\bin\Release

set DST=%TESTBUILD%\%T%.Tests

if not exist "%DST%" mkdir "%DST%"

copy /Y "%SRC%\%T%.exe" "%DST%\" >nul

copy /Y "%SRC%\%T%.json" "%DST%\" >nul

copy /Y "%SRC%\Newtonsoft.Json.dll" "%DST%\" >nul

exit /b 0



:CopyToolToCliHost

set T=%~1

set SRC=%ROOT%Tools\%T%\bin\Release

set DST=%TESTBUILD%\SimpleLLMChatCLI.Tests\tools\%T%

if not exist "%SRC%\%T%.exe" exit /b 0

if not exist "%DST%" mkdir "%DST%"

copy /Y "%SRC%\%T%.exe" "%DST%\" >nul

copy /Y "%SRC%\%T%.json" "%DST%\" >nul

if exist "%SRC%\Newtonsoft.Json.dll" copy /Y "%SRC%\Newtonsoft.Json.dll" "%DST%\" >nul

echo   CLI tools\%T%

exit /b 0



:CopyIfExists

if exist "%~1" (

  copy /Y "%~1" "%~2" >nul

  echo   packaged %~nx1

)

exit /b 0

