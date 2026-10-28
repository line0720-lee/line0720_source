"""공용 pytest 픽스처."""
from pathlib import Path

import pytest

PROJECT_ROOT = Path(__file__).resolve().parent.parent


@pytest.fixture(scope="session")
def samples_dir() -> Path:
    path = PROJECT_ROOT / "tests" / "samples"
    if not (path / "기본_16k.wav").exists():
        pytest.skip("샘플이 없습니다. powershell -File tests\\make_samples.ps1 로 먼저 만드세요.")
    return path


def make_small_config():
    from config import AppConfig, physical_cores

    return AppConfig(
        model_name="small",
        model_path=PROJECT_ROOT / "models" / "small",
        language="ko",
        cpu_threads=physical_cores(),
        glossary_path=PROJECT_ROOT / "glossary.txt",
    )


@pytest.fixture(scope="session")
def small_cfg():
    if not (PROJECT_ROOT / "models" / "small" / "model.bin").exists():
        pytest.skip("models\\small 모델이 없습니다.")
    return make_small_config()


@pytest.fixture(scope="session")
def small_engine(small_cfg):
    """small 모델을 세션 동안 한 번만 불러온다."""
    from stt_engine import SttEngine

    return SttEngine(small_cfg)
