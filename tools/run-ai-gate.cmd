@echo off
setlocal
REM ---------------------------------------------------------------------------------------------------
REM SpawnDev.AI browser suite, launched so it OUTLIVES the agent shell.
REM
REM   run-ai-gate.cmd <log-file> [runner args...]
REM   run-ai-gate.cmd D:\logs\ai-heavy.log --heavy
REM   run-ai-gate.cmd D:\logs\ai-one.log --heavy --filter SingleTurnProducesText
REM
REM Global rule 5b: a long run must be started outside the agent shell's job object, or it dies with the
REM shell. Launch this file itself with:
REM   Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{CommandLine='cmd.exe /c "<path>\run-ai-gate.cmd" <log> --heavy'}
REM WMI parents the process to WmiPrvSE and still lands in SessionId 1, so a HEADED run would also work.
REM
REM The runner streams every START/TEST line and a heartbeat naming the in-flight test (see
REM SpawnDev.AI.TestRunner/Program.cs). Tail the log to see progress - do NOT judge liveness by log SIZE,
REM and do not read silence as a hang: that misread killed the 2026-09-08 heavy gate.
REM ---------------------------------------------------------------------------------------------------
set "LOG=%~1"
if "%LOG%"=="" set "LOG=%TEMP%\ai-gate.log"
shift

set "ARGS=%1 %2 %3 %4 %5 %6 %7 %8 %9"

cd /d "%~dp0.."
echo === %DATE% %TIME% === >"%LOG%"
echo args: %ARGS% >>"%LOG%"
dotnet run --project SpawnDev.AI.TestRunner -c Release -- %ARGS% >>"%LOG%" 2>&1
echo EXITCODE=%ERRORLEVEL% >>"%LOG%"
