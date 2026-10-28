"""STT 엔진: Whisper 모델을 한 번 불러와 .wav 파일 1건씩 텍스트로 변환한다."""
from __future__ import annotations

import os

# 온라인 모델 조회를 막는다. faster_whisper를 import하기 전에 반드시 지정해야 한다.
os.environ["HF_HUB_OFFLINE"] = "1"

import argparse  # noqa: E402
import ctypes  # noqa: E402
import logging  # noqa: E402
import sys  # noqa: E402
import time  # noqa: E402
from dataclasses import dataclass, replace  # noqa: E402
from pathlib import Path  # noqa: E402
from typing import Callable  # noqa: E402

import av  # noqa: E402
from faster_whisper import WhisperModel  # noqa: E402
from faster_whisper.audio import decode_audio  # noqa: E402

from config import AppConfig, ConfigError, load_config, read_glossary  # noqa: E402

logger = logging.getLogger(__name__)

# Whisper의 initial_prompt 한도는 223토큰. 넘으면 faster-whisper가 뒷부분만 남기므로 앞부분을 직접 남긴다.
MAX_PROMPT_TOKENS = 223


class ModelNotFoundError(Exception):
    """모델 폴더(model.bin)가 없을 때. 메시지를 화면에 그대로 보여 줄 수 있다."""


class CorruptFileError(Exception):
    """손상되었거나 0바이트인 오디오 파일. 메시지는 '파일 손상: 원인' 형식."""


@dataclass
class TranscribeResult:
    text: str
    duration_sec: float  # 오디오 재생 시간
    elapsed_sec: float  # 변환에 걸린 시간


def _short_path(path: str) -> str:
    """Windows 8.3 짧은 경로로 바꾼다. 실패하거나 지원하지 않으면 원래 경로를 돌려준다."""
    try:
        buf = ctypes.create_unicode_buffer(32768)
        length = ctypes.windll.kernel32.GetShortPathNameW(path, buf, len(buf))
        if 0 < length < len(buf):
            return buf.value
    except (AttributeError, OSError):
        pass
    return path


class SttEngine:
    """모델을 생성 시 1회 로딩하고, transcribe()로 파일을 1건씩 변환한다."""

    def __init__(self, cfg: AppConfig):
        self._cfg = cfg
        model_path = Path(cfg.model_path)
        if not (model_path / "model.bin").is_file():
            raise ModelNotFoundError(
                f"모델 폴더를 찾을 수 없습니다: {model_path}\n"
                "models 폴더에 모델이 복사되어 있는지, config.ini의 모델 이름이 맞는지 확인하세요."
            )
        self._model = self._load_model(model_path)
        self._prompt = self._load_prompt()

    # ------------------------------------------------------------------ 모델 로딩

    def _load_model(self, model_path: Path) -> WhisperModel:
        """일반 경로 -> 짧은 경로 -> files=(메모리 로딩) 순으로 시도하고 성공한 방법을 로그에 남긴다."""
        options = dict(device="cpu", compute_type="int8", cpu_threads=self._cfg.cpu_threads, local_files_only=True)
        path_str = str(model_path)
        started = time.perf_counter()

        try:
            model = WhisperModel(path_str, **options)
            self._log_loaded("일반 경로", started)
            return model
        except Exception as exc:  # 비ASCII 경로 등으로 실패할 수 있다
            logger.warning("모델 로딩 실패(일반 경로): %s", exc)
            last_error = exc

        short = _short_path(path_str)
        if short != path_str:
            try:
                model = WhisperModel(short, **options)
                self._log_loaded(f"짧은 경로({short})", started)
                return model
            except Exception as exc:
                logger.warning("모델 로딩 실패(짧은 경로 %s): %s", short, exc)
                last_error = exc
        else:
            logger.warning("짧은 경로를 얻을 수 없어 건너뜁니다.")

        try:
            # 파일 내용을 메모리로 읽어 전달하면 C++ 쪽이 경로를 열지 않는다(메모리를 더 쓴다)
            files = {p.name: p.read_bytes() for p in model_path.iterdir() if p.is_file()}
            model = WhisperModel(path_str, files=files, **options)
            self._log_loaded("files= 메모리 로딩", started)
            return model
        except Exception as exc:
            logger.error("모델 로딩 실패(files=): %s", exc)
            last_error = exc

        raise RuntimeError(f"모델을 불러오지 못했습니다: {model_path} ({last_error})") from last_error

    def _log_loaded(self, how: str, started: float) -> None:
        logger.info("모델 로딩 완료: %s, 방식=%s, %.1f초", self._cfg.model_name, how, time.perf_counter() - started)

    # ------------------------------------------------------------------ 용어집

    def _load_prompt(self) -> str | None:
        text = read_glossary(self._cfg.glossary_path)
        return self._limit_prompt(text) if text else None

    def _limit_prompt(self, text: str) -> str:
        """토큰 수가 한도를 넘으면 앞부분만 남기고 경고 로그를 남긴다."""
        tokenizer = self._model.hf_tokenizer
        ids = tokenizer.encode(text, add_special_tokens=False).ids
        if len(ids) <= MAX_PROMPT_TOKENS:
            return text
        # 토큰 경계가 글자 중간이면 깨진 문자(U+FFFD)가 끝에 생기므로 제거한다
        limited = tokenizer.decode(ids[:MAX_PROMPT_TOKENS]).rstrip("�")
        logger.warning(
            "용어집이 %d토큰으로 한도(%d)를 넘어 앞부분만 사용합니다.", len(ids), MAX_PROMPT_TOKENS
        )
        return limited

    # ------------------------------------------------------------------ 변환

    def transcribe(self, wav: Path, on_progress: Callable[[float], None] | None = None) -> TranscribeResult:
        """파일 1건을 변환한다. 손상 파일은 CorruptFileError, 메모리 부족은 MemoryError 그대로 전달."""
        wav = Path(wav)
        started = time.perf_counter()

        if wav.stat().st_size == 0:
            raise CorruptFileError("파일 손상: 0바이트 파일입니다")

        audio = self._decode(wav)
        segments, info = self._model.transcribe(
            audio,
            language=self._cfg.language,
            vad_filter=True,
            beam_size=5,
            initial_prompt=self._prompt,
        )

        lines: list[str] = []
        last = 0.0
        for seg in segments:  # 제너레이터: 반복하는 동안 실제 변환이 진행된다
            line = seg.text.strip()
            if line:
                lines.append(line)
            if on_progress is not None and info.duration > 0:
                last = max(last, min(1.0, seg.end / info.duration))
                on_progress(last)
        if on_progress is not None:
            on_progress(1.0)

        return TranscribeResult(
            text="\n".join(lines),
            duration_sec=float(info.duration),
            elapsed_sec=time.perf_counter() - started,
        )

    @staticmethod
    def _decode(wav: Path):
        """오디오를 16kHz 모노로 디코딩한다. 열 수 없는 파일은 CorruptFileError로 바꾼다."""
        try:
            # 경로 대신 파일 객체를 넘겨 한글·특수문자 경로가 디코더에 직접 전달되지 않게 한다
            with open(wav, "rb") as fp:
                audio = decode_audio(fp)
        except (av.error.FFmpegError, ValueError) as exc:
            raise CorruptFileError(f"파일 손상: {exc}") from exc
        if len(audio) == 0:
            raise CorruptFileError("파일 손상: 오디오 데이터가 없습니다")
        return audio


def _main(argv: list[str] | None = None) -> int:
    """개발용 명령줄: python src\\stt_engine.py <wav> [--model small|medium|large-v3-turbo]"""
    parser = argparse.ArgumentParser(description=".wav 1건을 텍스트로 변환합니다.")
    parser.add_argument("wav", type=Path, help="변환할 .wav 파일")
    parser.add_argument("--model", choices=["small", "medium", "large-v3-turbo"], help="config.ini의 모델 대신 사용할 모델")
    args = parser.parse_args(argv)

    sys.stdout.reconfigure(encoding="utf-8")
    logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(name)s: %(message)s")

    try:
        cfg = load_config()
        if args.model:
            cfg = replace(cfg, model_name=args.model, model_path=cfg.model_path.parent / args.model)
        engine = SttEngine(cfg)
        result = engine.transcribe(args.wav)
    except (ConfigError, ModelNotFoundError, CorruptFileError) as exc:
        print(f"오류: {exc}")
        return 1

    print(result.text)
    print(f"재생 시간: {result.duration_sec:.1f}초")
    print(f"처리 시간: {result.elapsed_sec:.1f}초")
    return 0


if __name__ == "__main__":
    sys.exit(_main())
