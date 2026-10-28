"""설정(config.ini), 용어집, 로그 폴더, 마지막 작업 폴더를 다루는 모듈."""
from __future__ import annotations

import configparser
import ctypes
import logging
import os
import sys
from dataclasses import dataclass
from datetime import date
from pathlib import Path

import psutil

VALID_MODELS = ("small", "medium", "large-v3-turbo")
_APP_FOLDER_NAME = "STT변환기"
_LOG_FORMAT = "%(asctime)s [%(levelname)s] %(name)s: %(message)s"
_HANDLER_TAG = "_stt_log_handler"  # 우리가 붙인 로그 핸들러 표시


class ConfigError(Exception):
    """설정 오류. 메시지를 화면에 그대로 보여 줄 수 있도록 한국어로 쓴다."""


@dataclass(frozen=True)
class AppConfig:
    model_name: str
    model_path: Path
    language: str
    cpu_threads: int
    glossary_path: Path


def app_dir() -> Path:
    """프로그램 폴더. exe로 묶였으면 exe가 있는 폴더, 개발 중에는 프로젝트 루트."""
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parent.parent


def physical_cores() -> int:
    """물리 코어 수(알 수 없으면 논리 코어 수, 그것도 모르면 4)."""
    return psutil.cpu_count(logical=False) or os.cpu_count() or 4


def _new_parser() -> configparser.ConfigParser:
    # 경로에 '%'가 들어 있어도 치환되지 않도록 interpolation을 끈다
    return configparser.ConfigParser(interpolation=None)


def load_config(path: Path | None = None) -> AppConfig:
    """config.ini를 읽어 AppConfig를 만든다. 상대경로는 config.ini가 있는 폴더 기준."""
    ini_path = Path(path) if path is not None else app_dir() / "config.ini"
    if not ini_path.is_file():
        raise ConfigError(f"설정 파일(config.ini)을 찾을 수 없습니다: {ini_path}")

    parser = _new_parser()
    try:
        parser.read_string(ini_path.read_text(encoding="utf-8-sig"))
    except (configparser.Error, UnicodeDecodeError) as exc:
        raise ConfigError(f"설정 파일(config.ini)을 읽을 수 없습니다: {exc}") from exc

    name = parser.get("model", "name", fallback="medium").strip()
    if name not in VALID_MODELS:
        raise ConfigError(
            f"config.ini의 모델 이름 '{name}'을(를) 사용할 수 없습니다. "
            f"{' / '.join(VALID_MODELS)} 중 하나로 지정하세요."
        )

    base = ini_path.resolve().parent
    models_dir = Path(parser.get("model", "models_dir", fallback="models").strip() or "models")
    if not models_dir.is_absolute():
        models_dir = base / models_dir  # CWD가 아니라 config.ini 폴더 기준

    return AppConfig(
        model_name=name,
        model_path=models_dir / name,
        language=parser.get("stt", "language", fallback="ko").strip() or "ko",
        cpu_threads=physical_cores(),
        glossary_path=base / "glossary.txt",
    )


def read_glossary(path: Path) -> str | None:
    """용어집을 ', '로 이어 붙인 문자열로 돌려준다. 파일이 없거나 비어 있으면 None."""
    try:
        text = Path(path).read_text(encoding="utf-8-sig")
    except FileNotFoundError:
        return None
    terms = [line.strip() for line in text.splitlines()]
    terms = [t for t in terms if t and not t.startswith("#")]
    return ", ".join(terms) if terms else None


def _documents_dir() -> Path:
    """사용자의 실제 문서 폴더(OneDrive로 옮겨진 경우 포함)."""
    try:
        buf = ctypes.create_unicode_buffer(260)
        csidl_personal = 5
        if ctypes.windll.shell32.SHGetFolderPathW(None, csidl_personal, None, 0, buf) == 0 and buf.value:
            return Path(buf.value)
    except (AttributeError, OSError):
        pass
    return Path.home() / "Documents"


def _open_log_folder(folder: Path) -> tuple[Path, logging.Handler]:
    """로그 폴더를 만들고 오늘 날짜의 로그 파일 핸들러를 연다(실패하면 OSError)."""
    folder.mkdir(parents=True, exist_ok=True)
    handler = logging.FileHandler(folder / f"{date.today():%Y-%m-%d}.log", encoding="utf-8")
    return folder, handler


def setup_logging(base: Path | None = None) -> Path:
    """{base}\\logs\\YYYY-MM-DD.log 로 로그를 남기도록 설정하고 로그 폴더를 돌려준다.

    base 폴더에 쓸 수 없으면 문서\\STT변환기\\logs 로 대체한다. 여러 번 호출해도 핸들러는 하나만 유지한다.
    """
    base = Path(base) if base is not None else app_dir()
    try:
        folder, handler = _open_log_folder(base / "logs")
    except OSError:
        fallback = _documents_dir() / _APP_FOLDER_NAME / "logs"
        try:
            folder, handler = _open_log_folder(fallback)
        except OSError as exc:
            raise ConfigError(f"로그 폴더를 만들 수 없습니다: {fallback} ({exc})") from exc

    handler.setFormatter(logging.Formatter(_LOG_FORMAT))
    setattr(handler, _HANDLER_TAG, True)

    root = logging.getLogger()
    for old in [h for h in root.handlers if getattr(h, _HANDLER_TAG, False)]:
        root.removeHandler(old)  # 중복 방지: 이전에 붙인 핸들러는 교체
        old.close()
    root.addHandler(handler)
    root.setLevel(logging.INFO)
    return folder


def _user_settings_path() -> Path:
    appdata = os.environ.get("APPDATA")
    root = Path(appdata) if appdata else Path.home() / "AppData" / "Roaming"
    return root / _APP_FOLDER_NAME / "user_settings.ini"


def load_last_folder() -> Path | None:
    """마지막에 선택한 대상 폴더. 기록이 없거나 폴더가 사라졌으면 None."""
    path = _user_settings_path()
    parser = _new_parser()
    try:
        parser.read_string(path.read_text(encoding="utf-8-sig"))
    except (OSError, UnicodeDecodeError, configparser.Error):
        return None
    value = parser.get("ui", "last_folder", fallback="").strip()
    if not value:
        return None
    folder = Path(value)
    return folder if folder.is_dir() else None


def save_last_folder(folder: Path) -> None:
    """선택한 대상 폴더를 기억해 둔다. 저장에 실패해도 변환에는 영향이 없으므로 로그만 남긴다."""
    path = _user_settings_path()
    parser = _new_parser()
    parser["ui"] = {"last_folder": str(folder)}
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("w", encoding="utf-8") as fp:
            parser.write(fp)
    except OSError as exc:
        logging.getLogger(__name__).warning("마지막 폴더를 저장하지 못했습니다: %s", exc)
