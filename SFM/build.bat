@echo off
setlocal

:: ─────────────────────────────────────────────────────────────────────────────
:: SFM - Smart File Manager 빌드 스크립트
::   출력: SFM\publish\SFM.exe  (단일 파일, win-x64 self-contained)
:: ─────────────────────────────────────────────────────────────────────────────

set PROJECT=SFM.csproj
set OUTPUT=.\publish
set RID=win-x64
set CONFIG=Release

echo.
echo ■ SFM 빌드 시작 (%DATE% %TIME%)
echo ─────────────────────────────────────────────────────────────────────────────

:: dotnet CLI 확인
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [오류] dotnet CLI 를 찾을 수 없습니다.
    echo        https://dotnet.microsoft.com/download 에서 .NET 8 SDK 를 설치하세요.
    pause
    exit /b 1
)

:: 이전 출력 정리
if exist "%OUTPUT%" (
    echo [1/3] 이전 publish 폴더 정리 중...
    rmdir /s /q "%OUTPUT%"
)

:: NuGet 복원
echo [2/3] 패키지 복원 중...
dotnet restore "%PROJECT%" --runtime %RID% --verbosity quiet
if errorlevel 1 (
    echo [오류] 패키지 복원 실패.
    pause
    exit /b 1
)

:: 단일 EXE 퍼블리시
echo [3/3] 단일 EXE 퍼블리시 중...
dotnet publish "%PROJECT%" ^
    -c %CONFIG% ^
    -r %RID% ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:IncludeAllContentForSelfExtract=true ^
    -p:PublishTrimmed=false ^
    -p:PublishReadyToRun=true ^
    --output "%OUTPUT%" ^
    --verbosity minimal

if errorlevel 1 (
    echo.
    echo [오류] 빌드 실패. 위의 오류 메시지를 확인하세요.
    pause
    exit /b 1
)

:: 결과 확인
if exist "%OUTPUT%\SFM.exe" (
    echo.
    echo ─────────────────────────────────────────────────────────────────────────────
    echo ■ 빌드 성공!
    for %%F in ("%OUTPUT%\SFM.exe") do echo   출력 파일: %%~fF  ^(%%~zF bytes^)
    echo ─────────────────────────────────────────────────────────────────────────────
) else (
    echo [경고] publish 폴더에 SFM.exe 가 보이지 않습니다.
)

echo.
pause
endlocal
