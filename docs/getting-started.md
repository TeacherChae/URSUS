# Getting Started — URSUS 0.3.0

## first-run

[설치](installation.md)와 [API 키 설정](api-keys.md)을 마친 뒤 Rhino 8에서 Grasshopper를 열고 `URSUS_sample.gh`를 실행합니다. Solver의 Run 입력을 `false → true`로 바꾸면 한 번 실행됩니다. 결과의 경고와 누락 레이어를 확인한 후 해석하세요.

## component-wiring

기본 흐름은 `URSUS Solver → Visualizer → CSV Export`입니다. Solver의 `LegalCodes`, `Centroids`, `Values`를 같은 실행 결과에서 Visualizer/Exporter에 연결하십시오. 서로 다른 실행의 리스트를 섞거나 길이가 다른 리스트를 연결하지 마십시오.

서울 통계는 서울 행정동 데이터이므로 서울 밖 요청에서는 사용할 수 없는 레이어로 보고됩니다. 경계 데이터는 VWorld 키를 사용합니다. 기존 data.go.kr 공시지가·용도지역 adapter는 deprecated legacy이며 기본 flow에서 키를 요구하지 않습니다.
