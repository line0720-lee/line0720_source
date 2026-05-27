[Setup]
; 앱 기본 정보
AppName=SFM - Smart File Manager
AppVersion=1.1
AppPublisher=SFM
AppPublisherURL=https://sfm.local
AppSupportURL=https://sfm.local
AppUpdatesURL=https://sfm.local

; 설치 경로 (관리자: Program Files / 일반 유저: AppData\Local\Programs)
DefaultDirName={autopf}\SFM
DefaultGroupName=SFM

; 출력 파일
OutputDir=.
OutputBaseFilename=SFM_Setup_v1.1
SetupIconFile=sfm.ico

; 압축 방식 (LZMA2 최고 압축)
Compression=lzma2/ultra64
SolidCompression=yes

; 외관
WizardStyle=modern

; 권한: 관리자 필요 없으면 일반 유저도 설치 가능하도록 허용
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Windows 10 이상
MinVersion=10.0

; 언인스톨러 아이콘
UninstallDisplayName=SFM - Smart File Manager
UninstallDisplayIcon={app}\SFM.exe

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
; 바탕화면 아이콘 (기본 체크)
Name: "desktopicon"; Description: "바탕 화면에 아이콘 만들기"; GroupDescription: "추가 아이콘:"

[Files]
; publish 폴더 전체를 설치 대상으로
Source: "bin\Release\net8.0-windows\win-x64\publish\*"; \
  DestDir: "{app}"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; 시작 메뉴
Name: "{group}\SFM";            Filename: "{app}\SFM.exe"; Comment: "Smart File Manager"
Name: "{group}\SFM 제거";       Filename: "{uninstallexe}"
; 바탕화면 (체크된 경우만)
Name: "{userdesktop}\SFM";      Filename: "{app}\SFM.exe"; Tasks: desktopicon; Comment: "Smart File Manager"

[Run]
; 설치 완료 후 바로 실행 옵션 제공
Filename: "{app}\SFM.exe"; \
  Description: "SFM 실행"; \
  Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 언인스톨 시 데이터 폴더는 유지 (인덱스 DB 보존)
; Type: filesandordirs; Name: "{app}"
