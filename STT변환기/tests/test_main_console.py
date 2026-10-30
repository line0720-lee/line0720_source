"""콘솔 없는 exe의 --no-gui 모드에서 부모 콘솔(cmd/PowerShell)에 붙는 도우미 테스트(ctypes는 가짜로 바꾼다)."""
import io
import logging
import sys
from types import SimpleNamespace

import pytest

import config
import main


@pytest.fixture(autouse=True)
def tmp_logging(tmp_path, monkeypatch):
    """main()이 프로젝트 logs 폴더 대신 임시 폴더에 로그를 남기게 한다."""
    root = logging.getLogger()
    before = list(root.handlers)
    level = root.level
    monkeypatch.setattr(main, "setup_logging", lambda: config.setup_logging(tmp_path / "앱"))
    # 테스트가 sys.stdout/stderr를 바꿔도 끝나면 되돌린다
    monkeypatch.setattr(sys, "stdout", sys.stdout)
    monkeypatch.setattr(sys, "stderr", sys.stderr)
    yield
    for handler in list(root.handlers):
        if handler not in before:
            root.removeHandler(handler)
            handler.close()
    root.setLevel(level)


def _fake_kernel32(monkeypatch, attach):
    calls = []

    def attach_console(pid):
        calls.append(pid)
        return attach(pid)

    monkeypatch.setattr(main.ctypes, "windll", SimpleNamespace(kernel32=SimpleNamespace(AttachConsole=attach_console)))
    return calls


def _fake_open(monkeypatch, fail_on: int | None = None):
    """CONOUT$ 열기를 가짜로. fail_on번째(1부터) 열기에서 OSError. 연 스트림 목록을 돌려준다."""
    opened = []

    def fake_open(name, mode, **kwargs):
        if fail_on is not None and len(opened) + 1 == fail_on:
            raise OSError("CONOUT$ 열기 실패")
        stream = io.StringIO()
        opened.append((name, mode, kwargs, stream))
        return stream

    monkeypatch.setattr(main, "open", fake_open, raising=False)
    return opened


def test_attach_skipped_when_console_exists(monkeypatch):
    """python으로 실행했거나 출력을 리디렉션해 stdout·stderr가 원래 있으면 붙지 않는다."""
    monkeypatch.setattr(main, "_MISSING_STD_STREAMS", ())
    calls = _fake_kernel32(monkeypatch, lambda pid: 1)
    assert main._attach_parent_console() is False
    assert calls == []


def test_prepare_records_only_missing_stream_names(monkeypatch):
    """`STT변환기.exe … --no-gui > out.txt`처럼 stdout만 있으면 stderr만 없는 것으로 기록한다."""
    monkeypatch.setattr(main, "_MISSING_STD_STREAMS", main._MISSING_STD_STREAMS)  # 끝나면 되돌린다
    redirected = io.StringIO()
    monkeypatch.setattr(sys, "stdout", redirected)
    monkeypatch.setattr(sys, "stderr", None)
    main._prepare_std_streams()
    try:
        assert main._MISSING_STD_STREAMS == ("stderr",)
        assert sys.stdout is redirected
        assert sys.stderr is not None
    finally:
        sys.stderr.close()  # 빈 장치(devnull)


def test_attach_replaces_only_missing_stderr(monkeypatch):
    """출력 리디렉션(stdout)은 그대로 두고 없던 stderr만 CONOUT$로 연다."""
    monkeypatch.setattr(main, "_MISSING_STD_STREAMS", ("stderr",))
    _fake_kernel32(monkeypatch, lambda pid: 1)
    opened = _fake_open(monkeypatch)
    redirected = sys.stdout
    assert main._attach_parent_console() is True
    assert [o[0] for o in opened] == ["CONOUT$"]
    assert sys.stdout is redirected
    assert sys.stderr is opened[0][3]


def test_attach_replaces_only_missing_stdout(monkeypatch):
    monkeypatch.setattr(main, "_MISSING_STD_STREAMS", ("stdout",))
    _fake_kernel32(monkeypatch, lambda pid: 1)
    opened = _fake_open(monkeypatch)
    redirected = sys.stderr
    assert main._attach_parent_console() is True
    assert sys.stdout is opened[0][3] and sys.stderr is redirected


def test_attach_failure_is_silent(monkeypatch):
    monkeypatch.setattr(main, "_MISSING_STD_STREAMS", ("stdout", "stderr"))
    calls = _fake_kernel32(monkeypatch, lambda pid: 0)  # 부모 콘솔 없음(탐색기에서 실행 등)
    before = (sys.stdout, sys.stderr)
    assert main._attach_parent_console() is False
    assert calls == [-1]  # ATTACH_PARENT_PROCESS
    assert (sys.stdout, sys.stderr) == before


@pytest.mark.parametrize("error", [OSError("거부"), AttributeError("windll 없음")])
def test_attach_exception_is_silent(monkeypatch, error):
    monkeypatch.setattr(main, "_MISSING_STD_STREAMS", ("stdout", "stderr"))

    def boom(pid):
        raise error

    _fake_kernel32(monkeypatch, boom)
    before = (sys.stdout, sys.stderr)
    assert main._attach_parent_console() is False
    assert (sys.stdout, sys.stderr) == before


def test_attach_success_reopens_both_streams_on_conout(monkeypatch):
    monkeypatch.setattr(main, "_MISSING_STD_STREAMS", ("stdout", "stderr"))
    _fake_kernel32(monkeypatch, lambda pid: 1)
    opened = _fake_open(monkeypatch)
    assert main._attach_parent_console() is True
    assert [o[0] for o in opened] == ["CONOUT$", "CONOUT$"]
    assert all(o[2].get("encoding") == "utf-8" and o[2].get("buffering") == 1 for o in opened)
    assert sys.stdout is opened[0][3] and sys.stderr is opened[1][3]
    assert sys.stdout is not sys.stderr


def test_conout_open_failure_keeps_streams(monkeypatch):
    monkeypatch.setattr(main, "_MISSING_STD_STREAMS", ("stdout", "stderr"))
    _fake_kernel32(monkeypatch, lambda pid: 1)
    _fake_open(monkeypatch, fail_on=1)
    before = (sys.stdout, sys.stderr)
    assert main._attach_parent_console() is False
    assert (sys.stdout, sys.stderr) == before


def test_conout_second_open_failure_closes_first_and_keeps_streams(monkeypatch):
    monkeypatch.setattr(main, "_MISSING_STD_STREAMS", ("stdout", "stderr"))
    _fake_kernel32(monkeypatch, lambda pid: 1)
    opened = _fake_open(monkeypatch, fail_on=2)
    before = (sys.stdout, sys.stderr)
    assert main._attach_parent_console() is False
    assert (sys.stdout, sys.stderr) == before
    assert len(opened) == 1 and opened[0][3].closed  # 먼저 연 것은 닫았다


@pytest.mark.parametrize("argv", [[], ["--folder", "C:\\없는 폴더"], ["--subfolders"]])
def test_gui_mode_never_attaches(monkeypatch, argv):
    attached = []
    monkeypatch.setattr(main, "_attach_parent_console", lambda: attached.append(True) or True)
    monkeypatch.setattr(main, "run_gui", lambda *args: 0)
    assert main.main(argv) == 0
    assert attached == []


def test_no_gui_mode_attaches_once(monkeypatch, tmp_path):
    attached = []
    monkeypatch.setattr(main, "_attach_parent_console", lambda: attached.append(True) or False)
    assert main.main(["--no-gui", "--folder", str(tmp_path / "없는 폴더")]) == 2
    assert attached == [True]


def test_no_gui_usage_error_attaches_before_printing(monkeypatch, capsys):
    """--no-gui 인자 오류도 부모 콘솔에 보이도록 출력 전에 붙는다."""
    attached = []
    monkeypatch.setattr(main, "_attach_parent_console", lambda: attached.append(True) or False)
    assert main.main(["--no-gui"]) == 2
    assert attached == [True]
    assert "오류:" in capsys.readouterr().err
