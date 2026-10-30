"""tools\\package.py 테스트: 가짜 배포 폴더·모델 폴더(tmp_path)로 zip 구성·제외 항목·SHA256SUMS 형식을 검사한다."""
import hashlib
import importlib.util
import os
import re
import sys
import zipfile
from pathlib import Path

import pytest

# tools 폴더는 pythonpath에 없으므로 파일 경로로 불러온다
_TOOL = Path(__file__).resolve().parent.parent / "tools" / "package.py"
_spec = importlib.util.spec_from_file_location("package_tool", _TOOL)
package_tool = importlib.util.module_from_spec(_spec)
sys.modules["package_tool"] = package_tool
_spec.loader.exec_module(package_tool)

# check_dist 점검을 통과하는 최소 배포 폴더(배포 폴더 기준 상대경로)
_DIST_FILES = [
    "STT변환기.exe",
    "config.ini",
    "glossary.txt",
    "_internal/python311.dll",
    "_internal/VCRUNTIME140.dll",
    "_internal/VCRUNTIME140_1.dll",
    "_internal/MSVCP140.dll",
    "_internal/MSVCP140_1.dll",
    "_internal/ctranslate2/ctranslate2.dll",
    "_internal/ctranslate2/libiomp5md.dll",
    "_internal/onnxruntime/capi/onnxruntime.dll",
    "_internal/av.libs/avcodec-62-abc.dll",
    "_internal/av.libs/avformat-62-abc.dll",
    "_internal/av.libs/avutil-60-abc.dll",
    "_internal/av.libs/swresample-6-abc.dll",
    "_internal/faster_whisper/assets/silero_vad_v6.onnx",
    "models/medium/model.bin",
    "models/medium/config.json",
]
_MODEL_FILES = ["model.bin", "config.json", "tokenizer.json", ".cache/huggingface/download/model.bin.lock"]


def _touch(path: Path, content: bytes = b"x") -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content)


@pytest.fixture
def tree(tmp_path):
    """dist(시험 실행 흔적 logs·result 포함), models(small·medium·large-v3-turbo), samples, release 경로."""
    dist = tmp_path / "dist" / "STT변환기"
    for rel in _DIST_FILES:
        _touch(dist / rel)
    (dist / "config.ini").write_text("[model]\nname = medium\nmodels_dir = models\n", encoding="utf-8")
    _touch(dist / "logs" / "2026-10-29.log", "개인 경로가 들어 있는 로그".encode("utf-8"))
    _touch(dist / "result" / "STT_결과_20261029_080000.xlsx")

    models = tmp_path / "models"
    for name in ("small", "medium", "large-v3-turbo"):
        for rel in _MODEL_FILES:
            _touch(models / name / rel, name.encode())

    samples = tmp_path / "samples"
    _touch(samples / "기본_16k.wav", b"RIFF")
    _touch(samples / "빈파일.wav", b"")
    _touch(samples / "하위 폴더 (2팀)" / "회의 #1.wav", b"RIFF")
    return dist, models, samples, tmp_path / "release"


def _names(zip_path: Path) -> list[str]:
    with zipfile.ZipFile(zip_path) as zf:
        return zf.namelist()


def test_app_entries_only_build_outputs(tree):
    dist, *_ = tree
    entries, skipped = package_tool.app_entries(dist, "medium")
    arcs = [arc for _, arc in entries]
    assert "STT변환기/STT변환기.exe" in arcs
    assert "STT변환기/config.ini" in arcs
    assert "STT변환기/glossary.txt" in arcs
    assert "STT변환기/_internal/ctranslate2/ctranslate2.dll" in arcs
    assert "STT변환기/models/medium/model.bin" in arcs
    assert all(arc.startswith("STT변환기/") for arc in arcs)
    assert not [a for a in arcs if "/logs/" in a or "/result/" in a or ".cache" in a]
    assert skipped == ["logs", "result"]


def test_app_entries_skip_cache(tree):
    dist, *_ = tree
    _touch(dist / "models" / "medium" / ".cache" / "huggingface" / "x.lock")  # check_dist는 이것을 실패로 본다
    entries, _ = package_tool.app_entries(dist, "medium")
    assert not [arc for _, arc in entries if ".cache" in arc]


def test_app_entries_skip_other_model_in_dist(tree):
    dist, *_ = tree
    _touch(dist / "models" / "small" / "model.bin")
    entries, skipped = package_tool.app_entries(dist, "medium")
    assert not [arc for _, arc in entries if "/models/small/" in arc]
    assert "models\\small" in skipped


def test_model_entries_layout(tree):
    _, models, *_ = tree
    arcs = sorted(arc for _, arc in package_tool.model_entries(models, "small"))
    assert arcs == ["models/small/config.json", "models/small/model.bin", "models/small/tokenizer.json"]


def test_package_creates_zips_and_sums(tree, capsys):
    dist, models, samples, release = tree
    made = package_tool.package(dist, models, samples, release)
    names = sorted(p.name for p in made)
    assert names == sorted(["STT변환기_앱.zip", "모델_small.zip", "모델_large-v3-turbo.zip", "테스트샘플.zip"])

    app = _names(release / "STT변환기_앱.zip")
    assert "STT변환기/models/medium/model.bin" in app
    assert not [n for n in app if "logs" in n or "result" in n or ".cache" in n]
    assert "STT변환기/models/small/model.bin" not in app

    small = _names(release / "모델_small.zip")
    assert "models/small/model.bin" in small and not [n for n in small if ".cache" in n]

    samples_zip = _names(release / "테스트샘플.zip")
    assert sorted(samples_zip) == sorted(
        ["테스트샘플/기본_16k.wav", "테스트샘플/빈파일.wav", "테스트샘플/하위 폴더 (2팀)/회의 #1.wav"]
    )
    with zipfile.ZipFile(release / "테스트샘플.zip") as zf:
        assert zf.getinfo("테스트샘플/빈파일.wav").file_size == 0
        assert zf.getinfo("테스트샘플/기본_16k.wav").compress_type == zipfile.ZIP_DEFLATED
        assert zf.getinfo("테스트샘플/기본_16k.wav").flag_bits & 0x800  # 한글 이름은 UTF-8 표시로 저장

    assert "앱 zip에서 뺀 항목" in capsys.readouterr().out
    assert not list(release.glob("*.part"))


def test_sha256sums_format(tree):
    dist, models, samples, release = tree
    made = package_tool.package(dist, models, samples, release)
    raw = (release / "SHA256SUMS.txt").read_bytes()
    assert not raw.startswith(b"\xef\xbb\xbf")  # BOM 없음
    lines = raw.decode("utf-8").split("\n")
    assert lines[-1] == ""  # 마지막 줄도 줄바꿈으로 끝남
    lines = lines[:-1]
    assert len(lines) == len(made)
    for line in lines:
        m = re.fullmatch(r"([0-9a-f]{64})  (.+)", line)
        assert m, line
        digest, name = m.groups()
        assert digest == hashlib.sha256((release / name).read_bytes()).hexdigest()
    assert [line.split("  ", 1)[1] for line in lines] == sorted(p.name for p in made)


def test_sha256_of_matches_hashlib(tmp_path):
    path = tmp_path / "big.bin"
    data = b"abc" * 1_000_000  # 청크(1MB)보다 크게
    path.write_bytes(data)
    assert package_tool.sha256_of(path) == hashlib.sha256(data).hexdigest()


def test_package_removes_stale_outputs_only(tree):
    dist, models, samples, release = tree
    _touch(release / "모델_medium.zip")  # 예전에 다른 모델 구성으로 만든 zip
    _touch(release / "메모.txt")
    package_tool.package(dist, models, samples, release)
    assert not (release / "모델_medium.zip").exists()
    assert (release / "메모.txt").exists()
    assert "모델_medium.zip" not in (release / "SHA256SUMS.txt").read_text(encoding="utf-8")


def test_missing_samples_is_warning(tree, capsys):
    dist, models, samples, release = tree
    made = package_tool.package(dist, models, samples.parent / "없음", release)
    assert "테스트샘플.zip" not in [p.name for p in made]
    assert "경고" in capsys.readouterr().out


def test_refuses_without_exe(tree, capsys):
    dist, models, samples, release = tree
    (dist / "STT변환기.exe").unlink()
    code = package_tool.main(["--dist", str(dist), "--models-dir", str(models),
                              "--samples", str(samples), "--release", str(release)])
    assert code == 1
    assert "실행파일이 없습니다" in capsys.readouterr().out
    assert not release.exists()


def test_refuses_without_other_model(tree, capsys):
    dist, models, samples, release = tree
    (models / "large-v3-turbo" / "model.bin").unlink()
    code = package_tool.main(["--dist", str(dist), "--models-dir", str(models),
                              "--samples", str(samples), "--release", str(release)])
    assert code == 1
    assert "large-v3-turbo" in capsys.readouterr().out


def test_refuses_without_model_in_dist(tree, capsys):
    dist, models, samples, release = tree
    (dist / "models" / "medium" / "model.bin").unlink()
    assert package_tool.main(["--dist", str(dist), "--models-dir", str(models),
                              "--samples", str(samples), "--release", str(release)]) == 1
    assert "배포 폴더에 모델이 없습니다" in capsys.readouterr().out


def test_refuses_when_check_dist_fails(tree, capsys):
    dist, models, samples, release = tree
    _touch(dist / "_internal" / "cublas64_12.dll")  # GPU 전용 DLL이 섞임
    assert package_tool.main(["--dist", str(dist), "--models-dir", str(models),
                              "--samples", str(samples), "--release", str(release)]) == 1
    out = capsys.readouterr().out
    assert "배포 폴더 점검에 실패" in out
    assert not (release / "STT변환기_앱.zip").exists()


def test_refuses_file_dated_before_1980(tree, capsys):
    dist, models, samples, release = tree
    old = samples / "기본_16k.wav"
    os.utime(old, (0, 0))  # 1970-01-01: zip에는 1980년 이전 날짜를 넣을 수 없다(ValueError)
    assert package_tool.main(["--dist", str(dist), "--models-dir", str(models),
                              "--samples", str(samples), "--release", str(release)]) == 1
    out = capsys.readouterr().out
    assert "오류: 압축에 실패했습니다: 테스트샘플.zip" in out
    assert "1980년 이전" in out
    assert not list(release.glob("*.part"))  # 임시 파일은 지운다
    assert not (release / "테스트샘플.zip").exists()
    assert not (release / "SHA256SUMS.txt").exists()


@pytest.fixture
def docs(tmp_path):
    """반입 때 함께 가져갈 문서(README.md, 체크리스트.md)의 시험용 원본."""
    readme = tmp_path / "src_docs" / "README.md"
    checklist = tmp_path / "src_docs" / "verify" / "체크리스트.md"
    _touch(readme, "# 설명서\n해시는 SHA256SUMS.txt\n".encode("utf-8"))
    _touch(checklist, "# 체크리스트\n- [ ] 1-1\n".encode("utf-8"))
    return readme, checklist


def test_package_copies_docs_outside_zips_and_not_hashed(tree, docs, capsys):
    dist, models, samples, release = tree
    made = package_tool.package(dist, models, samples, release, docs=docs)
    readme, checklist = docs
    assert (release / "README.md").read_bytes() == readme.read_bytes()
    assert (release / "체크리스트.md").read_bytes() == checklist.read_bytes()
    sums_names = [line.split("  ", 1)[1] for line in (release / "SHA256SUMS.txt").read_text(encoding="utf-8").splitlines()]
    assert sums_names == sorted(p.name for p in made)
    assert all(name.endswith(".zip") for name in sums_names)
    for zip_path in made:
        assert not [n for n in _names(zip_path) if n.endswith(".md")]
    out = capsys.readouterr().out
    assert "README.md" in out and "체크리스트.md" in out


def test_package_replaces_stale_docs(tree, docs):
    dist, models, samples, release = tree
    _touch(release / "README.md", "예전 설명서".encode("utf-8"))
    _touch(release / "체크리스트.md", "예전 체크리스트".encode("utf-8"))
    package_tool.package(dist, models, samples, release, docs=docs)
    assert (release / "README.md").read_bytes() == docs[0].read_bytes()
    assert (release / "체크리스트.md").read_bytes() == docs[1].read_bytes()


def test_clean_release_removes_stale_docs(tmp_path):
    release = tmp_path / "release"
    for name in ("README.md", "체크리스트.md", "메모.md"):
        _touch(release / name)
    package_tool._clean_release(release)
    assert sorted(p.name for p in release.iterdir()) == ["메모.md"]


def test_refuses_without_doc_source(tree, docs, capsys):
    dist, models, samples, release = tree
    docs[1].unlink()
    with pytest.raises(package_tool.PackageError) as exc:
        package_tool.package(dist, models, samples, release, docs=docs)
    assert "체크리스트.md" in str(exc.value)
    assert not (release / "STT변환기_앱.zip").exists()


def test_default_docs_are_project_readme_and_checklist():
    root = Path(package_tool.PROJECT_ROOT)
    assert tuple(package_tool.DEFAULT_DOCS) == (root / "README.md", root / "verify" / "체크리스트.md")


def test_main_success(tree, capsys):
    dist, models, samples, release = tree
    assert package_tool.main(["--dist", str(dist), "--models-dir", str(models),
                              "--samples", str(samples), "--release", str(release)]) == 0
    assert "압축 완료" in capsys.readouterr().out
    assert (release / "SHA256SUMS.txt").is_file()
