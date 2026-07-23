# Stage D Review — Multi-provider capability registry

판정: `APPROVE WITH CONSTRAINT`

## 변경

- statistic/Geometry asset를 동일 registry에 등록할 수 있다.
- exact spatial schema, semantics와 coverage가 일치하는 모든 candidate를 반환한다.
- preference rank는 호환 candidate의 순서만 정하며 schema/semantics를 완화하지 않는다.
- 한 provider에 capability가 없어도 다른 provider candidate 탐색을 계속할 수 있다.
- 동일 asset 중복 등록은 거부한다.

## 검증

- test-first red: capability registry 계약 부재 확인
- 구현 후 tests: 154/154 통과
- VWorld legal/500m candidate가 NGII 250m 요청에 섞이지 않고 NGII→SGIS 순서가 유지됨을 확인

## 리뷰 결과

- blocker: 0
- major: 0
- minor: 0

## 제약

`CoverageId`는 provider의 마케팅상 최대 범위가 아니라 해당 adapter가 실제로 반환하도록 검증된 query/artifact 범위 ID다. 전국 source가 서울 subset을 제공한다면 adapter descriptor가 그 검증된 서울 coverage를 별도로 선언해야 한다. 지리적 포함관계 추론은 아직 하지 않는다.
