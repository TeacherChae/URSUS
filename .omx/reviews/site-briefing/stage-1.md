# Site Briefing Stage 1 Review Log

Stage: `1 — Address → ResolvedSite provenance`
Exit state: `Pre-implementation acquisition gate passed; production implementation pending`

## Acquisition gate attempt 1

Date: 2026-07-22
Result: `BLOCKED`

### Intended proof

- `seoul-wfs-acquisition-envelope/1` WGS84 `[126.7,37.4,127.3,37.72]`
- VWorld WFS `lt_c_ademd_info`, `COUNT=1000`, `STARTINDEX=received`
- complete pagination 후 canonical 중구 membership 74/74
- key·full URL·geometry를 제거한 `vworld-seoul-boundary-live-v1.json`

### Observed evidence

- `~/.config/URSUS/appsettings.json`에 VWorld key 항목은 있었다.
- provider는 WFS request를 `INVALID_KEY` / `등록되지 않은 인증키`로 거부했다.
- key 원문, full request URL, response credential은 log·fixture·repository에 기록하지 않았다.
- live fixture는 생성하지 않았다.

### Stop decision

Stage 0 contract의 fail-closed 규칙에 따라 radius fallback, 합성 결과 대체 또는 production implementation으로 우회하지 않는다. 유효한 VWorld key가 설정되면 동일 gate를 재시도한다.

## Credential direction update

Date: 2026-07-22
Decision: `DataGoKrKey deprecated`

- 현재 Setup/GH UX와 기본 Site Brief pipeline은 `VWorldKey`와 `SeoulKey`만 요구한다.
- 기존 설정과 명시적 data.go.kr 공시지가·용도지역 adapter 호환은 유지하지만 기본 dataset에서는 사용하지 않는다.
- VWorld의 유사 layer는 별도 provider contract와 live fixture 검증 전까지 기존 adapter의 대체재로 간주하지 않는다.
- 이 결정 당시의 Stage 1 blocker는 유효한 VWorld key 부재였다. 아래 재검증에서 key validity가 확인됐지만 74/74 acquisition은 별도 gate로 유지한다.

## Key revalidation and pre-review

Date: 2026-07-22
Result: `READY FOR ACQUISITION RETRY`

### Key evidence

- 로컬 설정의 VWorld key로 `lt_c_ademd_info`, `maxFeatures=1` 최소 WFS 요청을 다시 실행했다.
- HTTP 200과 실제 `FeatureCollection`/feature 반환을 확인했다.
- key 원문, full request URL과 geometry는 출력·저장하지 않았다.
- 이 결과는 credential validity만 증명하며 서울 envelope pagination이나 중구 74/74 완결을 증명하지 않는다.

### Independent pre-review

- 판정: `REQUEST CHANGES`, highest severity `MEDIUM`, acquisition retry는 finding 반영 후 승인.
- machine-local `.omx/state/`가 커밋되지 않도록 `.gitignore`에 추가했다.
- plan/journal/review state에서 key validity와 아직 실행 전인 74/74 acquisition을 분리해 기록했다.
- DataGoKr deprecation, legacy 호환, 기본 dataset 제외, secret/address leakage 0을 확인했다.
- 104/104 tests, Core/GH build와 `git diff --check` 통과 증거를 확인했다.

### Feedback-loop re-review

- 판정: `APPROVE` — blocker 0, major 0, medium 0.
- `.omx/state/` ignore와 세 문서의 상태 분리가 모두 반영됐음을 독립 reviewer가 확인했다.
- 정확한 서울 envelope/pagination/중구 74/74 live acquisition gate 실행을 승인했다.

## Acquisition gate attempt 2

Date: 2026-07-22
Result: `PASS`

### Verified evidence

- request: `seoul-wfs-acquisition-envelope/1`, WGS84 `[126.7,37.4,127.3,37.72]`
- pagination: 1 page, `numberMatched=758`, `numberReturned=758`, received 758/758
- canonical provider IDs: 758 unique, duplicate feature/canonical ID 0
- membership post-filter: 중구 required 74, matched 74, missing 0
- provider extras: 684 — 비교권역에 추가하지 않고 count만 진단으로 보존
- sanitized fixture: `docs/fixtures/vworld-seoul-boundary-live-v1.json`

### Sanitization

- key는 `<redacted>` marker만 남겼다.
- full request URL, geometry, exact address와 machine-local path는 저장하지 않았다.
- page별 response SHA-256과 canonical ID summary만 보존했다.

### Gate decision

Acquisition contract가 요구한 완결 pagination과 중구 74/74를 충족했다. Stage 1 production contract-lock tests와 구현을 시작할 수 있으나, Stage 1 자체는 아직 닫히지 않았다.

## Post-acquisition feedback loop

Date: 2026-07-22

### Review 1

- 판정: `REQUEST CHANGES`, highest severity `MEDIUM`.
- live fixture 내용은 758/758·74/74와 sanitization 계약에 맞지만 최초 회귀 테스트가 fixture의 선언값을 과도하게 신뢰했다.

### Feedback incorporation

- exact 1 page와 758/758, page start/matched/returned/feature count를 고정했다.
- page/provider canonical ID의 8자리 형식·정렬·중복·동일성을 재계산한다.
- embedded mapping에서 root를 제거해 서울 467개와 중구 74개를 독립 재생성하고 required/matched/missing/extras를 비교한다.
- response SHA-256 형식과 full URL, geometry/coordinates/features, golden exact address, Linux/Windows user path 부재를 artifact 전체에서 검사한다.

### Final re-review

- 판정: `APPROVE` — blocker 0, major 0, medium 0.
- reviewer의 non-blocking LOW 제안까지 반영해 `pages` 실제 배열 길이도 정확히 1로 고정했다.
- 최종 자동 검증은 105/105 tests, Core/GH build, package contract와 secret scan이다.

## Gate 1 — Pure contracts feedback loop

Date: 2026-07-23
Result: `APPROVE`

### Pre-implementation review

- 최초 판정은 `REQUEST CHANGES` — HIGH 1, MEDIUM 5, LOW 1이었다.
- Road/Parcel 모두 성공한 뒤 Boundary가 실패할 때 두 candidate를 보존할 수 없던 결과 행렬 모순을 `0..2`와 Boundary-only 제약으로 수정했다.
- Gate 1~4 의존성, 원인 candidate representative, WGS84/150m/name 비교, typed cache 정책, raw WFS name projection과 whitespace provenance 결정을 계약에 추가했다.

### Implementation review 1

- 판정: `REQUEST CHANGES` — HIGH 1, MEDIUM 2.
- 두 candidate Road/Parcel order, alternative→candidate 연결, reason candidate representative와 전체 fixture oracle 검증을 추가했다.
- Road/Parcel/DualEquivalent identity, mode별 legal-name field와 `VWorld address/2.0` 계약을 고정했다.

### Implementation review 2

- 판정: `REQUEST CHANGES` — HIGH 1, MEDIUM 4, LOW 1.
- `RepresentativeLocation.CandidateKind`, 동일 verbatim input provenance, disagreement canonical code distinct와 leaf enum/string validation을 추가했다.
- fixture를 20 mapped cases와 19 constructor rejection으로 확장하고 전체 public payload를 검증했다.

### Final re-review

- 판정: `APPROVE` — CRITICAL/HIGH/MEDIUM/LOW 모두 0.
- 서울 법정동 catalog 467, 중구 74, cohort SHA-256, exact input/semantic fingerprint와 immutable ownership을 확인했다.
- 검증: 118/118 tests, Release build 0 errors, `git diff --check` 통과.
- 기존 RhinoCommon `NU1701` warning만 비차단 항목으로 남았다.

## Gate 2 — Provider projection and cache feedback loop

Date: 2026-07-23
Result: `APPROVE`

### Implementation review 1

- 판정: `REQUEST CHANGES` — HIGH 1, MEDIUM 3.
- 기존 GeoJSON parser가 invalid hole/part를 제거하고 open ring을 자동 폐합한 뒤 warning만 남겨, 손실된 법정동 topology가 30일 성공 cache에 들어갈 수 있었다.
- Address OK의 service name/operation과 EPSG:4326 계약, malformed ERROR의 required code 검증이 부족했다.
- 실제 parcel fixture, cohort oracle 직접 사용, boundary cache TTL/ForceRefresh/corrupt, caller cancellation, raw name round-trip과 strict malformed topology 검증 공백이 있었다.

### Feedback incorporation

- Polygon/MultiPolygon의 모든 part/ring/coordinate를 선검증하고 explicit closure를 요구했다. 기존 parser가 warning을 하나라도 반환하면 성공하지 않고 `ProviderSchemaInvalid`로 닫는다.
- Address service name/version/operation, echoed input mode/query와 result CRS를 exact contract로 검증한다. ERROR code 누락은 schema failure이며 XML ExceptionReport만 명시적으로 no-code reported failure로 매핑한다.
- 실제 parcel capture와 `vworld-cohort-boundary-cases-v1.json`을 oracle로 사용했다.
- address Success 30일/NotFound 24시간, boundary 30일, ForceRefresh, logical corrupt repair, failure/cancellation non-cache와 `full_nm`/`emd_kor_nm` cache round-trip을 잠갔다.
- open ring, malformed hole, partially invalid MultiPolygon을 모두 fail-closed하고 cache를 만들지 않는다.

### Re-review and LOW closure

- 재리뷰 판정: `APPROVE` — CRITICAL/HIGH/MEDIUM 0, LOW 1.
- 남은 LOW는 WFS `features.OfType<JsonObject>()`가 비-object를 조용히 제거할 수 있다는 점이었다.
- raw `features.Count == numberReturned`와 모든 node의 object shape를 먼저 검사하도록 바꾸고 string feature fixture가 `ProviderSchemaInvalid`와 non-cache가 됨을 고정했다.
- 최종 판정: `APPROVE` — CRITICAL/HIGH/MEDIUM/LOW 모두 0.

### Verification

- 129/129 tests passed.
- Core Release 및 GH Release build 0 errors. 기존 RhinoCommon `NU1701`과 Linux의 Windows-only `CA1416` warning만 남았다.
- `git diff --check` clean, key·정확한 주소의 exception/cache filename 유출 없음.
