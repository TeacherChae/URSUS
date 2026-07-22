# URSUS Address-to-Urban-Context Brief v1 계약

상태: `Stage 0 approved — locked for Stage 1 implementation`
계약 버전: `site-brief/1.0-draft`
기준 결정: [Product Journal](product-journal.md)의 2026-07-22 결정
Golden fixture: [`fixtures/site-brief-golden-v1.json`](fixtures/site-brief-golden-v1.json)

## 1. 제품이 답하는 한 가지 질문

> **이 주소의 대지는 어디에 있으며, 현재 확보한 공공데이터가 주변 도시 맥락에 관해 무엇을 말하고 무엇을 말하지 못하는가?**

URSUS v1은 주소를 프로그램이나 수익성 판단으로 변환하지 않는다. 주소를 재현 가능한 `ResolvedSite`로 해석하고, 같은 시군구의 법정동 비교권역 안에서 다섯 도시 맥락을 **확인된 내용, 해석할 때 주의, 현재 데이터로 모르는 것, 추가로 확인할 자료**로 구분해 제시한다.

## 2. 입력과 주소 해석 계약

### 2.1 허용 입력

- 도로명 주소와 지번 주소를 지원한다.
- `AnalysisRequest.InputAddress1`을 새로 추가해 전달받은 문자열을 그대로 보존한다. 기존 `Address1`은 현재처럼 trim된 query 값으로 유지해 public semantics를 깨지 않는다. `ResolvedSite.InputAddress`는 `InputAddress1`에서 온다.
- 빈 값 판정, provider request와 request cache에는 trim/collapsed query 값을 사용하지만 exact input provenance에는 사용자의 원문을 보존한다.
- 사용자는 주소 유형을 따로 고르지 않는다. resolver가 `road`와 `parcel` 요청을 모두 수행하고 실제 성공 결과를 기록한다.
- 주소 유형은 문자열 모양으로 추측하지 않고 `Road`, `Parcel`, `DualEquivalent` 중 하나로 기록한다.
- 빈 주소, 결과 0건, 복수 후보, 지원 범위 밖, provider 오류는 기본 주소로 대체하지 않는다.

### 2.2 주소 요청과 후보 선택 결정표

각 mode의 `OK`는 `status=OK`, finite WGS84 point, non-empty `refined.text`와 일치하는 response contract를 모두 만족할 때만 인정한다.

Raw response mapping은 `OK`면 위 필수 field 검증 후 candidate, `NOT_FOUND`이면서 `record.total=0`이면 Not found, `ERROR`면 error code를 보존한 `ProviderFailure`, 그 밖의 status/shape는 `PROVIDER_SCHEMA_INVALID`인 `ProviderFailure`다. 실제 error envelope는 [error fixture](fixtures/vworld-address-error-captures.json)에 보존한다.

| Road 요청 | Parcel 요청 | 결과 |
|---|---|---|
| OK | Not found | `Resolved`, `AddressKind=Road` |
| Not found | OK | `Resolved`, `AddressKind=Parcel` |
| OK | OK, 좌표 150m 이내이며 같은 법정동 | `Resolved`, `AddressKind=DualEquivalent`; 두 candidate를 모두 보존하고 Road를 display representative로 사용 |
| OK | OK, 좌표 150m 초과 또는 법정동 불일치 | `NeedsSelection`; 자동 선택하지 않음 |
| Not found | Not found | `NotFound` |
| malformed/auth/timeout | 어떤 결과든 | `ProviderFailure`; 한 mode의 성공으로 provider contract 실패를 숨기지 않음 |

VWorld `getcoord`는 mode당 단일 결과를 반환하므로 v1 후보는 최대 2개다. 각 `AddressCandidate`는 `Kind`, `InputAddress`, `RefinedText`, `RefinedStructure`, WGS84 point, response contract version과 secret-free response fingerprint를 가진다. `NeedsSelection`은 이 후보 배열을 반환하지만 Site Brief를 만들지 않는다.

법정동명 corroboration field는 mode별로 고정한다. `Road`는 `refined.structure.level3`, `Parcel`은 `refined.structure.level4L`을 사용한다. 빈 값이면 해당 candidate는 malformed다. `level4A`는 행정동이므로 법정동 선택에 사용하지 않는다.

### 2.3 성공한 `ResolvedSite`의 최소 정보

| 필드 | 계약 |
|---|---|
| `InputAddress` | 사용자가 입력한 원문 |
| `NormalizedAddress` | resolver가 확정한 대표 주소 |
| `AddressKind` | `Road`, `Parcel` 또는 `DualEquivalent` |
| `Candidates` | 성공한 1–2개 mode의 secret-free 해석 결과 |
| `Wgs84` | resolver가 반환한 경도·위도와 `EPSG:4326` |
| `ProjectedCoordinate` | WGS84에서 파생한 EPSG:5179 좌표와 변환기 버전 |
| `LegalDistrictCode` | WFS `emd_cd` 원문과 `DistrictCode.CanonicalizeLegal` 결과. 10자리 코드·19자리 PNU가 candidate에 있으면 별도 raw provenance로 보존 |
| `LegalDistrictName` | WFS `full_nm` 원문과 display용 `emd_kor_nm`을 분리해 보존 |
| `ResolverSources` | VWorld address 2.0과 WFS `lt_c_ademd_info` 2.0.0을 모두 기록 |
| `ResolvedAt` | UTC timestamp |
| `ResolutionWarnings` | 후보 선택, 좌표 허용오차, 주소 별칭 등의 경고 |
| `ResolutionId` | 아래 canonical identity의 SHA-256 |

같은 건물을 나타내는 도로명·지번 golden 입력은 같은 canonical 법정동으로 해석되고 좌표가 150m 이내여야 한다. 정확히 같은 부동소수점 좌표일 필요는 없다. 건물 진입점, 대표점, 필지 중심점이라는 provider 의미 차이가 있기 때문이다.

현재 코드의 `VworldApiParser.AddressToCoordAsync`가 `type=road`를 고정한다는 사실은 Stage 1의 명시적 회귀 위험이다. 지번 주소를 도로명 모드로 재시도해 조용히 성공한 것처럼 취급해서는 안 된다. 실제 VWorld 2.0 캡처는 [road fixture](fixtures/vworld-address-road-sejongdaero-110.json)와 [parcel fixture](fixtures/vworld-address-parcel-taepyeongno-31.json)에 보존한다.

### 2.4 법정동 선택 알고리즘

Geocoder의 문자열 구조만으로 법정동 코드를 추정하지 않는다.

1. 각 성공 candidate의 WGS84 point를 기존 `Epsg5179.FromWgs84`로 변환한다.
2. 같은 분석 요청에서 받은 `lt_c_ademd_info` topology 각각에 대해 outer ring 내부이면서 hole 외부인지 even-odd ray-casting으로 판정한다.
3. 모든 predicate는 EPSG:5179에서 계산한다. point-to-segment 거리가 `0.01m` 이하이면 edge match이며, 그보다 먼 점만 strict containment에 참여한다. squared distance를 사용하고 새 geometry dependency를 추가하지 않는다.
4. 정확히 한 topology가 strict containment이면 해당 WFS `emd_cd`, `full_nm`, `emd_kor_nm`을 선택한다.
5. strict match가 없고 edge match가 있으면 경계 후보를 보존한 `NeedsSelection`이다. 경계점을 한 법정동으로 임의 배정하지 않는다.
6. strict/edge match가 모두 0개면 `OutOfCoverage`, strict match가 2개 이상이면 `NeedsSelection`이다.
7. road/parcel candidate가 각각 선택한 canonical 법정동이 다르면 거리와 무관하게 `NeedsSelection`이다.
8. 위 mode별 legal-name field와 WFS `emd_kor_nm`이 다르면 성공을 유지하지 않고 `ProviderFailure`로 처리한다.

실제 후보 identity와 topology는 [compact WFS fixture](fixtures/vworld-legal-district-candidates-city-hall.json)와 [full geometry capture](fixtures/vworld-legal-district-boundaries-city-hall.json)에 보존한다. 합성 ring fixture로 inside/hole/edge/multiple/none을 별도 테스트한다.

### 2.5 주소 해석 상태

| 상태 | 사용자에게 보이는 의미 | Site Brief 생성 |
|---|---|---|
| `Resolved` | 단일 후보와 법정동이 확정됨 | 가능 |
| `NeedsSelection` | 의미 있는 복수 후보가 있음 | 불가, 후보 선택 필요 |
| `NotFound` | 결과 없음 | 불가 |
| `OutOfCoverage` | 주소 point는 찾았으나 v1 법정동 경계 범위 밖 | Site Brief 불가; representative-location 진단만 반환 |
| `ProviderFailure` | 인증·timeout·schema 오류 | 불가, 원인 보존 |
| `InvalidInput` | null, empty 또는 Unicode whitespace-only 입력 | 불가 |

### 2.6 `AddressResolutionResult` 결과 계약

resolver의 public return은 immutable `AddressResolutionResult` 하나다.

```text
AddressResolutionResult
├── Status: AddressResolutionStatus
├── ReasonCode: AddressResolutionReasonCode
├── ResolvedSite?
├── ReferenceCohort?
├── Candidates[]
├── RepresentativeLocation?
├── LegalDistrictAlternatives[]
└── ProviderFailures[]
```

- `RepresentativeLocation`은 WGS84/EPSG:5179 point와 선택 근거를 가지며, 주소는 찾았지만 법정동을 확정하지 못한 경우에만 사용한다.
- `LegalDistrictAlternative`는 WFS raw/canonical code, full/display name, `Strict`/`Edge` match와 point-to-boundary distance를 가진다.
- `ProviderFailure`는 `Mode(Road|Parcel|Boundary)`, canonical code, provider code/level, secret·exact address가 없는 safe message를 가진다.
- `ReasonCode` v1은 `None`, `EmptyAddress`, `BothModesNotFound`, `DualModeDistanceExceeded`, `DistrictEdge`, `MultipleDistrictContainment`, `DistrictDisagreement`, `OutsideBoundaryCoverage`, `LegalNameMismatch`, `CohortBoundaryIncomplete`, `ProviderReportedError`, `ProviderSchemaInvalid`, `ProviderTransportFailure`로 제한한다. v1은 non-empty 문자열의 주소 형식을 로컬에서 판정하지 않는다. 따라서 그 밖의 미발견 문자열은 provider 결과에 따라 `NotFound`가 된다.

상태 불변조건은 다음과 같다.

| Status | 필수 payload | 반드시 비어야 하는 payload | Snapshot 생성 |
|---|---|---|---|
| `Resolved` | `ResolvedSite`, `ReferenceCohort`, 1–2 candidates, `ReasonCode=None` | representative, alternatives, failures | 생성; snapshot에 site/cohort 연결 |
| `NeedsSelection` | 2 candidates 또는 1개 이상 district alternatives, 해당 reason | resolved site, cohort, failures | 생성하지 않음 |
| `OutOfCoverage` | 1개 이상 candidate, representative location, `OutsideBoundaryCoverage` | resolved site, cohort, alternatives, failures | 생성하지 않음 |
| `ProviderFailure` | 1개 이상 failure, provider reason; 먼저 성공한 mode candidate는 보존 가능 | resolved site, cohort, representative, alternatives | 생성하지 않음 |
| `NotFound` | `BothModesNotFound` | resolved site, cohort, candidates, representative, alternatives, failures | 생성하지 않음 |
| `InvalidInput` | `EmptyAddress` | resolved site, cohort, candidates, representative, alternatives, failures | 생성하지 않음 |

한 mode가 성공했더라도 다른 mode가 `ERROR`, malformed 또는 transport failure면 `ProviderFailure`이며 성공 candidate만 진단용으로 보존한다. `NeedsSelection`은 v1에 사용자 선택 UI가 없으므로 후보를 반환하되 실행을 중단한다. 비성공 mapped output oracle은 [result cases fixture](fixtures/address-resolution-result-cases-v1.json)에 고정한다.

#### 닫힌 reason/status/payload 행렬

cardinality 표기는 `0`, `1`, `2`, `0..1`, `1..2`, `1..N`, `2..N`이며 null collection은 허용하지 않는다.

| Reason | Status | Site | Cohort | Candidates | Representative | Alternatives | Failures |
|---|---|---:|---:|---:|---:|---:|---:|
| `None` | `Resolved` | 1 | 1 | 1..2 | 0 | 0 | 0 |
| `DualModeDistanceExceeded` | `NeedsSelection` | 0 | 0 | 2 | 0 | 0 | 0 |
| `DistrictEdge` | `NeedsSelection` | 0 | 0 | 1..2 | 1 | 1..N, 모두 `Edge` | 0 |
| `MultipleDistrictContainment` | `NeedsSelection` | 0 | 0 | 1..2 | 1 | 2..N, 모두 `Strict` | 0 |
| `DistrictDisagreement` | `NeedsSelection` | 0 | 0 | 2 | 0 | 2, candidate kind마다 unique `Strict` 1개 | 0 |
| `OutsideBoundaryCoverage` | `OutOfCoverage` | 0 | 0 | 1..2 | 1 | 0 | 0 |
| `LegalNameMismatch` | `ProviderFailure` | 0 | 0 | 1..2 | 0 | 0 | 1..2, 모두 `Boundary` |
| `CohortBoundaryIncomplete` | `ProviderFailure` | 0 | 0 | 1..2 | 0 | 0 | 1, `Boundary` + `COHORT_BOUNDARY_INCOMPLETE` |
| `ProviderReportedError` | `ProviderFailure` | 0 | 0 | 0..1 | 0 | 0 | 1..2 |
| `ProviderSchemaInvalid` | `ProviderFailure` | 0 | 0 | 0..1 | 0 | 0 | 1..2 |
| `ProviderTransportFailure` | `ProviderFailure` | 0 | 0 | 0..1 | 0 | 0 | 1..2 |
| `BothModesNotFound` | `NotFound` | 0 | 0 | 0 | 0 | 0 | 0 |
| `EmptyAddress` | `InvalidInput` | 0 | 0 | 0 | 0 | 0 | 0 |

- `RepresentativeLocation`은 `DistrictEdge`, `MultipleDistrictContainment`, `OutsideBoundaryCoverage`에만 존재한다. 후보가 둘이면 Road candidate를 대표로 사용하고 `selectionBasis`에 그 사실을 기록한다.
- `DistrictEdge`와 `MultipleDistrictContainment`의 alternative는 `CandidateKind`를 가져 각 candidate에 연결된다. `DistrictDisagreement`는 Road와 Parcel 각각의 unique strict match 하나만 보존한다.
- public constructor는 위 행렬을 만족하지 않거나 collection argument가 null이면 `ArgumentException`을 던진다. 모든 collection은 입력을 한 번 defensive copy한 non-null read-only 값이며 empty는 같은 `Array.Empty<T>()` 의미를 사용한다.
- `AddressResolutionResult.Candidates`가 유일한 authoritative candidate order다. 성공 시 `ResolvedSite.Candidates`는 별도 복사본이 아니라 constructor가 전달한 **같은 ordered immutable value**를 참조해야 한다. canonical JSON에서는 두 배열이 ordinal-deep-equal이어야 하며 불일치하면 역직렬화를 거부한다.
- reason 우선순위는 provider failure → dual distance 초과 → candidate별 edge/multiple/outside/name mismatch → 두 unique 법정동 disagreement → cohort boundary completeness → success 순이다. 먼저 결정된 failure reason으로 실행을 중단한다.
- valid `Resolved/None`과 모든 비성공 결과, 그리고 행렬을 위반하는 constructor 입력은 [result cases fixture](fixtures/address-resolution-result-cases-v1.json)의 `cases`와 `constructorRejectionCases`를 테스트 oracle로 사용한다.

### 2.7 소유권, identity와 공개 정책

- `AnalysisSnapshot`에 optional immutable `ResolvedSite`와 `ReferenceCohort`를 추가한다. 기존 constructor 호출은 두 값이 `null`인 상태로 호환한다.
- Address-to-Urban-Context workflow에서 Site Brief를 만들려면 두 값이 모두 있어야 하며, `ResolvedSite.LegalDistrictCode`는 `ReferenceCohort.DistrictIds`에 포함돼야 한다.
- provider response fingerprint는 **root response object의 JSON Pointer `/service/time`만** 제거한다. 다른 위치나 이름의 `time` property는 보존한다. 그 뒤 모든 object의 property-name을 ordinal 순서, array는 원순서로 두고 UTF-8 no-BOM/no-indent JSON으로 canonicalize해 SHA-256한다. .NET serializer는 `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, strict number handling을 사용해 한글을 `\\u` escape로 바꾸지 않는다.
- `query-normalizer/1`은 .NET `string.Normalize(FormC)` 후 앞뒤의 모든 `char.IsWhiteSpace`를 제거하고, 내부의 연속 `char.IsWhiteSpace`를 U+0020 하나로 바꾼다. candidate의 `RefinedText`, provider query와 cache에는 이 값을 사용하지만 verbatim `InputAddress1`에는 적용하지 않는다.
- 단일 candidate의 `ResolutionId` canonical input은 `resolver-contract/1|{Kind}|{query-normalizer/1 RefinedText}|{longitude:R},{latitude:R}|{CanonicalLegalCode}|{ResponseFingerprint}`다.
- `DualEquivalent`는 Road, Parcel candidate canonical input을 kind 순서로 두고 `resolver-contract/1|DualEquivalent|{road UTF-8 byte count}:{road canonical input}|{parcel UTF-8 byte count}:{parcel canonical input}`을 SHA-256한다. byte-count framing 때문에 field 안의 `|`와 `:`도 모호하지 않다.
- `InputAddress`, `ResolvedAt`, API key와 요청 시간은 `ResolutionId`에 포함하지 않는다.
- 도로명·지번 alias는 v1에서 같은 `ResolutionId`나 provider cache entry로 합치지 않는다. 잘못된 parcel 동등성보다 중복 cache를 허용한다.
- provider request cache preimage는 `resolver-request-cache/1|{Road|Parcel}|{normalized query UTF-8 byte count}:{query-normalizer/1 result}`이며 SHA-256한다. 파일명·로그에 주소 원문을 쓰지 않는다.
- Road/Parcel/DualEquivalent resolution 및 Road/Parcel request cache preimage, NFC/whitespace variant와 `/service/time` 제거 예시는 [identity fixture](fixtures/resolution-identity-v1.json)가 canonical oracle이다.
- 사용자가 직접 보는 로컬 Inspector는 입력·정규화 주소를 표시할 수 있다. status, exception, console log에는 exact address를 쓰지 않고 `ResolutionId`와 법정동만 쓴다.
- Stage 5의 사용자가 직접 실행한 기본 export는 `InputAddress`, `NormalizedAddress`와 precise coordinate를 포함한다. 대지 주소는 Site Brief의 핵심 식별자다.
- 사용자가 `Anonymized`를 명시적으로 선택하면 exact/normalized address와 precise coordinate를 제외하고 canonical 법정동 코드·이름, cohort와 도시 맥락만 export한다. 로그, 예외와 cache filename에는 export mode와 무관하게 exact address를 쓰지 않는다.
- 기존 `Run(..., address1: null)` 동기 overload의 default-address 동작은 v1에서 하위 호환으로 유지하되 obsolete 경고와 `LEGACY_DEFAULT_ADDRESS_USED`를 남긴다. canonical `RunAsync(AnalysisRequest)`와 GH Site Brief flow는 빈 주소를 `InvalidInput`으로 거부한다.

## 3. Reference Cohort 계약

v1의 비교권역은 임의 거리나 생활권 추정이 아니라 **대상 법정동과 같은 시군구에 속한 법정동 전체**다.

- canonical 8자리 법정동 코드의 앞 5자리를 `SigunguCode`로 사용한다. 대상 `11140103`의 비교권역은 `11140`으로 시작하는 법정동이다.
- 서울 v1은 Stage 1에서 별도의 `SeoulLegalDistrictCatalog`를 만든다. 이 catalog는 `MappingLoader.Load().Values.SelectMany(...)`의 법정동 코드를 canonicalize한 뒤, 8자리 서울 코드만 남기고, `000`으로 끝나는 root를 제외하고, 중복을 제거해 ordinal 정렬한다. source version은 `embedded-adstrd-legald-v1`이다.
- 기존 `SeoulExpectedDistricts.Ids`는 `MappingLoader.Load().Keys`에서 만든 **426개 행정동** 집합이다. 기존 data-source completeness 검사를 위해 유지하며 reference cohort membership으로 사용하지 않는다.
- 대상의 5자리 `SigunguCode`로 `SeoulLegalDistrictCatalog` 전체를 filter해 cohort를 만든다. v1 catalog invariant는 서울 467개, `11140` 중구 74개, 대상 `11140103` 포함이다.
- cohort에는 policy/version, `SameSigunguLegalDistricts`, 시군구 코드·이름, membership source version, 정렬된 법정동 ID와 SHA-256 fingerprint를 저장한다.
- cohort fingerprint preimage는 `same-sigungu-legal-districts/1|SameSigunguLegalDistricts|{SigunguCode}|{MembershipSourceVersion}|{ordinal-sorted comma-joined DistrictIds}`이며 UTF-8 SHA-256을 사용한다.
- data 수집이 일부 법정동을 반환하지 않아도 cohort에서 제외하지 않는다. 해당 ID는 section별 missing으로 남는다.
- Stage 1은 단 하나의 경로만 구현한다: `GetLegalDistrictsForCohortAsync(ReferenceCohort, CancellationToken)`가 versioned 서울 수집 envelope `seoul-wfs-acquisition-envelope/1` = WGS84 `[126.7, 37.4, 127.3, 37.72]`로 **paginated WFS BBOX 요청 한 번**을 실행한다. 이 값은 비교권역이 아니라 서울 법정동을 수집하기 위한 운반 범위다.
- 요청은 `SERVICE=WFS`, `REQUEST=GetFeature`, `TYPENAME=lt_c_ademd_info`, `VERSION=2.0.0`, `COUNT=1000`, `STARTINDEX=received`, `SRSNAME=EPSG:4326`, `OUTPUT=application/json`, `EXCEPTIONS=text/xml`로 고정한다. 현재 `StreamFeaturesAsync`의 `numberMatched/numberReturned`, duplicate, early termination과 max-page 검사를 재사용한다.
- 모든 feature의 `emd_cd`를 canonicalize한 뒤 `ReferenceCohort.DistrictIds` membership으로 filter하고 ordinal 정렬한다. provider의 여분 ID는 진단 count로만 보존하고 cohort에 추가하지 않는다. 중복은 `ProviderSchemaInvalid`, 필수 ID 누락은 `CohortBoundaryIncomplete`/`COHORT_BOUNDARY_INCOMPLETE`로 fail-closed한다. 중구 golden은 정확히 **74/74**여야 한다.
- post-filter cache preimage는 `cohort-boundary-cache/1|seoul-wfs-acquisition-envelope/1|126.7,37.4,127.3,37.72|{ReferenceCohort.Sha256}`이며 UTF-8 SHA-256한다. 중구 golden cache ID는 `8a7b4997f1c842bab5362ce705754b1b91f91865ea742094440582c3b28e0d08`이다. 요청·page·filter·failure oracle은 [cohort boundary cases fixture](fixtures/vworld-cohort-boundary-cases-v1.json)에 고정한다.
- BBOX request shape와 pagination parameter는 기존 secret-free 실제 [full WFS capture](fixtures/vworld-legal-district-boundaries-city-hall.json)가 입증한다. 서울 envelope 자체의 실제 74/74 수집은 Stage 1의 **구현 전 acquisition gate**다. 정확한 parameter로 수동 캡처한 page count·canonical ID summary를 `vworld-seoul-boundary-live-v1.json`으로 sanitize해 추가하고 74/74가 아니면 code를 작성하지 않는다.
- solver의 `DEFAULT_RADIUS_KM=15`와 `VworldApiParser`의 5km overload는 모두 legacy boundary-query 기본값이다. 둘 다 cohort를 정의·filter·fingerprint하거나 멤버를 조용히 잘라내는 데 사용하지 않는다.
- 향후 도로·시설·건축물 같은 실제 인접 요소를 조사할 거리 범위는 별도 `ProximityScope` capability로 다룬다. 통계 비교권역과 물리적 주변 범위를 같은 숫자로 합치지 않는다.
- section마다 결측을 제거해 비교 집합을 몰래 바꾸지 않는다. 공통 cohort identity는 유지하고 `FiniteCount`와 `MissingCount`를 section별로 밝힌다.
- 다른 snapshot이나 다른 cohort에서 계산한 rank/percentile을 직접 비교하지 않는다.

시군구 경계가 실제 설계 비교에 부적절하다는 사용자 근거가 쌓이면 기존 결과를 조용히 바꾸지 않고 새 cohort policy version을 추가한다.

## 4. 상대 위치 계산 규칙

수치형 section은 원값과 단위가 주 결과이며 상대 위치는 보조 설명이다.

1. cohort 내 유한한 값만 계산에 참여한다. `NaN`, 무한대와 missing은 0으로 대체하지 않는다.
2. `DescendingRank`는 값이 큰 순서의 competition rank다. 동률은 같은 최소 rank를 받고 다음 rank를 건너뛴다. 예: `12, 10, 8, 8, 5` → `1, 2, 3, 3, 5`.
3. rank는 좋고 나쁨이 아니라 **숫자의 크기 순서**다. 필드명과 UI에 `higher-value rank` 의미를 표시한다.
4. `Percentile`은 midrank empirical percentile로 계산한다.

   ```text
   100 × (count(value < site) + 0.5 × count(value = site)) / FiniteCount
   ```

5. `FiniteCount < 5`이면 percentile을 제공하지 않고 `COHORT_TOO_SMALL`을 기록한다. raw value, rank, min/median/max는 계속 표시한다.
6. site 값이 missing이면 rank, percentile과 summary를 만들지 않고 `SITE_VALUE_MISSING`을 기록한다.
7. median은 정렬 후 홀수는 중앙값, 짝수는 두 중앙값의 산술평균이다.
8. 값의 단위 또는 metric contract가 다른 값은 같은 cohort 계산에 섞지 않는다.
9. `PlanningContext`는 category count와 비율을 그대로 제공한다. ordinal zoning score, rank와 percentile은 v1 brief에 포함하지 않는다.
10. 0–1 min-max 값은 시각화 내부 값일 뿐 export나 문장에서 적합도·품질 점수로 노출하지 않는다.
11. JSON 계산값은 `1e-9` 절대오차로 비교한다. UI는 원값의 의미 있는 단위를 보존하고 percentile은 소수 첫째 자리로 반올림하되 canonical JSON 숫자는 반올림하지 않는다.

이 규칙 묶음의 유일한 v1 `EvidencePolicy`는 `evidence-policy/1`이다. 필드는 `minimumPercentileCohort=5`, `rankOrder=descending-value`, `tieMethod=competition`, `percentileMethod=empirical-midrank`, `categoricalRelativePosition=none`, `wordingPolicy=site-brief-wording/1`뿐이다. 별도 가중치·점수 abstraction은 만들지 않는다.

## 5. Site Brief 정보 구조

### 5.0 Credential과 source availability

- 현재 기본 pipeline이 요구하는 credential은 `VWorldKey`와 `SeoulKey`뿐이다.
- `DataGoKrKey`는 deprecated legacy credential이다. 기존 설정 loader와 명시적 data.go.kr 공시지가·용도지역 adapter 호환을 위해서만 읽으며 Site Brief 성공 조건이 아니다.
- 두 legacy adapter는 기본 dataset에 포함하지 않는다. 호출자가 명시적으로 선택했을 때만 legacy 경로를 사용한다.
- 같은 이름의 데이터가 VWorld에 있다는 이유만으로 source를 조용히 교체하지 않는다. endpoint/service ID, field semantics, 서울 coverage, pagination, rate/cache 정책과 fixture contract를 검증해야 새 provider로 인정한다.
- 검증된 snapshot/provider가 없으면 `LandValueContext`와 `PlanningContext`는 추정값 대신 `Unavailable`과 `NextEvidenceCheck`를 제공한다.

```text
SiteBrief
├── ContractVersion
├── SiteIdentity
├── ReferenceCohort
├── UrbanContext
│   ├── People
│   ├── SocioeconomicContext
│   ├── MovementActivity
│   ├── LandValueContext
│   └── PlanningContext
├── EvidenceConfidence[]
├── UnknownsFromCurrentEvidence[]
└── NextEvidenceChecks[]
```

### 5.1 공통 수치 profile row

| 필드 | 의미 |
|---|---|
| `Section` / `MetricId` | 다섯 section과 기존 snapshot layer ID |
| `RawValue` / `Unit` | 가장 먼저 표시할 관측값 |
| `RelativePosition` | rank, percentile, min/median/max, finite/missing count |
| `Observation` | period ID, 완결 여부, observed/expected count, missing IDs |
| `SampleCount` | provider가 제공한 경우만 표시. 없음을 0으로 쓰지 않음 |
| `AcquisitionOrigin` | 최초 취득이 network/embedded 중 무엇인지 |
| `DeliveryOrigin` | 현재 결과가 network/cache/embedded 중 무엇인지 |
| `RetrievedAt` / `CacheAge` | 데이터 취득 시점과 cache 경과 |
| `MappingAssumption` | 행정동→법정동 변환 등 적용된 가정 |
| `Limitations` | 이 값으로 추론하면 안 되는 내용 |

### 5.2 다섯 section 의미

| Section / Metric | 확인된 내용으로 쓸 수 있는 문장 | 해석할 때 반드시 붙일 주의 |
|---|---|---|
| `People` / `resident_pop` | “해당 법정동의 상주인구는 X명이며, 이 snapshot cohort에서 숫자 크기 순위는 R/N이다.” | 실제 대지 이용자나 프로그램 수요를 뜻하지 않는다. |
| `SocioeconomicContext` / `avg_income` | “월평균 추정 소득 참고 지표는 X원이다.” | 구매력 전체, 임대료 지불능력이나 상업성을 뜻하지 않는다. |
| `MovementActivity` / `transit` | “대중교통 일평균 승차 활동은 X명/일이다.” | 접근성, 환승 편의나 보행성을 뜻하지 않는다. |
| `LandValueContext` / `land_price` | “표준지 공시지가 표본 S개의 평균은 X원/㎡다.” | 시장가격, 매입비나 개발수익성을 뜻하지 않는다. |
| `PlanningContext` / `zoning` | “법정동 수준 용도지역 표본은 A n건, B m건으로 구성된다.” | 필지별 허용 용도, 건폐율·용적률이나 법적 적합성을 뜻하지 않는다. |

## 6. 문장 생성 규칙

각 section은 사용자에게 아래 네 문장으로 보인다. 영문 이름은 serialization field이며 UI/export label은 쉬운 한국어를 사용한다.

1. `ConfirmedFinding` / **확인된 내용** — snapshot 또는 address resolution에 직접 있는 값만 쓴다.
2. `InterpretationCaution` / **해석할 때 주의** — 참고 지표, 공간 단위, 기간, 데이터 대응 가정과 결측의 한계를 쓴다.
3. `UnknownFromCurrentEvidence` / **현재 데이터로 모르는 것** — 현재 자료로 답할 수 없는 질문을 명시한다.
4. `NextEvidenceCheck` / **추가로 확인할 자료** — 추가 데이터나 현장 확인 항목을 제시하되 답을 미리 추정하지 않는다.

### 자동·리뷰 가능한 금지 규칙

- 근거 없이 `적합`, `부적합`, `최적`, `추천`, `유망`, `열악`, `우수`, `수요가 높다`, `접근성이 좋다`, `사업성이 있다`, `개발 가능`을 생성하지 않는다.
- `상위 N%`라는 표현은 midrank percentile을 “좋음”으로 오해시키므로 쓰지 않는다. `값 기준 percentile P`라고 쓴다.
- 서로 다른 metric을 가중합하거나 종합 점수, 신호등 등급으로 축약하지 않는다.
- missing을 평균, 0, 인접 법정동 값으로 자동 대체하지 않는다.
- 사실 문장마다 최소한 `raw value + unit + 법정동 또는 cohort + period(존재할 때)`를 연결한다.
- 해석 주의와 현재 모르는 내용은 경고 아이콘만으로 숨기지 않고 export에도 구조화해 남긴다.

## 7. EvidenceConfidence는 점수가 아니다

`EvidenceConfidence`는 section별 진단 묶음이다. 단일 숫자 confidence나 A–F 등급으로 합성하지 않는다.

- `Coverage`: observed/expected와 missing IDs
- `Freshness`: retrieved time, delivery origin, cache age
- `Mapping`: 원래 공간 단위와 변환 가정
- `Sample`: 표본 수가 제공됐는지와 실제 수
- `Warnings`: incomplete period, schema 또는 provider warning
- `Limitations`: metric이 지지하지 않는 추론

## 8. 지원하지 않는 질문

아래 질문에는 답을 생성하지 않고 `UnknownFromCurrentEvidence` 또는 후속 capability로 돌린다.

- 이 대지에 어떤 프로그램이 가장 적합한가?
- 이 프로그램에 가장 좋은 후보지는 어디인가?
- 보행·대중교통 접근성이 좋은가?
- 실제 유동인구와 방문 수요가 많은가?
- 토지 매입가와 개발 수익성은 얼마인가?
- 이 필지에 어떤 용도와 규모를 합법적으로 지을 수 있는가?
- 경사, 일조, 바람, 홍수·환경 위험은 어떠한가?
- 주변 건축물 밀도, 도로·블록과 생활 서비스 접근은 어떠한가?
- 이 지역은 성장 중인가 쇠퇴 중인가?

## 9. Golden fixture 사용법

Golden fixture는 두 층으로 구분한다.

1. **주소 oracle**: 서울시청의 도로명 주소와 지번 주소가 같은 대상이라는 서울시 공개 근거와 2026-07-22에 실제 수집한 secret-free VWorld 2.0 road/parcel/WFS 응답을 사용한다.
2. **brief derivation oracle**: 개인정보·API key·실제 최신 통계와 무관한 합성 snapshot이다. 숫자가 현재 서울시청 주변의 실측값이라고 주장하지 않는다. 계산, 결측, 동률과 문장 경계를 고정하기 위한 fixture다.

Stage 1 이후 resolver fixture가 provider response를 저장하더라도 API key, 요청 URL의 secret, 개인 경로와 cache 파일 위치는 포함하지 않는다.

## 10. Stage 0 인수 조건

- 도로명·지번 입력의 VWorld 2.0 secret-free capture와 같은 법정동으로 수렴하는 테스트 oracle이 있다.
- 모든 출력 필드가 address resolution, snapshot 또는 versioned policy로 추적된다.
- 다섯 section이 각각 확인된 내용/해석할 때 주의/현재 데이터로 모르는 것/추가로 확인할 자료를 가진다.
- 동률, small cohort, missing, categorical 처리 규칙이 결정돼 있다.
- 프로그램 추천, 접근성·사업성·법적 적합성 추정과 종합 점수가 없다.
- 실제 provider 값과 합성 계약 값이 명확히 분리돼 있다.
