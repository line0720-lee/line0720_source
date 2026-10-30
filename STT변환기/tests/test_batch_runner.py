"""batch_runner 테스트: 가짜 엔진·가짜 writer로 폴더 스캔, 상태 결정, 중지, 진행 이벤트를 확인한다."""
import shutil
import threading
from datetime import datetime
from pathlib import Path

import pytest

from batch_runner import (
    BatchSummary,
    NoWavFilesError,
    OutputNotWritableError,
    Progress,
    display_name,
    find_wavs,
    prepare_result_dir,
    run_batch,
)
from records import ResultRow
from stt_engine import CorruptFileError, TranscribeResult


class FakeEngine:
    """파일명에 따라 결과를 돌려주거나 예외를 던지는 가짜 엔진."""

    def __init__(self, on_call=None):
        self.calls: list[str] = []
        self._on_call = on_call

    def transcribe(self, wav: Path, on_progress=None) -> TranscribeResult:
        name = Path(wav).name
        self.calls.append(name)
        if self._on_call is not None:
            self._on_call(name)
        if on_progress is not None:
            on_progress(0.5)
        if "손상" in name:
            raise CorruptFileError("파일 손상: 테스트")
        if "메모리" in name:
            raise MemoryError()
        if "디코더" in name:
            raise RuntimeError("디코더 오류")
        if "없음" in name:
            raise FileNotFoundError(name)
        if "잠김" in name:
            raise PermissionError(name)
        if "빈메시지" in name:
            raise ValueError()
        if "긴사유" in name:
            raise RuntimeError("가" * 100 + "\n두 번째 줄")
        if "무음" in name:
            return TranscribeResult(text="  ", duration_sec=3.0, elapsed_sec=0.1)
        if on_progress is not None:
            on_progress(1.0)
        return TranscribeResult(text=f"{name} 내용", duration_sec=10.0, elapsed_sec=0.2)


class FakeWriter:
    """add_row로 받은 행을 리스트에 모으는 가짜 writer."""

    def __init__(self, path: Path):
        self.path = path
        self.rows: list[ResultRow] = []

    def add_row(self, row: ResultRow) -> None:
        self.rows.append(row)


@pytest.fixture
def make_folder(tmp_path):
    """이름 목록으로 빈 .wav 파일들을 만든 폴더를 돌려준다."""

    def _make(*names: str) -> Path:
        for name in names:
            target = tmp_path / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(b"x")
        return tmp_path

    return _make


def run(folder, names_engine=None, include_subfolders=False, stop_event=None, on_row=None):
    """run_batch를 가짜 writer와 함께 실행하고 (summary, writer, events, engine)을 돌려준다."""
    engine = names_engine or FakeEngine()
    holder: dict[str, FakeWriter] = {}

    def factory(result_dir: Path, started_at: datetime) -> FakeWriter:
        holder["writer"] = FakeWriter(result_dir / "fake.xlsx")
        return holder["writer"]

    events: list[Progress] = []
    summary = run_batch(
        folder,
        include_subfolders,
        engine,
        factory,
        events.append,
        stop_event or threading.Event(),
        on_row,
    )
    return summary, holder["writer"], events, engine


def test_find_wavs_flat_vs_recursive(make_folder):
    folder = make_folder("a.wav", "B.WAV", "메모.txt", "2팀/c.wav", "result/old.wav", "Result/old2.wav")
    flat = find_wavs(folder, include_subfolders=False)
    deep = find_wavs(folder, include_subfolders=True)
    assert [display_name(p, folder) for p in flat] == ["a.wav", "B.WAV"]
    # .WAV 포함, 선택 폴더 바로 아래 result\ (대소문자 무관) 제외
    assert [display_name(p, folder) for p in deep] == ["2팀\\c.wav", "a.wav", "B.WAV"]


def test_find_wavs_sorted_case_insensitive(make_folder):
    folder = make_folder("b.wav", "A.wav", "c.WAV")
    names = [display_name(p, folder) for p in find_wavs(folder, False)]
    assert names == ["A.wav", "b.wav", "c.WAV"]


def test_find_wavs_natural_order(make_folder):
    """탐색기처럼 숫자는 수 크기로 비교한다(1, 2, 10)."""
    folder = make_folder("회의10.wav", "회의2.wav", "회의1.wav")
    names = [display_name(p, folder) for p in find_wavs(folder, False)]
    assert names == ["회의1.wav", "회의2.wav", "회의10.wav"]


def test_find_wavs_mixed_order_matches_explorer(make_folder):
    """대소문자 무시, 한글이 코드포인트 순서 때문에 영문 뒤로 밀리는 것은 탐색기와 같다(영문 -> 한글)."""
    folder = make_folder("b.wav", "A.wav", "가.wav")
    names = [display_name(p, folder) for p in find_wavs(folder, False)]
    assert names == ["A.wav", "b.wav", "가.wav"]


def test_find_wavs_subfolder_sorted_by_full_display_name(make_folder):
    folder = make_folder("a.wav", "B.wav", "1.wav", "10팀/a.wav", "2팀/b.wav", "2팀/a.wav", "하위 폴더 (2팀)/회의 #1.wav")
    names = [display_name(p, folder) for p in find_wavs(folder, True)]
    assert names == [
        "1.wav",
        "2팀\\a.wav",
        "2팀\\b.wav",
        "10팀\\a.wav",
        "a.wav",
        "B.wav",
        "하위 폴더 (2팀)\\회의 #1.wav",
    ]


def test_find_wavs_keeps_nested_result_folder(make_folder):
    """result 제외는 선택 폴더 바로 아래의 result만 해당한다."""
    folder = make_folder("2팀/result/x.wav")
    assert len(find_wavs(folder, True)) == 1


def test_display_name_relative(tmp_path):
    assert display_name(tmp_path / "a.wav", tmp_path) == "a.wav"
    assert display_name(tmp_path / "2팀" / "b.wav", tmp_path) == "2팀\\b.wav"


def test_no_wavs_raises_and_no_result_dir(make_folder):
    folder = make_folder("메모.txt")
    engine = FakeEngine()
    factory_called = []
    with pytest.raises(NoWavFilesError) as info:
        run_batch(folder, True, engine, lambda *a: factory_called.append(a), lambda p: None, threading.Event())
    assert str(folder) in str(info.value)
    assert not (folder / "result").exists()
    assert not factory_called and engine.calls == []


def test_prepare_result_dir_creates_and_cleans_probe(tmp_path):
    result_dir = prepare_result_dir(tmp_path)
    assert result_dir == tmp_path / "result" and result_dir.is_dir()
    assert list(result_dir.iterdir()) == []  # 시험 파일이 남지 않는다
    assert prepare_result_dir(tmp_path) == result_dir  # 이미 있어도 그대로 사용


def test_unwritable_output_stops_before_engine(make_folder):
    folder = make_folder("a.wav")
    (folder / "result").write_text("폴더가 아니라 파일")  # result 폴더를 만들 수 없는 상황
    engine = FakeEngine()
    with pytest.raises(OutputNotWritableError) as info:
        run_batch(folder, False, engine, lambda *a: FakeWriter(Path("x")), lambda p: None, threading.Event())
    assert "결과 폴더에 쓸 수 없습니다" in str(info.value)
    assert str(folder / "result") in str(info.value)
    assert engine.calls == []


def test_unwritable_probe_write_stops_before_engine(make_folder, monkeypatch):
    folder = make_folder("a.wav")

    def deny(self, *args, **kwargs):
        raise PermissionError("쓰기 거부")

    monkeypatch.setattr(Path, "write_bytes", deny)
    monkeypatch.setattr(Path, "write_text", deny)
    engine = FakeEngine()
    with pytest.raises(OutputNotWritableError):
        run_batch(folder, False, engine, lambda *a: FakeWriter(Path("x")), lambda p: None, threading.Event())
    assert engine.calls == []


@pytest.mark.parametrize(
    "name, expected",
    [
        ("정상.wav", "완료"),
        ("무음.wav", "완료(인식된 음성 없음)"),
        ("손상.wav", "실패(파일 손상)"),
        ("메모리.wav", "실패(메모리 부족)"),
        ("디코더.wav", "실패(디코더 오류)"),
        ("없음.wav", "실패(파일 없음)"),
        ("잠김.wav", "실패(파일 접근 거부)"),
        ("빈메시지.wav", "실패(ValueError)"),
        ("긴사유.wav", "실패(" + "가" * 60 + ")"),
    ],
)
def test_status_mapping(make_folder, name, expected):
    summary, writer, _, _ = run(make_folder(name))
    row = writer.rows[0]
    assert row.status == expected
    assert row.failed == expected.startswith("실패")
    assert summary.memory_error is (name == "메모리.wav")


def test_failed_row_fields_and_success_row_fields(make_folder):
    _, writer, _, _ = run(make_folder("1정상.wav", "2손상.wav"))
    ok, bad = writer.rows
    assert (ok.text, ok.duration_sec) == ("1정상.wav 내용", 10.0)
    assert ok.elapsed_sec == 0.2
    assert bad.text == "" and bad.duration_sec is None
    assert bad.elapsed_sec >= 0 and isinstance(bad.finished_at, datetime)


def test_corrupt_file_is_recorded_and_skipped(make_folder):
    folder = make_folder("1.wav", "2손상.wav", "3.wav")
    summary, writer, _, engine = run(folder)
    assert [r.index for r in writer.rows] == [1, 2, 3]
    assert [r.name for r in writer.rows] == ["1.wav", "2손상.wav", "3.wav"]
    assert engine.calls == ["1.wav", "2손상.wav", "3.wav"]  # 실패 후에도 다음 파일 진행
    assert (summary.total, summary.done, summary.failed) == (3, 2, 1)
    assert summary.stopped is False and summary.memory_error is False


def test_subfolder_row_uses_relative_name(make_folder):
    folder = make_folder("a.wav", "2팀/b.wav")
    _, writer, _, _ = run(folder, include_subfolders=True)
    assert [r.name for r in writer.rows] == ["2팀\\b.wav", "a.wav"]


def test_stop_after_current_file(make_folder):
    folder = make_folder("1.wav", "2.wav", "3.wav")
    stop = threading.Event()
    engine = FakeEngine(on_call=lambda name: stop.set() if name == "1.wav" else None)
    summary, writer, _, _ = run(folder, engine, stop_event=stop)
    assert len(writer.rows) == 1  # 현재 파일은 끝까지 처리하고 기록
    assert engine.calls == ["1.wav"]
    assert summary.stopped is True
    assert (summary.total, summary.done, summary.failed) == (3, 1, 0)


def test_stop_during_last_file_is_not_stopped(make_folder):
    folder = make_folder("1.wav", "2.wav")
    stop = threading.Event()
    engine = FakeEngine(on_call=lambda name: stop.set() if name == "2.wav" else None)
    summary, writer, _, _ = run(folder, engine, stop_event=stop)
    assert len(writer.rows) == 2
    assert summary.stopped is False  # 건너뛴 파일이 없다


def test_progress_events(make_folder):
    folder = make_folder("1.wav", "2손상.wav", "3.wav")
    _, _, events, _ = run(folder)
    assert events[-1].index == events[-1].total == 3
    assert events[-1].file_fraction == 1.0
    assert all(0.0 <= e.file_fraction <= 1.0 for e in events)
    assert events[0] == Progress(1, 3, "1.wav", 0.0, 0, 0)
    # 각 이벤트의 done + failed는 그 시점에 이미 끝난 파일 수: 시작·진행 중이면 index-1, 마지막 이벤트(1.0)면 index
    for i, e in enumerate(events):
        last_of_file = i + 1 == len(events) or events[i + 1].index != e.index
        assert e.done + e.failed == (e.index if last_of_file else e.index - 1)
    finals = [e for i, e in enumerate(events) if i + 1 == len(events) or events[i + 1].index != e.index]
    assert [(e.done, e.failed) for e in finals] == [(1, 0), (1, 1), (2, 1)]


def test_progress_forwards_engine_fraction(make_folder):
    _, _, events, _ = run(make_folder("1.wav"))
    fractions = [e.file_fraction for e in events]
    assert fractions[0] == 0.0 and 0.5 in fractions and fractions[-1] == 1.0


def test_on_row_called_per_file(make_folder):
    folder = make_folder("1.wav", "2손상.wav", "3.wav")
    seen: list[ResultRow] = []
    writer_snapshot: list[int] = []

    def on_row(row: ResultRow) -> None:
        seen.append(row)
        writer_snapshot.append(len(holder_writer[0].rows))  # on_row 시점에 이미 writer에 기록돼 있어야 한다

    holder_writer: list[FakeWriter] = []
    engine = FakeEngine()
    events: list[Progress] = []

    def factory(result_dir, started_at):
        holder_writer.append(FakeWriter(result_dir / "fake.xlsx"))
        return holder_writer[0]

    run_batch(folder, False, engine, factory, events.append, threading.Event(), on_row)
    assert [r.index for r in seen] == [1, 2, 3]
    assert seen == holder_writer[0].rows
    assert [r.status for r in seen] == [r.status for r in holder_writer[0].rows]
    assert writer_snapshot == [1, 2, 3]


def test_excel_path_read_after_loop(make_folder):
    """writer가 중간에 대체 파일명으로 바꿔도 summary에는 마지막 경로가 담긴다."""
    folder = make_folder("1.wav", "2.wav")

    class SwitchingWriter(FakeWriter):
        def add_row(self, row):
            super().add_row(row)
            if row.index == 1:
                self.path = self.path.with_name("fake_임시저장.xlsx")

    summary = run_batch(
        folder, False, FakeEngine(), lambda d, t: SwitchingWriter(d / "fake.xlsx"), lambda p: None, threading.Event()
    )
    assert isinstance(summary, BatchSummary)
    assert summary.excel_path == folder / "result" / "fake_임시저장.xlsx"


def test_writer_exception_propagates(make_folder):
    class BrokenWriter(FakeWriter):
        def add_row(self, row):
            raise OSError("디스크 가득 참")

    with pytest.raises(OSError, match="디스크 가득 참"):
        run_batch(
            make_folder("1.wav"), False, FakeEngine(), lambda d, t: BrokenWriter(d / "x.xlsx"), lambda p: None, threading.Event()
        )


def test_failures_are_logged_with_traceback(make_folder, caplog):
    import logging

    with caplog.at_level(logging.ERROR, logger="batch_runner"):
        run(make_folder("디코더.wav"))
    assert any(r.exc_info for r in caplog.records)


@pytest.mark.model
def test_real_engine_smoke(samples_dir, small_engine, tmp_path):
    work = tmp_path / "samples"
    shutil.copytree(samples_dir, work)
    holder: dict[str, FakeWriter] = {}

    def factory(result_dir, started_at):
        holder["writer"] = FakeWriter(result_dir / "fake.xlsx")
        return holder["writer"]

    summary = run_batch(work, True, small_engine, factory, lambda p: None, threading.Event())
    statuses = {r.name: r.status for r in holder["writer"].rows}
    for row in holder["writer"].rows:
        print(f"{row.index} | {row.name} | {row.status} | {row.duration_sec} | {row.elapsed_sec:.1f}s")

    assert statuses["기본_16k.wav"] == "완료"
    assert statuses["전화_8k.wav"] == "완료"
    assert statuses["스테레오_44k.wav"] == "완료"
    assert statuses["하위 폴더 (2팀)\\회의 #1.wav"] == "완료"
    assert statuses["무음.wav"] == "완료(인식된 음성 없음)"
    assert statuses["빈파일.wav"] == "실패(파일 손상)"
    assert statuses["가짜.wav"] == "실패(파일 손상)"
    assert (summary.total, summary.done, summary.failed) == (7, 5, 2)


def test_fallback_sort_key_natural_order():
    """windll이 없는 환경용 대체 정렬 키도 숫자 크기·대소문자 무시로 정렬한다."""
    from batch_runner import _fallback_sort_key

    names = ["회의10.wav", "B.wav", "회의2.wav", "a.wav", "10팀\\a.wav", "2팀\\a.wav"]
    assert sorted(names, key=_fallback_sort_key) == [
        "2팀\\a.wav",
        "10팀\\a.wav",
        "a.wav",
        "B.wav",
        "회의2.wav",
        "회의10.wav",
    ]


def test_fallback_sort_key_superscript_digit_does_not_crash():
    """'²'처럼 isdigit()은 참이지만 int()로 바꿀 수 없는 글자가 있어도 정렬이 멈추지 않는다."""
    from batch_runner import _fallback_sort_key

    # re의 \d는 '²'를 숫자로 보지 않으므로 '1²2'처럼 숫자 사이에 끼면 '²'만 따로 한 조각이 된다
    for name in ("회의².wav", "회의1²2.wav", "²"):
        _fallback_sort_key(name)  # 예외 없음
    names = ["회의10.wav", "회의1²2.wav", "회의2.wav"]
    assert sorted(names, key=_fallback_sort_key) == ["회의1²2.wav", "회의2.wav", "회의10.wav"]
