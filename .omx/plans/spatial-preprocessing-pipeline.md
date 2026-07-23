# URSUS Spatial Data Preprocessing Pipeline

상태: `Foundation implemented and reviewed — live dataset acquisition gates pending`
기준일: 2026-07-23
목표: 통계값과 Geometry를 출처 이름이나 겉보기 해상도로 결합하지 않고, 공간 단위·코드 namespace·버전·공식 crosswalk가 검증된 경우에만 일대일로 결합한다.

## 1. 현재 구조 감사

현재 실행 경로는 모든 통계값을 canonical 법정동 코드로 투영하고 하나의 VWorld 법정동 Geometry 집합과 결합한다.

- `DistrictDataRecord`는 모든 결과 키를 법정동 8자리로 강제한다.
- 행정동 원천은 평균 지표를 복제하고 합계 지표를 관련 법정동 수로 균등 분배한다.
- `AnalysisSnapshot`은 모든 layer가 하나의 `DistrictIndex`와 `Topologies`를 공유한다고 가정한다.
- `DataSourceRegistry`는 통계 source는 여러 개 등록하지만 boundary source는 하나만 보유한다.
- source metadata에는 native/output 공간 단위, ID namespace, 기준 버전, ID field와 join evidence가 없다.

따라서 현재 경로는 기존 결과를 보존해야 하는 legacy projection이지, native 공간 단위의 exact preprocessing 계약이 아니다.

## 2. 불변 원칙

1. 같은 `250m`, 같은 컬럼명 또는 같은 provider라는 사실만으로 결합하지 않는다.
2. 통계와 Geometry는 `authority + namespace + version + level/resolution`이 같은 canonical 공간 단위를 선언해야 한다.
3. provider 원본 ID는 검증된 identity projection 또는 공식 일대일 crosswalk를 거쳐 canonical unit ID가 된다.
4. 통계 ID와 Geometry ID는 각각 유일해야 한다.
5. exact visualization은 두 canonical ID 집합이 완전히 같을 때만 생성한다.
6. missing, duplicate, unmapped, many-to-one, one-to-many와 schema mismatch를 0 또는 추정값으로 숨기지 않는다.
7. coarse 통계를 finer Geometry에 복제하지 않는다.
8. provider fallback은 같은 canonical 공간 단위와 dataset semantics를 제공할 때만 허용한다.
9. VWorld에 capability가 없으면 SGIS, 국토지리정보원, 서울시 등 등록된 다른 provider를 탐색한다.
10. 기존 법정동 projection은 호환 경로로 유지하되 native/exact 결과처럼 표시하지 않는다.

## 3. 핵심 계약

```text
SpatialUnitSchema
├── Kind
├── Authority
├── Namespace
├── Version
├── Level
└── ResolutionMeters?

ProviderDatasetIdentity
├── ProviderId
├── DatasetId
├── SchemaVersion
└── EvidenceReference

Raw source unit ID
  → ExactSpatialIdProjection
  → CanonicalSpatialUnitId

CanonicalStatisticalLayer
CanonicalGeometryLayer
  → ExactSpatialJoiner
  → ExactSpatialLayerBinding
```

Geometry의 CRS는 공간 단위 identity와 별도로 Geometry layer에 기록한다. 통계 테이블에는 좌표가 없을 수 있으므로 CRS가 없다는 이유만으로 동일 공간 단위 identity를 부정하지 않는다.

## 4. 단계와 feedback loop

각 단계는 `contract lock → regression test → implementation → full verification → review → correction → gate close` 순서로 진행한다. 리뷰 기록은 `.omx/reviews/spatial-preprocessing/stage-{N}.md`에 남긴다.

### Stage A — 공간 identity와 provenance

상태: `완료` — 146/146 tests, review approve

구현:

- immutable `SpatialUnitSchema`
- immutable `ProviderDatasetIdentity`
- canonical `SpatialUnitId`
- whitespace, invalid resolution, incomplete grid schema, identity equality 검증

Exit:

- 같은 표시명이나 해상도라도 authority/namespace/version이 다르면 incompatible
- 동일 schema와 canonical value만 같은 unit identity
- collection과 문자열 입력의 방어적 정규화

### Stage B — 원본 layer와 검증된 ID projection

상태: `완료` — 149/149 tests, review approve

구현:

- raw statistical/geometry records와 layer
- identity/official-crosswalk projection 종류와 evidence
- source ID → canonical ID projection
- duplicate source ID, unmapped ID, duplicate canonical target와 schema mismatch 거부

Exit:

- 컬럼명 차이는 adapter가 흡수할 수 있다.
- 다른 code namespace는 evidence가 있는 일대일 crosswalk 없이는 결합할 수 없다.
- many-to-one/one-to-many은 exact projection으로 통과하지 않는다.

### Stage C — exact join과 진단

상태: `완료` — 152/152 tests, review approve

구현:

- `ExactSpatialJoiner`
- schema mismatch, missing Geometry, missing statistic, duplicate/unmapped 진단
- exact ID set일 때만 immutable visualization binding 생성

Exit:

- `stat count == geometry count == joined count > 0`
- 각 canonical ID가 정확히 한 value와 한 Polygon을 가진다.
- mismatch에서는 partial choropleth를 성공 결과로 만들지 않는다.

### Stage D — multi-provider capability registry

상태: `완료` — 154/154 tests, review approve with documented coverage constraint

구현:

- statistic/geometry asset descriptor
- 동일 spatial schema와 semantics에 대한 복수 provider 등록
- exact compatibility filter와 deterministic priority
- provider failure 시 다음 compatible candidate를 선택할 수 있는 ordered candidates

Exit:

- VWorld에 없는 unit을 이유로 탐색을 종료하지 않는다.
- SGIS/NGII/서울시 candidate를 같은 registry에 등록할 수 있다.
- 다른 version/resolution을 fallback으로 반환하지 않는다.

### Stage E — Snapshot과 visualization 통합

상태: `완료` — 156/156 tests, review approve

구현:

- `AnalysisSnapshot`에 layer별 exact spatial binding을 optional tail로 추가
- choropleth가 요청 layer의 binding Geometry/ID를 사용
- legacy `DistrictIndex/Topologies` 경로 유지
- 서로 다른 spatial schema layer의 implicit overlay 거부

Exit:

- 250m layer는 250m Geometry로, 행정동 layer는 행정동 Geometry로 독립 시각화 가능
- 기존 snapshot constructor와 현재 GH 실행은 회귀 없이 유지
- exact binding이 아닌 결과는 신규 시각화 경로에 진입하지 못함

### Final gate — 통합 검증과 문서화

상태: `완료` — 157/157 tests, final review approve with acquisition gates

- 빈 통계/Geometry 집합은 exact 결과가 아닌 `EmptyInput`으로 거부한다.
- Core와 Grasshopper Release build를 통과했다.
- 변경 C# 파일 whitespace verification과 diff check를 통과했다.
- Linux SDK에 WindowsDesktop target이 없어 Setup project를 포함한 solution build는 실행 환경 gate로 남겼다.
- 제품 저널과 Site Briefing plan을 native spatial truth 계약에 맞춰 갱신했다.

## 5. 테스트 시나리오

- 같은 250m·다른 namespace/version은 join 실패
- 다른 source column name·같은 canonical ID는 join 성공
- 공식 일대일 crosswalk는 join 성공
- duplicate source ID, duplicate canonical target, unmapped ID 거부
- 통계에만 있는 ID와 Geometry에만 있는 ID를 각각 진단
- 통계/Geometry exact set에서 feature 수와 ID가 일치
- VWorld miss 뒤 compatible NGII/SGIS candidate 탐색
- incompatible provider를 priority가 높아도 제외
- layer별 snapshot Geometry 선택
- legacy 법정동 choropleth 회귀

## 6. 이번 구현 범위

포함:

- provider-neutral preprocessing 계약과 fail-closed 구현
- multi-provider capability routing
- snapshot/choropleth의 per-layer spatial binding
- 기존 source metadata가 향후 native/output contract를 선언할 수 있는 확장점
- tests, review records, journal과 기존 Site Brief plan의 의존성 갱신

제외:

- SGIS/NGII/서울 250m 대용량 파일의 실제 배포·다운로드
- 아직 schema와 이용조건을 캡처하지 않은 live provider adapter
- 행정동 통계를 250m로 추정 분배
- 기존 법정동 projection의 즉시 삭제

실제 provider adapter는 이 계약 위에서 dataset별 acquisition gate를 별도로 통과해야 한다.

## 7. 중단 조건

- exact join이 입증되지 않은 데이터를 성공 choropleth로 만들지 않는다.
- 기존 142개 회귀 테스트 또는 전체 build가 실패하면 gate를 닫지 않는다.
- provider 문서와 실제 fixture가 ID namespace/version을 입증하지 못하면 adapter 구현으로 우회하지 않는다.
