"""main의 화면 표시용 순수 함수 테스트(창을 띄우지 않는다)."""
from datetime import datetime
from pathlib import Path

import pytest

from batch_runner import BatchSummary
from main import (
    AFTER_DESTROY,
    AFTER_KEEP,
    AFTER_WAIT,
    CLOSE_ASK_FORCE,
    CLOSE_ASK_WAIT,
    CLOSE_NOW,
    close_after_answer,
    close_prompt,
    exit_code,
    format_file,
    format_finished,
    format_overall,
    row_values,
    summary_message,
)
from records import ResultRow


def _summary(**kw) -> BatchSummary:
    base = dict(
        total=3,
        done=3,
        failed=0,
        stopped=False,
        excel_path=Path(r"C:\녹음\result\STT_결과_20261028_093015.xlsx"),
        memory_error=False,
    )
    base.update(kw)
    return BatchSummary(**base)


def _row(status="완료", duration=754.0, elapsed=84.6) -> ResultRow:
    return ResultRow(1, "회의.wav", "내용", duration, elapsed, status, datetime(2026, 10, 28, 9, 31, 40))


# --------------------------------------------------------------- 진행률


def test_format_overall_first_file_start():
    value, text = format_overall(index=1, total=4, fraction=0.0, completed=0)
    assert value == 0.0
    assert text == "0 / 전체 4건 (0%)"


def test_format_overall_mid_file():
    value, text = format_overall(index=2, total=4, fraction=0.5, completed=1)
    assert value == pytest.approx(37.5)
    assert text == "1 / 전체 4건 (37%)"  # 반올림하지 않고 버린다(끝나기 전에 100%가 되지 않게)


def test_format_overall_last_file_done():
    value, text = format_overall(index=3, total=3, fraction=1.0, completed=3)
    assert value == pytest.approx(100.0)
    assert text == "3 / 전체 3건 (100%)"


def test_format_overall_clamps_and_handles_zero_total():
    assert format_overall(index=1, total=0, fraction=0.0, completed=0) == (0.0, "0 / 전체 0건 (0%)")
    value, _ = format_overall(index=1, total=2, fraction=1.7, completed=0)
    assert value == pytest.approx(50.0)


def test_format_file():
    value, text = format_file("하위 폴더 (2팀)\\회의 #1.wav", 0.256)
    assert value == pytest.approx(25.6)
    assert text == "현재 파일: 하위 폴더 (2팀)\\회의 #1.wav  25%"


def test_format_finished_complete_is_100():
    value, text = format_finished(_summary(done=2, failed=1))
    assert value == 100.0
    assert text == "3 / 전체 3건 (100%)"


def test_format_finished_stopped_is_partial():
    value, text = format_finished(_summary(total=4, done=1, failed=0, stopped=True))
    assert value == pytest.approx(25.0)
    assert text == "1 / 전체 4건 (25%)"


# --------------------------------------------------------------- 목록 표


def test_row_values_success():
    assert row_values(_row()) == ("완료", "00:12:34", "85초")


def test_row_values_failed_has_blank_duration():
    assert row_values(_row(status="실패(파일 손상)", duration=None, elapsed=0.2)) == ("실패(파일 손상)", "", "0초")


# --------------------------------------------------------------- 완료 알림


def test_summary_message_complete():
    msg = summary_message(_summary(done=2, failed=1))
    assert "전체 3건" in msg and "성공 2건" in msg and "실패 1건" in msg
    assert "STT_결과_20261028_093015.xlsx" in msg
    assert "중지" not in msg
    assert "small" not in msg


def test_summary_message_stopped():
    msg = summary_message(_summary(total=5, done=2, failed=0, stopped=True))
    assert "중지됨" in msg
    assert "3건" in msg  # 처리하지 않은 건수


def test_summary_message_memory_error():
    msg = summary_message(_summary(done=2, failed=1, memory_error=True))
    assert "메모리가 부족했습니다. config.ini의 name을 small로 바꿔 보세요." in msg


def test_summary_message_fallback_file():
    path = Path(r"C:\녹음\result\STT_결과_20261028_093015_임시저장.xlsx")
    msg = summary_message(_summary(excel_path=path))
    assert "STT_결과_20261028_093015_임시저장.xlsx" in msg
    assert "열려 있어" in msg


def test_exit_code():
    assert exit_code(_summary()) == 0
    assert exit_code(_summary(done=2, failed=1)) == 1
    assert exit_code(_summary(total=3, done=1, failed=0, stopped=True)) == 1


# --------------------------------------------------------------- 창 닫기 판단


def test_close_prompt():
    assert close_prompt(running=False, close_requested=False) == CLOSE_NOW
    assert close_prompt(running=False, close_requested=True) == CLOSE_NOW
    assert close_prompt(running=True, close_requested=False) == CLOSE_ASK_WAIT
    assert close_prompt(running=True, close_requested=True) == CLOSE_ASK_FORCE


@pytest.mark.parametrize(
    "prompt, answer, still_running, expected",
    [
        (CLOSE_ASK_WAIT, True, True, AFTER_WAIT),  # 현재 파일 끝나면 닫기
        (CLOSE_ASK_WAIT, False, True, AFTER_KEEP),
        (CLOSE_ASK_WAIT, True, False, AFTER_DESTROY),  # 질문 중에 끝났으면 기다릴 이벤트가 없으니 바로 닫기
        (CLOSE_ASK_WAIT, False, False, AFTER_KEEP),
        (CLOSE_ASK_FORCE, True, True, AFTER_DESTROY),
        (CLOSE_ASK_FORCE, False, True, AFTER_KEEP),  # 종료 요청은 유지된 채 계속
        (CLOSE_ASK_FORCE, True, False, AFTER_DESTROY),
        (CLOSE_ASK_FORCE, False, False, AFTER_KEEP),
    ],
)
def test_close_after_answer(prompt, answer, still_running, expected):
    assert close_after_answer(prompt, answer, still_running) == expected
