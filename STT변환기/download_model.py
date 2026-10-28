"""Whisper 모델 다운로드 스크립트.

인터넷이 되는 PC에서만 실행한다. 이 프로젝트에서 네트워크를 쓰는 유일한 스크립트이며,
프로그램 본체(STT변환기.exe)는 실행 중 인터넷에 접속하지 않는다.

사용법:
    python download_model.py [small] [medium] [large-v3-turbo]
    (인자를 생략하면 medium 하나만 받는다)
"""
import sys
from pathlib import Path

# 모델 이름 -> Hugging Face 저장소
MODEL_REPOS = {
    "small": "Systran/faster-whisper-small",
    "medium": "Systran/faster-whisper-medium",
    "large-v3-turbo": "mobiuslabsgmbh/faster-whisper-large-v3-turbo",
}
DEFAULT_MODEL = "medium"

# 현재 작업 폴더가 아니라 이 스크립트가 있는 폴더 기준으로 models\ 위치를 정한다
MODELS_DIR = Path(__file__).resolve().parent / "models"


def folder_size_gb(folder: Path) -> float:
    """폴더 안 모든 파일의 크기 합계를 GB 단위로 돌려준다."""
    total = sum(f.stat().st_size for f in folder.rglob("*") if f.is_file())
    return total / (1024 ** 3)


def download(name: str) -> bool:
    """모델 1개를 models\{name}\ 에 내려받고 성공 여부를 돌려준다."""
    from huggingface_hub import snapshot_download

    repo_id = MODEL_REPOS[name]
    target = MODELS_DIR / name
    print(f"[{name}] 다운로드 시작: {repo_id} -> {target}")
    snapshot_download(repo_id=repo_id, local_dir=str(target))

    model_bin = target / "model.bin"
    if not model_bin.is_file():
        print(f"[{name}] 오류: model.bin 파일이 없습니다. 다운로드가 완전하지 않습니다.")
        return False
    print(
        f"[{name}] 완료: model.bin {model_bin.stat().st_size / (1024 ** 3):.2f} GB, "
        f"폴더 전체 {folder_size_gb(target):.2f} GB"
    )
    return True


def main(argv: list[str]) -> int:
    names = argv or [DEFAULT_MODEL]

    # 다운로드를 시작하기 전에 모델 이름부터 모두 확인한다
    unknown = [n for n in names if n not in MODEL_REPOS]
    if unknown:
        print(f"오류: 알 수 없는 모델 이름: {', '.join(unknown)}")
        print(f"사용 가능한 모델: {', '.join(MODEL_REPOS)}")
        return 1

    failed = []
    for name in names:
        try:
            ok = download(name)
        except Exception as exc:  # 네트워크 오류 등 - 다음 모델은 계속 시도
            print(f"[{name}] 오류: 다운로드에 실패했습니다. ({exc})")
            ok = False
        if not ok:
            failed.append(name)

    if failed:
        print(f"실패한 모델: {', '.join(failed)}")
        return 1
    print("모든 모델 다운로드가 끝났습니다.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
