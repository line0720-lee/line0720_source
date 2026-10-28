# 온프레미스 STT 변환기 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **단계 게이트:** Task 1~7은 사용자 프롬프트의 1~7단계와 1:1로 대응한다. 각 Task가 끝나면 멈추고 "만든 파일 / 실행한 테스트와 결과 / 남은 문제"를 보고한 뒤 사용자 승인을 받는다. 커밋도 승인 때 함께 한다. 저장소 루트(`Study\`)에 다른 프로젝트의 미커밋 변경이 많으므로 `git add`는 반드시 `STT변환기/` 아래 경로만 지정한다.

**Goal:** 선택한 폴더의 .wav를 로컬 Whisper(faster-whisper, CPU int8)로 변환해 `선택폴더\result\STT_결과_YYYYMMDD_HHMMSS.xlsx`에 1파일 1행으로 기록하는, 폐쇄망 Windows 11 일반 사용자 PC용 `STT변환기.exe`(PyInstaller onedir)를 만든다.

**Architecture:** `stt_engine`(모델 1회 로딩·1건 변환) ← `batch_runner`(검색·순차 처리·오류 격리) → `excel_writer`(행 기록·건별 저장). `main`은 tkinter GUI와 `--no-gui` 명령줄 모드 둘 다 제공하고, 변환은 작업 스레드에서 돌리며 화면과는 `queue.Queue` + `root.after()`로만 통신한다. 경로·설정·로그·사용자 설정은 `config`가 맡는다.

**Tech Stack:** Python 3.11, faster-whisper(CTranslate2, PyAV, onnxruntime VAD), openpyxl, tkinter, psutil, PyInstaller, pytest(개발용)

**Spec:** `design.md` (2026-10-28 개정본) + 사용자 프롬프트(조건 1~11, 추가 조건 12~22)

## Global Constraints

- 실행 중 외부 네트워크 접속 금지. `faster_whisper`를 import하기 **전에** `os.environ["HF_HUB_OFFLINE"]="1"` 설정. 모델은 `config.ini` 기반 로컬 경로로만 로딩(`local_files_only=True`).
- `device="cpu"`, `compute_type="int8"`, `cpu_threads`=물리 코어 수(psutil). CUDA 코드·DLL 금지.
- 모델: `small` / `medium` / `large-v3-turbo`, 기본 `medium`. 경로 = `{models_dir}\{name}\` (예: `models\medium\`). 상대경로는 **exe 폴더**(개발 중엔 프로젝트 루트) 기준, CWD 기준 금지.
- STT: `language`(config, 기본 `ko`), `vad_filter=True`, `beam_size=5`, `initial_prompt`=glossary.txt(없으면 생략, 약 224토큰 초과 시 앞부분만 쓰고 로그 경고).
- 텍스트 설정 파일(config.ini, glossary.txt, user_settings.ini)은 `utf-8-sig`로 읽는다.
- 결과: `선택폴더\result\`. 시트 "STT결과", 열 A 번호 / B 파일명 / C 변환 내용 / D 재생 시간(hh:mm:ss) / E 처리 시간(초) / F 상태 / G 변환 일시. 머리글 굵게·배경 `D9D9D9`·틀 고정(A2), C열 너비 100·줄바꿈·위쪽 맞춤, 실패 행 F열 빨간 글씨(`FF0000`). 1건마다 저장.
- B열: 선택 폴더 바로 아래 파일은 파일명만, 하위폴더 파일은 항상 상대경로(`2팀\회의녹음_01.wav`).
- 상태 문구(설계서 고정): `완료`, `완료(인식된 음성 없음)`, `실패(파일 손상)`, `실패(메모리 부족)`, 그 밖은 `실패(사유)`.
- 대상 폴더는 `%APPDATA%\STT변환기\user_settings.ini`에 기억.
- 로그: `{exe 폴더}\logs\YYYY-MM-DD.log`, 쓰기 불가 시 `문서\STT변환기\logs\`.
- 코드 주석·화면 문구는 한국어. 관리자 권한·레지스트리·설치 프로그램 사용 금지.
- 소스는 `src\`에 평면 모듈로 두고 `from config import ...`처럼 import한다(`pytest.ini`의 `pythonpath = src`).

## Review Focus

1. **한글·괄호가 든 경로에서 모델 로딩** — 개발 폴더(`C:\Claude(바이브코딩)\Study\STT변환기`)와 배포 이름(`STT변환기\`) 모두 비ASCII다. Windows에서 CTranslate2가 비ASCII 경로의 `model.bin`을 못 여는 사례가 있으므로 Task 2에서 실제 경로로 먼저 확인하고, 실패하면 8.3 짧은 경로 → `files=` 메모리 로딩 순으로 대체한다. (Task 2 `test_engine_loads_from_korean_path`)
2. **변환 중 사용자가 결과 엑셀을 열어 둠** — 저장이 PermissionError가 나도 변환은 계속되고 `_임시저장.xlsx`로 이어 저장해야 한다. (Task 4 `test_save_falls_back_when_locked`)
3. **쓰기 불가 폴더 선택(읽기 전용 공유폴더 등)** — 1시간 변환 뒤가 아니라 시작 전에 막아야 한다. (Task 3 `test_unwritable_output_stops_before_engine`)
4. **긴 파일(1시간 이상)** — 화면이 멈추지 않고 파일 안 진행률이 움직이며, 32,767자 초과 처리가 맞아야 한다. (Task 2 진행률 콜백, Task 4 `test_overflow_*`)
5. **이름만 .wav인 파일·0바이트·8kHz 모노·44.1kHz 스테레오** — 앞 두 개는 `실패(파일 손상)`으로 넘어가고, 뒤 두 개는 정상 변환돼야 한다. (Task 2 샘플, Task 3 `test_corrupt_file_is_recorded_and_skipped`)

## File Structure

```
STT변환기\                       ← 프로젝트 루트 (= 배포 폴더 이름과 같음)
├─ design.md
├─ src\
│   ├─ config.py        경로(app_dir)·config.ini·로그·사용자 설정·glossary 읽기
│   ├─ records.py       ResultRow 데이터클래스 (batch_runner ↔ excel_writer 공유)
│   ├─ stt_engine.py    모델 로딩, 1건 변환, 명령줄 테스트 진입점
│   ├─ batch_runner.py  .wav 검색, result 폴더 확인, 순차 처리, 오류 분류
│   ├─ excel_writer.py  엑셀 생성·서식·건별 저장·잠김 대체 저장·32,767자 초과
│   └─ main.py          tkinter GUI + --no-gui 명령줄 모드
├─ config.ini, glossary.txt
├─ requirements.txt, requirements-dev.txt, pytest.ini, .gitignore
├─ download_model.py
├─ build.bat, stt.spec
├─ tools\check_dist.py, tools\benchmark.py, tools\package.py
├─ verify\offline.wsb, verify\run_in_sandbox.cmd, verify\체크리스트.md
├─ tests\make_samples.ps1, tests\test_*.py, tests\samples\ (생성물, git 제외)
├─ models\ (git 제외)
└─ README.md
```

---

### Task 1: 환경 구축·모델 다운로드 (1단계)

**Files:**
- Create: `requirements.txt`, `requirements-dev.txt`, `pytest.ini`, `.gitignore`, `config.ini`, `glossary.txt`, `download_model.py`, `README.md`(뼈대)

**Interfaces:**
- Produces: `.venv\`(Python 3.11), `models\{small|medium|large-v3-turbo}\` 안에 `model.bin`, `config.json`, `tokenizer.json`, `vocabulary.*`, `preprocessor_config.json`

- [ ] **Step 1: Python 3.11 설치 (사용자 승인 필요)** — 현재 PC에는 3.14만 있다. `winget install --id Python.Python.3.11 --scope user` (관리자 불필요). 확인: `py -3.11 --version` → `Python 3.11.x`
- [ ] **Step 2: 가상환경** — `py -3.11 -m venv .venv` 후 `.venv\Scripts\python -m pip install -r requirements.txt -r requirements-dev.txt`
  - `requirements.txt`: `faster-whisper`, `openpyxl`, `psutil`, `pyinstaller` — 설치 직후 `pip freeze`로 확인한 정확한 버전을 `==`로 고정한다. `nvidia-*` 패키지가 설치되면 안 된다.
  - `requirements-dev.txt`: `pytest`
- [ ] **Step 3: 설정 파일 작성**
  - `config.ini`:
    ```ini
    [model]
    ; small / medium / large-v3-turbo
    name = medium
    ; 상대경로는 STT변환기.exe가 있는 폴더 기준
    models_dir = models
    [stt]
    language = ko
    ```
  - `glossary.txt`: 한 줄에 용어 하나, 예시 3줄과 `#` 주석 안내.
  - `.gitignore`: `.venv/ models/ build/ dist/ tests/samples/ __pycache__/ *.xlsx release/`
- [ ] **Step 4: `download_model.py`** — `python download_model.py [small] [medium] [large-v3-turbo]`(인자 없으면 `medium`). `huggingface_hub.snapshot_download(repo_id, local_dir=models\{name})`. 저장소: `Systran/faster-whisper-small`, `Systran/faster-whisper-medium`, `mobiuslabsgmbh/faster-whisper-large-v3-turbo`. 끝나면 `model.bin` 존재와 폴더 크기를 출력하고, 없으면 종료 코드 1.
- [ ] **Step 5: 실행** — `.venv\Scripts\python download_model.py medium small large-v3-turbo`
  Expected: 세 폴더에 `model.bin`이 있고 크기 출력(대략 small 0.5GB, medium 1.5GB, turbo 1.6GB)
- [ ] **Step 6: 보고·승인 후 커밋** — `git add STT변환기/requirements*.txt STT변환기/pytest.ini STT변환기/.gitignore STT변환기/config.ini STT변환기/glossary.txt STT변환기/download_model.py STT변환기/README.md STT변환기/design.md STT변환기/docs`

### Task 2: config·stt_engine — .wav 1건 변환 (2단계)

**Files:**
- Create: `src\config.py`, `src\stt_engine.py`, `tests\make_samples.ps1`, `tests\test_config.py`, `tests\test_stt_engine.py`, `tests\conftest.py`

**Interfaces:**
- Produces (`config.py`):
  - `app_dir() -> Path` — frozen이면 `Path(sys.executable).parent`, 아니면 프로젝트 루트
  - `class ConfigError(Exception)` — 화면에 그대로 보여 줄 한국어 메시지
  - `@dataclass(frozen=True) class AppConfig: model_name: str; model_path: Path; language: str; cpu_threads: int; glossary_path: Path`
  - `load_config(path: Path | None = None) -> AppConfig` — 기본 `app_dir()/"config.ini"`, 이름이 세 모델이 아니면 `ConfigError`
  - `physical_cores() -> int` — `psutil.cpu_count(logical=False) or os.cpu_count() or 4`
  - `read_glossary(path: Path) -> str | None` — 빈 줄·`#` 줄 제외, `", "`로 이어 붙임. 파일 없거나 비면 `None`
  - `setup_logging(base: Path | None = None) -> Path` — 로그 폴더 반환(쓰기 불가 시 문서 폴더로 대체)
  - `load_last_folder() -> Path | None`, `save_last_folder(folder: Path) -> None`
- Produces (`stt_engine.py`):
  - `class ModelNotFoundError(Exception)`, `class CorruptFileError(Exception)`
  - `@dataclass class TranscribeResult: text: str; duration_sec: float; elapsed_sec: float`
  - `class SttEngine: __init__(self, cfg: AppConfig)`; `transcribe(self, wav: Path, on_progress: Callable[[float], None] | None = None) -> TranscribeResult`
  - 모듈 맨 위에서 `os.environ["HF_HUB_OFFLINE"]="1"`을 설정한 뒤 `faster_whisper`를 import

- [ ] **Step 1: 샘플 생성 스크립트** — `tests\make_samples.ps1`: `System.Speech`의 `Microsoft Heami Desktop`(ko-KR, 이 PC에 설치 확인됨)으로 `tests\samples\`에 다음을 만든다: `기본_16k.wav`(16kHz 모노, 문장 3개), `전화_8k.wav`(8kHz 모노), `스테레오_44k.wav`(44.1kHz 스테레오, `SpeechAudioFormatInfo`), `무음.wav`(10초 무음), `빈파일.wav`(0바이트), `가짜.wav`(텍스트 파일 이름만 .wav), `하위 폴더 (2팀)\회의 #1.wav`. 긴 파일(`긴파일_65분.wav`)은 `-Long` 옵션일 때만 만든다(문장 반복 합성).
- [ ] **Step 2: 실패하는 테스트 작성** — `tests\test_config.py`
  - `test_load_config_defaults`: 임시 config.ini(`name = medium`, `models_dir = models`) → `cfg.model_path == tmp/"models"/"medium"`, `cfg.language == "ko"`, `cfg.cpu_threads >= 1`
  - `test_load_config_rejects_unknown_model`: `name = large` → `ConfigError`
  - `test_relative_models_dir_ignores_cwd`: CWD를 다른 폴더로 바꿔도 `model_path`가 config.ini 폴더 기준
  - `test_read_glossary_utf8_sig`: BOM 포함 `"# 주석\n전산팀\n\nEMR\n"` → `"전산팀, EMR"`; 파일 없음 → `None`
  - `test_last_folder_roundtrip`: `APPDATA`를 tmp로 바꾸고 저장 후 읽기 → 같은 경로; 저장된 폴더가 지워졌으면 `None`
  - `test_setup_logging_falls_back`: 쓰기 불가 base(존재하지 않는 드라이브 등) → 문서 폴더 아래 경로 반환
- [ ] **Step 3: 실행해 실패 확인** — `.venv\Scripts\python -m pytest tests\test_config.py -v` → ImportError로 FAIL
- [ ] **Step 4: `src\config.py` 구현** → 다시 실행해 PASS
- [ ] **Step 5: 엔진 테스트 작성** — `tests\test_stt_engine.py` (`@pytest.mark.model`, `models\small`이 없으면 skip; 빠른 small로 시험)
  - `test_engine_loads_from_korean_path`: 프로젝트 경로 그대로(`C:\Claude(바이브코딩)\...`) 로딩 성공
  - `test_transcribe_basic`: `기본_16k.wav` → 텍스트에 합성 문장의 핵심 단어 1개 이상 포함, `duration_sec > 0`, `elapsed_sec > 0`
  - `test_transcribe_8k_and_stereo`: 두 파일 모두 빈 문자열이 아님
  - `test_silence_returns_empty`: `무음.wav` → `text == ""`
  - `test_zero_byte_and_fake_raise_corrupt`: 두 파일 모두 `CorruptFileError`
  - `test_progress_callback_monotonic`: 콜백 값이 0~1 사이에서 줄지 않음
  - `test_missing_model_raises`: 없는 경로 → `ModelNotFoundError`(메시지에 경로 포함)
  - `test_no_network_during_transcribe`: `socket.socket.connect`를 예외를 내도록 바꿔 놓고 로딩+변환 성공
- [ ] **Step 6: `src\stt_engine.py` 구현**
  - `model_path/"model.bin"`이 없으면 `ModelNotFoundError`
  - `WhisperModel(str(path), device="cpu", compute_type="int8", cpu_threads=cfg.cpu_threads, local_files_only=True)`. 비ASCII 경로에서 로딩이 실패하면 `GetShortPathNameW` 짧은 경로로 다시 시도하고, 그래도 실패하면 `files=`(파일 내용을 메모리로 읽어 전달)로 로딩. 어떤 방법을 썼는지 로그에 남긴다.
  - glossary는 `self._model.hf_tokenizer`로 토큰 수를 세어 223토큰을 넘으면 앞부분만 쓰고 경고 로그(faster-whisper 자체는 **뒷부분**을 남기므로 직접 잘라야 한다)
  - 0바이트이거나 `decode_audio`/`av` 예외면 `CorruptFileError`
  - 세그먼트 텍스트는 `strip()` 후 줄바꿈(`"\n"`)으로 연결. 진행률 = `seg.end / info.duration`
  - `if __name__ == "__main__":` `python src\stt_engine.py <wav> [--model small]` → 텍스트·재생 시간·처리 시간 출력
- [ ] **Step 7: 실행** — `powershell -ExecutionPolicy Bypass -File tests\make_samples.ps1`(System.Speech는 Windows PowerShell 5.1에 있음) 후 `.venv\Scripts\python -m pytest tests -v -m model` → PASS; `.venv\Scripts\python src\stt_engine.py tests\samples\기본_16k.wav` → 한국어 텍스트 출력
- [ ] **Step 8: 보고·승인 후 커밋**

### Task 3: batch_runner — 폴더 일괄 처리·예외 처리 (3단계)

**Files:**
- Create: `src\records.py`, `src\batch_runner.py`, `tests\test_batch_runner.py`

**Interfaces:**
- Consumes: `SttEngine.transcribe`, `CorruptFileError`, `TranscribeResult` (Task 2)
- Produces (`records.py`): `@dataclass class ResultRow: index: int; name: str; text: str; duration_sec: float | None; elapsed_sec: float; status: str; finished_at: datetime` + `failed` 속성(`status.startswith("실패")`)
- Produces (`batch_runner.py`):
  - `class NoWavFilesError(Exception)`, `class OutputNotWritableError(Exception)`
  - `RESULT_DIR_NAME = "result"`
  - `find_wavs(folder: Path, include_subfolders: bool) -> list[Path]` — 확장자 대소문자 무시, `folder/"result"` 아래 제외, 상대경로 문자열 기준 정렬
  - `display_name(wav: Path, root: Path) -> str` — 상대경로, 구분자 `\`
  - `prepare_result_dir(folder: Path) -> Path` — 생성 + 시험 파일 쓰기·삭제, 실패 시 `OutputNotWritableError`
  - `@dataclass class Progress: index: int; total: int; name: str; file_fraction: float; done: int; failed: int`
  - `@dataclass class BatchSummary: total: int; done: int; failed: int; stopped: bool; excel_path: Path | None; memory_error: bool`
  - `class RowWriter(Protocol): path: Path; def add_row(self, row: ResultRow) -> None`
  - `run_batch(folder: Path, include_subfolders: bool, engine, writer_factory: Callable[[Path, datetime], RowWriter], on_progress: Callable[[Progress], None], stop_event: threading.Event, on_row: Callable[[ResultRow], None] | None = None) -> BatchSummary` — `on_row`는 파일 1건이 끝날 때마다(엑셀 저장 후) 호출, 화면의 파일 목록 표 갱신용

- [ ] **Step 1: 실패하는 테스트 작성** — 가짜 엔진(`FakeEngine`: 파일명에 따라 텍스트 반환·`CorruptFileError`·`MemoryError`·`RuntimeError("디코더 오류")`)과 리스트에 쌓는 가짜 writer 사용
  - `test_find_wavs_flat_vs_recursive`: 하위폴더 옵션에 따라 개수가 다름, `.WAV` 포함, `result\` 안의 .wav 제외
  - `test_display_name_relative`: `root\a.wav` → `"a.wav"`, `root\2팀\b.wav` → `"2팀\\b.wav"`
  - `test_no_wavs_raises_and_no_result_dir`: `NoWavFilesError`이고 `result\` 폴더가 생기지 않음
  - `test_unwritable_output_stops_before_engine`: `prepare_result_dir`가 실패하도록 만들면 `OutputNotWritableError`, 엔진 호출 0회
  - `test_status_mapping`: 정상 → `"완료"`, 빈 텍스트 → `"완료(인식된 음성 없음)"`, `CorruptFileError` → `"실패(파일 손상)"`, `MemoryError` → `"실패(메모리 부족)"`이고 `summary.memory_error is True`, `RuntimeError("디코더 오류")` → `"실패(디코더 오류)"`
  - `test_corrupt_file_is_recorded_and_skipped`: 3건 중 가운데 실패 → 행 3개, 번호 1·2·3, `done == 2`, `failed == 1`
  - `test_stop_after_current_file`: 첫 파일 처리 중 `stop_event.set()` → 행 1개, `stopped is True`, 미처리 건은 기록 안 함
  - `test_progress_events`: 마지막 이벤트 `index == total`, `file_fraction` 0~1, 각 이벤트의 `done + failed`가 이미 끝난 건수와 같음
  - `test_on_row_called_per_file`: 3건 처리 → `on_row` 3회, 받은 `ResultRow.index`가 1·2·3이고 상태가 writer에 기록된 것과 같음
- [ ] **Step 2: 실행해 실패 확인** — `pytest tests\test_batch_runner.py -v`
- [ ] **Step 3: 구현** — 순서: `find_wavs` → 없으면 `NoWavFilesError` → `prepare_result_dir` → `writer_factory(result_dir, started_at)` → 파일마다 `try/except`로 상태 결정 후 `add_row`. 예외는 로그에 traceback까지 남기고, 엑셀 사유는 첫 줄 60자 이내로 줄인다.
- [ ] **Step 4: 실행해 PASS 확인**, 이어 실제 엔진으로 `tests\samples` 일괄 처리 스모크(`writer_factory`는 가짜) 1회
- [ ] **Step 5: 보고·승인 후 커밋**

### Task 4: excel_writer — 엑셀 생성·서식·건별 저장 (4단계)

**Files:**
- Create: `src\excel_writer.py`, `tests\test_excel_writer.py`

**Interfaces:**
- Consumes: `ResultRow` (Task 3), `RowWriter` 프로토콜 충족
- Produces:
  - `CELL_LIMIT = 32767`, `OVERFLOW_NOTE = "\n…(이하 생략: '전문' 시트와 txt 파일 참조)"`
  - `class ExcelWriter: __init__(self, result_dir: Path, started_at: datetime, on_warning: Callable[[str], None] | None = None)`
  - `path: Path` (대체 저장 후에는 새 경로), `add_row(self, row: ResultRow) -> None`

- [ ] **Step 1: 실패하는 테스트 작성** — 저장된 파일을 `openpyxl.load_workbook`으로 다시 열어 확인
  - `test_filename_and_location`: `started_at=2026-10-28 09:30:15` → `result_dir/"STT_결과_20261028_093015.xlsx"`
  - `test_header_and_format`: 시트명 `"STT결과"`, A1~G1이 `번호, 파일명, 변환 내용, 재생 시간, 처리 시간(초), 상태, 변환 일시`, 굵게, 배경 `D9D9D9`, `freeze_panes == "A2"`, C열 너비 100, C2 `wrap_text`·`vertical == "top"`
  - `test_row_values`: `duration_sec=754.4` → D `"00:12:34"`, `elapsed_sec=85.4` → E `85`, G `"2026-10-28 09:31:40"`, 실패 행 F 글자색 `FF0000`, 성공 행은 빨강 아님, `duration_sec=None` → D 빈칸
  - `test_saved_after_each_row`: `add_row` 1회 직후 파일을 열면 데이터 행 1개
  - `test_overflow_cell_sheet_txt`: 길이 70,000 텍스트 → C 길이 ≤ 32,767이고 `OVERFLOW_NOTE`로 끝남; "전문" 시트에 (번호, 파일명, 조각, 내용) 3행, 이어 붙이면 원문과 같음; `result_dir/"2팀_회의녹음_01.txt"`(B열 `2팀\회의녹음_01.wav`의 `\`→`_`, 확장자 .txt) 내용이 원문과 같음
  - `test_hundred_rows`: 100행 추가 → 데이터 행 100개, 번호 1~100 연속(설계서 테스트 "100건 일괄 처리")
  - `test_save_falls_back_when_locked`: 저장 함수가 3번 `PermissionError`를 내게 만들면 `path`가 `..._임시저장.xlsx`로 바뀌고 `on_warning` 1회 호출, 다음 행도 새 파일에 저장
- [ ] **Step 2: 실행해 실패 확인**
- [ ] **Step 3: 구현** — 열 너비 A 6, B 40, C 100, D 12, E 14, F 28, G 20. 저장 재시도 3회, 간격 1초(테스트에서는 `sleep` 주입). "전문" 시트는 첫 초과 때 만든다.
- [ ] **Step 4: 실행해 PASS 확인**, 이어 실제 엔진 + `ExcelWriter`로 `tests\samples` 일괄 처리 → 결과 xlsx를 열어 행 수·상태·서식을 출력해 확인(설계서 예외 표의 엑셀 표기와 대조)
- [ ] **Step 5: 보고·승인 후 커밋**

### Task 5: main — GUI와 명령줄 모드 (5단계)

**Files:**
- Create: `src\main.py`, `tests\test_main_cli.py`

**Interfaces:**
- Consumes: `load_config`, `setup_logging`, `load_last_folder`, `save_last_folder`, `SttEngine`, `run_batch`, `ExcelWriter`, 예외 클래스들
- Produces: `main(argv: list[str] | None = None) -> int`; 명령줄 `--folder <경로> --no-gui [--subfolders]`, 종료 코드 0=모두 완료, 1=실패 건 있음, 2=시작 불가(모델 없음·.wav 없음·쓰기 불가)

- [ ] **Step 1: 실패하는 테스트 작성** — `tests\test_main_cli.py` (`@pytest.mark.model`)
  - `test_cli_converts_samples`: tmp에 `기본_16k.wav`, `빈파일.wav` 복사 → `main(["--folder", tmp, "--no-gui"]) == 1`, `tmp/"result"`에 xlsx 1개, 행 2개
  - `test_cli_no_wavs_returns_2`: 빈 폴더 → `2`, `result\` 없음
- [ ] **Step 2: 구현**
  - 화면 구성(위→아래):
    1. 대상 폴더(읽기 전용 입력칸) + [폴더 변경], [하위폴더 포함] 체크박스, 오른쪽에 "대상 파일 N개"
    2. [시작] [중지] [결과 파일 열기](완료 후 활성, `os.startfile`)
    3. 전체 진행률 바(`(index-1+file_fraction)/total`) + "n / 전체 N건 (xx%)"
    4. "현재 파일: …" + 파일 안 진행률 바(`file_fraction`)
    5. 건수 3칸: "전체 N" / "성공 n"(초록) / "실패 n"(빨강). 화면에서는 "성공"이라 쓰고, 엑셀 F열은 설계서대로 "완료"
    6. 파일 목록 표(`ttk.Treeview`, 세로 스크롤): 번호 / 파일명 / 상태 / 재생 시간 / 처리 시간. [시작] 전에는 모두 "대기", 처리 중인 행은 "변환 중"(굵게 또는 강조색, 자동으로 그 행까지 스크롤), 끝나면 `on_row`로 "완료" 또는 "실패(사유)"(빨간 글씨)
    7. 상태 표시줄(모델 로딩 중, 중지 요청됨, 임시저장 전환 등)
  - 폴더를 고르거나 [하위폴더 포함]을 바꾸면 그 자리에서 `find_wavs`를 다시 실행해 개수와 목록 표를 새로 채운다(0개면 [시작] 비활성화 + "대상 .wav 파일이 없습니다")
  - 변환 중에는 [폴더 변경]·[하위폴더 포함]·[시작] 비활성화, [중지]만 활성. 끝나면 반대로
  - 첫 실행(기억한 폴더 없음)이나 기억한 폴더가 사라졌으면 창이 뜬 직후 폴더 선택 창을 띄우고 선택 즉시 `save_last_folder`
  - 프로그램 시작 시 `load_config` + 모델 폴더 존재 확인. 실패하면 오류 안내 후 [시작] 비활성화. 모델 로딩은 첫 [시작] 때 작업 스레드에서 1회(상태 표시줄 "모델을 불러오는 중…"), 이후 재사용
  - 작업 스레드 → 화면은 `queue.Queue`에 넣고 `root.after(100, ...)`로 꺼내 반영(tkinter는 스레드에 안전하지 않음)
  - [중지] → `stop_event.set()`, 상태 표시줄 "현재 파일을 끝낸 뒤 멈춥니다"
  - 변환 중 창 닫기 → 확인창 후 중지 요청, 현재 파일이 끝나면 종료
  - 완료 알림: 건수 요약. `memory_error`면 "small 모델로 바꿔 보세요(config.ini의 name)" 안내. 임시저장으로 바뀌었으면 그 파일명을 알림
- [ ] **Step 3: 테스트 PASS 확인** 후 `.venv\Scripts\python src\main.py`로 화면을 띄워 사용자와 함께 확인:
  - 첫 실행 때 폴더 선택 창 → 고른 폴더가 다음 실행에 채워짐
  - 폴더 선택·하위폴더 체크 변경 시 "대상 파일 N개"와 목록 표가 바로 바뀜
  - [시작] → 진행률 바 2개·현재 파일·성공/실패 건수·목록 표 상태가 실시간으로 바뀌고, 그동안 창을 움직이거나 크기를 바꿔도 멈추지 않음
  - [중지] → 현재 파일이 끝난 뒤 멈추고, 남은 행은 "대기"로 남음
  - 완료 후 [결과 파일 열기] → `result\` 안의 엑셀이 열림
- [ ] **Step 4: 보고·승인 후 커밋**

### Task 6: 빌드 — PyInstaller onedir (6단계)

**Files:**
- Create: `stt.spec`, `build.bat`, `tools\check_dist.py`

**Interfaces:**
- Produces: `dist\STT변환기\STT변환기.exe`, `dist\STT변환기\_internal\`, `dist\STT변환기\models\{config.ini의 name}\`, `config.ini`, `glossary.txt`

- [ ] **Step 1: `stt.spec`** — onedir, `console=False`, `name="STT변환기"`. `collect_data_files("faster_whisper")`(VAD `.onnx`가 `assets`에 있음), `collect_dynamic_libs("ctranslate2")`, `collect_dynamic_libs("onnxruntime")`, `collect_dynamic_libs("av")`, `collect_submodules("av")`; `excludes`에 `torch`, `nvidia`, `tensorflow`.
- [ ] **Step 2: `build.bat`** — `.venv` 확인 → `pyinstaller --noconfirm --clean stt.spec` → `config.ini`, `glossary.txt`, `models\{name}` 복사(`name`은 config.ini에서 읽음; `robocopy`) → `tools\check_dist.py` 실행, 실패하면 종료 코드 1
- [ ] **Step 3: `tools\check_dist.py`** — 다음을 확인하고 결과를 표로 출력: `STT변환기.exe`, `_internal\python311.dll`, `vcruntime140.dll`, `vcruntime140_1.dll`, `msvcp140.dll`, ctranslate2 DLL, onnxruntime DLL, `av` DLL, faster_whisper `assets\*.onnx`가 **있음**; 파일명에 `cublas`, `cudnn`, `cudart`, `cufft`, `nvrtc`가 들어간 DLL이 **없음**; `models\{name}\model.bin` 있음
- [ ] **Step 4: 실행** — `build.bat` → check_dist 전부 OK. 이어 Python이 보이지 않게 실행: `cmd /c "set PATH=%SystemRoot%\system32;%SystemRoot% && set PYTHONHOME= && set PYTHONPATH= && dist\STT변환기\STT변환기.exe --folder tests\samples --no-gui"` → 종료 코드 1(가짜·빈 파일 실패), `tests\samples\result\`에 xlsx
- [ ] **Step 5: 복사 실행** — `dist\STT변환기`를 `%USERPROFILE%\Desktop\STT변환기_테스트\`로 복사해 같은 명령 실행 → 성공(레지스트리·설치 없음 확인)
- [ ] **Step 6: 보고·승인 후 커밋**(`stt.spec`, `build.bat`, `tools\check_dist.py`만; `dist\`는 제외)

### Task 7: 오프라인 검증·모델별 시간·배포 압축 (7단계)

**Files:**
- Create: `verify\offline.wsb`, `verify\run_in_sandbox.cmd`, `verify\체크리스트.md`, `tools\benchmark.py`, `tools\package.py`
- Modify: `README.md`

**Interfaces:**
- Consumes: `dist\STT변환기\`, `SttEngine`
- Produces: `release\STT변환기_앱.zip`(모델 1개 포함), `release\모델_{name}.zip`(다른 모델들), `release\SHA256SUMS.txt`

- [ ] **Step 1: Windows Sandbox 준비 (사용자 작업)** — 이 PC에는 Sandbox가 켜져 있지 않다(`WindowsSandbox.exe` 없음). 켜려면 관리자 권한과 재부팅이 필요하므로 사용자가 직접 "Windows 기능 켜기/끄기 → Windows Sandbox"를 켠다. 켜지 않기로 하면 Step 2~3은 `verify\체크리스트.md`대로 다른 PC(네트워크 케이블 제거)에서 사용자가 실행한다.
- [ ] **Step 2: `verify\offline.wsb`** — `<Networking>Disable</Networking>`, `dist\STT변환기`(읽기 전용)와 `tests\samples`(쓰기 가능)를 매핑, `LogonCommand`로 `run_in_sandbox.cmd` 실행 → exe를 `--no-gui`로 돌리고 종료 코드·로그를 매핑 폴더에 남김. Sandbox에는 Python이 없으므로 "Python 미설치 + 네트워크 차단" 검증을 겸한다.
- [ ] **Step 3: 실행·확인** — Sandbox 실행 후 매핑 폴더의 xlsx·로그·종료 코드를 확인. 확인하지 못한 항목은 "미확인"으로 보고한다.
- [ ] **Step 4: `tools\benchmark.py`** — `python tools\benchmark.py --models small medium large-v3-turbo` → `기본_16k.wav`와 `긴파일_65분.wav`(있으면)로 모델별 로딩 시간·처리 시간·실시간 배수(처리 시간 / 재생 시간)를 표로 출력하고 README "모델별 처리 시간"에 붙인다. 측정 PC 사양(Intel Core Ultra 7 256V, 8코어, RAM 15.5GB)도 함께 적는다.
- [ ] **Step 5: `tools\package.py`** — `zipfile`(ZIP64 지원; PowerShell `Compress-Archive`는 큰 파일에서 문제가 있어 쓰지 않음)로 앱 zip(config.ini의 모델 1개 포함)과 나머지 모델별 zip을 `release\`에 만들고 `SHA256SUMS.txt`(`hashlib.sha256`) 작성
- [ ] **Step 6: README 완성** — 빌드 방법, 반입 절차(설계서 배포 절차 2), 압축 해제·해시 대조(`certutil -hashfile STT변환기_앱.zip SHA256`), 바탕화면 바로가기 만들기, 사용 방법, 모델 바꾸기(config.ini `name` + 모델 zip 풀기), 문제 해결(SmartScreen·백신 예외, 메모리 부족 시 small), 모델별 처리 시간 표. 해시값은 README가 아니라 `SHA256SUMS.txt`에만 둔다.
- [ ] **Step 7: 최종 보고·승인 후 커밋**(`verify\`, `tools\`, `README.md`; `release\`는 제외)
