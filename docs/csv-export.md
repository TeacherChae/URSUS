# CSV Export — URSUS 0.3.0

Solver 결과를 CSV Export 컴포넌트에 연결하고 저장 경로와 실행 트리거를 지정합니다.

## no-content

Solver가 성공했고 `LegalCodes`, 이름, 값이 생성되었는지 확인합니다.

## file-path

쓰기 가능한 `.csv` 경로를 지정합니다. 폴더가 존재해야 하며 이미 열린 파일은 닫습니다.

## no-data

요청 영역에서 사용 가능한 레이어가 있는지 Solver의 `MissingLayers`와 경고를 확인합니다. 누락 데이터를 임의 평균으로 대치하지 않습니다.
