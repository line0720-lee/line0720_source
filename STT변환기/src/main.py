"""STT 변환기 진입점: tkinter 화면(GUI)과 --no-gui 명령줄 모드.

- 화면 모드: python src\\main.py  (exe로 묶이면 STT변환기.exe 더블클릭)
- 명령줄 모드: python src\\main.py --folder <경로> --no-gui [--subfolders]
  종료 코드 0 = 모두 완료, 1 = 실패 건 있음, 2 = 시작 불가(설정·모델 없음, 폴더 없음, .wav 없음, 쓰기 불가)
"""
from __future__ import annotations

import ctypes
import os
import sys

# 온라인 모델 조회를 막는다. 프로젝트 모듈(faster_whisper)을 import하기 전에 지정해야 한다.
os.environ["HF_HUB_OFFLINE"] = "1"


_STD_STREAMS_MISSING = False  # 시작할 때 stdout/stderr가 없었는지(콘솔 없는 exe·pythonw)
_ATTACH_PARENT_PROCESS = -1  # AttachConsole: 이 프로그램을 실행한 부모 프로세스의 콘솔


def _prepare_std_streams() -> None:
    """콘솔 없는 exe(console=False)는 stdout/stderr가 None이라 print·로그 출력에서 죽으므로 빈 장치로 돌린다."""
    global _STD_STREAMS_MISSING
    for name in ("stdout", "stderr"):
        if getattr(sys, name) is None:
            _STD_STREAMS_MISSING = True
            setattr(sys, name, open(os.devnull, "w", encoding="utf-8"))  # noqa: SIM115 - 프로그램 끝까지 연다
    encoding = (getattr(sys.stdout, "encoding", "") or "").lower().replace("-", "")
    if encoding != "utf8":
        try:
            sys.stdout.reconfigure(encoding="utf-8", errors="replace")  # 한글 출력이 cp949에서 깨지지 않게
        except (AttributeError, OSError, ValueError):
            pass


_prepare_std_streams()


def _attach_parent_console() -> bool:
    """명령줄(--no-gui) 모드 전용: 콘솔 없는 exe를 cmd/PowerShell에서 실행했을 때 그 창에 진행 문구가 보이게 한다.

    부모 콘솔에 붙어(AttachConsole) stdout/stderr를 CONOUT$로 다시 연다. 원래 출력이 있었거나(python 실행·
    출력 리디렉션) 붙지 못하면(탐색기에서 실행 등) 아무것도 하지 않는다(빈 장치 유지). 화면 모드에서는 부르지 않는다.
    """
    if not _STD_STREAMS_MISSING:
        return False
    try:
        if not ctypes.windll.kernel32.AttachConsole(_ATTACH_PARENT_PROCESS):
            return False
        out = open("CONOUT$", "w", encoding="utf-8", errors="replace", buffering=1)  # noqa: SIM115
        try:
            err = open("CONOUT$", "w", encoding="utf-8", errors="replace", buffering=1)  # noqa: SIM115
        except (OSError, ValueError):
            out.close()
            raise
    except (AttributeError, OSError, ValueError):
        return False
    sys.stdout, sys.stderr = out, err
    return True


import argparse  # noqa: E402
import logging  # noqa: E402
import queue  # noqa: E402
import threading  # noqa: E402
import tkinter as tk  # noqa: E402
from dataclasses import replace  # noqa: E402
from pathlib import Path  # noqa: E402
from tkinter import filedialog, font as tkfont, messagebox, ttk  # noqa: E402

from batch_runner import (  # noqa: E402
    BatchSummary,
    NoWavFilesError,
    OutputNotWritableError,
    Progress,
    display_name,
    find_wavs,
    run_batch,
)
from config import (  # noqa: E402
    VALID_MODELS,
    AppConfig,
    ConfigError,
    load_config,
    load_last_folder,
    save_last_folder,
    setup_logging,
)
from excel_writer import ExcelWriter, format_duration  # noqa: E402
from records import ResultRow  # noqa: E402
from stt_engine import ModelNotFoundError, SttEngine  # noqa: E402

logger = logging.getLogger("main")

APP_TITLE = "STT 변환기"
EXIT_OK, EXIT_FAILED, EXIT_CANNOT_START = 0, 1, 2
_FALLBACK_SUFFIX = "_임시저장"
MEMORY_HINT = "메모리가 부족했습니다. config.ini의 name을 small로 바꿔 보세요."
MODEL_MEMORY_MESSAGE = "메모리가 부족해 모델을 불러오지 못했습니다. config.ini의 name을 small로 바꿔 보세요."
MEMORY_ERROR_MESSAGE = "메모리가 부족해 변환을 계속할 수 없습니다. config.ini의 name을 small로 바꿔 보세요."


# =============================================================== 화면과 무관한 순수 함수(테스트 대상)


def format_overall(index: int, total: int, fraction: float, completed: int) -> tuple[float, str]:
    """전체 진행률 바 값(0~100)과 "n / 전체 N건 (xx%)" 문구. index는 1부터, completed = 성공+실패 건수."""
    if total <= 0:
        return 0.0, "0 / 전체 0건 (0%)"
    fraction = max(0.0, min(1.0, fraction))
    value = max(0.0, min(100.0, (index - 1 + fraction) / total * 100))
    return value, f"{completed} / 전체 {total}건 ({int(value)}%)"  # 버림: 끝나기 전에 100%가 되지 않게


def format_file(name: str, fraction: float) -> tuple[float, str]:
    """현재 파일 진행률 바 값(0~100)과 "현재 파일: 이름  xx%" 문구."""
    value = max(0.0, min(1.0, fraction)) * 100
    return value, f"현재 파일: {name}  {int(value)}%"


def format_finished(summary: BatchSummary) -> tuple[float, str]:
    """끝났을 때의 전체 진행률. 중지하지 않았으면 100%, 중지했으면 처리한 만큼만."""
    completed = summary.done + summary.failed
    if summary.total <= 0:
        return 0.0, "0 / 전체 0건 (0%)"
    value = 100.0 if not summary.stopped else completed / summary.total * 100
    return value, f"{completed} / 전체 {summary.total}건 ({int(value)}%)"


def row_values(row: ResultRow) -> tuple[str, str, str]:
    """목록 표의 (상태, 재생 시간, 처리 시간) 칸. 재생 시간을 모르면(실패) 빈칸."""
    duration = "" if row.duration_sec is None else format_duration(row.duration_sec)
    return row.status, duration, f"{round(row.elapsed_sec)}초"


def is_fallback_file(path: Path | None) -> bool:
    """엑셀이 열려 있어 '_임시저장.xlsx'로 바꿔 저장했는지."""
    return path is not None and Path(path).stem.endswith(_FALLBACK_SUFFIX)


def summary_message(summary: BatchSummary) -> str:
    """완료 알림 문구: 건수, 중지 여부, 결과 파일, 임시저장·메모리 부족 안내."""
    lines = ["변환을 중지했습니다. (중지됨)" if summary.stopped else "변환을 마쳤습니다."]
    lines.append(f"전체 {summary.total}건 · 성공 {summary.done}건 · 실패 {summary.failed}건")
    if summary.stopped:
        left = summary.total - summary.done - summary.failed
        lines.append(f"처리하지 않은 {left}건은 결과 파일에 들어가지 않았습니다.")
    if summary.excel_path is not None:
        lines.append("")
        lines.append(f"결과 파일: {Path(summary.excel_path).name}")
        if is_fallback_file(summary.excel_path):
            lines.append("원래 결과 파일이 열려 있어 위 이름으로 바꿔 저장했습니다.")
    if summary.memory_error:
        lines.append("")
        lines.append(MEMORY_HINT)
    return "\n".join(lines)


def exit_code(summary: BatchSummary) -> int:
    """명령줄 종료 코드: 실패 건이 있거나 중간에 멈췄으면 1, 모두 완료면 0."""
    return EXIT_FAILED if summary.failed or summary.stopped else EXIT_OK


CLOSE_NOW, CLOSE_ASK_WAIT, CLOSE_ASK_FORCE = "close_now", "ask_wait", "ask_force"
AFTER_DESTROY, AFTER_WAIT, AFTER_KEEP = "destroy", "wait", "keep"


def close_prompt(running: bool, close_requested: bool) -> str:
    """창 닫기 버튼을 눌렀을 때: 바로 닫기 / "현재 파일 후 종료" 질문 / "바로 종료" 질문."""
    if not running:
        return CLOSE_NOW
    return CLOSE_ASK_FORCE if close_requested else CLOSE_ASK_WAIT


def close_after_answer(prompt: str, answer: bool, still_running: bool) -> str:
    """질문창이 닫힌 뒤 할 일. 질문창이 떠 있는 동안 변환이 끝났을 수 있으므로 still_running을 다시 확인한다.

    - 예 + 변환이 이미 끝남 → 바로 닫기(끝날 때 닫아 줄 이벤트가 다시 오지 않는다)
    - "현재 파일 후 종료"에 예 → 중지 요청 후 끝나면 닫기
    - "바로 종료"에 예 → 바로 닫기
    - 아니요 → 그대로 둔다(이미 종료를 요청했다면 그 요청은 유지)
    """
    if not answer:
        return AFTER_KEEP
    if not still_running or prompt == CLOSE_ASK_FORCE:
        return AFTER_DESTROY
    return AFTER_WAIT


def load_app_config(model_override: str | None = None) -> AppConfig:
    """config.ini를 읽고(필요하면 모델만 바꿔) 모델 폴더에 model.bin이 있는지 확인한다.

    ConfigError 또는 ModelNotFoundError를 던진다. 실제 모델 로딩(수 초~수십 초)은 하지 않는다.
    """
    cfg = load_config()
    if model_override:
        cfg = replace(cfg, model_name=model_override, model_path=cfg.model_path.parent / model_override)
    if not (Path(cfg.model_path) / "model.bin").is_file():
        raise ModelNotFoundError(
            f"모델 폴더를 찾을 수 없습니다: {cfg.model_path}\n"
            f"models 폴더에 모델이 복사되어 있는지, config.ini의 모델 이름(name = {cfg.model_name})이 맞는지 확인하세요."
        )
    return cfg


# =============================================================== 명령줄 모드


def _echo(message: str) -> None:
    """명령줄 진행 문구: 로그에 남기고 콘솔에도 출력한다(콘솔이 없으면 빈 장치로 사라짐)."""
    logger.log(logging.ERROR if message.startswith("오류") else logging.INFO, message)
    try:
        print(message, flush=True)
    except (OSError, ValueError):
        pass


class _BatchState:
    """진행 중인 일괄 처리에 대해 예외 처리기가 알아야 할 것: 만든 writer(부분 결과 파일)와 기록한 행 수."""

    def __init__(self) -> None:
        self.writer = None
        self.rows = 0

    def wrap_factory(self, factory):
        def make(result_dir: Path, started_at):
            self.writer = factory(result_dir, started_at)
            return self.writer

        return make

    def partial_path(self) -> Path | None:
        """지금까지 저장된 결과 파일(임시저장으로 바뀌었으면 그 이름). 없으면 None."""
        path = getattr(self.writer, "path", None)
        return Path(path) if path is not None and Path(path).exists() else None


def run_cli(folder: Path, include_subfolders: bool, model_override: str | None) -> int:
    """명령줄 변환. Ctrl+C는 결과가 1건이라도 저장됐으면 1, 아니면 2로 끝낸다(traceback 없이)."""
    state = _BatchState()
    try:
        return _run_cli(Path(folder), include_subfolders, model_override, state)
    except KeyboardInterrupt:
        logger.warning("명령줄 변환을 사용자가 중단(Ctrl+C), 저장된 행 %d건", state.rows)
        partial = state.partial_path()
        if state.rows and partial is not None:
            _echo(f"사용자가 중단했습니다. 그때까지의 결과({state.rows}건)는 저장되어 있습니다: {partial}")
            return EXIT_FAILED
        _echo("사용자가 중단했습니다. 저장된 결과가 없습니다.")
        return EXIT_CANNOT_START


def _run_cli(folder: Path, include_subfolders: bool, model_override: str | None, state: _BatchState) -> int:
    if not folder.is_dir():
        _echo(f"오류: 대상 폴더를 찾을 수 없습니다: {folder}")
        return EXIT_CANNOT_START
    try:
        wavs = find_wavs(folder, include_subfolders)
    except OSError as exc:
        _echo(f"오류: 대상 폴더를 읽을 수 없습니다: {folder} ({exc})")
        return EXIT_CANNOT_START
    if not wavs:
        _echo(f"오류: 대상 .wav 파일이 없습니다: {folder}")
        return EXIT_CANNOT_START

    try:
        cfg = load_app_config(model_override)
    except (ConfigError, ModelNotFoundError) as exc:
        _echo(f"오류: {exc}")
        return EXIT_CANNOT_START

    _echo(f"대상 폴더: {folder} ({len(wavs)}건, 하위폴더 {'포함' if include_subfolders else '제외'})")
    _echo(f"모델을 불러오는 중… ({cfg.model_name})")
    try:
        engine = SttEngine(cfg)
    except MemoryError:
        logger.exception("모델 로딩 중 메모리 부족")
        _echo(f"오류: {MODEL_MEMORY_MESSAGE}")
        return EXIT_CANNOT_START
    except (ModelNotFoundError, RuntimeError) as exc:
        logger.exception("모델 로딩 실패")
        _echo(f"오류: 모델을 불러오지 못했습니다: {exc}")
        return EXIT_CANNOT_START

    def on_progress(p: Progress) -> None:
        if p.file_fraction == 0.0:
            _echo(f"[{p.index}/{p.total}] 변환 중: {p.name}")

    def on_row(row: ResultRow) -> None:
        state.rows += 1
        status, duration, elapsed = row_values(row)
        _echo(f"    → {status} (재생 {duration or '-'}, 처리 {elapsed})")

    def make_writer(result_dir: Path, started_at):
        return ExcelWriter(result_dir, started_at, on_warning=lambda msg: _echo(f"알림: {msg}"))

    try:
        summary = run_batch(
            folder, include_subfolders, engine, state.wrap_factory(make_writer), on_progress, threading.Event(), on_row
        )
    except (NoWavFilesError, OutputNotWritableError) as exc:
        _echo(f"오류: {exc}")
        return EXIT_CANNOT_START
    except Exception as exc:  # 엑셀 저장 실패·메모리 부족 등으로 일괄 처리가 중간에 멈춤
        logger.exception("명령줄 변환 중 오류")
        message = MEMORY_ERROR_MESSAGE if isinstance(exc, MemoryError) else f"변환 중 오류가 발생했습니다: {exc}"
        partial = state.partial_path()
        if partial is not None:
            message += f"\n지금까지의 결과({state.rows}건)는 저장되어 있습니다: {partial}"
        _echo(f"오류: {message}")
        return EXIT_FAILED if state.rows else EXIT_CANNOT_START

    _echo(summary_message(summary))
    return exit_code(summary)


# =============================================================== 화면(GUI) 모드


class App:
    """STT 변환기 창. 작업 스레드는 tkinter를 건드리지 않고 queue로만 알리며, 화면이 100ms마다 꺼내 반영한다."""

    POLL_MS = 100
    MAX_EVENTS_PER_POLL = 500  # 이벤트가 몰려도 화면이 멈추지 않게 한 번에 처리할 최대 개수
    COLOR_SUCCESS = "#1a7f37"
    COLOR_FAILED = "#c62828"

    def __init__(
        self,
        root: tk.Tk,
        *,
        log_dir: Path | None = None,
        initial_folder: Path | None = None,
        include_subfolders: bool = False,
        model_override: str | None = None,
    ) -> None:
        self.root = root
        self.log_dir = log_dir
        self.events: queue.Queue = queue.Queue()
        self.stop_event = threading.Event()
        self.worker: threading.Thread | None = None
        self.engine: SttEngine | None = None  # 첫 [시작] 때 작업 스레드에서 1회 로딩 후 재사용
        self.cfg: AppConfig | None = None
        self.config_error: str | None = None
        self.folder: Path | None = None
        self.wav_count = 0
        self.running = False
        self.close_requested = False
        self.destroyed = False
        self.result_path: Path | None = None
        self.current_iid: str | None = None
        self.finished_iids: set[str] = set()
        self.scan_error: str | None = None
        self._modal_depth = 0  # 닫기 질문창이 떠 있는 동안에는 queue를 꺼내지 않는다
        self._pending_summary: BatchSummary | None = None
        self._pending_error: str | None = None
        self._draining = False
        self._startup_id: str | None = None
        self._poll_id: str | None = None

        root.report_callback_exception = self._on_callback_error
        self._setup_look()
        self._build_widgets()
        self.subfolders_var.set(include_subfolders)
        self._load_startup_config(model_override)

        root.protocol("WM_DELETE_WINDOW", self.on_close)

        need_dialog = True
        if initial_folder is not None and Path(initial_folder).is_dir():
            self.set_folder(Path(initial_folder))
            save_last_folder(Path(initial_folder))
            need_dialog = False
        elif initial_folder is None:
            last = load_last_folder()
            if last is not None:
                self.set_folder(last)
                need_dialog = False
        if need_dialog:
            self.refresh_files()

        # 창이 화면에 뜬 뒤 설정 오류 안내 → (필요하면) 폴더 선택 창 순서로 띄운다
        self._startup_id = root.after(300, lambda: self._after_startup(need_dialog))
        self._poll_id = root.after(self.POLL_MS, self._poll)

    # ------------------------------------------------------------------ 모양

    def _setup_look(self) -> None:
        root = self.root
        root.title(APP_TITLE)
        for name in ("TkDefaultFont", "TkTextFont", "TkHeadingFont", "TkMenuFont", "TkCaptionFont"):
            try:
                tkfont.nametofont(name).configure(family="맑은 고딕", size=10)
            except tk.TclError:
                pass
        self.bold_font = tkfont.Font(root, family="맑은 고딕", size=10, weight="bold")
        # 고해상도 화면 배율(100% = 1.0). 픽셀로 지정하는 크기(창·표 칸 너비·여백)에 곱한다.
        self.scale = scale = max(1.0, root.winfo_fpixels("1i") / 96)
        line = tkfont.nametofont("TkDefaultFont").metrics("linespace")  # 글자 크기는 포인트라 이미 배율 반영됨
        style = ttk.Style(root)
        style.configure("Treeview", rowheight=line + int(6 * scale))
        style.configure("Success.TLabel", foreground=self.COLOR_SUCCESS)
        style.configure("Failed.TLabel", foreground=self.COLOR_FAILED)

        root.minsize(int(760 * scale), int(560 * scale))
        root.geometry(f"{int(900 * scale)}x{int(660 * scale)}")

    def _build_widgets(self) -> None:
        root = self.root
        pad = {"padx": 8, "pady": 4}
        root.columnconfigure(0, weight=1)
        root.rowconfigure(5, weight=1)

        # 1. 대상 폴더 + [폴더 변경] + [하위폴더 포함] + 대상 파일 수
        top = ttk.Frame(root)
        top.grid(row=0, column=0, sticky="ew", **pad)
        top.columnconfigure(1, weight=1)
        ttk.Label(top, text="대상 폴더").grid(row=0, column=0, padx=(0, 6))
        self.folder_var = tk.StringVar()
        ttk.Entry(top, textvariable=self.folder_var, state="readonly").grid(row=0, column=1, sticky="ew")
        self.folder_btn = ttk.Button(top, text="폴더 변경", command=self.choose_folder)
        self.folder_btn.grid(row=0, column=2, padx=6)
        self.subfolders_var = tk.BooleanVar(value=False)
        self.subfolders_chk = ttk.Checkbutton(
            top, text="하위폴더 포함", variable=self.subfolders_var, command=self.refresh_files
        )
        self.subfolders_chk.grid(row=0, column=3, padx=(0, 12))
        self.count_var = tk.StringVar(value="대상 파일 0개")
        ttk.Label(top, textvariable=self.count_var).grid(row=0, column=4, sticky="e")

        # 2. [시작] [중지] [결과 파일 열기]
        buttons = ttk.Frame(root)
        buttons.grid(row=1, column=0, sticky="w", **pad)
        self.start_btn = ttk.Button(buttons, text="시작", command=self.on_start)
        self.start_btn.grid(row=0, column=0, padx=(0, 6))
        self.stop_btn = ttk.Button(buttons, text="중지", command=self.on_stop)
        self.stop_btn.grid(row=0, column=1, padx=(0, 6))
        self.open_btn = ttk.Button(buttons, text="결과 파일 열기", command=self.open_result)
        self.open_btn.grid(row=0, column=2)

        # 3. 전체 진행률
        overall = ttk.Frame(root)
        overall.grid(row=2, column=0, sticky="ew", **pad)
        overall.columnconfigure(0, weight=1)
        self.overall_bar = ttk.Progressbar(overall, maximum=100)
        self.overall_bar.grid(row=0, column=0, sticky="ew")
        self.overall_var = tk.StringVar(value="0 / 전체 0건 (0%)")
        ttk.Label(overall, textvariable=self.overall_var, width=22, anchor="e").grid(row=0, column=1, padx=(8, 0))

        # 4. 현재 파일 + 파일 안 진행률
        current = ttk.Frame(root)
        current.grid(row=3, column=0, sticky="ew", **pad)
        current.columnconfigure(0, weight=1)
        self.file_var = tk.StringVar(value="현재 파일: -")
        ttk.Label(current, textvariable=self.file_var).grid(row=0, column=0, sticky="w")
        self.file_bar = ttk.Progressbar(current, maximum=100)
        self.file_bar.grid(row=1, column=0, sticky="ew", pady=(2, 0))

        # 5. 건수: 전체 / 성공(초록) / 실패(빨강)
        counts = ttk.Frame(root)
        counts.grid(row=4, column=0, sticky="w", **pad)
        self.total_var = tk.StringVar(value="전체 0")
        self.done_var = tk.StringVar(value="성공 0")
        self.failed_var = tk.StringVar(value="실패 0")
        ttk.Label(counts, textvariable=self.total_var, width=12).grid(row=0, column=0)
        ttk.Label(counts, textvariable=self.done_var, width=12, style="Success.TLabel").grid(row=0, column=1)
        ttk.Label(counts, textvariable=self.failed_var, width=12, style="Failed.TLabel").grid(row=0, column=2)

        # 6. 파일 목록 표
        table = ttk.Frame(root)
        table.grid(row=5, column=0, sticky="nsew", **pad)
        table.columnconfigure(0, weight=1)
        table.rowconfigure(0, weight=1)
        columns = ("no", "name", "status", "duration", "elapsed")
        self.tree = ttk.Treeview(table, columns=columns, show="headings", selectmode="browse")
        headings = {"no": "번호", "name": "파일명", "status": "상태", "duration": "재생 시간", "elapsed": "처리 시간"}
        widths = {"no": 50, "name": 360, "status": 180, "duration": 90, "elapsed": 80}
        for col in columns:
            self.tree.heading(col, text=headings[col])
            anchor = "w" if col in ("name", "status") else "center"
            width = int(widths[col] * self.scale)  # 150% 화면에서 긴 실패 사유가 잘리지 않게 배율을 곱한다
            self.tree.column(col, width=width, minwidth=int(40 * self.scale), anchor=anchor, stretch=(col == "name"))
        self.tree.tag_configure("current", font=self.bold_font, background="#fff4ce")
        self.tree.tag_configure("failed", foreground=self.COLOR_FAILED)
        scroll = ttk.Scrollbar(table, orient="vertical", command=self.tree.yview)
        self.tree.configure(yscrollcommand=scroll.set)
        self.tree.grid(row=0, column=0, sticky="nsew")
        scroll.grid(row=0, column=1, sticky="ns")

        # 7. 상태 표시줄
        self.status_var = tk.StringVar(value="준비")
        ttk.Label(root, textvariable=self.status_var, relief="sunken", anchor="w", padding=(6, 2)).grid(
            row=6, column=0, sticky="ew"
        )
        self._update_buttons()

    # ------------------------------------------------------------------ 시작 시 확인

    def _load_startup_config(self, model_override: str | None) -> None:
        """config.ini와 모델 폴더만 확인한다. 실패하면 [시작]을 막고 안내한다."""
        try:
            self.cfg = load_app_config(model_override)
            logger.info("설정 확인 완료: 모델=%s, 경로=%s", self.cfg.model_name, self.cfg.model_path)
        except (ConfigError, ModelNotFoundError) as exc:
            self.cfg = None
            self.config_error = str(exc)
            logger.error("시작 확인 실패: %s", exc)
        self.status_var.set(self._idle_status())
        self._update_buttons()

    def _after_startup(self, need_dialog: bool) -> None:
        if self.destroyed:
            return
        if self.config_error:
            messagebox.showerror(APP_TITLE, self.config_error, parent=self.root)
        if need_dialog:
            self.choose_folder()

    # ------------------------------------------------------------------ 폴더·파일 목록

    def choose_folder(self) -> bool:
        """폴더 선택 창. 고르면 바로 기억해 두고 목록을 새로 채운다. 취소하면 False."""
        if self.running:
            return False
        initial = str(self.folder) if self.folder is not None and self.folder.is_dir() else None
        chosen = filedialog.askdirectory(
            parent=self.root, title="변환할 .wav 파일이 있는 폴더를 선택하세요", initialdir=initial, mustexist=True
        )
        if not chosen:
            return False
        folder = Path(chosen)
        save_last_folder(folder)
        logger.info("대상 폴더 선택: %s", folder)
        self.set_folder(folder)
        return True

    def set_folder(self, folder: Path) -> None:
        self.folder = Path(folder)
        self.folder_var.set(str(self.folder))
        self.refresh_files()

    def refresh_files(self) -> None:
        """find_wavs를 다시 실행해 대상 파일 수와 목록 표를 새로 채운다(모두 "대기")."""
        self.tree.delete(*self.tree.get_children())
        self.current_iid = None
        self.finished_iids = set()
        wavs: list[Path] = []
        self.scan_error = None
        if self.folder is not None:
            try:
                if not self.folder.is_dir():
                    raise FileNotFoundError(f"폴더가 없습니다: {self.folder}")
                wavs = find_wavs(self.folder, self.subfolders_var.get())
            except OSError as exc:
                logger.warning("폴더를 읽을 수 없습니다: %s (%s)", self.folder, exc)
                self.scan_error = f"폴더를 읽을 수 없습니다: {self.folder}"
        for index, wav in enumerate(wavs, start=1):
            self.tree.insert("", "end", iid=str(index), values=(index, display_name(wav, self.folder), "대기", "", ""))
        self.wav_count = len(wavs)
        self.count_var.set(f"대상 파일 {self.wav_count}개")
        self._reset_progress(self.wav_count)
        self.status_var.set(self._idle_status())
        self._update_buttons()

    def _idle_status(self) -> str:
        """작업 중이 아닐 때 상태 표시줄 문구. 설정 오류가 가장 우선이다."""
        if self.cfg is None:
            return "설정 또는 모델에 문제가 있어 변환을 시작할 수 없습니다. config.ini와 models 폴더를 확인하세요."
        if self.folder is None:
            return "대상 폴더를 선택하세요 ([폴더 변경])"
        if self.scan_error:  # 없어졌거나 읽을 수 없는 폴더를 ".wav 없음"으로 잘못 알리지 않는다
            return self.scan_error
        if self.wav_count == 0:
            return "대상 .wav 파일이 없습니다"
        return f"준비 (모델: {self.cfg.model_name})"

    def _reset_progress(self, total: int) -> None:
        self.overall_bar["value"] = 0
        self.overall_var.set(f"0 / 전체 {total}건 (0%)")
        self.file_bar["value"] = 0
        self.file_var.set("현재 파일: -")
        self.total_var.set(f"전체 {total}")
        self.done_var.set("성공 0")
        self.failed_var.set("실패 0")

    # ------------------------------------------------------------------ 버튼

    @staticmethod
    def _enable(widget, enabled: bool) -> None:
        widget.configure(state="normal" if enabled else "disabled")

    def _update_buttons(self) -> None:
        idle = not self.running
        can_start = idle and self.cfg is not None and self.folder is not None and self.wav_count > 0
        self._enable(self.start_btn, can_start)
        self._enable(self.stop_btn, self.running and not self.stop_event.is_set())
        self._enable(self.folder_btn, idle)
        self._enable(self.subfolders_chk, idle)
        has_result = idle and self.result_path is not None and Path(self.result_path).exists()
        self._enable(self.open_btn, has_result)

    def on_start(self) -> None:
        if self.running:
            return
        if self.cfg is None:
            messagebox.showerror(APP_TITLE, self.config_error or "설정을 확인하세요.", parent=self.root)
            return
        if self.folder is None or not self.folder.is_dir():
            messagebox.showwarning(
                APP_TITLE, f"대상 폴더를 찾을 수 없습니다:\n{self.folder}\n\n폴더를 다시 선택하세요.", parent=self.root
            )
            self.choose_folder()
            return
        self.refresh_files()  # 시작 직전 목록을 다시 맞춘다(번호가 run_batch와 같아야 함)
        if self.wav_count == 0:
            messagebox.showinfo(APP_TITLE, "대상 .wav 파일이 없습니다", parent=self.root)
            return

        self.stop_event = threading.Event()
        self.running = True
        self.result_path = None
        self._pending_summary = None
        self._pending_error = None
        include = self.subfolders_var.get()
        logger.info("변환 시작 요청: %s (하위폴더 %s)", self.folder, "포함" if include else "제외")
        self.status_var.set("모델을 불러오는 중…" if self.engine is None else "변환 중…")
        self.worker = threading.Thread(
            target=self._work, args=(self.folder, include, self.stop_event), name="stt-worker", daemon=True
        )
        self.worker.start()
        self._update_buttons()

    def on_stop(self) -> None:
        if not self.running or self.stop_event.is_set():
            return
        self.stop_event.set()
        logger.info("사용자 중지 요청")
        self.status_var.set("현재 파일을 끝낸 뒤 멈춥니다")
        self._update_buttons()

    def open_result(self) -> None:
        if self.result_path is None or not Path(self.result_path).exists():
            messagebox.showwarning(APP_TITLE, "결과 파일을 찾을 수 없습니다.", parent=self.root)
            return
        try:
            os.startfile(self.result_path)  # 연결된 프로그램(엑셀)으로 연다
        except OSError as exc:
            logger.exception("결과 파일 열기 실패: %s", self.result_path)
            messagebox.showerror(APP_TITLE, f"결과 파일을 열 수 없습니다:\n{self.result_path}\n({exc})", parent=self.root)

    def on_close(self) -> None:
        prompt = close_prompt(self.running, self.close_requested)
        if prompt == CLOSE_NOW:
            self._destroy()
            return
        if prompt == CLOSE_ASK_WAIT:
            question = "변환 중입니다. 현재 파일을 끝낸 뒤 종료할까요?"
        else:  # 이미 종료를 요청했는데 또 닫으면(긴 파일 처리 중) 바로 끝낼 수 있게 한다
            question = (
                "현재 파일을 아직 변환하고 있습니다. 기다리지 않고 바로 종료할까요?\n"
                "(이미 끝난 파일의 결과는 저장되어 있고, 지금 파일의 결과만 빠집니다)"
            )
        answer = self._ask_yes_no(question)
        if self.destroyed:
            return
        # 질문창이 떠 있는 동안 작업 스레드가 끝났을 수 있다(그러면 닫아 줄 "finished"를 기다릴 필요가 없다)
        still_running = self.running and self.worker is not None and self.worker.is_alive()
        action = close_after_answer(prompt, answer, still_running)
        if action == AFTER_DESTROY:
            logger.info("창 닫기: %s", "변환 중 강제 종료" if still_running else "변환이 끝난 뒤 종료")
            self._destroy()
        elif action == AFTER_WAIT:
            self.close_requested = True
            self.stop_event.set()
            logger.info("변환 중 창 닫기 요청: 현재 파일 후 종료")
            self.status_var.set("현재 파일을 끝낸 뒤 종료합니다")
            self._update_buttons()

    def _ask_yes_no(self, question: str) -> bool:
        """질문창. 떠 있는 동안 queue 반영을 멈춰, 그 아래에서 창이 닫히거나 상태가 바뀌지 않게 한다."""
        self._modal_depth += 1
        try:
            return bool(messagebox.askyesno(APP_TITLE, question, parent=self.root))
        finally:
            self._modal_depth -= 1

    def _destroy(self) -> None:
        if self.destroyed:
            return
        self.destroyed = True
        for after_id in (self._startup_id, self._poll_id):
            try:
                self.root.after_cancel(after_id)  # 창을 없앤 뒤 예약된 콜백이 실행되지 않게 한다
            except (tk.TclError, ValueError):
                pass
        self.root.destroy()

    # ------------------------------------------------------------------ 작업 스레드(tkinter 사용 금지)

    def _work(self, folder: Path, include_subfolders: bool, stop_event: threading.Event) -> None:
        put = self.events.put
        state = _BatchState()
        try:
            if self.engine is None:
                put(("status", f"모델을 불러오는 중… ({self.cfg.model_name}, 처음 한 번만 걸립니다)"))
                try:
                    self.engine = SttEngine(self.cfg)
                except MemoryError:
                    logger.exception("모델 로딩 중 메모리 부족")
                    put(("error", MODEL_MEMORY_MESSAGE))
                    return
                except (ModelNotFoundError, RuntimeError) as exc:
                    logger.exception("모델 로딩 실패")
                    put(("error", f"모델을 불러오지 못했습니다: {exc}"))
                    return
                put(("status", "변환 중…"))
            if stop_event.is_set():
                put(("status", "시작 전에 중지했습니다"))
                return

            def make_writer(result_dir: Path, started_at):
                return ExcelWriter(result_dir, started_at, on_warning=lambda msg: put(("warning", msg)))

            summary = run_batch(
                folder,
                include_subfolders,
                self.engine,
                state.wrap_factory(make_writer),
                on_progress=lambda p: put(("progress", p)),
                stop_event=stop_event,
                on_row=lambda row: put(("row", row)),
            )
            put(("summary", summary))
        except (NoWavFilesError, OutputNotWritableError) as exc:
            logger.warning("변환을 시작하지 못했습니다: %s", exc)
            put(("error", str(exc)))
        except Exception as exc:  # 엑셀 저장 실패·메모리 부족 등. 화면에 알리고 버튼을 되돌려야 한다
            logger.exception("변환 중 오류")
            if isinstance(exc, MemoryError):
                message = MEMORY_ERROR_MESSAGE
            else:
                message = f"변환 중 오류가 발생했습니다: {exc}"
            partial = state.partial_path()  # 중간에 멈춰도 그때까지 저장된 엑셀은 열 수 있게 한다
            if partial is not None:
                put(("result_path", partial))
                message += f"\n\n지금까지의 결과는 저장되어 있습니다: {partial.name}"
            if self.log_dir and not isinstance(exc, MemoryError):
                message += f"\n\n자세한 내용은 로그 폴더를 확인하세요:\n{self.log_dir}"
            put(("error", message))
        finally:
            put(("finished", None))

    # ------------------------------------------------------------------ 화면 반영(UI 스레드)

    def _poll(self) -> None:
        if self.destroyed:
            return
        try:
            self.drain_queue()
        finally:
            if not self.destroyed:
                self._poll_id = self.root.after(self.POLL_MS, self._poll)

    def drain_queue(self) -> None:
        """queue에 쌓인 이벤트를 화면에 반영한다. 알림창·닫기 질문창이 떠 있는 동안에는 꺼내지 않는다."""
        if self._draining or self._modal_depth:
            return
        self._draining = True
        try:
            for _ in range(self.MAX_EVENTS_PER_POLL):
                try:
                    kind, payload = self.events.get_nowait()
                except queue.Empty:
                    break
                getattr(self, f"_on_{kind}")(payload)
                if self.destroyed:
                    break
        finally:
            self._draining = False

    def _on_status(self, message: str) -> None:
        self.status_var.set(message)

    def _on_warning(self, message: str) -> None:
        self.status_var.set(message)

    def _on_result_path(self, path: Path) -> None:
        self.result_path = path  # 오류로 멈췄을 때의 부분 결과 파일([결과 파일 열기]용)

    def _on_error(self, message: str) -> None:
        self._pending_error = message
        self.status_var.set(message.splitlines()[0])

    def _on_progress(self, p: Progress) -> None:
        value, text = format_overall(p.index, p.total, p.file_fraction, p.done + p.failed)
        self.overall_bar["value"] = value
        self.overall_var.set(text)
        value, text = format_file(p.name, p.file_fraction)
        self.file_bar["value"] = value
        self.file_var.set(text)
        self.total_var.set(f"전체 {p.total}")
        self.done_var.set(f"성공 {p.done}")
        self.failed_var.set(f"실패 {p.failed}")

        iid = str(p.index)
        if iid != self.current_iid and iid not in self.finished_iids and self.tree.exists(iid):
            self.current_iid = iid
            self.tree.set(iid, "status", "변환 중")
            self.tree.item(iid, tags=("current",))
            self.tree.see(iid)

    def _on_row(self, row: ResultRow) -> None:
        iid = str(row.index)
        self.finished_iids.add(iid)
        if self.current_iid == iid:
            self.current_iid = None
        if not self.tree.exists(iid):
            return
        status, duration, elapsed = row_values(row)
        self.tree.set(iid, "status", status)
        self.tree.set(iid, "duration", duration)
        self.tree.set(iid, "elapsed", elapsed)
        self.tree.item(iid, tags=("failed",) if row.failed else ())

    def _on_summary(self, summary: BatchSummary) -> None:
        self._pending_summary = summary
        self.result_path = summary.excel_path
        value, text = format_finished(summary)
        self.overall_bar["value"] = value
        self.overall_var.set(text)
        self.total_var.set(f"전체 {summary.total}")
        self.done_var.set(f"성공 {summary.done}")
        self.failed_var.set(f"실패 {summary.failed}")
        logger.info(
            "변환 결과: 전체 %d, 성공 %d, 실패 %d, 중지 %s, 결과 %s",
            summary.total, summary.done, summary.failed, summary.stopped, summary.excel_path,
        )

    def _on_finished(self, _payload) -> None:
        self.running = False
        if self.current_iid is not None and self.tree.exists(self.current_iid):
            # 오류로 멈춰 결과가 없는 행은 "대기"로 되돌린다
            self.tree.set(self.current_iid, "status", "대기")
            self.tree.item(self.current_iid, tags=())
        self.current_iid = None
        summary, error = self._pending_summary, self._pending_error
        self._pending_summary = self._pending_error = None

        if summary is not None:
            if summary.stopped:
                self.status_var.set("중지됨")
            elif is_fallback_file(summary.excel_path):
                self.status_var.set(f"완료 (임시저장 파일: {Path(summary.excel_path).name})")
            else:
                self.status_var.set("완료")
        self._update_buttons()

        if self.close_requested:
            logger.info("현재 파일 처리 후 종료")
            self._destroy()
            return
        if error is not None:
            messagebox.showerror(APP_TITLE, error, parent=self.root)
        elif summary is not None:
            messagebox.showinfo(APP_TITLE, summary_message(summary), parent=self.root)

    def _on_callback_error(self, exc_type, exc, tb) -> None:
        """tkinter 콜백에서 난 예외: 콘솔이 없으므로 로그에 남기고 알린다."""
        logger.error("화면 처리 중 오류", exc_info=(exc_type, exc, tb))
        try:
            where = f"\n\n로그 폴더: {self.log_dir}" if self.log_dir else ""
            messagebox.showerror(APP_TITLE, f"화면 처리 중 오류가 발생했습니다: {exc}{where}", parent=self.root)
        except tk.TclError:
            pass


def _enable_dpi_awareness() -> None:
    """고해상도 노트북에서 글자가 흐릿하지 않게 한다(Tk 생성 전에 호출)."""
    try:
        ctypes.windll.shcore.SetProcessDpiAwareness(1)
    except (AttributeError, OSError):
        pass


def run_gui(log_dir: Path | None, initial_folder: Path | None, include_subfolders: bool, model_override: str | None) -> int:
    _enable_dpi_awareness()
    root = tk.Tk()
    app = App(
        root,
        log_dir=log_dir,
        initial_folder=initial_folder,
        include_subfolders=include_subfolders,
        model_override=model_override,
    )
    root.mainloop()
    if app.worker is not None:
        app.worker.join(timeout=1.0)  # 정상 종료라면 작업 스레드는 이미 끝나는 중이다
    if app.worker is not None and app.worker.is_alive():
        # 강제 종료: 모델 연산 중인 작업 스레드를 기다리지 않고 바로 끝낸다(끝난 파일 결과는 이미 저장됨)
        logger.info("프로그램 종료 (작업 스레드 강제 종료)")
        logging.shutdown()
        os._exit(EXIT_OK)
    return EXIT_OK


# =============================================================== 진입점


class UsageError(Exception):
    """명령줄 인자가 잘못됨. argparse가 콘솔(없을 수도 있음)에 쓰고 바로 끝내지 않도록 예외로 바꾼다."""


class _Parser(argparse.ArgumentParser):
    def error(self, message: str):  # noqa: D401 - argparse 재정의
        raise UsageError(message)


def _build_parser() -> argparse.ArgumentParser:
    parser = _Parser(
        prog="STT변환기",
        description="폴더의 .wav 파일을 텍스트로 변환해 엑셀로 저장합니다.",
        allow_abbrev=False,  # --no 같은 줄임말로 모드가 바뀌지 않게 한다
    )
    parser.add_argument(
        "folder_arg", nargs="?", type=Path, metavar="폴더", help="대상 폴더(--folder와 같음, exe에 폴더를 끌어다 놓으면 이 값)"
    )
    parser.add_argument("--folder", type=Path, help="대상 폴더(화면 모드에서는 처음에 채울 폴더). 둘 다 주면 이 값이 우선")
    parser.add_argument("--no-gui", action="store_true", help="화면 없이 명령줄로 변환")
    parser.add_argument("--subfolders", action="store_true", help="하위폴더의 .wav도 포함")
    parser.add_argument("--model", choices=VALID_MODELS, help="config.ini 대신 사용할 모델(시험·비교용)")
    return parser


def _show_startup_error(message: str) -> None:
    """창이 아직 없을 때(인자 오류·예상하지 못한 오류) 숨긴 Tk 창을 잠깐 만들어 오류를 알린다."""
    try:
        _enable_dpi_awareness()
        root = tk.Tk()
        root.withdraw()
        try:
            messagebox.showerror(APP_TITLE, message, parent=root)
        finally:
            root.destroy()
    except tk.TclError:
        logger.exception("오류 알림창을 띄우지 못했습니다")


def main(argv: list[str] | None = None) -> int:
    raw_args = sys.argv[1:] if argv is None else list(argv)
    if "--no-gui" in raw_args:  # 줄임말은 막아 두었으므로(allow_abbrev=False) 정확히 이 값일 때만 명령줄 모드
        _attach_parent_console()  # 경고·인자 오류까지 호출한 창에 보이도록 가장 먼저 붙는다

    try:
        log_dir: Path | None = setup_logging()
    except ConfigError as exc:
        log_dir = None  # 로그 없이도 변환은 할 수 있게 한다
        print(f"경고: {exc}", file=sys.stderr)

    parser = _build_parser()
    try:
        args = parser.parse_args(raw_args)
        args.folder = args.folder if args.folder is not None else args.folder_arg
        if args.no_gui and args.folder is None:
            parser.error("--no-gui에는 대상 폴더(--folder <경로>)가 필요합니다")
    except UsageError as exc:
        logger.error("잘못된 명령줄 인자: %s (%s)", raw_args, exc)
        if "--no-gui" in raw_args:
            print(f"{parser.format_usage()}오류: {exc}", file=sys.stderr)
        else:  # 콘솔 없는 exe에서는 stderr가 보이지 않으므로 창으로 알린다
            _show_startup_error(f"명령줄 인자가 잘못되었습니다:\n{exc}\n\n{parser.format_usage()}")
        return EXIT_CANNOT_START
    except SystemExit as exc:  # --help
        return exc.code if isinstance(exc.code, int) else EXIT_CANNOT_START

    mode = "명령줄" if args.no_gui else "화면"
    logger.info("프로그램 시작 (%s 모드, 인자=%s)", mode, raw_args)
    code = EXIT_FAILED
    try:
        if args.no_gui:
            code = run_cli(args.folder, args.subfolders, args.model)
        else:
            code = run_gui(log_dir, args.folder, args.subfolders, args.model)
    except Exception as exc:
        logger.exception("예상하지 못한 오류")
        if not args.no_gui:
            where = f"\n\n로그 폴더: {log_dir}" if log_dir else ""
            _show_startup_error(f"예상하지 못한 오류가 발생했습니다: {exc}{where}")
        else:
            _echo(f"오류: 예상하지 못한 오류가 발생했습니다: {exc}")
        code = EXIT_FAILED
    finally:
        logger.info("프로그램 종료 (종료 코드 %d)", code)
    return code


if __name__ == "__main__":
    sys.exit(main())
