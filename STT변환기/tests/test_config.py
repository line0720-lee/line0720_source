"""config 모듈 테스트: 설정 로딩, 용어집, 마지막 폴더, 로그 폴더."""
import logging
import os
from pathlib import Path

import pytest

import config
from config import ConfigError, load_config, read_glossary


def _write_ini(folder: Path, name: str = "medium", models_dir: str = "models", language: str = "ko") -> Path:
    ini = folder / "config.ini"
    ini.write_text(
        f"[model]\nname = {name}\nmodels_dir = {models_dir}\n\n[stt]\nlanguage = {language}\n",
        encoding="utf-8-sig",
    )
    return ini


def test_load_config_defaults(tmp_path):
    cfg = load_config(_write_ini(tmp_path))
    assert cfg.model_name == "medium"
    assert cfg.model_path == tmp_path / "models" / "medium"
    assert cfg.language == "ko"
    assert cfg.cpu_threads >= 1
    assert cfg.glossary_path == tmp_path / "glossary.txt"


def test_load_config_rejects_unknown_model(tmp_path):
    with pytest.raises(ConfigError) as exc:
        load_config(_write_ini(tmp_path, name="large"))
    assert "large" in str(exc.value)


def test_load_config_missing_file(tmp_path):
    with pytest.raises(ConfigError):
        load_config(tmp_path / "없는설정.ini")


def test_relative_models_dir_ignores_cwd(tmp_path, monkeypatch):
    cfg_dir = tmp_path / "앱"
    cfg_dir.mkdir()
    other = tmp_path / "다른곳"
    other.mkdir()
    ini = _write_ini(cfg_dir, name="small", models_dir="models")
    monkeypatch.chdir(other)
    cfg = load_config(ini)
    assert cfg.model_path == cfg_dir / "models" / "small"


def test_absolute_models_dir_is_kept(tmp_path):
    abs_models = tmp_path / "공용모델"
    cfg = load_config(_write_ini(tmp_path, models_dir=str(abs_models)))
    assert cfg.model_path == abs_models / "medium"


def test_read_glossary_utf8_sig(tmp_path):
    path = tmp_path / "glossary.txt"
    path.write_text("# 주석\n전산팀\n\nEMR\n", encoding="utf-8-sig")
    assert read_glossary(path) == "전산팀, EMR"
    assert read_glossary(tmp_path / "없음.txt") is None


def test_read_glossary_empty_returns_none(tmp_path):
    path = tmp_path / "glossary.txt"
    path.write_text("# 주석만 있음\n\n   \n", encoding="utf-8-sig")
    assert read_glossary(path) is None


def test_last_folder_roundtrip(tmp_path, monkeypatch):
    monkeypatch.setenv("APPDATA", str(tmp_path / "appdata"))
    assert config.load_last_folder() is None  # 아직 저장 전

    target = tmp_path / "녹음 파일 (1)"
    target.mkdir()
    config.save_last_folder(target)
    assert config.load_last_folder() == target

    target.rmdir()  # 저장된 폴더가 사라지면 None
    assert config.load_last_folder() is None


@pytest.fixture
def clean_root_logger():
    """setup_logging이 붙인 핸들러를 테스트 뒤에 정리한다."""
    root = logging.getLogger()
    before = list(root.handlers)
    level = root.level
    yield root
    for handler in list(root.handlers):
        if handler not in before:
            root.removeHandler(handler)
            handler.close()
    root.setLevel(level)


def test_setup_logging_uses_base_logs_folder(tmp_path, clean_root_logger):
    folder = config.setup_logging(tmp_path)
    assert folder == tmp_path / "logs"
    logging.getLogger("테스트").info("로그 시험")
    for handler in clean_root_logger.handlers:
        handler.flush()
    logs = list(folder.glob("*.log"))
    assert len(logs) == 1
    assert "로그 시험" in logs[0].read_text(encoding="utf-8")


def test_setup_logging_is_idempotent(tmp_path, clean_root_logger):
    config.setup_logging(tmp_path)
    count = len(clean_root_logger.handlers)
    config.setup_logging(tmp_path)
    assert len(clean_root_logger.handlers) == count


def test_setup_logging_falls_back(tmp_path, monkeypatch, clean_root_logger):
    blocker = tmp_path / "파일"  # 폴더가 아니라 파일 -> 아래에 logs 폴더를 만들 수 없다
    blocker.write_text("x", encoding="utf-8")
    docs = tmp_path / "문서"
    monkeypatch.setattr(config, "_documents_dir", lambda: docs)
    folder = config.setup_logging(blocker / "하위")
    assert folder == docs / "STT변환기" / "logs"
    assert folder.is_dir()


def test_physical_cores_positive():
    assert config.physical_cores() >= 1


def test_app_dir_is_project_root():
    # 개발 중(비 frozen)에는 src의 상위 폴더 = 프로젝트 루트
    assert (config.app_dir() / "config.ini").exists()
    assert os.path.isabs(config.app_dir())
