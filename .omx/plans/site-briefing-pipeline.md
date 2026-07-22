# URSUS Address-to-Urban-Context Brief 실행 계획

상태: `Active Goal Plan`
결정 기준: `docs/product-journal.md`의 2026-07-22 Site Briefing 및 Address-to-Urban-Context 결정
목표: 사용자의 주소를 위치·법정동·비교권역으로 해석하고, 현재 검증된 데이터로 다섯 도시 맥락과 신뢰도를 설명하는 Site Brief pipeline을 완성한다.

## 1. 제품 약속과 완료 상태

> 주소를 입력하면 해당 대지의 위치를 확인하고, 주변의 인구·사회경제·이동 활동·토지가치·토지이용 맥락을 출처와 데이터 품질이 명확한 Site Brief로 정리한다.

다음 흐름이 Windows Rhino 8에서 재현 가능하게 동작하면 목표가 완료된다.

```text
User Address
  → Address Resolution
  → ResolvedSite + Reference Cohort
  → AnalysisSnapshot
  → Urban Context Profiles
  → SiteBrief
  → Grasshopper Inspector
  → secret-free Export Bundle
```

사용자는 결과만 보고 다음을 답할 수 있어야 한다.

- 입력 주소가 어느 위치와 법정동으로 해석됐는가?
- 무엇을 reference cohort로 비교했는가?
- 사람·사회경제·이동 활동·토지가치·토지이용 맥락은 어떠한가?
- 각 근거의 원값, 단위, 상대 위치와 관측 기간은 무엇인가?
- coverage, sample, cache와 mapping 품질은 충분한가?
- 현재 데이터로 말할 수 없는 것은 무엇이며 다음에 무엇을 조사해야 하는가?

## 2. v1 정보 구조와 의미 경계

```text
SiteBrief
├── SiteIdentity
│   ├── InputAddress / NormalizedAddress / AddressKind
│   ├── Coordinates / CRS
│   ├── LegalDistrictCode / LegalDistrictName
│   ├── ResolverSource / ResolvedAt / ResolutionWarnings
│   └── ReferenceCohort / CohortFingerprint
├── UrbanContext
│   ├── People
│   ├── SocioeconomicContext
│   ├── MovementActivity
│   ├── LandValueContext
│   └── PlanningContext
├── EvidenceConfidence
└── UnknownsFromCurrentEvidence / NextEvidenceChecks
```

### 다섯 context section

| Section | Source | 허용되는 설명 | 금지되는 과장 |
|---|---|---|---|
| `People` | 상주인구 | 상주인구 원값과 cohort 내 상대 위치 | 실제 이용자·프로그램 수요 |
| `SocioeconomicContext` | 월평균 추정 소득 | 사회경제적 proxy와 상대 위치 | 구매력 전체·상업성·임대료 지불능력 |
| `MovementActivity` | 대중교통 일평균 승차 | 대중교통 이용 활동 신호 | 접근성·환승 편의·보행성 |
| `LandValueContext` | 공시지가 평균·표본 | 공시지가 기반 토지가치 맥락 | 시장가격·매입비·개발수익성 |
| `PlanningContext` | 용도지역 histogram | 법정동 수준 토지이용 구성 | 필지별 허용 용도·건폐율·용적률·법적 적합성 |

모든 section에는 기간, coverage, missing, sample, acquisition/delivery origin, cache age, mapping assumption과 limitation을 연결한다.

### 상대 위치 표현

- 원값과 단위가 항상 중심이다.
- `rank / cohort count`, percentile과 `min / median / max`를 보조로 제공한다.
- 비교 cohort는 대상 법정동과 같은 시군구의 versioned 법정동 membership 전체다. legacy 15km radius는 cohort에 사용하지 않는다.
- cohort identity와 finite/missing count를 함께 저장하고, 값이 없는 member를 비교권역에서 제거하지 않는다.
- 향후 실제 인접 시설을 조사할 거리 범위는 별도 `ProximityScope`로 두며 통계 비교권역과 섞지 않는다.
- 작은 cohort와 동률 처리 규칙은 Stage 0에서 고정한다.
- min-max 0–1 값은 내부 시각화에만 사용할 수 있으며 적합도·품질 점수로 표시하지 않는다.

## 3. 범위 고정

### 포함

- 도로명·지번 주소 입력과 fail-closed resolution
- versioned `ResolvedSite`, `ReferenceCohort`, `EvidencePolicy`, `SiteBrief` 계약
- 기존 `AnalysisSnapshot`에서 network 0으로 파생되는 profile
- 수치형 raw/relative evidence와 범주형 composition
- provenance, confidence, warning, failure와 현재 데이터로 모르는 내용
- 별도 Grasshopper Inspector
- deterministic JSON/CSV/provenance export bundle
- 회귀 테스트, Windows/Rhino 검증과 학생·설계사무소 관찰

### 제외

- Site → Program 추천 또는 program ranking
- Program → Site screening과 UAM recipe 실행
- 종합 적합도 점수와 암묵적 균등 가중 overlay
- 전체 AnalysisRecipe/Scenario 비교와 LLM
- 신규 provider·데이터셋 및 전국 coverage 확대
- 지형·환경·도시형태·생활서비스·시계열을 현재 데이터로 추정
- parcel 수준 건축 규제·buildability와 사업성 판정
- cloud, 계정, telemetry와 collaboration

범위 밖 요구는 구현하지 않고 `UnknownFromCurrentEvidence` 또는 journal의 후속 capability로 기록한다.

### Credential 경계

- 현재 Setup/GH UX와 기본 pipeline은 `VWorldKey`와 `SeoulKey`만 요구한다.
- `DataGoKrKey`는 deprecated이며 신규 UX에서 제거한다. 기존 설정 loader와 명시적 data.go.kr 공시지가·용도지역 adapter 호환 surface만 유지한다.
- VWorld에 유사 layer가 있다는 사실만으로 기존 adapter를 교체하지 않는다. endpoint/service ID, 의미, coverage, pagination, rate/cache와 fixture contract를 별도 검증한다.
- 검증 전에는 `LandValueContext`와 `PlanningContext`를 `Unavailable`로 표시하며, DataGoKr credential 부재를 pipeline 실패로 취급하지 않는다.

## 4. 사업 검증 가설

- 첫 검증 사용자는 건축 설계 학생이다.
- 장기 지불 고객 후보는 설계사무소, 도시설계·리서치 조직과 초기 사업기획팀이다.
- 수익 가치는 데이터 접근 자체가 아니라 반복 가능한 Site Brief, 검증된 descriptor, 보고서 workflow와 업데이트 신뢰성이다.
- 초기 사업 지표는 대지 리서치 시간, 근거 정리 시간, 산출물 실제 사용, 반복 사용과 출처 오류 감소다.
- Site Briefing은 상업 제품 전체가 아니라 설계 초기 리서치를 evidence workflow로 표준화하는 첫 wedge다.

## 5. 공통 feedback loop

각 단계는 아래 순서를 완료해야 닫힌다.

1. **Contract lock** — 구현 전에 해당 단계의 인수 조건과 회귀 테스트를 고정한다.
2. **Small implementation** — 현재 패턴을 재사용하고 최소 diff로 구현한다.
3. **Verification** — 단계 테스트와 전체 tests/build/static 검사를 실행한다.
4. **Independent review** — 지정 reviewer가 blocker/major/minor와 검증 공백을 기록한다.
5. **User review packet** — 변경 파일, 사용 예, 테스트 증거, reviewer 판정, 남은 결정과 다음 단계 영향을 제시한다.
6. **Feedback incorporation** — 사용자 피드백을 같은 단계에 반영하고 재검증한다.
7. **Gate close** — blocker/major 0, 테스트 통과, 사용자 피드백 반영 기록 후 다음 단계로 이동한다.

리뷰 기록은 `.omx/reviews/site-briefing/stage-{N}.md`에 남긴다. 사용자 피드백 전에는 다음 단계 구현을 시작하지 않는다.

## 6. 단계별 계획

### Stage 0 — 제품 계약과 golden address/brief

**진행 상태:** `Approved and closed` — Review 8 blocker 0, major 0, minor 0; 2026-07-22 사용자 재승인

**목적:** 구현 전에 주소 입력, 다섯 context section과 해석 금지선을 고정한다.

**작업**

- 도로명 주소 하나와 지번 주소 하나를 secret-free golden case로 선정한다.
- 각 주소의 expected normalized address, 좌표, 법정동과 reference cohort를 기록한다.
- 임의 radius가 아니라 동일 시군구의 versioned 법정동 membership을 reference cohort로 고정한다.
- 다섯 section별 확인된 내용/해석할 때 주의/현재 데이터로 모르는 것/추가로 확인할 자료 예시를 작성한다.
- rank, percentile, 동률, small cohort와 missing 정책을 결정한다.
- 위 표의 “금지되는 과장”을 자동/리뷰 가능한 문장 규칙으로 바꾼다.
- Site Brief profile table과 상세 provenance 예시를 만든다.
- 기본 full-site export와 선택적 anonymized export의 공개 범위를 결정한다.

**산출물**

- Address-to-Urban-Context contract 문서
- golden address → expected SiteBrief fixture 초안
- v1 field/versioning/wording rules
- unsupported questions 목록

**검증**

- 모든 출력이 주소 resolution, snapshot 또는 명시 policy로 추적 가능하다.
- generic layer 나열이 아니라 다섯 section으로 읽힌다.
- golden cohort가 동일 시군구 membership 전체를 포함하고 section missing count와 fingerprint가 재계산된다.
- 사용자에게 영문 evidence jargon을 노출하지 않고 네 가지 쉬운 한국어 label로 읽힌다.
- 프로그램 추천, 접근성·사업성·법적 적합성 추정과 종합 점수가 없다.

**독립 리뷰:** `architect` 또는 `critic` — 제품 경계, 숨은 가치판단, 상업적 wedge와 과도한 추상화 검토.

**Exit gate:** contract blocker/major 0, golden brief 승인, 사용자 피드백 반영.

---

### Stage 1 — Address → ResolvedSite provenance

**진행 상태:** `Acquisition gate passed; implementation pending` — 2026-07-22 독립 사전 리뷰와 재리뷰를 통과한 뒤 서울 고정 envelope를 실제 조회했다. pagination은 1 page, 758/758로 완결됐고 중구 cohort는 74/74였다. sanitized evidence는 `docs/fixtures/vworld-seoul-boundary-live-v1.json`에 있으며 production code는 아직 작성하지 않았다.

**목적:** 사용자가 입력한 주소와 선택된 법정동·비교권역 사이를 재현 가능하게 만든다.

**작업**

- production code 전 contract-lock으로 위 정확한 URL parameter로 VWorld를 1회 실제 조회한다. key·URL을 제거하고 page별 `numberMatched/numberReturned`, canonical ID와 중구 post-filter 74/74를 `docs/fixtures/vworld-seoul-boundary-live-v1.json`에 남긴다. 74/74가 아니면 구현으로 우회하지 않고 Stage 1을 실패하여 acquisition policy를 다시 리뷰한다.
- 상태·reason·candidate·representative location·district alternatives·typed provider failures를 담는 immutable `AddressResolutionResult`와 `ResolvedSite`를 구현한다.
- `AnalysisRequest.InputAddress1`에 verbatim 입력을 추가하되 기존 trimmed `Address1` semantics를 유지한다.
- 입력 주소, normalized address, address kind, candidate refined structure, WGS84/EPSG:5179 좌표, WFS raw/canonical 법정동 코드·이름, address+WFS source/time과 warning을 보존한다.
- `AnalysisSnapshot`에 optional immutable `ResolvedSite`와 `ReferenceCohort`를 backward-compatible constructor tail로 연결한다.
- 빈 주소, 0건, 복수 후보, 범위 밖, 불완전 응답을 기본 주소로 대체하지 않는다.
- road/parcel dual-request 결정표와 WFS topology point-in-polygon 규칙으로 주소 유형·법정동을 선택한다.
- `MappingLoader.Load().Values.SelectMany(...)`에서 canonical 8자리 법정동만 정규화·중복 제거·ordinal 정렬하는 versioned `SeoulLegalDistrictCatalog`를 새로 만든다. 기존 `SeoulExpectedDistricts` 426개 행정동 집합은 data-source completeness 용도로 유지한다.
- canonical 법정동 코드의 앞 5자리로 `SeoulLegalDistrictCatalog`를 filter해 `SameSigunguLegalDistricts` cohort를 만든다. 서울 467개, 중구 74개, 대상 `11140103` 포함을 invariant로 고정한다.
- `GetLegalDistrictsForCohortAsync` 하나만 추가해 `seoul-wfs-acquisition-envelope/1` WGS84 `[126.7,37.4,127.3,37.72]`을 `COUNT=1000`, `STARTINDEX=received`로 paginated WFS BBOX 조회한다. 반환 feature를 canonical membership으로 filter해 중구 74/74 exact-set을 검사한다.
- post-filter cache는 `cohort-boundary-cache/1|seoul-wfs-acquisition-envelope/1|126.7,37.4,127.3,37.72|{CohortSha256}`를 hash한다. missing은 `CohortBoundaryIncomplete` + Boundary failure `COHORT_BOUNDARY_INCOMPLETE`, duplicate·pagination 계약 위반은 기존 `ProviderSchemaInvalid`로 fail-closed한다.
- snapshot은 cohort member 전체를 identity로 유지하고 미수집 관측값을 missing으로 보존한다. 대상 법정동 membership 또는 cohort boundary 완결에 실패하면 성공 brief를 만들지 않는다.
- solver 15km와 parser 5km 기본값은 모두 legacy다. 둘 다 cohort의 정의·filter·fingerprint와 member boundary 수집에 사용하지 않는다.
- mode별 legal-name field와 EPSG:5179 `0.01m` edge tolerance를 계약대로 적용한다.
- provider cache는 normalized input/mode/version, `ResolutionId`는 timing을 제외한 resolved fields로 생성하며 alias cache는 v1에서 합치지 않는다.
- 진단·로그·cache filename에는 exact address를 쓰지 않는다. 사용자 실행 export는 전체 주소와 precise coordinate를 기본 포함하고, 명시적 `Anonymized` mode에서만 주소·precise coordinate를 제외한다.
- legacy sync overload만 default address를 유지하고 obsolete warning을 남기며 canonical async/GH flow는 빈 주소를 거부한다.

**테스트 우선 계약**

- 도로명/지번 normalization과 canonical 법정동
- 동일 resolved site의 deterministic identity
- 0건·복수 후보·provider error fail-closed
- result state invariant와 non-success mapped golden cases
- 동일 시군구 membership, cohort fingerprint와 legacy radius 비의존성
- 기존 행정동 set 426개와 새 법정동 catalog 467개의 용도 분리
- 중구 membership 74개, 대상 포함, ordinal 정렬, 중복·root 제거와 fingerprint `0a6144d5067f73cc9c4f510b76a11d08058015bd49101b60260bf091004f2fa8`
- membership-driven boundary 74/74 완결성과 15km/5km overload 비의존성
- `vworld-cohort-boundary-cases-v1.json`의 complete+extra, missing, duplicate와 pagination-early-termination 응답 oracle
- `CohortBoundaryIncomplete` public result cardinality와 wrong-mode/missing-failure constructor rejection
- address change가 stale snapshot으로 표시됨
- key/URL redaction과 address export policy
- 기존 snapshot constructor와 GH document compatibility

**독립 리뷰:** `api-reviewer` + `security-reviewer` — resolution contract, privacy, cache identity와 compatibility 검토.

**Exit gate:** golden address resolution 일치, 전체 회귀 통과, review blocker/major 0, 사용자 피드백 반영.

추가 acquisition gate: secret-free 실제 캡처가 서울 envelope의 완결 pagination과 중구 74/74를 입증하지 못하면 Stage 1은 닫히지 않는다.

---

### Stage 2 — Core SiteBrief model과 serialization

**목적:** UI·파일 형식에서 독립적인 canonical Site Brief 계약을 만든다.

**작업**

- `SiteIdentity`, five context profiles, `EvidenceConfidence`, `UnknownFromCurrentEvidence`를 최소 immutable types로 구현한다.
- generic source metadata와 사용자용 section 의미를 명시적으로 매핑한다.
- 방어적 복사, deterministic ordering, schema version과 canonical serialization을 보장한다.
- unknown field/version, NaN, empty cohort와 invalid resolved site 정책을 정의한다.
- snapshot을 복제하지 않고 brief에 필요한 projection과 provenance만 저장한다.

**테스트 우선 계약**

- collection immutability와 defensive copy
- JSON round-trip/canonical ordering
- unsupported version fail-closed
- numeric/categorical/missing 타입 보존
- section-source mapping의 stable IDs
- secret/user path leakage 0

**독립 리뷰:** `api-reviewer` — public contract, taxonomy, versioning과 serialization 검토.

**Exit gate:** 계약 테스트와 전체 회귀 통과, API review blocker/major 0, 사용자 피드백 반영.

---

### Stage 3 — Snapshot → Urban Context Profiles

**목적:** origin network 없이 다섯 맥락, confidence와 현재 데이터로 모르는 내용을 결정적으로 파생한다.

**작업**

- resolved legal district를 snapshot district index에 연결한다.
- layer별 target raw value와 unit, cohort rank/percentile/min/median/max를 계산한다.
- missing과 partial을 0으로 대치하지 않는다.
- observation, coverage, sample, origins, cache age와 mapping quality를 section에 연결한다.
- zoning histogram은 composition으로 유지하고 ordinal score를 기본 생성하지 않는다.
- warning/failure와 미지원 질문을 typed unknown/next evidence check로 변환한다.
- 생성 문장은 확인된 내용·해석할 때 주의·현재 데이터로 모르는 것·추가로 확인할 자료를 포함하고 설계·시장·법률 결론을 만들지 않는다.
- `DataGoKrKey`를 prerequisite로 검사하지 않는다. 검증된 land-value/zoning snapshot 또는 VWorld provider contract가 없으면 해당 section은 `Unavailable`로 유지한다.

**테스트 우선 계약**

- network/source dependency 0
- district order/culture에 무관한 동일 결과
- rank/percentile tie, small/empty cohort와 NaN/Infinity
- categorical composition/unknown 보존
- partial/stale/cache/mapping warning 손실 없음
- 다섯 section의 과장 금지 wording fixtures
- bounded loop cancellation

**독립 리뷰:** `code-reviewer` 또는 `quality-reviewer` — 통계 의미, proxy 한계, 결측과 provenance 손실 검토.

**Exit gate:** golden SiteBrief 일치, 전체 회귀 통과, review blocker/major 0, 사용자 피드백 반영.

---

### Stage 4 — Grasshopper Urban Context Inspector

**목적:** 주소에서 생성된 Site Brief를 설계자가 읽고 탐색할 수 있게 한다.

**작업**

- 기존 Solver port를 확장하지 않고 별도 Inspector component를 추가한다.
- `Snapshot/ResolvedSite`를 입력받고 `SiteBrief`, profile rows, warnings와 status를 출력한다.
- profile table을 중심에 두고 section별 상세 provenance drill-down을 제공한다.
- raw value/unit, rank/cohort, period, coverage, sample, origin과 **해석할 때 주의**를 같은 흐름으로 보여준다.
- missing/partial/stale/failure를 색상만이 아니라 text/status로 구분한다.
- document reopen과 Inspector 조작이 network 또는 CSV side effect를 일으키지 않게 한다.
- ComponentGuid와 port manifest를 회귀 테스트로 고정한다.

**검증**

- Linux 가능한 adapter/core 자동 테스트
- Windows Rhino 8 component load와 기존 sample open
- screenshot 기준 정보 위계, clipping, empty/error state와 긴 한글 문자열
- Inspector 조작 시 HTTP/CSV side effect 0

**독립 리뷰:** `ux-researcher`/`vision` + visual verdict — 정보 위계, 이해 가능성, 접근성과 GH 사용성 검토.

**Exit gate:** 자동 테스트, Windows screenshot/manual evidence, UX blocker/major 0, 사용자 피드백 반영.

---

### Stage 5 — Site Brief export bundle

**목적:** 발표·검토·재현에 사용할 portable 산출물을 만든다.

```text
site-brief/
├── site-brief.json
├── context-profiles.csv
├── provenance.csv
└── README.txt
```

**작업**

- canonical brief와 사람이 읽는 profiles/provenance를 같은 run identity로 묶는다.
- resolved address, cohort, EvidencePolicy와 schema version을 기록한다. 기본 bundle은 전체 대지 주소를 포함하고 `Anonymized` bundle만 주소와 precise coordinate를 제외한다.
- atomic write, overwrite와 failure safety를 기존 패턴으로 구현한다.
- API key, 사용자 경로, raw secret과 불필요한 provider payload를 제외한다.
- image/PDF/자동 caption은 사용자 검증 전에는 추가하지 않는다.

**테스트 우선 계약**

- deterministic content/stable headers
- JSON/CSV escaping, 한글, unit와 missing 표현
- partial write가 완성 bundle로 노출되지 않음
- secret/path leakage 0, 기본 full-site identity와 anonymized disclosure 정책
- 같은 resolved site/snapshot/policy에서 동일 semantic output

**독립 리뷰:** `security-reviewer` 또는 `api-reviewer` — address privacy, secret, provenance, portable contract와 failure safety 검토.

**Exit gate:** bundle round-trip/contract 통과, review blocker/major 0, 사용자 피드백 반영.

---

### Stage 6 — Windows/Rhino, 학생과 설계사무소 검증

**목적:** 기술 완성, 설명 가능성과 상업적 시간 절감 가설을 검증한다.

**기술 검증**

- installer/portable 신규 설치, 업그레이드, 제거
- 기존 `.gh` 문서와 신규 Inspector load
- Run/Cancel/document close/native disposal 회귀
- 도로명·지번 및 provider schema drift fixture
- Site Brief/Inspector/export 변경 시 network 0

**학생 검증 — 3–5명**

- 주소 입력부터 sample Site Brief 도달 시간
- 다섯 맥락을 원값·비교범위·source로 설명하는지
- proxy의 한계와 missing/partial/mapping warning을 인지하는지
- brief에서 확인된 내용, 해석 주의, 현재 모르는 것과 추가 확인 자료를 구분하는지
- 실제 과제 산출물에 어떤 output을 사용하는지

**설계사무소 검증 — 2–3팀**

- 기존 대지 리서치·근거 정리 시간과 URSUS 사용 시간을 비교한다.
- brief가 제안서, 내부 검토 또는 발주처 설명에 실제 사용되는지 본다.
- 반복 사용 의사, 필요한 신뢰 수준과 지불 단위를 관찰한다.
- 추가 요구는 현재 pipeline의 결함과 후속 capability를 구분해 기록한다.

**성공 가설**

- 새 사용자 80% 이상이 외부 도움 없이 15분 안에 sample brief에 도달한다.
- 사용자가 주요 context 두 가지 이상을 원값과 provenance로 설명한다.
- 결측 또는 low-quality evidence를 정상 근거로 오인하지 않는다.
- 동일 주소/resolution/snapshot/policy로 동일 brief를 재생성한다.
- 설계사무소에서 기존 대비 리서치·정리 시간이 측정 가능하게 줄고 실제 산출물에 사용된다.

**독립 리뷰:** `verifier` + `product-analyst` — 요구사항 추적, 실환경 증거, 시간 절감과 release readiness 검토.

**Exit gate:** blocker/major 0, 학생·사무소 관찰 결과 반영, 최종 사용자 승인.

## 7. 전체 검증 명령 기준선

각 구현 단계에서 최소 다음을 실행한다.

```bash
dotnet run --project src/URSUS.Tests/URSUS.Tests.csproj -c Release --no-restore
dotnet build src/URSUS/URSUS.csproj -c Release --no-restore
dotnet build src/URSUS.GH/URSUS.GH.csproj -c Release --no-restore
python installer/verify_package_contract.py
git diff --check
```

Windows Desktop SDK가 있는 환경에서는 solution/Setup/Inno build와 Rhino 8 수동 검증을 추가한다.

## 8. 진행 상태

| Stage | 상태 | 다음 gate |
|---|---|---|
| 0. Product contract/golden brief | Approved and closed | 완료 |
| 1. Address resolution provenance | In progress: acquisition gate passed (758/758, 중구 74/74) | contract-lock tests → production implementation → API/security review |
| 2. Core model/serialization | Pending | API 리뷰 |
| 3. Context profile derivation | Pending | 계산·품질 리뷰 |
| 4. GH Inspector | Pending | UX·visual 리뷰 |
| 5. Export bundle | Pending | 보안·계약 리뷰 |
| 6. Real-world/business validation | Pending | verifier·product review |

단계 상태와 reviewer 판정은 feedback 반영이 끝난 시점에만 갱신한다.
