"""tools\\benchmark.py 테스트: 모델 없이 쓸 수 있는 도우미(wav 이어 붙이기, 시간·배수 문구, 표)만 검사한다."""
import importlib.util
import sys
import wave
from pathlib import Path

import pytest

_TOOL = Path(__file__).resolve().parent.parent / "tools" / "benchmark.py"
_spec = importlib.util.spec_from_file_location("benchmark_tool", _TOOL)
benchmark = importlib.util.module_from_spec(_spec)
sys.modules["benchmark_tool"] = benchmark  # dataclass가 모듈을 찾을 수 있게 등록
_spec.loader.exec_module(benchmark)


def _make_wav(path: Path, seconds: float, rate: int = 16000, channels: int = 1) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(channels)
        wf.setsampwidth(2)
        wf.setframerate(rate)
        wf.writeframes(b"\x01\x00" * channels * int(rate * seconds))
    return path


def test_wav_duration(tmp_path):
    assert benchmark.wav_duration(_make_wav(tmp_path / "a.wav", 2.5)) == 2.5


def test_build_long_wav_reaches_target(tmp_path):
    src = _make_wav(tmp_path / "기본.wav", 1.5, rate=8000, channels=2)
    dst = tmp_path / "out" / "긴파일.wav"
    duration = benchmark.build_long_wav(src, dst, 10)
    assert duration == 10.5  # 1.5초 × 7회(올림)
    with wave.open(str(dst), "rb") as wf:
        assert (wf.getnchannels(), wf.getsampwidth(), wf.getframerate()) == (2, 2, 8000)


def test_build_long_wav_exact_multiple(tmp_path):
    src = _make_wav(tmp_path / "a.wav", 2)
    assert benchmark.build_long_wav(src, tmp_path / "b.wav", 6) == 6


def test_build_long_wav_rejects_empty(tmp_path):
    src = _make_wav(tmp_path / "a.wav", 0)
    with pytest.raises(ValueError, match="재생 시간이 0"):
        benchmark.build_long_wav(src, tmp_path / "b.wav", 6)


def test_valid_models_come_from_config():
    import config

    assert benchmark.VALID_MODELS is config.VALID_MODELS


def test_main_missing_sample(tmp_path, monkeypatch, capsys):
    monkeypatch.setattr(benchmark, "SAMPLES_DIR", tmp_path / "samples")
    assert benchmark.main(["--models", "small", "--out", str(tmp_path / "out.md")]) == 1
    assert "오류: 샘플이 없습니다" in capsys.readouterr().out
    assert not (tmp_path / "out.md").exists()


def test_main_missing_model(tmp_path, monkeypatch, capsys):
    samples = tmp_path / "samples"
    _make_wav(samples / benchmark.BASE_WAV_NAME, 1)
    monkeypatch.setattr(benchmark, "SAMPLES_DIR", samples)
    monkeypatch.setattr(benchmark, "MODELS_DIR", tmp_path / "models")
    assert benchmark.main(["--models", "small", "medium", "--out", str(tmp_path / "out.md")]) == 1
    out = capsys.readouterr().out
    assert "오류: 모델이 없습니다: small, medium" in out
    assert not (tmp_path / "out.md").exists()


def test_main_rejects_unknown_model(capsys):
    with pytest.raises(SystemExit) as info:
        benchmark.main(["--models", "tiny"])
    assert info.value.code == 2


def test_ensure_long_wav_reuses_existing(tmp_path):
    base = _make_wav(tmp_path / "기본.wav", 1)
    out = tmp_path / "bench"
    first = benchmark.ensure_long_wav(0.05, base=base, out_dir=out)  # 3초
    assert first.name == "긴파일_0.05분.wav"
    mtime = first.stat().st_mtime_ns
    again = benchmark.ensure_long_wav(0.05, base=base, out_dir=out)
    assert again == first and again.stat().st_mtime_ns == mtime


def test_ensure_long_wav_rebuilds_short_file(tmp_path):
    base = _make_wav(tmp_path / "기본.wav", 1)
    out = tmp_path / "bench"
    _make_wav(out / "긴파일_0.1분.wav", 1)  # 6초가 필요한데 1초짜리
    path = benchmark.ensure_long_wav(0.1, base=base, out_dir=out)
    assert benchmark.wav_duration(path) == 6


def test_format_rtf():
    assert benchmark.format_rtf(30, 120) == "0.25"
    assert benchmark.format_rtf(1, 3) == "0.33"
    assert benchmark.format_rtf(5, 0) == "-"


def test_format_seconds():
    assert benchmark.format_seconds(15.44) == "15.4초"
    assert benchmark.format_seconds(308) == "5분 08초"
    assert benchmark.format_seconds(3900) == "65분 00초"


def test_format_table():
    rows = [benchmark.Measurement("small", "기본_16k.wav", 15.4, 2.1, 3.86, 120)]
    table = benchmark.format_table(rows).splitlines()
    assert table[0].startswith("| 모델 | 파일 |")
    assert table[2] == "| small | 기본_16k.wav | 15.4초 | 2.1초 | 3.9초 | 0.25 |"
