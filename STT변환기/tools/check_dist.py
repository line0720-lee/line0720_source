"""배포 폴더(dist\\STT변환기) 점검: 꼭 있어야 할 파일과 없어야 할 CUDA DLL을 확인해 표로 출력한다.

사용법(프로젝트 폴더에서):
    .venv\\Scripts\\python tools\\check_dist.py                 배포 폴더 점검(모두 OK면 종료 코드 0, 아니면 1)
    .venv\\Scripts\\python tools\\check_dist.py --dist <폴더>    다른 위치의 배포 폴더 점검
    .venv\\Scripts\\python tools\\check_dist.py --print-model    config.ini의 모델 이름만 출력(build.bat용)
"""
from __future__ import annotations

import argparse
import configparser
import fnmatch
import sys
import unicodedata
from dataclasses import dataclass
from pathlib import Path

APP_NAME = "STT변환기"
EXE_NAME = f"{APP_NAME}.exe"
VALID_MODELS = ("small", "medium", "large-v3-turbo")  # src\config.py와 같게 유지
CUDA_MARKERS = ("cublas", "cudnn", "cudart", "cufft", "nvrtc")  # GPU 전용 DLL(배포본에 있으면 안 됨)
PROJECT_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_DIST = PROJECT_ROOT / "dist" / APP_NAME

# _internal 바로 아래에 있어야 하는 VC++ 런타임(python311.dll·ctranslate2.dll·onnxruntime.dll이 불러 씀).
# numpy.libs 등 하위 폴더에 있는 다른 사본은 인정하지 않는다. MSVCP140_1.dll은 onnxruntime.dll이 import한다.
# 파일 시스템이 대소문자를 구분하지 않으므로 실제 이름(VCRUNTIME140.dll, MSVCP140_1.dll 등)과 같게 찾는다.
_VC_RUNTIME_DLLS = ("vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "msvcp140_1.dll")

# _internal 아래 어디에 있든 있어야 하는 파일(항목 이름, 파일명 패턴). 대소문자는 구분하지 않는다.
_REQUIRED_IN_INTERNAL = [
    ("ctranslate2 DLL (ctranslate2.dll)", "ctranslate2.dll"),
    ("ctranslate2 OpenMP DLL (libiomp5md.dll)", "libiomp5md.dll"),
    ("onnxruntime DLL (onnxruntime.dll)", "onnxruntime.dll"),
    ("av DLL (avcodec-*.dll)", "avcodec-*.dll"),
    ("av DLL (avformat-*.dll)", "avformat-*.dll"),
    ("av DLL (avutil-*.dll)", "avutil-*.dll"),
    ("av DLL (swresample-*.dll)", "swresample-*.dll"),
]


@dataclass(frozen=True)
class CheckRow:
    item: str  # 확인 항목
    expected: str  # "있음" 또는 "없음"
    ok: bool
    detail: str  # 찾은 경로(배포 폴더 기준) 또는 실패 사유


def read_model_name(ini_path: Path) -> str:
    """config.ini의 [model] name. 파일이 없거나 이름이 잘못되면 ValueError."""
    parser = configparser.ConfigParser(interpolation=None)
    try:
        parser.read_string(Path(ini_path).read_text(encoding="utf-8-sig"))
    except (OSError, UnicodeDecodeError, configparser.Error) as exc:
        raise ValueError(f"config.ini를 읽을 수 없습니다: {ini_path} ({exc})") from exc
    name = parser.get("model", "name", fallback="medium").strip()
    if name not in VALID_MODELS:
        raise ValueError(f"config.ini의 모델 이름 '{name}'을(를) 사용할 수 없습니다 ({' / '.join(VALID_MODELS)})")
    return name


def _rel(path: Path, base: Path) -> str:
    try:
        return str(path.relative_to(base))
    except ValueError:
        return str(path)


def _exists_row(item: str, path: Path, dist: Path) -> CheckRow:
    ok = path.is_file()
    return CheckRow(item, "있음", ok, _rel(path, dist) if ok else f"없음: {_rel(path, dist)}")


def _pattern_row(item: str, pattern: str, files: list[Path], dist: Path) -> CheckRow:
    found = [f for f in files if fnmatch.fnmatch(f.name.lower(), pattern.lower())]
    if found:
        return CheckRow(item, "있음", True, ", ".join(_rel(f, dist) for f in found[:3]))
    return CheckRow(item, "있음", False, f"_internal 아래에 {pattern} 없음")


def run_checks(dist: Path, model_name: str) -> list[CheckRow]:
    """배포 폴더를 점검해 항목별 결과를 돌려준다(폴더가 없어도 예외 없이 실패 행으로)."""
    dist = Path(dist)
    internal = dist / "_internal"
    files = [p for p in internal.rglob("*") if p.is_file()] if internal.is_dir() else []

    rows = [
        _exists_row(EXE_NAME, dist / EXE_NAME, dist),
        _exists_row("config.ini", dist / "config.ini", dist),
        _exists_row("glossary.txt", dist / "glossary.txt", dist),
        _exists_row("_internal\\python311.dll", internal / "python311.dll", dist),
    ]
    rows += [_exists_row(f"_internal\\{name}", internal / name, dist) for name in _VC_RUNTIME_DLLS]
    rows += [_pattern_row(item, pattern, files, dist) for item, pattern in _REQUIRED_IN_INTERNAL]

    vad = sorted((internal / "faster_whisper" / "assets").glob("*.onnx"))
    rows.append(
        CheckRow("faster_whisper assets\\*.onnx (VAD)", "있음", bool(vad),
                 ", ".join(_rel(f, dist) for f in vad) or "_internal\\faster_whisper\\assets에 .onnx 없음")
    )

    dlls = [f for f in files if f.suffix.lower() == ".dll"]
    for marker in CUDA_MARKERS:
        found = [f for f in dlls if marker in f.name.lower()]
        rows.append(
            CheckRow(f"파일명에 '{marker}' 포함 DLL", "없음", not found,
                     ", ".join(_rel(f, dist) for f in found) or "-")
        )

    rows.append(_exists_row(f"models\\{model_name}\\model.bin", dist / "models" / model_name / "model.bin", dist))
    models = dist / "models"
    caches = [p for p in models.rglob(".cache") if p.is_dir()] if models.is_dir() else []
    rows.append(
        CheckRow("models 아래 .cache 폴더", "없음", not caches, ", ".join(_rel(p, dist) for p in caches) or "-")
    )
    return rows


# ------------------------------------------------------------------ 표 출력


def display_width(text: str) -> int:
    """콘솔에서 차지하는 칸 수(한글 등 전각 문자는 2칸)."""
    return sum(2 if unicodedata.east_asian_width(ch) in ("W", "F") else 1 for ch in text)


def _pad(text: str, width: int) -> str:
    return text + " " * (width - display_width(text))


def format_table(rows: list[CheckRow]) -> str:
    header = ("확인 항목", "기대", "결과", "비고")
    table = [(r.item, r.expected, "OK" if r.ok else "실패", r.detail) for r in rows]
    widths = [max(display_width(line[i]) for line in [header, *table]) for i in range(3)]

    def line(cells) -> str:
        return " | ".join(_pad(cell, widths[i]) for i, cell in enumerate(cells[:3])) + " | " + cells[3]

    sep = "-+-".join("-" * w for w in widths) + "-+-" + "-" * 10
    return "\n".join([line(header), sep, *(line(cells) for cells in table)])


def main(argv: list[str] | None = None) -> int:
    try:
        sys.stdout.reconfigure(errors="replace")  # 콘솔 코드 페이지에 없는 문자가 있어도 죽지 않게
    except (AttributeError, ValueError):
        pass
    parser = argparse.ArgumentParser(description="배포 폴더 점검")
    parser.add_argument("--dist", type=Path, default=DEFAULT_DIST, help="배포 폴더(기본: dist\\STT변환기)")
    parser.add_argument("--config", type=Path, help="모델 이름을 읽을 config.ini(기본: 배포 폴더, 없으면 프로젝트)")
    parser.add_argument("--print-model", action="store_true", help="config.ini의 모델 이름만 출력")
    args = parser.parse_args(argv)

    if args.print_model:
        ini = args.config or PROJECT_ROOT / "config.ini"
    else:
        ini = args.config or (args.dist / "config.ini" if (args.dist / "config.ini").is_file()
                              else PROJECT_ROOT / "config.ini")
    try:
        model_name = read_model_name(ini)
    except ValueError as exc:
        print(f"오류: {exc}", file=sys.stderr)
        return 1
    if args.print_model:
        print(model_name)
        return 0

    rows = run_checks(args.dist, model_name)
    print(f"배포 폴더 점검: {args.dist} (모델: {model_name})")
    print(format_table(rows))
    failed = [r for r in rows if not r.ok]
    if failed:
        print(f"\n실패 {len(failed)}건: " + ", ".join(r.item for r in failed))
        return 1
    print(f"\n모두 OK ({len(rows)}항목)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
