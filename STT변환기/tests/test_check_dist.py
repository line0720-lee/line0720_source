"""tools\\check_dist.py 테스트: 가짜 배포 폴더(tmp_path)를 만들어 확인 결과 표를 검사한다."""
import importlib.util
import sys
from pathlib import Path

import pytest

# tools 폴더는 pythonpath에 없으므로 파일 경로로 불러온다(dataclass가 모듈을 찾을 수 있게 sys.modules에 등록)
_TOOL = Path(__file__).resolve().parent.parent / "tools" / "check_dist.py"
_spec = importlib.util.spec_from_file_location("check_dist", _TOOL)
check_dist = importlib.util.module_from_spec(_spec)
sys.modules["check_dist"] = check_dist
_spec.loader.exec_module(check_dist)

# 정상 빌드에 있어야 하는 파일(배포 폴더 기준 상대경로)
_GOOD_FILES = [
    "STT변환기.exe",
    "config.ini",
    "glossary.txt",
    "_internal/python311.dll",
    "_internal/VCRUNTIME140.dll",
    "_internal/VCRUNTIME140_1.dll",
    "_internal/MSVCP140.dll",
    "_internal/ctranslate2/ctranslate2.dll",
    "_internal/ctranslate2/libiomp5md.dll",
    "_internal/onnxruntime/capi/onnxruntime.dll",
    "_internal/av.libs/avcodec-62-abc.dll",
    "_internal/av.libs/avformat-62-abc.dll",
    "_internal/av.libs/avutil-60-abc.dll",
    "_internal/av.libs/swresample-6-abc.dll",
    "_internal/faster_whisper/assets/silero_vad_v6.onnx",
    "models/medium/model.bin",
]


def _make_dist(root: Path, files=_GOOD_FILES) -> Path:
    dist = root / "STT변환기"
    for rel in files:
        path = dist / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"x")
    return dist


def _failed(rows) -> list[str]:
    return [row.item for row in rows if not row.ok]


def test_good_dist_all_ok(tmp_path):
    rows = check_dist.run_checks(_make_dist(tmp_path), "medium")
    assert _failed(rows) == []
    items = " ".join(row.item for row in rows)
    for name in ("STT변환기.exe", "python311.dll", "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll",
                 "ctranslate2.dll", "onnxruntime.dll", "avcodec", "assets", "cublas", "cudnn", "cudart",
                 "cufft", "nvrtc", "model.bin", ".cache"):
        assert name in items, f"{name} 확인 항목이 없습니다"


def test_missing_python_dll_fails(tmp_path):
    files = [f for f in _GOOD_FILES if not f.endswith("python311.dll")]
    rows = check_dist.run_checks(_make_dist(tmp_path, files), "medium")
    assert any("python311.dll" in item for item in _failed(rows))


def test_python_dll_must_be_directly_in_internal(tmp_path):
    files = [f for f in _GOOD_FILES if not f.endswith("python311.dll")] + ["_internal/sub/python311.dll"]
    rows = check_dist.run_checks(_make_dist(tmp_path, files), "medium")
    assert any("python311.dll" in item for item in _failed(rows))


@pytest.mark.parametrize(
    "removed, expected",
    [
        ("STT변환기.exe", "STT변환기.exe"),
        ("VCRUNTIME140_1.dll", "vcruntime140_1.dll"),
        ("ctranslate2.dll", "ctranslate2.dll"),
        ("onnxruntime.dll", "onnxruntime.dll"),
        ("avcodec-62-abc.dll", "avcodec"),
        ("silero_vad_v6.onnx", "assets"),
        ("config.ini", "config.ini"),
    ],
)
def test_missing_required_file_fails(tmp_path, removed, expected):
    files = [f for f in _GOOD_FILES if not f.endswith(removed)]
    rows = check_dist.run_checks(_make_dist(tmp_path, files), "medium")
    assert any(expected in item for item in _failed(rows))


def test_dll_name_match_ignores_case(tmp_path):
    files = [f.replace("VCRUNTIME140.dll", "vcruntime140.DLL") for f in _GOOD_FILES]
    assert _failed(check_dist.run_checks(_make_dist(tmp_path, files), "medium")) == []


@pytest.mark.parametrize("cuda_dll", ["cublas64_12.dll", "cudnn64_9.dll", "cudart64_12.dll", "cufft64_11.dll",
                                      "nvrtc64_120_0.dll", "CUBLASLt64_12.dll"])
def test_cuda_dll_present_fails(tmp_path, cuda_dll):
    files = _GOOD_FILES + [f"_internal/ctranslate2/{cuda_dll}"]
    rows = check_dist.run_checks(_make_dist(tmp_path, files), "medium")
    failed = [row for row in rows if not row.ok]
    assert len(failed) == 1
    assert cuda_dll in failed[0].detail


def test_model_bin_for_configured_model(tmp_path):
    dist = _make_dist(tmp_path)
    assert _failed(check_dist.run_checks(dist, "medium")) == []
    assert any("small" in item for item in _failed(check_dist.run_checks(dist, "small")))


def test_cache_folder_under_models_fails(tmp_path):
    files = _GOOD_FILES + ["models/medium/.cache/huggingface/download/model.bin.metadata"]
    rows = check_dist.run_checks(_make_dist(tmp_path, files), "medium")
    assert any(".cache" in item for item in _failed(rows))


def test_missing_dist_folder_fails_without_error(tmp_path):
    rows = check_dist.run_checks(tmp_path / "없는 폴더", "medium")
    assert rows and _failed(rows)


def test_read_model_name(tmp_path):
    ini = tmp_path / "config.ini"
    ini.write_text("[model]\n; 주석\nname = small \nmodels_dir = models\n", encoding="utf-8-sig")
    assert check_dist.read_model_name(ini) == "small"


def test_read_model_name_rejects_unknown(tmp_path):
    ini = tmp_path / "config.ini"
    ini.write_text("[model]\nname = large\n", encoding="utf-8")
    with pytest.raises(ValueError):
        check_dist.read_model_name(ini)


def test_main_prints_table_and_exit_codes(tmp_path, capsys):
    dist = _make_dist(tmp_path)
    (dist / "config.ini").write_text("[model]\nname = medium\n", encoding="utf-8-sig")
    assert check_dist.main(["--dist", str(dist)]) == 0
    out = capsys.readouterr().out
    assert "OK" in out and "python311.dll" in out

    (dist / "_internal" / "python311.dll").unlink()
    assert check_dist.main(["--dist", str(dist)]) == 1
    assert "실패" in capsys.readouterr().out


def test_main_print_model(tmp_path, capsys):
    ini = tmp_path / "config.ini"
    ini.write_text("[model]\nname = large-v3-turbo\n", encoding="utf-8-sig")
    assert check_dist.main(["--print-model", "--config", str(ini)]) == 0
    assert capsys.readouterr().out.strip() == "large-v3-turbo"


def test_format_table_aligns_korean(tmp_path):
    rows = [check_dist.CheckRow("한글 항목", "있음", True, "a"), check_dist.CheckRow("abc", "없음", False, "b")]
    lines = check_dist.format_table(rows).splitlines()
    widths = {check_dist.display_width(line.split("|")[0]) for line in lines if "|" in line}
    assert len(widths) == 1  # 첫 칸 너비가 한글·영문 모두 같게 맞춰진다
