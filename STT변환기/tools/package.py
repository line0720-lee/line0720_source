"""배포 압축: dist\\STT변환기와 모델·테스트 샘플을 release\\ 폴더에 zip으로 묶고 SHA256SUMS.txt를 만든다.

사용법(프로젝트 폴더에서, build.bat 다음에):
    .venv\\Scripts\\python tools\\package.py

만드는 파일(release\\):
    STT변환기_앱.zip          dist\\STT변환기 전체(config.ini의 모델 1개 포함). 압축 안의 최상위 폴더는 STT변환기\\
    모델_{이름}.zip            나머지 모델. 앱 폴더(STT변환기\\) 안에서 풀면 models\\{이름}\\ 이 생긴다
    테스트샘플.zip             tests\\samples (폐쇄망 PC 확인용). 압축 안의 최상위 폴더는 테스트샘플\\
    SHA256SUMS.txt            위 zip들의 SHA-256 (sha256sum 형식: 해시, 공백 2칸, 파일명 / UTF-8)
    README.md, 체크리스트.md   프로젝트 README.md와 verify\\체크리스트.md의 사본(반입해 읽는 참고 문서).
                              zip 밖에 그대로 두며 SHA256SUMS.txt에는 넣지 않는다(해시는 승인 대상 zip만)

앱 zip에는 build.bat이 만든 것(STT변환기.exe, _internal\\, config.ini, glossary.txt, models\\{모델}\\)만 넣는다.
dist 안에서 시험 실행하며 생긴 logs\\, result\\ 같은 폴더는 넣지 않는다. 모델의 .cache(내려받기 기록)도 뺀다.
종료 코드: 0 = 성공, 1 = 실패(배포 폴더 점검 실패, exe·모델·문서 원본 없음, 압축·복사 실패)
"""
from __future__ import annotations

import argparse
import hashlib
import os
import shutil
import sys
import zipfile
from collections.abc import Sequence
from pathlib import Path

_TOOLS_DIR = Path(__file__).resolve().parent
if str(_TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(_TOOLS_DIR))  # 테스트에서 파일 경로로 불러와도 check_dist를 찾게 한다

import check_dist  # noqa: E402

APP_NAME = check_dist.APP_NAME
EXE_NAME = check_dist.EXE_NAME
VALID_MODELS = check_dist.VALID_MODELS
PROJECT_ROOT = _TOOLS_DIR.parent
DEFAULT_DIST = PROJECT_ROOT / "dist" / APP_NAME
DEFAULT_MODELS = PROJECT_ROOT / "models"
DEFAULT_SAMPLES = PROJECT_ROOT / "tests" / "samples"
DEFAULT_RELEASE = PROJECT_ROOT / "release"
# release\에 사본으로 둘 참고 문서(원본 경로). 사본 이름은 원본 파일 이름 그대로다.
DEFAULT_DOCS = (PROJECT_ROOT / "README.md", PROJECT_ROOT / "verify" / "체크리스트.md")
DOC_NAMES = tuple(p.name for p in DEFAULT_DOCS)

APP_ZIP = f"{APP_NAME}_앱.zip"
SAMPLES_ZIP = "테스트샘플.zip"
SAMPLES_TOP = "테스트샘플"
SUMS_FILE = "SHA256SUMS.txt"
APP_TOP_FILES = (EXE_NAME, "config.ini", "glossary.txt")  # 앱 폴더 바로 아래에 넣을 파일
APP_TOP_DIRS = ("_internal",)  # 통째로 넣을 폴더(models는 따로 처리)
EXCLUDED_DIR_NAMES = (".cache",)
_CHUNK = 1024 * 1024


class PackageError(Exception):
    """압축을 시작할 수 없거나 실패함. 메시지는 화면에 그대로 보여 준다."""


def model_zip_name(model: str) -> str:
    return f"모델_{model}.zip"


# ------------------------------------------------------------------ 압축할 파일 목록


def _walk(folder: Path) -> list[Path]:
    """folder 아래 파일 전체(EXCLUDED_DIR_NAMES 폴더 제외), 이름순."""
    found = []
    for dirpath, dirnames, filenames in os.walk(folder):
        dirnames[:] = sorted(d for d in dirnames if d not in EXCLUDED_DIR_NAMES)
        found += [Path(dirpath) / name for name in sorted(filenames)]
    return found


def _arc(*parts: str) -> str:
    """zip 안 경로(구분자는 '/')."""
    return "/".join(p.replace("\\", "/").strip("/") for p in parts if p)


def app_entries(dist: Path, model: str) -> tuple[list[tuple[Path, str]], list[str]]:
    """앱 zip에 넣을 (원본 파일, zip 안 경로) 목록과, 넣지 않고 건너뛴 dist 바로 아래 항목 이름 목록."""
    dist = Path(dist)
    entries: list[tuple[Path, str]] = []
    for name in APP_TOP_FILES:
        entries.append((dist / name, _arc(APP_NAME, name)))
    for name in APP_TOP_DIRS:
        for path in _walk(dist / name):
            entries.append((path, _arc(APP_NAME, path.relative_to(dist).as_posix())))
    for path in _walk(dist / "models" / model):
        entries.append((path, _arc(APP_NAME, path.relative_to(dist).as_posix())))

    allowed = set(APP_TOP_FILES) | set(APP_TOP_DIRS) | {"models"}
    skipped = sorted(p.name for p in dist.iterdir() if p.name not in allowed)
    models = dist / "models"
    if models.is_dir():
        skipped += sorted(f"models\\{p.name}" for p in models.iterdir() if p.name != model)
    return entries, skipped


def model_entries(models_dir: Path, model: str) -> list[tuple[Path, str]]:
    """모델 zip: 앱 폴더 안에서 풀면 models\\{model}\\... 이 되도록 'models/{model}/...'로 넣는다."""
    folder = Path(models_dir) / model
    return [(path, _arc("models", model, path.relative_to(folder).as_posix())) for path in _walk(folder)]


def samples_entries(samples: Path) -> list[tuple[Path, str]]:
    samples = Path(samples)
    return [(path, _arc(SAMPLES_TOP, path.relative_to(samples).as_posix())) for path in _walk(samples)]


# ------------------------------------------------------------------ 압축·해시


def write_zip(target: Path, entries: list[tuple[Path, str]]) -> Path:
    """임시 이름(.part)으로 쓴 뒤 바꿔, 중간에 실패해도 불완전한 zip이 남지 않게 한다."""
    target = Path(target)
    part = target.with_name(target.name + ".part")
    try:
        with zipfile.ZipFile(part, "w", compression=zipfile.ZIP_DEFLATED, allowZip64=True) as zf:
            for path, arcname in entries:
                zf.write(path, arcname)
        os.replace(part, target)
    except BaseException:
        part.unlink(missing_ok=True)
        raise
    return target


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as fp:
        for chunk in iter(lambda: fp.read(_CHUNK), b""):
            digest.update(chunk)
    return digest.hexdigest()


def write_sums(release: Path, zips: list[Path]) -> Path:
    """sha256sum 형식(해시 + 공백 2칸 + 파일명, 줄바꿈 LF), UTF-8(BOM 없음)."""
    lines = [f"{sha256_of(z)}  {z.name}\n" for z in sorted(zips, key=lambda p: p.name)]
    path = Path(release) / SUMS_FILE
    path.write_bytes("".join(lines).encode("utf-8"))
    return path


def _clean_release(release: Path, doc_names: Sequence[str] = DOC_NAMES) -> None:
    """지난번에 만든 결과물(zip·SUMS·문서 사본)을 지운다(모델을 바꿨을 때 예전 zip이 남지 않게).
    다른 파일은 건드리지 않는다."""
    for path in list(release.glob("*.zip")) + list(release.glob("*.zip.part")) + [release / SUMS_FILE]:
        name = path.name.removesuffix(".part")
        if name in (APP_ZIP, SAMPLES_ZIP, SUMS_FILE) or (name.startswith("모델_") and name.endswith(".zip")):
            path.unlink(missing_ok=True)
    for name in dict.fromkeys([*DOC_NAMES, *doc_names]):
        (release / name).unlink(missing_ok=True)


def copy_docs(release: Path, docs: Sequence[Path]) -> list[Path]:
    """참고 문서를 release\\에 같은 이름으로 복사한다. zip에도 SHA256SUMS.txt에도 넣지 않는다."""
    copied = []
    for source in docs:
        copied.append(Path(shutil.copy2(source, Path(release) / Path(source).name)))
    return copied


# ------------------------------------------------------------------ 전체 흐름


def _check_inputs(dist: Path, models_dir: Path) -> tuple[str, list[str]]:
    """exe·모델이 모두 있는지 확인하고 (앱에 넣을 모델, 따로 묶을 모델 목록)을 돌려준다."""
    if not (dist / EXE_NAME).is_file():
        raise PackageError(f"실행파일이 없습니다: {dist / EXE_NAME}  (build.bat으로 먼저 빌드하세요)")
    try:
        model = check_dist.read_model_name(dist / "config.ini")
    except ValueError as exc:
        raise PackageError(str(exc)) from exc
    if not (dist / "models" / model / "model.bin").is_file():
        raise PackageError(f"배포 폴더에 모델이 없습니다: {dist / 'models' / model}  (build.bat을 다시 실행하세요)")
    others = [m for m in VALID_MODELS if m != model]
    missing = [m for m in others if not (models_dir / m / "model.bin").is_file()]
    if missing:
        raise PackageError(
            "모델 폴더가 없습니다: " + ", ".join(str(models_dir / m) for m in missing)
            + "  (download_model.py로 먼저 받으세요)"
        )
    return model, others


def _size(path: Path) -> str:
    size = path.stat().st_size
    return f"{size / 1024 ** 3:.2f}GB" if size >= 1024 ** 3 else f"{size / 1024 ** 2:.1f}MB"


def package(
    dist: Path,
    models_dir: Path,
    samples: Path,
    release: Path,
    run_check: bool = True,
    docs: Sequence[Path] = DEFAULT_DOCS,
) -> list[Path]:
    """release 폴더에 zip과 SHA256SUMS.txt를 만들고 문서 사본(docs)을 둔 뒤 만든 zip 목록을 돌려준다.

    실패하면 PackageError. docs는 zip 밖에 복사만 하고 SHA256SUMS.txt에는 넣지 않는다.
    """
    dist, models_dir, samples, release = Path(dist), Path(models_dir), Path(samples), Path(release)
    docs = [Path(d) for d in docs]
    model, others = _check_inputs(dist, models_dir)
    missing_docs = [d for d in docs if not d.is_file()]
    if missing_docs:
        raise PackageError("함께 둘 문서 파일이 없습니다: " + ", ".join(str(d) for d in missing_docs))

    if run_check:
        rows = check_dist.run_checks(dist, model)
        failed = [r for r in rows if not r.ok]
        if failed:
            print(check_dist.format_table(rows))
            raise PackageError("배포 폴더 점검에 실패했습니다: " + ", ".join(r.item for r in failed))
        print(f"배포 폴더 점검: 모두 OK ({len(rows)}항목)")

    release.mkdir(parents=True, exist_ok=True)
    _clean_release(release, [d.name for d in docs])

    entries, skipped = app_entries(dist, model)
    if skipped:
        print("앱 zip에서 뺀 항목(빌드 결과물이 아님): " + ", ".join(skipped))
    jobs = [(release / APP_ZIP, entries)]
    jobs += [(release / model_zip_name(m), model_entries(models_dir, m)) for m in others]
    if samples.is_dir():
        jobs.append((release / SAMPLES_ZIP, samples_entries(samples)))
    else:
        print(f"경고: 테스트 샘플 폴더가 없어 {SAMPLES_ZIP}은 만들지 않습니다: {samples}")

    made = []
    for target, items in jobs:
        print(f"압축 중: {target.name} ({len(items)}개 파일)…", flush=True)
        try:
            made.append(write_zip(target, items))
        except ValueError as exc:  # zipfile은 1980년 이전 날짜의 파일을 넣지 못한다(strict_timestamps)
            raise PackageError(
                f"압축에 실패했습니다: {target.name} (파일 날짜가 1980년 이전이거나 zip에 넣을 수 없는 파일입니다: {exc})"
            ) from exc
        except (OSError, zipfile.LargeZipFile) as exc:  # LargeZipFile은 allowZip64=True라 보통 나지 않는다
            raise PackageError(f"압축에 실패했습니다: {target.name} ({exc})") from exc
        print(f"  완료: {target.name} {_size(target)}", flush=True)

    sums = write_sums(release, made)
    print(f"\n{sums.name}:")
    print(sums.read_text(encoding="utf-8"), end="")

    try:
        copied = copy_docs(release, docs)
    except OSError as exc:
        raise PackageError(f"문서 사본을 만들지 못했습니다: {exc}") from exc
    if copied:
        print("\n참고 문서 사본(zip 밖, SHA256SUMS.txt에는 넣지 않음): " + ", ".join(p.name for p in copied))
    return made


def main(argv: list[str] | None = None) -> int:
    try:
        sys.stdout.reconfigure(errors="replace")  # 콘솔 코드 페이지에 없는 문자가 있어도 죽지 않게
    except (AttributeError, ValueError):
        pass
    parser = argparse.ArgumentParser(description="배포 압축(release\\ 폴더)")
    parser.add_argument("--dist", type=Path, default=DEFAULT_DIST, help="배포 폴더(기본: dist\\STT변환기)")
    parser.add_argument("--models-dir", type=Path, default=DEFAULT_MODELS, help="모델 폴더(기본: models)")
    parser.add_argument("--samples", type=Path, default=DEFAULT_SAMPLES, help="테스트 샘플 폴더(기본: tests\\samples)")
    parser.add_argument("--release", type=Path, default=DEFAULT_RELEASE, help="결과 폴더(기본: release)")
    args = parser.parse_args(argv)
    try:
        made = package(args.dist, args.models_dir, args.samples, args.release)
    except PackageError as exc:
        print(f"오류: {exc}")
        return 1
    print(f"\n압축 완료: {args.release} ({len(made)}개 zip + {SUMS_FILE} + 문서 사본 {', '.join(DOC_NAMES)})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
