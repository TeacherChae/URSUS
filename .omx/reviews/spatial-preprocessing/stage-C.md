# Stage C Review — Exact one-to-one join

판정: `APPROVE`

## 변경

- canonical statistic/Geometry schema가 다르면 ID가 같아도 `SchemaMismatch`다.
- 양쪽 ID 차집합을 `MissingGeometryIds`와 `MissingStatisticIds`로 분리한다.
- exact set에서만 `ExactSpatialLayerBinding`을 생성할 수 있다.
- 양쪽이 모두 빈 집합인 경우 exact evidence로 승격하지 않는다.
- binding은 통계/Geometry provider와 두 projection evidence를 모두 보존한다.
- feature는 canonical ID ordinal 순서로 고정한다.

## 검증

- test-first red: join/result/binding 계약 부재 확인
- 구현 후 tests: 152/152 통과

## 리뷰 결과

- blocker: 0
- major: 0
- minor: 1 → 수정 완료

최종 diff 리뷰에서 빈 통계와 빈 Geometry가 동일한 empty set이라는 이유로 `Exact`가 되는 문제를 발견했다. `EmptyInput` status를 추가하고 binding 생성을 거부했다.

이 단계는 ID/schema exactness를 검증한다. 실제 provider Geometry가 공식 사양과 일치하는지는 provider별 acquisition fixture가 증명해야 하며, evidence 없는 source는 projection을 만들 수 없다.
