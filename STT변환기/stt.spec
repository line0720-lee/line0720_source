# -*- mode: python ; coding: utf-8 -*-
# STT변환기 PyInstaller 설정(onedir, 콘솔 창 없음). 빌드는 build.bat으로 한다.
#   결과: dist\STT변환기\STT변환기.exe + _internal\  (models\, config.ini, glossary.txt는 build.bat이 복사)
from PyInstaller.utils.hooks import collect_data_files, collect_dynamic_libs, collect_submodules

APP_NAME = "STT변환기"
# GPU(CUDA) 전용 DLL: CPU 전용 배포본에는 넣지 않는다.
# (ctranslate2 휠에 cudnn64_9.dll이 들어 있지만 ctranslate2.dll이 직접 불러오지 않아 CPU 실행에는 필요 없다)
CUDA_MARKERS = ("cublas", "cudnn", "cudart", "cufft", "nvrtc")


def _is_cuda(path):
    """파일명(경로의 마지막 부분)에 CUDA 표시가 들어간 DLL인지."""
    name = path.replace("\\", "/").rsplit("/", 1)[-1].lower()
    return name.endswith(".dll") and any(marker in name for marker in CUDA_MARKERS)


# collect_dynamic_libs는 (원본 경로, 넣을 폴더) 목록을 돌려준다
binaries = (
    collect_dynamic_libs("ctranslate2")
    + collect_dynamic_libs("onnxruntime")
    + collect_dynamic_libs("av")
)
binaries = [b for b in binaries if not _is_cuda(b[0])]

a = Analysis(
    ["src\\main.py"],
    pathex=["src"],
    binaries=binaries,
    datas=collect_data_files("faster_whisper"),  # VAD 모델(assets\silero_vad_v6.onnx)
    hiddenimports=collect_submodules("av"),
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=["torch", "nvidia", "tensorflow"],
    noarchive=False,
    optimize=0,
)
# 훅(pyinstaller-hooks-contrib)이 따로 모은 DLL까지 한 번 더 걸러 낸다
a.binaries = [b for b in a.binaries if not _is_cuda(b[0])]

pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name=APP_NAME,
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
coll = COLLECT(
    exe,
    a.binaries,
    a.datas,
    strip=False,
    upx=False,
    upx_exclude=[],
    name=APP_NAME,
)
