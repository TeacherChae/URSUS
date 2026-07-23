# Stage 0 Review — Current-state audit and implementation plan

판정: `APPROVE WITH CONSTRAINTS`

## 검증 증거

- 변경 전 tests: 142/142 통과
- `URSUS.csproj` Release build: 성공, 기존 NU1701 1건
- `URSUS.GH.csproj` Release build: 성공, 기존 platform/nullability 경고 36건
- git worktree는 계획 문서 추가 전 clean이었다.

## 발견한 구조 위험

1. `DistrictDataRecord`와 `AnalysisSnapshot`이 법정동 global index를 강제한다.
2. 행정동→법정동 변환은 exact spatial join이 아니라 복제/균등 분배 추정이다.
3. boundary registry cardinality가 1이라 multi-provider 탐색이 불가능하다.
4. metadata가 native/output spatial schema와 ID evidence를 표현하지 못한다.
5. 현재 Site Brief plan은 신규 provider를 제외하고 resolved legal district를 모든 profile의 index로 사용한다.

## 리뷰 수정

- provider 이름보다 canonical spatial schema를 먼저 선택하도록 계획을 수정했다.
- 통계 테이블에는 좌표가 없을 수 있으므로 CRS를 spatial identity가 아니라 Geometry layer 속성으로 분리했다.
- exact join과 aggregation/resampling을 같은 abstraction으로 합치지 않았다.
- 기존 법정동 결과를 삭제하지 않고 명시적 legacy projection으로 유지한다.
- live SGIS/NGII adapter는 schema fixture 없이 구현하지 않는 acquisition gate로 남겼다.

## Gate 조건

- 신규 계약은 기존 142개 테스트를 깨지 않는 additive path여야 한다.
- exact ID 집합이 아닌 결과는 신규 choropleth binding이 될 수 없다.
- 단계별 테스트, 전체 build와 다음 리뷰에서 major 이상 0이어야 한다.
