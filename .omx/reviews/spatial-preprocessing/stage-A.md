# Stage A Review — Spatial identity and provenance

판정: `APPROVE`

## 변경

- `SpatialUnitSchema`가 kind, authority, namespace, version, level과 선택적 resolution을 불변 identity로 묶는다.
- standard grid는 유효한 양의 resolution을 필수로 요구한다.
- `SpatialUnitId`는 schema 없는 문자열 ID를 허용하지 않는다.
- `ProviderDatasetIdentity`는 provider/dataset/schema/evidence를 모두 요구한다.

## 검증

- test-first red: `URSUS.Preprocessing` 계약 부재로 build 실패 확인
- 구현 후 tests: 146/146 통과

## 리뷰 결과

- blocker: 0
- major: 0
- minor: 0

`Identity` 문자열은 진단용이며 equality는 record field 전체를 사용한다. source unit value는 의미를 바꾸지 않도록 trim 외 대소문자 변환을 하지 않는다.
