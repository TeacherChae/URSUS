# Site Briefing Stage 0 Review Log

Stage: `0 — 제품 계약과 golden address/brief`
Exit state: `Approved and closed`

## Review 1 — Independent critic

Date: 2026-07-22
Verdict: `REJECT`

### Blockers

1. 주소 oracle이 provisional 좌표 하나만 가져 road/parcel VWorld 결과를 검증하지 못했다.
2. request mode, dual success/disagreement, ambiguity와 법정동 선택 알고리즘이 실행 가능하게 결정되지 않았다.
3. `ResolvedSite` 소유 위치, deterministic identity, alias cache와 exact-address 공개 정책이 미결정이었다.

### Majors

1. canonical schema와 golden fixture의 `ReferenceCohort`, `EvidenceConfidence`, provenance가 불일치했다.
2. 15km 파라미터를 자연스러운 생활권처럼 오해할 수 있었고 실제 square BBOX/center/bounds가 cohort fingerprint에 없었다.
3. fail-closed 계약과 legacy `DEFAULT_ADDRESS` 동작의 호환 정책이 없었다.

### Minor observations

- JSON, rank/percentile/median과 초기 cohort hash 계산은 통과했다.
- 상업적 wedge의 경계는 명확하지만 반복 사용·지불의향·시간 절감 threshold는 사용자 검증 전에 추가해야 한다.
- `EvidencePolicy`는 최소 필드로 제한하지 않으면 과도한 abstraction이 될 수 있다.

### Remediation

- VWorld address 2.0의 road/parcel 및 `lt_c_ademd_info` WFS 응답을 2026-07-22에 다시 수집하고 key를 제거한 fixture로 저장했다.
- user-facing address kind 선택을 없애고 dual-request decision table을 고정했다.
- WFS topology point-in-polygon으로 법정동을 선택하고 none/edge/multiple을 fail-closed 처리하도록 고정했다.
- `ResolvedSite`와 `ReferenceCohort`를 optional `AnalysisSnapshot` state로 고정했다.
- ResolutionId, provider cache, alias, exact-address log/export와 legacy default-address 정책을 고정했다.
- cohort를 `AddressCenteredBoundingBox`라고 명명하고 center/bounds/IDs 전체를 fingerprint에 포함했다.
- golden fixture에 full SiteIdentity variants, cohort, observation, coverage, origin/cache/mapping/limitation과 section별 `EvidenceConfidence[]`를 채웠다.
- `EvidencePolicy`를 계산/wording에 필요한 여섯 필드로 제한했다.

## Review 2 — Independent critic

Date: 2026-07-22
Verdict: `REJECT`

계산, geometry, BBOX/cohort와 legacy fail-closed 정책은 통과했다. 남은 지적은 다음과 같다.

- alternate mode `NOT_FOUND`가 raw response가 아니라 요약이어서 failure parser 계약을 고정하지 못했다.
- WFS raw 8자리 코드·full name과 golden의 10자리/short name, address+WFS source provenance가 불일치했다.
- candidate의 refined structure/resolved time이 빠지고 canonical SiteBrief 하나 대신 `siteIdentityByInput`이라는 fixture 전용 배열이 들어갔다.
- mode별 법정동명 field와 edge tolerance가 없었다.
- 현재 trimmed `AnalysisRequest.Address1`과 verbatim `InputAddress` 계약을 연결할 API 결정이 없었다.
- UTF-8 canonical JSON escaping profile이 불명확했다.

### Remediation 2

- alternate road/parcel 요청의 전체 secret-free `NOT_FOUND` envelope와 실제 missing/invalid-key `ERROR` envelope를 저장하고 raw status mapping을 고정했다.
- 법정동 provenance를 WFS `emd_cd`, `full_nm`, `emd_kor_nm`과 canonical code로 분리하고 address/WFS source를 함께 기록했다.
- 모든 candidate에 `InputAddress`, `RefinedStructure`를, 각 `ResolvedSite`에 `ResolvedAt`을 채웠다.
- brief derivation은 road golden 하나의 canonical `SiteIdentity`를 사용하고 parcel은 별도 resolution oracle로 분리했다.
- Road `level3`, Parcel `level4L`을 legal-name corroboration으로 고정했다.
- EPSG:5179 point-to-segment `0.01m` edge tolerance와 even-odd predicate를 고정했다.
- `AnalysisRequest.InputAddress1` verbatim property를 추가하고 기존 `Address1` trimmed semantics를 유지하기로 했다.
- UTF-8 no-BOM, no-indent, ordinal property order와 `UnsafeRelaxedJsonEscaping`을 fingerprint profile로 고정했다.

## Review 3 — Independent critic

Date: 2026-07-22
Verdict: `REJECT`

Review 1–2의 주소 oracle, response fingerprint, ResolutionId, WFS geometry, cohort와 상대 위치 계산은 모두 통과했다. 남은 major는 비성공 주소 해석의 public 결과가 문장으로만 정의되어 실제 구현·테스트가 서로 다른 payload를 만들 수 있다는 점 하나였다.

### Remediation 3

- immutable `AddressResolutionResult`의 status, reason, resolved state, candidate, representative point, district alternatives와 typed provider failure payload를 고정했다.
- 여섯 status별 필수/금지 payload와 snapshot 생성 여부를 불변조건으로 고정했다.
- 양 mode 미발견, captured provider errors, schema/transport failure, edge, multiple containment, district disagreement, 150m 초과, coverage 밖, legal-name mismatch와 빈 입력을 canonical mapped-result fixture로 추가했다.
- 다른 mode가 실패하면 성공 candidate를 진단용으로만 보존하고 Site Brief와 snapshot은 만들지 않도록 고정했다.

## Review 4 — Independent critic

Date: 2026-07-22
Verdict: `REJECT` — blockers 0, majors 4, minors 3

Review 3의 요구 시나리오는 모두 존재하고 기존 주소·geometry·계산 oracle도 재검증을 통과했다. 남은 문제는 다음 네 계약 공백이었다.

1. status 수준 불변조건만으로 reason별 cardinality, candidate 중복 소유와 invalid constructor를 닫지 못했다.
2. 합성 representative EPSG:5179 좌표 세 개가 WGS84 변환값을 소수점 반올림해 `0.035–1.398m` 어긋났다.
3. `DualEquivalent`와 request-cache hash preimage, Unicode/whitespace normalization, `/service/time` 제거 범위가 불충분했다.
4. `OutOfCoverage`가 한 표에서는 위치 brief를 허용하면서 다른 계약에서는 snapshot/Site Brief를 금지했다.

### Remediation 4

- 12개 reason 각각의 status와 모든 payload cardinality를 닫힌 행렬로 고정하고 representative 허용 reason을 세 개로 제한했다.
- collection non-null/defensive-copy, resolved candidate 동일 ordered immutable value, reason 우선순위와 invalid-state constructor rejection oracle을 추가했다.
- complete `Resolved/None` mapped output을 추가하고 세 representative EPSG:5179 좌표를 `URSUS.Epsg5179/1` round-trip 값으로 교체했다.
- Form C + .NET whitespace 규칙인 `query-normalizer/1`, byte-count-framed dual preimage와 exact request-cache preimage를 고정했다.
- single/dual/cache hash, NFD/whitespace variant와 `/service/time` 제거 범위를 계산 가능한 identity fixture로 추가했다.
- `OutOfCoverage`는 Site Brief를 만들지 않고 representative-location 진단만 반환한다고 단일화했다.
- plan 상태, journal 단계/결정과 socioeconomic proxy caution의 minor 불일치를 정리했다.

## Review 5 — Independent critic

Date: 2026-07-22
Verdict: `OKAY` — blockers 0, majors 0, minors 0

### Verified evidence

- 12개 reason matrix, payload cardinality, representative 제한, immutable candidate ownership, defensive-copy 요구, 우선순위, complete `Resolved/None`과 14개 constructor rejection case가 닫혀 있다.
- 다섯 WGS84/EPSG:5179 pair, 실제 WFS containment, 17.76m road/parcel 거리와 canonical 법정동 `11140103`이 재계산된다.
- single/dual/cache hash, NFC/whitespace normalization과 `/service/time` canonicalization이 fixture와 일치한다.
- `OutOfCoverage`는 Site Brief/snapshot을 만들지 않고 candidate와 representative-location 진단만 보존한다.
- golden rank, percentile, median, coverage, zoning share, BBOX/cohort hash와 wording boundary가 재계산된다.
- 8개 JSON fixture, provider fingerprint, secret redaction, local path와 `git diff --check`가 통과했다.

## User feedback

Date: 2026-07-22
Verdict: `CHANGES REQUESTED`

1. 기존 15km의 선정 근거가 없으므로 제품 비교권역으로 확정하지 않는다.
2. `fact/caution/gap/next investigation`은 사용자가 이해하기 어려우므로 쉬운 한국어로 바꾼다.
3. 대지분석 export에서 전체 주소를 기본적으로 숨기지 않는다.

### Feedback remediation

- legacy 15km BBOX를 reference cohort에서 제거하고 같은 시군구의 versioned 법정동 membership 전체로 교체했다.
- 통계 비교권역과 향후 물리적 주변 거리 `ProximityScope`를 분리했다.
- user-facing 네 label을 확인된 내용/해석할 때 주의/현재 데이터로 모르는 것/추가로 확인할 자료로 바꾸고 serialization field도 의미가 드러나게 변경했다.
- 기본 export에는 전체 주소·precise coordinate를 포함하고 사용자가 선택한 `Anonymized` export에서만 제외하도록 뒤집었다.
- golden cohort, missing/coverage, fingerprint와 canonical resolved-result fixture를 새 정책으로 재계산했다.

## Review 6 — Independent critic

Date: 2026-07-22
Verdict: `REJECT` — blockers 1, majors 1, minors 2

### Findings

1. 문서가 cohort source로 지정한 `SeoulExpectedDistricts.Ids`는 mapping key에서 만든 426개 행정동 집합이었다. 그대로 filter하면 중구 15개만 나오고 대상 법정동 `11140103`도 포함하지 않아 golden 74개를 만들 수 없다.
2. solver 15km는 배제했지만 parser 5km의 사용 금지와 74개 boundary 완결 수집 방법이 불분명했다.
3. golden 사용자 문장에 `evidence`, `fixture`, `proxy`, `coverage`, `mapping`, `histogram`같은 구현 용어가 남아 있었다.
4. contract 상태가 재리뷰 진행 상태와 맞지 않았다.

### Remediation 6

- mapping value의 법정동 코드로 만드는 별도 versioned `SeoulLegalDistrictCatalog`를 Stage 1 API로 고정했다. 기존 426개 행정동 set은 데이터 완결성 검사 용도로만 유지한다.
- 서울 467개, 중구 74개, 대상 포함, root·중복 제거, ordinal 정렬과 golden hash를 Stage 1 테스트 계약에 추가했다.
- solver 15km와 parser 5km 둘 다 cohort에 사용하지 않도록 고정했다. boundary는 membership ID로 수집하고 중구 74/74 exact-set이 아니면 `COHORT_BOUNDARY_INCOMPLETE`로 실패한다.
- golden 사용자 문장을 “현재 자료”, “예시 자료”, “참고 지표”, “수집 범위”, “대응 방식”, “구성 분포”로 바꿔 구현 용어를 제거했다.
- plan, contract와 review log의 상태를 재리뷰 대기로 동기화했다.

## Review 7 — Independent critic

Date: 2026-07-22
Verdict: `REJECT` — blockers 1, majors 1, minors 1

### Findings

1. boundary 수집이 개별 ID 조회와 paginated 시군구 조회 두 안으로 남아 있고, 현재 BBOX API에서 74개를 가져오는 정확한 method·parameter·cache·response oracle이 없었다.
2. `COHORT_BOUNDARY_INCOMPLETE`가 닫힌 `AddressResolutionResult` reason/status/payload 행렬에 없어 public failure로 반환할 수 없었다.
3. golden 문장 세 곳의 `구성 분포은`이 문법적으로 잘못되었다.

### Remediation 7

- `GetLegalDistrictsForCohortAsync` 하나만 구현하도록 선택했다. versioned 서울 WGS84 운반 envelope `[126.7,37.4,127.3,37.72]`로 현재 검증된 WFS BBOX + `COUNT/STARTINDEX` pagination을 실행한 후 membership으로 filter한다.
- 비교권역과 운반 envelope를 분리했고, request parameter, exact 74/74, provider extra·missing·duplicate·pagination failure와 post-filter cache SHA를 `vworld-cohort-boundary-cases-v1.json`에 고정했다.
- `CohortBoundaryIncomplete` reason을 `ProviderFailure` status와 Boundary failure `COHORT_BOUNDARY_INCOMPLETE`에 연결하고 result golden case와 invalid-constructor case를 추가했다.
- 세 문장 모두 `구성 분포는`으로 고쳤다.

## Review 8 — Independent critic

Date: 2026-07-22
Verdict: `OKAY` — blockers 0, majors 0, minors 0

### Verified evidence

- 단일 수집 경로가 fixed 서울 WGS84 envelope + `COUNT=1000`/`STARTINDEX=received` pagination + canonical membership filter + 74/74로 닫혀 있다. 15km와 5km fallback은 모두 금지됐다.
- 기존 실제 capture가 WFS request shape를, 합성 boundary case fixture가 complete+extra, missing, duplicate와 early pagination을 입증한다.
- Stage 1은 code 작성 전 secret-free 실제 74/74 capture를 필수로 하며 실패 시 policy 재리뷰로 중단한다.
- `CohortBoundaryIncomplete` public result와 wrong-mode/missing-failure constructor rejection이 닫힌 행렬과 일치한다.
- JSON, cohort/cache hash, 행정동 426개, 서울 법정동 467개, 중구 74개가 재계산되고 fixture 복사본이 일치한다.
- 한국어 문장과 기존 golden, identity, missing-data, standard/anonymized export 불변조건이 유지됐다.

사용자 재승인은 별도 gate다. 재승인 후에도 Stage 1의 실제 74/74 acquisition gate가 통과하지 못하면 구현으로 진행하지 않는다.

## User reapproval

Date: 2026-07-22
Verdict: `APPROVED`

Stage 0를 닫고 Stage 1의 pre-implementation 74/74 acquisition gate로 이동한다.
