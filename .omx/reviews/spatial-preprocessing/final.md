# Final Review — Spatial preprocessing foundation

판정: `APPROVE WITH ACQUISITION GATES`

## 계약 검토

- 공간 단위 identity는 `authority + namespace + version + level/resolution`을 모두 포함한다.
- 통계와 Geometry는 원본 ID를 보존하고, evidence가 있는 identity projection 또는 공식 일대일 crosswalk만 canonical ID를 만든다.
- exact join은 non-empty canonical ID 집합이 완전히 일치할 때만 binding을 생성한다.
- provider 우선순위는 compatibility 검사를 우회하지 않으며, VWorld miss가 다른 provider 탐색을 중단시키지 않는다.
- visualization은 layer 고유 Geometry와 provenance를 사용하고 legacy 법정동 경로는 호환 경로로 남는다.

## 최종 피드백과 수정

- blocker: 0
- major: 0
- minor: 1 → 수정 완료

통계와 Geometry가 모두 비어 있을 때 두 ID 집합이 같다는 이유로 exact 결과가 만들어질 수 있었다. `EmptyInput` 상태를 추가하고 `joined count > 0`을 binding 생성 조건으로 고정했다.

## 검증 증거

- `URSUS.Tests` Release: 157/157 통과
- `URSUS` Release build: 성공
- `URSUS.GH` Release build: 성공
- 변경 C# 파일 whitespace verification: 성공
- `git diff --check`: 성공
- 전체 solution build: Linux SDK에 `Microsoft.NET.Sdk.WindowsDesktop.targets`가 없어 `URSUS.Setup`에서 중단

전체 solution 실패는 이번 변경의 compile/test 오류가 아니라 Windows installer project의 환경 제약이다. Core, GH와 테스트 프로젝트는 별도로 성공했다.

## 열린 gate

- live SGIS/NGII/서울시 adapter는 아직 구현하지 않았다.
- 실제 dataset마다 schema/version, ID namespace, 이용조건, coverage와 crosswalk evidence를 fixture로 잠가야 한다.
- 첫 acquisition 후보는 서울시 250m 통계와 같은 기관이 배포한 공식 격자 파일의 paired 검증이다.
- NGII Geometry와 타 기관 통계의 cross-provider 결합은 동일 ID 체계 또는 공식 일대일 crosswalk를 증명한 뒤에만 허용한다.
