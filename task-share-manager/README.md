# 업무 공유 매니저 (Task Share Manager)

병원 내부망(폐쇄망) 배포용 업무 공유·관리 웹앱. Flask + SQLite + Bootstrap + FullCalendar.

## 개발 환경 실행

```bash
python -m venv venv
venv/Scripts/pip install -r requirements.txt   # Windows
# source venv/bin/activate && pip install -r requirements.txt   # macOS/Linux

venv/Scripts/python db/seed.py     # DB 초기화 + 더미 데이터(사용자 3, 카테고리 2, 업무 5) 삽입
venv/Scripts/python app.py         # http://localhost:5050
```

시드 계정: 사번 `E001`/`E002`/`E003`, 비밀번호 공통 `1234`.

## 테스트

```bash
venv/Scripts/pytest tests/ -v
```

## 폐쇄망 배포 가이드

### 1. 오프라인 파이썬 패키지 준비 (인터넷 되는 PC에서)

```bash
pip download -r requirements.txt -d offline_packages
```

`offline_packages/` 폴더와 프로젝트 전체(정적 자산 `static/vendor` 포함)를 USB 등으로 내부망에 반입한다.

### 2. 내부망 서버에서 설치

```bash
python -m venv venv
venv/Scripts/pip install --no-index --find-links=offline_packages -r requirements.txt
venv/Scripts/python db/seed.py
```

### 3. 서버 자동 시작 (Windows)

작업 스케줄러에 "로그온 시 시작" 트리거로 아래 작업 등록:
- 프로그램: `<프로젝트경로>\venv\Scripts\python.exe`
- 인수: `<프로젝트경로>\app.py`
- 시작 위치: `<프로젝트경로>`

또는 NSSM 같은 도구로 Windows 서비스 등록도 가능하다.

### 4. SQLite 백업

`tasks.db`는 단일 파일이므로, 서버가 내려간 상태(또는 파일 잠금이 없는 새벽 시간대)에 아래처럼 주기적으로 복사하면 된다.

```bash
copy tasks.db backup\tasks_%date:~0,4%%date:~5,2%%date:~8,2%.db
```

작업 스케줄러로 매일 자정 실행하도록 등록해 백업을 자동화할 수 있다.

## 시나리오 테스트 체크리스트

- [ ] 사번/비밀번호로 로그인 → 대시보드로 이동하고 사용자 이름이 표시된다
- [ ] 카테고리 관리에서 새 카테고리를 생성하면 필터/등록 모달 드롭다운에 즉시 반영된다
- [ ] 업무를 등록하면 목록에 카드로 나타나고 상태 배지가 "진행중"이다
- [ ] 다른 계정으로 로그인해 방금 등록한 업무를 조회하면 403(조회 권한 없음)이 반환된다
- [ ] 카테고리 관리에서 해당 카테고리를 다른 사용자에게 공유하면, 그 사용자는 업무 목록/달력에서 해당 카테고리 업무를 볼 수 있다
- [ ] 업무를 개별 공유(공유 대상자 등록)하면, 대상자가 목록/달력에서 해당 업무를 볼 수 있다
- [ ] "완료 처리" 클릭 시 카드가 회색으로 바뀌고 상단 현황 요약의 완료 건수·진행률이 갱신된다
- [ ] "취소" 클릭 시 취소선이 표시되고 현황 요약에 반영된다
- [ ] 제목을 비운 채 저장하면 400 에러 메시지가 모달에 표시된다
- [ ] 종료일을 시작일보다 이전으로 입력하면 저장이 거부된다
- [ ] `/calendar`에서 업무가 시작일~종료일 구간의 이벤트로, 상태별 색상으로 표시된다
- [ ] 달력의 날짜를 클릭하면 그 날짜가 기본값으로 채워진 등록 모달이 열린다
- [ ] 오늘 시작/마감인 업무가 대시보드 상단 "오늘의 주요일정"에 노출되고, "수술/시술/치료" 카테고리 업무가 우선 정렬된다
- [ ] 로그아웃 후 `/dashboard`에 직접 접근하면 `/login`으로 리다이렉트된다 (미로그인 시 `/api/*` 401 → `requireLogin()`이 이동시킴)
