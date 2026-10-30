"""download_model.py 테스트: 네트워크 없이 소스가 경고 없이 컴파일되는지만 확인한다(실제 다운로드는 하지 않음)."""
import warnings
from pathlib import Path

_SCRIPT = Path(__file__).resolve().parent.parent / "download_model.py"


def test_compiles_without_invalid_escape_warning():
    """docstring의 '\\{' 같은 잘못된 이스케이프는 3.11 DeprecationWarning, 3.12+ SyntaxWarning이 된다."""
    source = _SCRIPT.read_text(encoding="utf-8")
    with warnings.catch_warnings():
        warnings.simplefilter("error")
        compile(source, str(_SCRIPT), "exec")
