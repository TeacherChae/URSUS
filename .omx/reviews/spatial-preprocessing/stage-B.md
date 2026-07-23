# Stage B Review — Raw layers and exact ID projection

판정: `APPROVE`

## 변경

- 통계와 Geometry가 서로 다른 source ID field를 보존하는 raw layer 계약을 추가했다.
- shared namespace identity projection과 evidence-backed official crosswalk를 분리했다.
- projected record는 source ID와 canonical `SpatialUnitId`를 함께 보존한다.
- duplicate source ID, duplicate canonical target, unmapped ID와 source schema mismatch를 typed failure로 거부한다.

## 검증

- test-first red: raw layer/projection 타입 부재로 build 실패 확인
- 구현 후 tests: 149/149 통과

## 리뷰와 수정

- blocker: 0
- major: 0
- minor: 1 → 수정 완료

사용되지 않고 factory도 없던 `DerivedFromOfficialSpecification` enum 값을 삭제했다. 계산식 기반 ID 변환은 실제 공식 사양 fixture와 결정적 구현이 생길 때 별도 projection 종류로 추가한다.
