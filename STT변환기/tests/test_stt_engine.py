"""stt_engine 테스트. 실제 모델이 필요한 것은 @pytest.mark.model (빠른 small 모델 사용)."""
import logging
import os
import socket
import types
import wave
from pathlib import Path

import pytest

import stt_engine
from config import AppConfig
from stt_engine import CorruptFileError, ModelNotFoundError, SttEngine, TranscribeResult

# 합성 문장(기본_16k.wav 등)에 들어 있는 핵심 단어. TTS+small 모델이라 일부는 철자가 틀릴 수 있다.
KEYWORDS = ("병리과", "조직검사", "검체", "판독", "화요일", "회의")


# ---------------------------------------------------------------- 실제 모델 테스트

@pytest.mark.model
def test_engine_loads_from_korean_path(small_cfg, caplog):
    # 프로젝트 경로(한글·괄호 포함)에서 대체 방법(짧은 경로·files=) 없이 일반 경로로 로딩되어야 한다
    assert any(ord(ch) > 127 for ch in str(small_cfg.model_path))
    with caplog.at_level(logging.INFO, logger="stt_engine"):
        engine = SttEngine(small_cfg)
    assert engine is not None
    messages = [r.getMessage() for r in caplog.records]
    assert any("방식=일반 경로" in m for m in messages), messages


@pytest.mark.model
def test_transcribe_basic(small_engine, samples_dir):
    result = small_engine.transcribe(samples_dir / "기본_16k.wav")
    assert isinstance(result, TranscribeResult)
    assert any(word in result.text for word in KEYWORDS), result.text
    assert result.duration_sec > 0
    assert result.elapsed_sec > 0


@pytest.mark.model
@pytest.mark.parametrize("name", ["전화_8k.wav", "스테레오_44k.wav"])
def test_transcribe_8k_and_stereo(small_engine, samples_dir, name):
    result = small_engine.transcribe(samples_dir / name)
    assert result.text.strip() != ""


@pytest.mark.model
def test_transcribe_nested_special_filename(small_engine, samples_dir):
    # 공백·괄호·#이 들어간 하위 폴더 경로
    result = small_engine.transcribe(samples_dir / "하위 폴더 (2팀)" / "회의 #1.wav")
    assert result.text.strip() != ""


@pytest.mark.model
def test_silence_returns_empty(small_engine, samples_dir):
    result = small_engine.transcribe(samples_dir / "무음.wav")
    assert result.text == ""
    assert result.duration_sec == pytest.approx(10.0, abs=0.1)


@pytest.mark.model
@pytest.mark.parametrize("name", ["빈파일.wav", "가짜.wav"])
def test_zero_byte_and_fake_raise_corrupt(small_engine, samples_dir, name):
    with pytest.raises(CorruptFileError) as exc:
        small_engine.transcribe(samples_dir / name)
    assert "파일 손상" in str(exc.value)


@pytest.mark.model
def test_progress_callback_monotonic(small_engine, samples_dir):
    values = []
    small_engine.transcribe(samples_dir / "기본_16k.wav", on_progress=values.append)
    assert values, "진행률 콜백이 한 번도 호출되지 않았다"
    assert all(0.0 <= v <= 1.0 for v in values)
    assert values == sorted(values)
    assert values[-1] == 1.0


@pytest.mark.model
def test_no_network_during_transcribe(monkeypatch, small_cfg, samples_dir):
    def _blocked(self, *args, **kwargs):
        raise AssertionError("네트워크 접속 시도가 감지되었다")

    monkeypatch.setattr(socket.socket, "connect", _blocked)
    # 온라인 조회 차단 설정이 faster_whisper(huggingface_hub)보다 먼저 들어가 있어야 한다
    assert os.environ["HF_HUB_OFFLINE"] == "1"
    import huggingface_hub.constants as hf_constants

    if hasattr(hf_constants, "HF_HUB_OFFLINE"):
        assert hf_constants.HF_HUB_OFFLINE is True
    engine = SttEngine(small_cfg)  # 로딩부터 변환까지 모두 차단 상태에서 수행
    result = engine.transcribe(samples_dir / "기본_16k.wav")
    assert result.text.strip() != ""


@pytest.mark.model
def test_glossary_over_limit_keeps_front_part(small_engine):
    # 서로 다른 한글 단어 400개 -> 223토큰을 훨씬 넘는다
    words = [f"{chr(0xAC00 + i * 7)}{chr(0xB000 + i * 5)}{chr(0xC000 + i * 3)}" for i in range(400)]
    text = ", ".join(words)
    tok = small_engine._model.hf_tokenizer
    assert len(tok.encode(text, add_special_tokens=False).ids) > stt_engine.MAX_PROMPT_TOKENS

    limited = small_engine._limit_prompt(text)
    assert text.startswith(limited)  # 뒷부분이 아니라 앞부분이 남는다
    assert len(tok.encode(limited, add_special_tokens=False).ids) <= stt_engine.MAX_PROMPT_TOKENS
    assert "�" not in limited


@pytest.mark.model
def test_glossary_under_limit_is_unchanged(small_engine):
    assert small_engine._limit_prompt("병리과, 조직검사") == "병리과, 조직검사"


# ---------------------------------------------------------------- 모델이 필요 없는 테스트

def _cfg(tmp_path: Path, model_dir: Path, glossary: Path | None = None) -> AppConfig:
    return AppConfig(
        model_name="small",
        model_path=model_dir,
        language="ko",
        cpu_threads=2,
        glossary_path=glossary or tmp_path / "없는_용어집.txt",
    )


def test_missing_model_raises(tmp_path):
    missing = tmp_path / "없는 모델"
    with pytest.raises(ModelNotFoundError) as exc:
        SttEngine(_cfg(tmp_path, missing))
    assert str(missing) in str(exc.value)


def test_model_dir_without_model_bin_raises(tmp_path):
    (tmp_path / "빈폴더").mkdir()
    with pytest.raises(ModelNotFoundError):
        SttEngine(_cfg(tmp_path, tmp_path / "빈폴더"))


@pytest.fixture
def fake_model_dir(tmp_path):
    folder = tmp_path / "모델"
    folder.mkdir()
    (folder / "model.bin").write_bytes(b"bin")
    (folder / "config.json").write_text("{}", encoding="utf-8")
    (folder / "tokenizer.json").write_text("{}", encoding="utf-8")
    return folder


class _FakeTokenizer:
    """글자 하나를 토큰 하나로 취급하는 가짜 토크나이저."""

    def encode(self, text, add_special_tokens=True):
        return types.SimpleNamespace(ids=[ord(ch) for ch in text])

    def decode(self, ids):
        return "".join(chr(i) for i in ids)


def _make_fake_whisper(fail_times: int, calls: list):
    class FakeWhisper:
        def __init__(self, path, **kwargs):
            calls.append((path, kwargs))
            if len(calls) <= fail_times:
                raise RuntimeError("로딩 실패(가짜)")
            self.hf_tokenizer = _FakeTokenizer()

    return FakeWhisper


def test_load_fallback_order(monkeypatch, tmp_path, fake_model_dir):
    calls = []
    monkeypatch.setattr(stt_engine, "WhisperModel", _make_fake_whisper(2, calls))
    monkeypatch.setattr(stt_engine, "_short_path", lambda p: "SHORT/PATH")

    SttEngine(_cfg(tmp_path, fake_model_dir))

    assert len(calls) == 3
    # 1차: 원래 경로
    assert calls[0][0] == str(fake_model_dir)
    assert "files" not in calls[0][1]
    # 2차: 짧은 경로
    assert calls[1][0] == "SHORT/PATH"
    # 3차: files= (파일 내용을 메모리로 전달)
    assert calls[2][1]["files"]["model.bin"] == b"bin"
    assert set(calls[2][1]["files"]) == {"model.bin", "config.json", "tokenizer.json"}
    for _, kwargs in calls:
        assert kwargs["device"] == "cpu"
        assert kwargs["compute_type"] == "int8"
        assert kwargs["cpu_threads"] == 2
        assert kwargs["local_files_only"] is True


def test_load_stops_after_first_success(monkeypatch, tmp_path, fake_model_dir):
    calls = []
    monkeypatch.setattr(stt_engine, "WhisperModel", _make_fake_whisper(0, calls))
    SttEngine(_cfg(tmp_path, fake_model_dir))
    assert len(calls) == 1


def test_load_memory_error_is_not_retried(monkeypatch, tmp_path, fake_model_dir):
    """메모리 부족은 다른 방법(짧은 경로·files= 메모리 로딩)으로 다시 시도하지 않고 그대로 올린다."""
    calls = []

    class OutOfMemoryWhisper:
        def __init__(self, path, **kwargs):
            calls.append((path, kwargs))
            raise MemoryError("bad allocation")

    monkeypatch.setattr(stt_engine, "WhisperModel", OutOfMemoryWhisper)
    monkeypatch.setattr(stt_engine, "_short_path", lambda p: "SHORT/PATH")
    with pytest.raises(MemoryError):
        SttEngine(_cfg(tmp_path, fake_model_dir))
    assert len(calls) == 1
    assert "files" not in calls[0][1]


@pytest.mark.parametrize("fail_before", [1, 2])
def test_load_memory_error_in_fallback_is_not_retried(monkeypatch, tmp_path, fake_model_dir, fail_before):
    """대체 경로 시도 중에 메모리가 부족해도 다음 방법으로 넘어가지 않는다."""
    calls = []

    class Whisper:
        def __init__(self, path, **kwargs):
            calls.append((path, kwargs))
            if len(calls) <= fail_before:
                raise RuntimeError("로딩 실패(가짜)")
            raise MemoryError("bad allocation")

    monkeypatch.setattr(stt_engine, "WhisperModel", Whisper)
    monkeypatch.setattr(stt_engine, "_short_path", lambda p: "SHORT/PATH")
    with pytest.raises(MemoryError):
        SttEngine(_cfg(tmp_path, fake_model_dir))
    assert len(calls) == fail_before + 1


def test_load_all_fail_raises(monkeypatch, tmp_path, fake_model_dir):
    calls = []
    monkeypatch.setattr(stt_engine, "WhisperModel", _make_fake_whisper(99, calls))
    monkeypatch.setattr(stt_engine, "_short_path", lambda p: "SHORT/PATH")
    with pytest.raises(RuntimeError) as exc:
        SttEngine(_cfg(tmp_path, fake_model_dir))
    assert "모델" in str(exc.value)
    assert len(calls) == 3


def _write_tone_wav(path: Path, seconds: float = 1.0):
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(16000)
        w.writeframes(b"\x00\x01" * int(16000 * seconds))


def test_transcribe_passes_stt_options(monkeypatch, tmp_path, fake_model_dir):
    """language / vad_filter / beam_size / initial_prompt가 설정대로 전달되는지 확인."""
    glossary = tmp_path / "glossary.txt"
    glossary.write_text("# 주석\n병리과\n조직검사\n", encoding="utf-8-sig")
    captured = {}

    class FakeWhisper:
        def __init__(self, path, **kwargs):
            self.hf_tokenizer = _FakeTokenizer()

        def transcribe(self, audio, **kwargs):
            captured.update(kwargs)
            segs = [types.SimpleNamespace(text="  첫째 줄 ", end=0.5), types.SimpleNamespace(text=" ", end=0.7),
                    types.SimpleNamespace(text="둘째 줄", end=1.0)]
            return iter(segs), types.SimpleNamespace(duration=1.0)

    monkeypatch.setattr(stt_engine, "WhisperModel", FakeWhisper)
    wav = tmp_path / "t.wav"
    _write_tone_wav(wav)
    engine = SttEngine(_cfg(tmp_path, fake_model_dir, glossary))
    result = engine.transcribe(wav)

    assert captured["language"] == "ko"
    assert captured["vad_filter"] is True
    assert captured["beam_size"] == 5
    assert captured["initial_prompt"] == "병리과, 조직검사"
    assert result.text == "첫째 줄\n둘째 줄"  # 공백뿐인 세그먼트는 제외, 줄바꿈으로 연결
    assert result.duration_sec == 1.0


def test_no_glossary_omits_initial_prompt(monkeypatch, tmp_path, fake_model_dir):
    captured = {}

    class FakeWhisper:
        def __init__(self, path, **kwargs):
            self.hf_tokenizer = _FakeTokenizer()

        def transcribe(self, audio, **kwargs):
            captured.update(kwargs)
            return iter([]), types.SimpleNamespace(duration=1.0)

    monkeypatch.setattr(stt_engine, "WhisperModel", FakeWhisper)
    wav = tmp_path / "t.wav"
    _write_tone_wav(wav)
    SttEngine(_cfg(tmp_path, fake_model_dir)).transcribe(wav)
    assert captured["initial_prompt"] is None


def test_wav_without_samples_is_corrupt(monkeypatch, tmp_path, fake_model_dir):
    class FakeWhisper:
        def __init__(self, path, **kwargs):
            self.hf_tokenizer = _FakeTokenizer()

    monkeypatch.setattr(stt_engine, "WhisperModel", FakeWhisper)
    wav = tmp_path / "헤더만.wav"
    _write_tone_wav(wav, seconds=0)
    with pytest.raises(CorruptFileError):
        SttEngine(_cfg(tmp_path, fake_model_dir)).transcribe(wav)


def test_memory_error_propagates(monkeypatch, tmp_path, fake_model_dir):
    class FakeWhisper:
        def __init__(self, path, **kwargs):
            self.hf_tokenizer = _FakeTokenizer()

    def _boom(*args, **kwargs):
        raise MemoryError()

    monkeypatch.setattr(stt_engine, "WhisperModel", FakeWhisper)
    monkeypatch.setattr(stt_engine, "decode_audio", _boom)
    wav = tmp_path / "t.wav"
    _write_tone_wav(wav)
    with pytest.raises(MemoryError):
        SttEngine(_cfg(tmp_path, fake_model_dir)).transcribe(wav)

