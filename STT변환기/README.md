# STT변환기

폴더 안의 .wav 파일을 인터넷 연결 없이 로컬 Whisper 모델(faster-whisper)로 일괄 변환하여, 결과를 엑셀 파일 1개에 정리하는 Windows 데스크톱 프로그램입니다. 인터넷 PC에서 개발·빌드한 뒤 폐쇄망 PC에는 실행 폴더와 모델 폴더만 복사해 사용합니다. 자세한 설계는 `design.md`를 참고하세요.

## 개발 환경 준비

Python 3.11이 필요합니다. 프로젝트 폴더(`STT변환기`)에서 실행합니다.

```
py -3.11 -m venv .venv
.venv\Scripts\python -m pip install -r requirements.txt -r requirements-dev.txt
```

## 모델 다운로드

인터넷이 되는 PC에서만 실행합니다. 모델은 `models\{모델이름}\` 폴더에 저장됩니다.

```
.venv\Scripts\python download_model.py medium small large-v3-turbo
```

인자를 생략하면 기본 모델(`medium`)만 받습니다. 사용할 수 있는 이름은 `small`, `medium`, `large-v3-turbo`입니다.

## 빌드

(작성 예정)

## 반입·설치

(작성 예정)

## 사용 방법

(작성 예정)

## 모델별 처리 시간

(작성 예정)

## 문제 해결

(작성 예정)
