"""모델별 처리 시간 측정: 모델 로딩 시간·처리 시간·실시간 배수(처리 시간 / 재생 시간)를 표로 출력한다.

사용법(프로젝트 폴더에서):
    .venv\\Scripts\\python tools\\benchmark.py --models small medium large-v3-turbo

측정 파일:
    - tests\\samples\\기본_16k.wav (짧은 파일)
    - tests\\samples\\긴파일_65분.wav (있으면)
    - tests\\output\\benchmark\\긴파일_5분.wav (없으면 기본_16k.wav를 이어 붙여 만든다)
결과 표는 화면에 출력하고 tests\\output\\benchmark\\benchmark_결과.md에도 저장한다.
"""
from __future__ import annotations

import argparse
import platform
import sys
import time
import wave
from dataclasses import dataclass
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parent.parent
SRC_DIR = PROJECT_ROOT / "src"
SAMPLES_DIR = PROJECT_ROOT / "tests" / "samples"
OUTPUT_DIR = PROJECT_ROOT / "tests" / "output" / "benchmark"
MODELS_DIR = PROJECT_ROOT / "models"
BASE_WAV_NAME = "기본_16k.wav"
LONG_65_NAME = "긴파일_65분.wav"

if str(SRC_DIR) not in sys.path:
    sys.path.insert(0, str(SRC_DIR))  # 프로그램 모듈(src\config.py, src\stt_engine.py)을 불러오기 위해

from config import VALID_MODELS  # noqa: E402  (모델 이름 목록은 프로그램과 같은 것을 쓴다)


@dataclass(frozen=True)
class Measurement:
    model: str
    file: str
    duration_sec: float  # 재생 시간
    load_sec: float  # 모델 로딩 시간(같은 모델이면 같은 값)
    elapsed_sec: float  # 처리 시간
    chars: int  # 변환된 글자 수(확인용)


# ------------------------------------------------------------------ wav 도우미(모델 불필요)


def wav_duration(path: Path) -> float:
    """PCM .wav 파일의 재생 시간(초)."""
    with wave.open(str(path), "rb") as wf:
        rate = wf.getframerate()
        return wf.getnframes() / rate if rate else 0.0


def build_long_wav(src: Path, dst: Path, target_sec: float) -> float:
    """src를 처음부터 끝까지 반복해 이어 붙여 target_sec 이상인 dst를 만들고 dst의 재생 시간을 돌려준다.

    원본과 같은 형식(채널·샘플 크기·샘플레이트)으로 쓴다. 원본 길이가 0이면 ValueError.
    """
    with wave.open(str(src), "rb") as wf:
        params = wf.getparams()
        frames = wf.readframes(wf.getnframes())
    one = params.nframes / params.framerate if params.framerate else 0.0
    if one <= 0:
        raise ValueError(f"재생 시간이 0인 파일은 이어 붙일 수 없습니다: {src}")
    repeat = max(1, int(-(-target_sec // one)))  # 올림
    dst.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(dst), "wb") as out:
        out.setnchannels(params.nchannels)
        out.setsampwidth(params.sampwidth)
        out.setframerate(params.framerate)
        for _ in range(repeat):
            out.writeframes(frames)
    return wav_duration(dst)


def ensure_long_wav(minutes: float, base: Path = SAMPLES_DIR / BASE_WAV_NAME, out_dir: Path = OUTPUT_DIR) -> Path:
    """긴파일_{minutes}분.wav가 없거나 너무 짧으면 만든다."""
    target = round(minutes * 60, 6)  # 0.1 * 60 = 6.000000000000001 같은 오차 제거
    dst = out_dir / f"긴파일_{minutes:g}분.wav"
    if dst.is_file():
        try:
            if wav_duration(dst) >= target:
                return dst
        except (wave.Error, EOFError):
            pass  # 깨진 파일이면 다시 만든다
    build_long_wav(base, dst, target)
    return dst


# ------------------------------------------------------------------ 표 출력


def format_seconds(sec: float) -> str:
    """초를 표에 쓸 문구로: 60초 미만은 '12.3초', 이상은 '5분 08초'."""
    if sec < 60:
        return f"{sec:.1f}초"
    minutes, seconds = divmod(int(round(sec)), 60)
    return f"{minutes}분 {seconds:02d}초"


def format_rtf(elapsed_sec: float, duration_sec: float) -> str:
    """실시간 배수(처리 시간 / 재생 시간)를 소수 둘째 자리까지. 재생 시간이 0이면 '-'."""
    if duration_sec <= 0:
        return "-"
    return f"{elapsed_sec / duration_sec:.2f}"


def format_table(rows: list[Measurement]) -> str:
    """Markdown 표."""
    lines = [
        "| 모델 | 파일 | 재생 시간 | 모델 로딩 | 처리 시간 | 실시간 배수 |",
        "| --- | --- | ---: | ---: | ---: | ---: |",
    ]
    for r in rows:
        lines.append(
            f"| {r.model} | {r.file} | {format_seconds(r.duration_sec)} | {format_seconds(r.load_sec)} "
            f"| {format_seconds(r.elapsed_sec)} | {format_rtf(r.elapsed_sec, r.duration_sec)} |"
        )
    return "\n".join(lines)


# ------------------------------------------------------------------ 측정(모델 필요)


def measure_model(model: str, wavs: list[Path], models_dir: Path = MODELS_DIR) -> list[Measurement]:
    """모델을 1회 불러온 뒤 파일마다 변환 시간을 잰다.

    파일별 처리 시간에는 첫 파일에서 한 번만 하는 준비(VAD 모델 로딩 등)도 들어가므로 첫 파일 값이 약간 크게 나온다.
    """
    from config import AppConfig, physical_cores
    from stt_engine import SttEngine  # faster_whisper를 불러오므로 측정할 때만 import한다

    cfg = AppConfig(
        model_name=model,
        model_path=models_dir / model,
        language="ko",
        cpu_threads=physical_cores(),
        glossary_path=PROJECT_ROOT / "glossary.txt",
    )
    started = time.perf_counter()
    engine = SttEngine(cfg)
    load_sec = time.perf_counter() - started
    print(f"  {model}: 모델 로딩 {load_sec:.1f}초", file=sys.stderr, flush=True)

    rows = []
    for wav in wavs:
        result = engine.transcribe(wav)
        rows.append(Measurement(model, wav.name, result.duration_sec, load_sec, result.elapsed_sec, len(result.text)))
        print(f"  {model}: {wav.name} 처리 {result.elapsed_sec:.1f}초 / 재생 {result.duration_sec:.1f}초",
              file=sys.stderr, flush=True)
    del engine
    return rows


def pc_summary() -> str:
    try:
        import psutil

        cores = f"물리 {psutil.cpu_count(logical=False)}코어 / 논리 {psutil.cpu_count()}스레드"
        ram = f"RAM {psutil.virtual_memory().total / 1024 ** 3:.1f}GB"
    except ImportError:
        cores, ram = "코어 수 알 수 없음", "RAM 알 수 없음"
    return f"{platform.processor() or platform.machine()}, {cores}, {ram}, {platform.platform()}"


def main(argv: list[str] | None = None) -> int:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except (AttributeError, ValueError):
        pass
    parser = argparse.ArgumentParser(description="모델별 처리 시간 측정")
    parser.add_argument("--models", nargs="+", choices=VALID_MODELS, default=list(VALID_MODELS))
    parser.add_argument("--long-minutes", type=float, default=5, help="만들어 쓸 긴 파일 길이(분, 기본 5)")
    parser.add_argument("--out", type=Path, default=OUTPUT_DIR / "benchmark_결과.md", help="결과 표를 저장할 파일")
    args = parser.parse_args(argv)

    base = SAMPLES_DIR / BASE_WAV_NAME
    if not base.is_file():
        print(f"오류: 샘플이 없습니다: {base}  (powershell -File tests\\make_samples.ps1 로 먼저 만드세요)")
        return 1
    missing = [m for m in args.models if not (MODELS_DIR / m / "model.bin").is_file()]
    if missing:
        print(f"오류: 모델이 없습니다: {', '.join(missing)}  (download_model.py로 먼저 받으세요)")
        return 1

    wavs = [base, ensure_long_wav(args.long_minutes)]
    if (SAMPLES_DIR / LONG_65_NAME).is_file():
        wavs.append(SAMPLES_DIR / LONG_65_NAME)
    print("측정 파일: " + ", ".join(f"{w.name}({format_seconds(wav_duration(w))})" for w in wavs), file=sys.stderr)

    rows: list[Measurement] = []
    for model in args.models:
        rows += measure_model(model, wavs)

    table = format_table(rows)
    report = f"측정 PC: {pc_summary()}\n\n{table}\n"
    print(report)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(report, encoding="utf-8")
    print(f"저장: {args.out}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
