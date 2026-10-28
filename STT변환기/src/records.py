"""파일 1건의 처리 결과를 담는 자료형. 일괄 처리(batch_runner)와 엑셀 기록(excel_writer)이 함께 쓴다."""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime


@dataclass
class ResultRow:
    index: int  # 처리 순서(1부터)
    name: str  # 선택 폴더 기준 표시 이름(하위폴더 파일은 상대경로)
    text: str  # 변환 내용(실패하면 빈 문자열)
    duration_sec: float | None  # 오디오 재생 시간(실패하면 None)
    elapsed_sec: float  # 이 파일에 쓴 처리 시간
    status: str  # 완료 / 완료(인식된 음성 없음) / 실패(사유)
    finished_at: datetime  # 행별 완료 시각

    @property
    def failed(self) -> bool:
        return self.status.startswith("실패")
