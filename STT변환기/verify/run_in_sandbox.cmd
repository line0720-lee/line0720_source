@echo off
setlocal
chcp 65001 >nul
REM ================================================================
REM Windows Sandbox 안에서 실행되는 오프라인 검증 스크립트 (offline.wsb의 LogonCommand)
REM   Sandbox 안의 폴더 (offline.wsb에서 매핑):
REM     C:\app      = dist\STT변환기 (읽기 전용)
REM     C:\samples  = tests\samples (읽기 전용)
REM     C:\verify   = verify (읽기 전용, 이 파일이 있는 곳)
REM     C:\work     = tests\output\sandbox (쓰기 가능, 결과를 남기는 곳)
REM   결과: tests\output\sandbox\run\ 아래
REM     samples\result\STT_결과_*.xlsx  변환 결과
REM     exitcode.txt                    exe 종료 코드 (기대값 1: 7건 중 2건 실패)
REM     logs_documents\                 이번 실행의 프로그램 로그 (앱 폴더가 읽기 전용이라 문서\STT변환기\logs에 남는다)
REM     python_check.txt                Python이 없는지 (where python 결과)
REM     network_check.txt               네트워크가 끊겨 있는지 (ping 결과)
REM     done.txt                        스크립트가 끝까지 실행되었다는 표시
REM   Sandbox의 코드 페이지가 달라도 깨지지 않도록 명령에는 한글을 쓰지 않는다(exe는 *.exe로 찾는다).
REM   이 파일은 UTF-8(BOM 없음)로 저장한다. chcp 65001 줄보다 앞에는 한글을 쓰지 않는다.
REM ================================================================
set "APP=C:\app"
set "RUN=C:\work\run"

if exist "%RUN%" rmdir /s /q "%RUN%"
mkdir "%RUN%"
robocopy "C:\samples" "%RUN%\samples" /E /NFL /NDL /NJH /NJS /NP >nul

set "EXE="
for %%f in ("%APP%\*.exe") do set "EXE=%%f"
REM 괄호 블록 안에서 경로를 펼치면 경로의 ')' 때문에 깨지므로 goto로 나눈다
if not defined EXE goto :no_exe

echo start %DATE% %TIME% > "%RUN%\timing.txt"
start "" /wait "%EXE%" --folder "%RUN%\samples" --no-gui --subfolders
set "RC=%errorlevel%"
echo end   %DATE% %TIME% >> "%RUN%\timing.txt"
echo exit_code=%RC% > "%RUN%\exitcode.txt"

REM 로그 복사: 앱 폴더는 읽기 전용이라 이번 실행의 로그는 문서\STT변환기\logs(대체 위치)에만 남는다.
REM   %APP%\logs에는 호스트에서 시험 실행한 예전 로그만 있으므로 복사하지 않는다.
REM   Sandbox는 실행할 때마다 새로 만들어지므로 문서 폴더의 로그는 모두 이번 실행의 것이다.
for /d %%d in ("%USERPROFILE%\Documents\STT*") do (
    if exist "%%d\logs" robocopy "%%d\logs" "%RUN%\logs_documents" /E /NFL /NDL /NJH /NJS /NP >nul
)
goto :finish

:no_exe
echo no exe in "%APP%" > "%RUN%\exitcode.txt"

:finish
where python > "%RUN%\python_check.txt" 2>&1
ping -n 1 -w 2000 8.8.8.8 > "%RUN%\network_check.txt" 2>&1
echo done %DATE% %TIME% > "%RUN%\done.txt"
endlocal
