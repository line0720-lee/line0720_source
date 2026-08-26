# 업무 공유 매니저 (Task Share Manager) — 설계 문서

- 작성일: 2026-08-26
- 담당: 정보전략파트(이영동) / 실무형 AI 추진 T/F
- 개발 기간: 2026-09-01 ~ 2026-10-16 (약 6주, 5단계 분할)
- 상태: **설계 확정 (1~5단계) — 구현 시작 예정**

## 1. 배경 (5W1H)

| 구분 | 내용 |
|------|------|
| Why | 개인별 업무·일정을 체계적으로 관리하고, 필요한 대상에게만 선택적으로 공유할 도구가 없어 협업에 어려움이 있음 |
| What | 업무 카테고리 관리 / 카테고리별 공유 대상자 등록 / 사용자별 업무 등록·일정 관리 / 업무 상태(진행중·완료·취소) 관리 / 대상자 지정 기반 공유 / 당일 주요일정 안내 / 달력 시각화 |
| Who | 개발: 정보전략파트. 사용자: 실무 담당자 개인. 공유 대상: 카테고리·업무별 지정 동료 |
| When | 2026-09-01 ~ 2026-10-16, 5단계로 분할 |
| Where | 병원 내부망(폐쇄망) 배포. 외부 CDN 사용 불가 → 라이브러리는 `/static`에 로컬 번들 |
| How | Python Flask + SQLite(단일 파일 DB) + HTML/Bootstrap + FullCalendar(로컬 번들) |

## 2. 프로젝트 위치

- 새 폴더: `task-share-manager/` (저장소 루트, 기존 IT_Error/SFM 등과 동급의 독립 프로젝트)
- 실행 포트: **5050** (저장소 내 기존 견적서 Flask 앱이 5000 사용 중이라 충돌 방지)
- 가상환경: `venv`, `requirements.txt`에는 `flask`만 (SQLite는 표준 라이브러리)

## 3. 데이터 모델 (최종, 인증 반영)

```sql
users(
  id INTEGER PRIMARY KEY,
  emp_no TEXT UNIQUE NOT NULL,      -- 사번
  name TEXT NOT NULL,
  dept TEXT,
  password_hash TEXT NOT NULL,      -- werkzeug.security 해시
  created_at TEXT DEFAULT CURRENT_TIMESTAMP
)

categories(
  id INTEGER PRIMARY KEY,
  name TEXT NOT NULL,
  description TEXT,
  owner_id INTEGER NOT NULL REFERENCES users(id)
)

category_shares(
  category_id INTEGER REFERENCES categories(id),
  user_id INTEGER REFERENCES users(id),
  PRIMARY KEY(category_id, user_id)
)

tasks(
  id INTEGER PRIMARY KEY,
  title TEXT NOT NULL,
  content TEXT,
  category_id INTEGER REFERENCES categories(id),
  assignee_id INTEGER REFERENCES users(id),
  start_date TEXT,
  end_date TEXT,
  status TEXT NOT NULL DEFAULT '진행중' CHECK(status IN ('진행중','완료','취소')),
  created_at TEXT DEFAULT CURRENT_TIMESTAMP
)

task_shares(
  task_id INTEGER REFERENCES tasks(id),
  user_id INTEGER REFERENCES users(id),
  PRIMARY KEY(task_id, user_id)
)
```

- 시드 데이터: 더미 사용자 3명(초기 비밀번호 `"1234"` 해시), 카테고리 2개, 업무 5개
- "진료현장 수기대상(수술·시술·치료)" 우선 노출은 **별도 스키마 없이 카테고리로 구분**한다 (예: "수술", "시술", "치료" 카테고리). tasks 테이블에 추가 필드를 두지 않음.

## 4. 단계별 설계

### 1단계 — 프로젝트 초기 구성 & 데이터 모델
- 폴더 구조: `app.py`, `requirements.txt`, `db/schema.sql`, `db/init_db.py`, `db/seed.py`, `templates/`, `static/`, `.gitignore`, `README.md`
- `/health` 헬스체크 라우트: `{"status":"ok","db":"connected"}`
- 실행 순서: `python db/init_db.py` → `python db/seed.py` → `python app.py`

### 2단계 — 백엔드 API (인증 + CRUD + 공유)
- **인증**
  - `POST /api/register` {emp_no, name, dept, password}
  - `POST /api/login` {emp_no, password} → 세션에 `user_id` 저장
  - `POST /api/logout`
  - `GET /api/me`
  - `@login_required` 데코레이터로 이후 모든 API 보호 (세션 없으면 401)
- **카테고리 API** (`/api/categories`): CRUD + `POST/DELETE /api/categories/<id>/shares` (소유자만 공유 등록/해제 가능)
- **업무 API** (`/api/tasks`): CRUD + `PATCH /api/tasks/<id>/status` (진행중/완료/취소) + `POST/DELETE /api/tasks/<id>/shares`
- **권한 규칙**: 조회 가능 조건 = `assignee_id == 로그인 사용자` OR 카테고리 소유자 OR 카테고리 공유대상 OR 업무 개별 공유대상 (하나라도 해당 시 조회 가능)
- **에러 포맷**: `{"error": "메시지"}` + 상태코드(400/401/403/404/409) 통일
- 각 라우트에 curl 요청 예시를 주석으로 첨부

### 3단계 — 핵심 UI
- Bootstrap 기반, 라이브러리는 `/static`에 로컬 번들 (CDN 금지)
- **상단 사용자 표시는 임시 셀렉트가 아니라 실제 로그인 화면 사용** (2단계 인증 API와 일치시키기 위한 결정 — 원래 초안의 "로그인 대체용 셀렉트"는 폐기)
  - 로그인 페이지(사번+비밀번호) → 세션 유지 → 상단에는 로그인된 사용자 이름/로그아웃 버튼만 표시
- 카테고리 필터 + 업무 목록(카드 또는 테이블, 상태별 색상 구분)
- 업무 등록·수정 모달(제목/내용/카테고리/담당자/시작일/종료일/상태)
- 상태 변경 버튼(완료 처리/취소)
- 카테고리 생성 및 카테고리별 공유 대상자 지정 화면
- 2단계 API 연동, 반응형 레이아웃

### 4단계 — 달력 시각화 & 당일 주요일정
- FullCalendar(로컬 번들)로 업무 시작일~종료일 이벤트 표시, 상태별 색상
- 이벤트 클릭 → 업무 상세 모달, 날짜 클릭 → 해당 날짜로 업무 즉시 등록
- 대시보드 상단 "오늘의 주요일정" 위젯: 당일 시작/마감 업무 + "수술/시술/치료" 카테고리 업무를 우선 노출
- 월간/주간 보기 전환, 3단계와 동일한 디자인 톤 유지

### 5단계 — 선택적 공유 완성 & 테스트·배포
- **권한 규칙 최종 점검**: "이 업무를 특정 대상에게만 공유" 흐름이 목록/달력 양쪽에서 실제로 대상자 외에는 노출되지 않는지 회귀 점검 (2단계 권한 규칙을 UI/달력까지 일관 적용)
- **담당자별 업무 현황 요약 화면**: 로그인 사용자 기준 진행중/완료/취소 건수 + 진행률(완료 건수 / 전체 건수) 표시
- **예외 처리·유효성 검사 보강**: 빈 값(제목 등 필수값 누락), 날짜 역전(종료일 < 시작일), 존재하지 않는 category_id/assignee_id 등 400 에러로 명확히 반환
- **폐쇄망 배포 가이드** (README에 포함): 오프라인 pip 패키지 설치법(`pip download` → 내부망 반입 → `pip install --no-index`), Windows 서비스/작업 스케줄러로 서버 자동 시작, `tasks.db` 파일 백업 방법(주기적 파일 복사)
- **시나리오 테스트 체크리스트**: 등록 → 공유 대상 지정 → 상태 변경(진행중→완료/취소) → 달력에서 확인, 각 단계별 기대 결과를 체크리스트 형태로 README 또는 별도 문서에 정리
- 최종적으로 전체 기능을 한 번 통합 점검하고 README를 정리

## 5. 미해결/보류 항목

- 없음 — 1~5단계 설계 확정 완료. 다음 단계는 구현 계획 수립(superpowers:writing-plans) 및 실제 코드 작성.
