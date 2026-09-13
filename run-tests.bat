@echo off
setlocal EnableExtensions EnableDelayedExpansion
set ROOT=%~dp0
set TESTS=%ROOT%Tests
set TESTBUILD=%ROOT%Tests-Build

:: Optional single-suite filter: run-tests.bat FileTools
set FILTER=%~1

:: Run id from %DATE%/%TIME% (no PowerShell / WMIC / cscript)
set RUNID=%DATE:~-4%%DATE:~4,2%%DATE:~7,2%-%TIME:~0,2%%TIME:~3,2%%TIME:~6,2%
set RUNID=%RUNID: =0%
set RUNID=%RUNID:/=%
set RUNID=%RUNID:.=%
set RUNID=%RUNID::=%

set LOGDIR=%TESTS%\logs\%RUNID%
mkdir "%LOGDIR%" 2>nul

echo Log directory: %LOGDIR%
echo.

set FAILED=0
set ALLPERF=%LOGDIR%\all.perf.tsv
> "%ALLPERF%" echo suite	case	status	case_ms	process_ms

set SUMMARY=%LOGDIR%\summary.txt
> "%SUMMARY%" echo SimpleLLMChat test run %RUNID%
>> "%SUMMARY%" echo.

call :RunSuite FileTools FileTools.Tests
call :RunSuite MemoryTools MemoryTools.Tests
call :RunSuite SkillTools SkillTools.Tests
call :RunSuite ShellTools ShellTools.Tests
call :RunSuite PythonTools PythonTools.Tests
call :RunSuite WebTools WebTools.Tests
call :RunSuite DesktopTools DesktopTools.Tests
call :RunSuite SimpleLLMChatCLI SimpleLLMChatCLI.Tests
call :RunSuite SimpleLLMChatGUI SimpleLLMChatGUI.Tests

>> "%SUMMARY%" echo.
>> "%SUMMARY%" echo Hot spots (all suites) - slowest 20 by process_ms:
call :SummarizePerf

echo.
echo === Summary ===
type "%SUMMARY%"
echo.
echo Logs: %LOGDIR%

if %FAILED% neq 0 (
  echo FAILED
  exit /b 1
)
echo PASSED
exit /b 0

:RunSuite
set NAME=%~1
set PROJ=%~2
if not "%FILTER%"=="" if /I not "%FILTER%"=="%NAME%" exit /b 0

set EXE=%TESTBUILD%\%PROJ%\%PROJ%.exe
if not exist "%EXE%" (
  echo [SKIP suite] %NAME% - EXE missing: %EXE%
  >> "%SUMMARY%" echo %NAME%: MISSING EXE
  set FAILED=1
  exit /b 0
)

set LOG=%LOGDIR%\%NAME%.log
echo --- %NAME% ---
"%EXE%" --log "%LOG%"
set EC=!ERRORLEVEL!

if exist "%LOGDIR%\%NAME%.perf.tsv" call :AppendPerf "%LOGDIR%\%NAME%.perf.tsv" "%NAME%"

if !EC! neq 0 (
  >> "%SUMMARY%" echo %NAME%: FAIL exit=!EC! log=%LOG%
  set FAILED=1
) else (
  >> "%SUMMARY%" echo %NAME%: PASS log=%LOG%
)
exit /b 0

:AppendPerf
:: Prepend suite name to each data row (skip header). Tab-separated.
set _PERF=%~1
set _SUITE=%~2
for /f "usebackq skip=1 delims=" %%L in ("%_PERF%") do (
  >> "%ALLPERF%" echo %_SUITE%	%%L
)
exit /b 0

:SummarizePerf
:: Rank by process_ms using zero-padded prefix + SORT (XP has no PowerShell).
set _HOT=%LOGDIR%\hotspots.tmp
set _SORTED=%LOGDIR%\hotspots.sorted
if not exist "%ALLPERF%" (
  >> "%SUMMARY%" echo (no perf data)
  exit /b 0
)

if exist "%_HOT%" del /q "%_HOT%" >nul 2>&1
if exist "%_SORTED%" del /q "%_SORTED%" >nul 2>&1

set _COUNT=0
for /f "usebackq skip=1 tokens=1-5 delims=	" %%A in ("%ALLPERF%") do (
  set /a _COUNT+=1
  set "_PAD=000000000000%%E"
  set "_PAD=!_PAD:~-12!"
  >> "%_HOT%" echo !_PAD!	%%A	%%B	%%C	process_ms=%%E	case_ms=%%D
)

if !_COUNT! equ 0 (
  >> "%SUMMARY%" echo (no cases)
  exit /b 0
)

sort /R "%_HOT%" /O "%_SORTED%"
set _N=0
for /f "usebackq tokens=1* delims=	" %%A in ("%_SORTED%") do (
  set /a _N+=1
  if !_N! leq 20 >> "%SUMMARY%" echo %%B
)

if exist "%_HOT%" del /q "%_HOT%" >nul 2>&1
if exist "%_SORTED%" del /q "%_SORTED%" >nul 2>&1
exit /b 0
