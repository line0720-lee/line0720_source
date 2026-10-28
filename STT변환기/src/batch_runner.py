"""폴더 일괄 처리: .wav 스캔, 결과 폴더 준비, 파일별 변환·예외 처리, 진행 알림, 사용자 중지를 담당한다.

GUI(별도 스레드)와 엑셀 기록 모듈에 의존하지 않도록 엔진과 writer는 덕 타이핑(Protocol)으로 받는다.
"""
from __future__ import annotations

import ctypes
import logging
import re
import threading
import time
from dataclasses import dataclass
from datetime import datetime
from functools import cmp_to_key
from pathlib import Path
from typing import Callable, Protocol

from records import ResultRow
from stt_engine import CorruptFileError, TranscribeResult

logger = logging.getLogger(__name__)

RESULT_DIR_NAME = "result"
_PROBE_NAME = ".쓰기확인.tmp"  # 결과 폴더 쓰기 시험용 임시 파일
_MAX_REASON_CHARS = 60  # 엑셀 상태 칸에 쓰는 실패 사유의 최대 글자 수


class NoWavFilesError(Exception):
    """대상 폴더에 처리할 .wav 파일이 없을 때. 메시지를 화면에 그대로 보여 줄 수 있다."""


class OutputNotWritableError(Exception):
    """result 폴더를 만들 수 없거나 쓸 수 없을 때. 변환을 시작하기 전에 발생한다."""


class Transcriber(Protocol):
    """변환 엔진이 갖춰야 할 인터페이스(SttEngine 또는 테스트용 가짜 엔진)."""

    def transcribe(self, wav: Path, on_progress: Callable[[float], None] | None = None) -> TranscribeResult: ...


class RowWriter(Protocol):
    """결과 행을 저장하는 객체(엑셀 writer 또는 테스트용 가짜). 저장 파일이 바뀔 수 있어 path는 실행 중 변할 수 있다."""

    path: Path

    def add_row(self, row: ResultRow) -> None: ...


@dataclass
class Progress:
    index: int  # 지금 처리 중인(또는 방금 끝난) 파일 번호(1부터)
    total: int
    name: str
    file_fraction: float  # 현재 파일의 진행률 0.0~1.0
    done: int  # 이 이벤트 시점까지 성공한 건수
    failed: int  # 이 이벤트 시점까지 실패한 건수


@dataclass
class BatchSummary:
    total: int  # 대상 파일 수(중지해도 전체 수)
    done: int
    failed: int
    stopped: bool  # 중지 요청으로 건너뛴 파일이 있으면 True
    excel_path: Path | None
    memory_error: bool  # 메모리 부족 실패가 한 건이라도 있었는지


def _explorer_sort_key() -> Callable[[str], object]:
    """윈도우 탐색기와 같은 정렬 키.

    처리 순서가 엑셀 번호가 되므로 사용자가 탐색기에서 보는 순서와 같아야 한다.
    (숫자는 수 크기로 비교: 회의2 < 회의10, 대소문자 무시) 탐색기가 쓰는 StrCmpLogicalW를 그대로 쓴다.
    """
    try:
        compare = ctypes.windll.shlwapi.StrCmpLogicalW
        compare.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p]
        compare.restype = ctypes.c_int
    except (AttributeError, OSError):
        return _fallback_sort_key  # windll이 없는 환경(비 Windows)에서만 쓰는 순수 파이썬 대체
    return cmp_to_key(compare)


def _fallback_sort_key(name: str) -> list:
    """숫자 덩어리는 정수로, 나머지는 소문자로 나누어 비교하는 대체 정렬 키(숫자 < 문자 순)."""
    return [(0, int(part), "") if part.isdigit() else (1, 0, part.lower()) for part in re.split(r"(\d+)", name) if part]


def find_wavs(folder: Path, include_subfolders: bool) -> list[Path]:
    """대상 .wav 파일 목록. 확장자 대소문자 무시, folder\\result 아래는 제외, 표시 이름 기준으로 정렬한다."""
    folder = Path(folder)
    candidates = folder.rglob("*") if include_subfolders else folder.iterdir()
    sort_key = _explorer_sort_key()
    wavs = []
    for path in candidates:
        if path.suffix.lower() != ".wav" or not path.is_file():
            continue
        parts = path.relative_to(folder).parts
        if len(parts) > 1 and parts[0].lower() == RESULT_DIR_NAME:
            continue  # 선택 폴더 바로 아래의 result 폴더(이전 결과)만 제외한다
        wavs.append(path)
    return sorted(wavs, key=lambda p: sort_key(display_name(p, folder)))


def display_name(wav: Path, root: Path) -> str:
    """선택 폴더 기준 상대경로(구분자 \\). 선택 폴더 바로 아래 파일은 파일명만 나온다."""
    return "\\".join(Path(wav).relative_to(root).parts)


def prepare_result_dir(folder: Path) -> Path:
    """folder\\result를 만들고 시험 파일을 쓰고 지워서 쓰기 가능 여부를 확인한다."""
    result_dir = Path(folder) / RESULT_DIR_NAME
    try:
        result_dir.mkdir(exist_ok=True)
        probe = result_dir / _PROBE_NAME
        probe.write_bytes(b"ok")
        probe.unlink()
    except OSError as exc:
        logger.exception("결과 폴더 쓰기 확인 실패: %s", result_dir)
        raise OutputNotWritableError(
            f"결과 폴더에 쓸 수 없습니다: {result_dir}\n폴더 권한을 확인하거나 다른 폴더를 선택하세요. ({exc})"
        ) from exc
    return result_dir


def _short_reason(exc: Exception) -> str:
    """실패 사유: 메시지 첫 줄을 60자 이내로. 메시지가 비어 있으면 예외 클래스 이름."""
    lines = str(exc).strip().splitlines()
    reason = lines[0].strip() if lines else ""
    return reason[:_MAX_REASON_CHARS] or type(exc).__name__


def _failure_status(exc: Exception) -> str:
    """예외를 엑셀 상태 문구로 바꾼다. 설계서에 고정된 문구가 우선이다."""
    if isinstance(exc, CorruptFileError):
        return "실패(파일 손상)"
    if isinstance(exc, MemoryError):
        return "실패(메모리 부족)"
    if isinstance(exc, FileNotFoundError):
        return "실패(파일 없음)"
    if isinstance(exc, PermissionError):
        return "실패(파일 접근 거부)"
    return f"실패({_short_reason(exc)})"


def _transcribe_one(engine: Transcriber, wav: Path, index: int, name: str, on_fraction) -> tuple[ResultRow, bool]:
    """파일 1건을 변환해 ResultRow로 만든다. 돌려주는 bool은 메모리 부족 여부. 예외는 밖으로 내보내지 않는다."""
    started = time.perf_counter()
    try:
        result = engine.transcribe(wav, on_fraction)
    except Exception as exc:  # 한 파일의 실패가 전체를 멈추면 안 되므로 모두 기록하고 다음으로 진행한다
        logger.exception("파일 처리 실패: %s", name)
        row = ResultRow(
            index=index,
            name=name,
            text="",
            duration_sec=None,
            elapsed_sec=time.perf_counter() - started,
            status=_failure_status(exc),
            finished_at=datetime.now(),
        )
        return row, isinstance(exc, MemoryError)

    status = "완료" if result.text.strip() else "완료(인식된 음성 없음)"
    row = ResultRow(
        index=index,
        name=name,
        text=result.text,
        duration_sec=result.duration_sec,
        elapsed_sec=result.elapsed_sec,
        status=status,
        finished_at=datetime.now(),
    )
    return row, False


def run_batch(
    folder: Path,
    include_subfolders: bool,
    engine: Transcriber,
    writer_factory: Callable[[Path, datetime], RowWriter],
    on_progress: Callable[[Progress], None],
    stop_event: threading.Event,
    on_row: Callable[[ResultRow], None] | None = None,
) -> BatchSummary:
    """폴더의 .wav를 순서대로 변환해 파일마다 writer에 기록한다.

    - .wav가 없으면 NoWavFilesError(result 폴더도 만들지 않음), 쓰기 불가면 OutputNotWritableError(엔진 호출 전).
    - 파일 1건이 실패해도 다음 파일로 진행한다. writer.add_row의 예외는 파일 실패가 아니므로 그대로 전달한다.
    - stop_event가 켜지면 현재 파일을 마친 뒤 중단하고, 그때까지의 결과는 이미 writer에 저장되어 있다.
    """
    folder = Path(folder)
    wavs = find_wavs(folder, include_subfolders)
    if not wavs:
        raise NoWavFilesError(f"대상 .wav 파일이 없습니다: {folder}")

    result_dir = prepare_result_dir(folder)
    started_at = datetime.now()
    writer = writer_factory(result_dir, started_at)

    total = len(wavs)
    done = failed = 0
    stopped = memory_error = False
    logger.info("일괄 처리 시작: %s (%d건, 하위폴더 %s)", folder, total, "포함" if include_subfolders else "제외")

    for index, wav in enumerate(wavs, start=1):
        if stop_event.is_set():
            stopped = True
            logger.info("사용자 중지: %d건 중 %d건 처리 후 중단", total, index - 1)
            break

        name = display_name(wav, folder)
        on_progress(Progress(index, total, name, 0.0, done, failed))

        def forward(fraction: float, _index=index, _name=name) -> None:
            on_progress(Progress(_index, total, _name, max(0.0, min(1.0, fraction)), done, failed))

        row, was_memory_error = _transcribe_one(engine, wav, index, name, forward)
        writer.add_row(row)
        if on_row is not None:
            on_row(row)

        if row.failed:
            failed += 1
        else:
            done += 1
        memory_error = memory_error or was_memory_error
        on_progress(Progress(index, total, name, 1.0, done, failed))

    summary = BatchSummary(
        total=total,
        done=done,
        failed=failed,
        stopped=stopped,
        excel_path=writer.path,  # 저장 중 대체 파일명으로 바뀔 수 있어 반복이 끝난 뒤에 읽는다
        memory_error=memory_error,
    )
    logger.info("일괄 처리 종료: 성공 %d, 실패 %d, 중지 %s, 결과 %s", done, failed, stopped, summary.excel_path)
    return summary
