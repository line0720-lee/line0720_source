@echo off
setlocal
for /f "tokens=2 delims=:" %%a in ('chcp') do set "OLDCP=%%a"
chcp 65001 >nul
REM ================================================================
REM STT변환기 빌드: PyInstaller onedir -> config.ini·glossary.txt·모델 복사 -> 배포 폴더 점검
REM   사용법: 프로젝트 폴더에서 build.bat 실행 (결과: dist\STT변환기\)
REM   종료 코드: 0 = 성공, 1 = 실패(빌드·복사·점검 중 하나)
REM   이 파일은 UTF-8(BOM 없음)·CRLF로 저장한다. chcp 65001 줄보다 앞에는 한글을 쓰지 않는다.
REM   의존성 고정: requirements.lock (.venv에서 pip freeze로 만든 전체 패키지 버전 목록)
REM     새 PC에서 같은 빌드 환경 만들기: .venv\Scripts\python -m pip install -r requirements.lock
REM ================================================================
cd /d "%~dp0"
set "PY=.venv\Scripts\python.exe"
set "DIST=dist\STT변환기"
set "RC=1"

if not exist "%PY%" goto :no_venv

echo [1/4] PyInstaller 빌드 중... (몇 분 걸립니다)
"%PY%" -m PyInstaller --noconfirm --clean stt.spec
if errorlevel 1 goto :fail_build
if not exist "%DIST%\STT변환기.exe" goto :fail_build

set "MODEL="
for /f "usebackq delims=" %%m in (`.venv\Scripts\python.exe tools\check_dist.py --print-model`) do set "MODEL=%%m"
if not defined MODEL goto :fail_model_name

echo [2/4] config.ini, glossary.txt 복사
copy /y config.ini "%DIST%\config.ini" >nul || goto :fail_copy
copy /y glossary.txt "%DIST%\glossary.txt" >nul || goto :fail_copy

echo [3/4] 모델 복사: models\%MODEL% (약 1~3GB, 수 분 걸릴 수 있습니다)
if not exist "models\%MODEL%\model.bin" goto :no_model
REM robocopy 종료 코드는 0~7이 성공, 8 이상이 실패. .cache(내려받기 기록)는 빼고 복사한다.
robocopy "models\%MODEL%" "%DIST%\models\%MODEL%" /E /XD .cache /NFL /NDL /NJH /NP
if errorlevel 8 goto :fail_robocopy

echo [4/4] 배포 폴더 점검
"%PY%" tools\check_dist.py
if errorlevel 1 goto :fail_check

echo.
echo 빌드 완료: %DIST%\
set "RC=0"
goto :end

:no_venv
echo 오류: .venv가 없습니다. README.md의 "개발 환경 준비"대로 먼저 만드세요.
goto :end
:fail_build
echo 오류: PyInstaller 빌드에 실패했습니다. 위 메시지를 확인하세요.
goto :end
:fail_model_name
echo 오류: config.ini에서 모델 이름을 읽지 못했습니다.
goto :end
:fail_copy
echo 오류: config.ini 또는 glossary.txt를 배포 폴더에 복사하지 못했습니다.
goto :end
:no_model
echo 오류: 모델 폴더가 없습니다: models\%MODEL%  (download_model.py로 먼저 받으세요)
goto :end
:fail_robocopy
echo 오류: 모델 복사에 실패했습니다. (robocopy 종료 코드 %errorlevel%)
goto :end
:fail_check
echo 오류: 배포 폴더 점검에서 실패한 항목이 있습니다. 위 표를 확인하세요.
goto :end

:end
endlocal & chcp %OLDCP% >nul & exit /b %RC%
