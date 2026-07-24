# URSUS Product Journal

> 제품 아이디어, 임시 가설, PRD 초안, 결정과 검증 결과를 날짜별로 쌓는 living document
> 마지막 갱신: 2026-07-24 / 현재 제품 버전: 0.3.0

## 이 문서를 쓰는 방법

이 문서는 완결된 명세서가 아니다. 아직 거친 생각도 버리지 않고 기록하되, 시간이 지나도 무엇이 사실이고 무엇이 가설인지 구분하기 위한 작업장이다.

- 새 논의는 `YYYY-MM-DD — 제목`으로 추가한다.
- 각 항목은 가능한 한 `관찰 / 생각 / 결정 / 검증 / 남은 질문`을 구분한다.
- 확정되지 않은 문장은 **가설** 또는 **아이디어**라고 표시한다.
- 구현한 사실은 코드·테스트·사용자 검증 중 무엇으로 확인했는지 적는다.
- 오래된 생각은 삭제해서 역사를 바꾸지 말고 `폐기`, `대체`, `완료` 상태와 이유를 남긴다.
- 사용자용 설치·API·데이터 해석 문서는 이 파일에 복제하지 않고 해당 문서로 연결한다.

### 상태 표기

| 상태 | 의미 |
|---|---|
| `아이디어` | 아직 문제와 가치가 검증되지 않은 메모 |
| `가설` | 검증 방법이 필요한 제품 판단 |
| `제안` | 구현 전에 합의가 필요한 방향 |
| `결정` | 현재 개발에서 따라야 하는 선택 |
| `완료` | 구현과 자동 검증이 끝난 항목 |
| `검증 필요` | 구현은 있으나 실제 사용자/환경 증거가 없는 항목 |
| `폐기` | 더 이상 따르지 않으며 이유가 기록된 항목 |

---

## 현재 한 장 요약

### 현재 제품 정의

URSUS는 Rhino/Grasshopper 안에서 공공 도시 데이터를 초기 설계 판단에 연결하는 **provenance-first 분석 workbench**다.

범용 GIS나 데이터 catalog를 복제하는 것이 목표가 아니다. URSUS가 제공해야 할 핵심 가치는 다음이다.

> 어떤 설계 판단에 어떤 데이터, 기간, 범위, 표본과 가정이 쓰였는지 다시 설명하고 재현할 수 있게 한다.

### 현재 강점

- source abstraction과 query-keyed cache
- 기간, coverage, 결측, API/cache origin, 표본 수 provenance
- 불완전한 pagination과 손상된 cache의 fail-closed 처리
- topology-aware choropleth/extrusion과 선택적 IDW
- native 공간 단위와 provider provenance를 보존하는 exact 통계↔Geometry 전처리 계약
- 명시적 Run/Cancel, generation 격리와 snapshot 기반 재계산
- 가중치 변경 시 네트워크 재호출 없는 derived analysis
- bounded HTTP, retry, parsing, aggregation, mesh와 visual cache
- Setup/Inno/portable/CI가 공유하는 검증 가능한 배포 계약

### 현재 가장 큰 빈칸

- 실제 Windows Rhino 8 설치·업그레이드·제거·기존 문서 회귀 증거
- 실제 사용자가 결과를 해석하는 Quality/Scenario Inspector
- 목적, 방향성, 가중치, 정규화 범위, 결측 정책을 보존하는 analysis recipe
- 실제 provider 응답의 schema drift fixture
- categorical zoning을 숫자 점수로 왜곡하지 않는 표현
- “학생이 이 결과로 어떤 설계 결정을 하는가”에 대한 관찰 근거

### 제품 원칙 — 현재 결정

1. **Provenance before pixels** — 지도보다 먼저 기간, 단위, coverage, 원천/캐시, 표본과 mapping assumption을 보존한다.
2. **Explicit expensive work** — 네트워크와 대형 geometry는 명시 실행·취소가 있고, slider 조작은 snapshot derived 계산으로 끝낸다.
3. **Native spatial truth by default** — 각 통계는 원래 집계 단위와 동일한 공식 Geometry에 exact join한다. coarse 값을 finer Geometry로 복제하지 않으며 IDW는 사용자가 연속장 가정을 선택한 trend 표현이다.
4. **Bounded by contract** — HTTP, page, retry, cache, mesh, legend와 배포 payload에 명시적 상한/계약/진단을 둔다.
5. **No universal score** — overlay를 객관적 정답처럼 제시하지 않는다. 목적과 가정을 포함한 versioned recipe의 결과로 다룬다.
6. **Expand by validated capability** — 지역 선택 UI부터 열지 않는다. 데이터 범위와 의미가 검증된 capability만 노출한다.

### 현재 집중 목표

다음 제품 목표는 **주소 기반 Urban Context Site Briefing pipeline 구축 하나로 제한**한다.

- 사용자가 도로명·지번 주소를 입력하면 위치와 법정동을 확인하고, 주변 지역의 도시 조건을 원값, 품질, provenance와 함께 설명한다.
- v1의 설명 범위는 위치, 사람, 사회경제, 이동 활동, 토지가치와 토지이용 맥락이다.
- 프로그램 추천, 후보지 탐색, LLM assistant, 신규 데이터셋과 전국 확장은 이 목표가 검증될 때까지 뒤로 미룬다.
- 기본 결과는 종합 점수가 아니라 evidence profile, 결측·한계와 추가 조사 항목이다.
- 각 구현 단계는 자동 검증, 독립 리뷰, 사용자 피드백을 통과한 뒤에만 다음 단계로 이동한다.

---

## 2026-07-23 — 결정 및 완료: 통계와 Geometry의 exact spatial preprocessing

**상태:** `기반 구현 완료 / live provider acquisition 검증 필요`

### 관찰

기존 pipeline은 서울 행정동 통계를 법정동으로 복제하거나 균등 분배한 뒤 VWorld 법정동 경계에 표시했다. 이는 시각화를 가능하게 하는 legacy projection이지만, 원래 통계 집계 단위와 Geometry 단위가 동일하다는 뜻은 아니다.

또한 다음 가정은 성립하지 않는다.

- 둘 다 `250m`라고 표시되면 같은 격자다.
- 양쪽 컬럼 이름이 `grid_id`이면 같은 code namespace다.
- 서울시가 국가표준격자를 사용하면 NGII 파일의 ID와 자동으로 호환된다.
- VWorld에 Geometry가 없으면 해당 공간 단위를 지원할 수 없다.

같은 해상도라도 authority, 원점, 계층, code namespace 또는 version이 다르면 다른 공간 단위다. 반대로 source 컬럼 이름이 달라도 공식적으로 같은 namespace이거나 검증된 일대일 crosswalk가 있으면 결합할 수 있다.

### 결정

1. provider 원본 ID를 전역 공통 키로 직접 사용하지 않는다.
2. 공간 단위 identity는 `kind + authority + namespace + version + level/resolution`로 정의한다.
3. source ID는 evidence가 있는 shared-namespace identity 또는 공식 일대일 crosswalk를 거쳐 canonical unit ID가 된다.
4. 통계와 Geometry의 canonical schema와 ID 집합이 모두 정확히 같을 때만 visualization binding을 만든다.
5. duplicate, unmapped, missing, many-to-one, one-to-many과 schema mismatch는 fail-closed한다.
6. 각 layer는 자기 spatial schema와 Geometry를 소유하며 global 법정동 topology를 모든 layer에 강제하지 않는다.
7. provider 선택은 VWorld 우선 고정이 아니라 exact capability 탐색이다. VWorld에 없으면 같은 schema/semantics를 검증한 SGIS, 국토지리정보원, 서울시 등 다음 candidate를 탐색한다.
8. cross-provider 결합은 기본적으로 호환되지 않는다고 가정한다. 같은 provider가 통계와 격자 파일을 함께 배포하면 그 pair를 우선하고, 다른 provider Geometry는 schema/crosswalk fixture가 입증될 때만 사용한다.
9. 기존 행정동→법정동 변환은 삭제하지 않지만 `MappingQuality`를 가진 legacy estimated projection으로 남긴다.

### 구현

새 provider-neutral 경로는 다음 순서다.

```text
Raw statistical/geometry records
  → versioned SpatialUnitSchema
  → evidence-backed ExactSpatialIdProjection
  → CanonicalStatisticalLayer / CanonicalGeometryLayer
  → ExactSpatialJoiner
  → ExactSpatialLayerBinding
  → AnalysisSnapshot.SpatialLayers
  → layer-native choropleth
```

- 공간 identity와 provider dataset provenance를 immutable contract로 고정했다.
- raw source ID field를 보존하고 다른 컬럼 이름을 adapter 경계에서 흡수한다.
- official crosswalk는 source와 target 모두 유일한 일대일 관계만 허용한다.
- 통계에만 있는 ID와 Geometry에만 있는 ID를 분리 진단한다.
- statistic/Geometry capability를 여러 provider에서 조회하는 registry를 추가했다.
- exact schema/semantics/coverage candidate만 deterministic priority로 반환한다.
- `AnalysisSnapshot`에 optional per-layer exact binding을 추가하고 기존 constructor와 법정동 시각화 경로를 유지했다.
- native spatial layer의 암묵적 cross-unit overlay와 EPSG:5179 이전 시각화를 거부한다.
- cache identity에 provider schema/evidence와 양쪽 projection evidence를 포함한다.

상세 계약과 단계별 리뷰는 [Spatial Data Preprocessing Plan](../.omx/plans/spatial-preprocessing-pipeline.md) 및 `.omx/reviews/spatial-preprocessing/`에 기록했다.

### 검증

- 변경 전 142/142 tests
- Stage A 146/146
- Stage B 149/149
- Stage C 152/152
- Stage D 154/154
- Stage E 156/156
- 최종 fail-closed empty-set 회귀 추가 후 157/157
- 기존 snapshot/choropleth와 GH 호환 경로는 유지했다.

### 다음 acquisition gate

구조가 생겼다고 실제 dataset pair가 승인된 것은 아니다. 각 source는 다음을 별도로 통과해야 한다.

1. provider 공식 문서와 실제 파일의 ID field/schema/version 캡처
2. 통계 ID와 Geometry ID의 uniqueness 및 exact-set 검사
3. CRS, Geometry validity와 grid/경계 기준연도 검사
4. sanitized fixture와 content/schema fingerprint
5. license, 갱신주기, cache와 배포 크기 결정
6. 실패 시 같은 schema/semantics의 다음 provider candidate 검증

첫 실제 후보는 서울 250m 생활인구 통계와 서울시가 함께 배포하는 250m 격자 파일이다. NGII Geometry를 섞는 경로는 두 code namespace의 동일성 또는 공식 crosswalk가 입증된 뒤에만 연다. 집계구는 그 단위로만 제공되는 통계를 채택할 때 SGIS 기준연도 경계와 한 pair로 검증한다.

### 2026-07-24 검증 결과: 첫 acquisition gate HOLD

서울시가 함께 배포하는 실제 pair를 내려받아 `OA-22784 / 250_LOCAL_RESD_20260719.zip`, `서울시_250m격자.zip`, 2025.05 매뉴얼을 비교했다.

확인된 장점:

- 통계 `250M격자`와 Geometry `CELL_ID`가 8,567개 실제 ID를 공유한다.
- 10,125개 Geometry는 모두 유일하고 valid한 250m Polygon이며 면적은 62,500m²다.
- 투영 파라미터는 EPSG:5179와 같고, `CELL_X/CELL_Y`가 Polygon 중심과 정확히 일치한다.

그러나 gate는 닫지 않았다.

1. 통계의 실제 unique key는 `일자 + 시간 + 행정동코드 + 250M격자`다. 같은 시간·격자에 최대 4개의 행정동 row가 있다.
2. 매뉴얼은 이를 250m 격자 데이터라고 부르지만 행정동별 row를 한 격자 값으로 합치는 공식 규칙을 설명하지 않는다.
3. 일부 행정동 part는 3 이하 비식별 값 `*`이다. 이를 0으로 바꾸거나 숫자 part만 더하면 exact total을 발명하게 된다.
4. 통계 ID 하나가 Geometry에 없고, 매뉴얼의 10,021개와 실제 Shapefile 10,125개도 일치하지 않는다.
5. 서울시는 2026-07-31 이후 데이터 정의서·매뉴얼·활용 매뉴얼·격자 파일 보완을 공지했다.

따라서 “같은 provider와 같은 격자 ID”는 확인했지만 “한 격자당 정확한 단일 통계값”은 아직 입증하지 못했다. 2026-07-31 이후 artifact를 다시 캡처하고 cross-admin aggregation과 `*` semantics가 공식화되기 전에는 live adapter를 구현하지 않는다. 증거와 hash는 `docs/fixtures/seoul-250m-acquisition-gate-1.json`, 리뷰는 `.omx/reviews/spatial-preprocessing/acquisition-gate-1.md`에 고정했다.

---

## 2026-07-22 — 결정: Site Briefing pipeline을 먼저 완성한다

**상태:** `결정`

### 관찰

제품 방향은 두 흐름으로 나뉠 수 있다.

1. 프로그램이 고정되고 후보 대지를 비교하는 `Program → Site`
2. 대지가 고정되고 프로그램 가설을 탐색하는 `Site → Program`

그러나 현재 첫 사용자 가설은 대지를 지정받아 초기 분석을 수행하는 건축 설계 스튜디오 학생이고, 현재 엔진도 주소/BBOX에서 snapshot을 만드는 구조다. Site Briefing 없이 Site → Program을 먼저 열면 공공데이터의 관찰 사실과 프로그램 추천 사이에 숨은 가치판단이 생긴다. Program → Site를 먼저 열면 넓은 비교 cohort, parcel constraint와 검증된 전국 coverage가 필요해 현재 제품 범위를 크게 넓힌다.

Zoneomics 조사에서도 강한 제품은 데이터 종류를 늘리는 대신 zoning 조회, site selection, report와 API라는 명확한 workflow를 완성한다는 점을 확인했다. URSUS는 이를 복제하기보다, 규제 가능성보다 앞선 설계 초기 단계에서 **대지를 설명 가능한 evidence brief로 만드는 일**을 먼저 소유한다.

### 결정

- 다음 구현 목표는 `Site Briefing pipeline` 하나다.
- `Site → Program`은 Site Briefing이 사용자 검증을 통과한 뒤, 검증된 소수의 ProgramDescriptor를 비교하는 별도 단계로 검토한다.
- `Program → Site`와 UAM Site Screening은 후속 workflow로 유지하되 이번 목표에 포함하지 않는다.
- 현재 snapshot을 네트워크와 geometry truth의 기준으로 재사용하고, Site Brief는 snapshot에서 결정적으로 파생한다.
- 암묵적인 균등 가중 overlay를 Site Brief의 중심 결과로 사용하지 않는다.
- 기존 Solver 포트를 확장하기보다 별도 Inspector가 snapshot을 소비한다.
- 구현 단계마다 독립 reviewer를 배정하고, 검증 증거와 남은 질문을 사용자에게 제시해 피드백을 반영한 뒤 다음 단계로 진행한다.

### v1 pipeline 경계

```text
User Address
  → AddressResolutionResult (ResolvedSite + ReferenceCohort)
  → AnalysisSnapshot + EvidencePolicy
  → raw and relative evidence derivation
  → quality/provenance/unknown assembly
  → SiteBrief
  → Inspector + export bundle
```

`SiteBrief`는 최소한 다음을 포함한다.

- 대지와 reference cohort
- layer별 원값·단위와 cohort 내 상대적 위치
- 관측 기간, coverage, missing IDs와 sample count
- acquisition/delivery origin, cache age와 mapping assumption
- 범주형 composition과 숫자로 축약하지 않은 zoning context
- 확인된 내용, 해석할 때 주의, 현재 데이터로 모르는 것과 추가로 확인할 자료

### 이번 목표의 비목표

- 프로그램 자동 추천 또는 프로그램 적합도 순위
- 후보지 검색과 최적 입지 점수
- `AnalysisRecipe` 전체 시나리오/가중치 시스템
- LLM assistant 또는 자연어 recipe compiler
- 신규 provider·데이터셋 및 전국 coverage 확대
- 범용 GIS, cloud account와 collaboration

### 단계와 피드백 게이트

1. **Brief contract와 golden case** — 사용자 질문, 주소 결과 계약, 산출물과 해석 경계를 고정한다.
2. **Address resolution provenance** — 주소를 fail-closed `ResolvedSite`와 `ReferenceCohort`로 해석한다.
3. **Core model과 serialization** — versioned immutable contract와 round-trip을 구현한다.
4. **Snapshot → SiteBrief derivation** — network 0의 결정적 profile/quality/unknown 계산을 구현한다.
5. **Grasshopper Inspector** — 별도 컴포넌트로 사람이 읽을 수 있는 brief를 제공한다.
6. **Export bundle** — profile, provenance와 machine-readable brief를 함께 내보낸다.
7. **Windows/Rhino 및 학생 검증** — 실환경 안정성과 실제 설명 가능성을 관찰한다.

모든 단계는 `구현 → 자동 검증 → 독립 리뷰 → 사용자 리뷰 패킷 → 피드백 반영 → 재검증` 순서로 닫는다. 사용자 피드백 전에는 다음 단계 구현을 시작하지 않는다. 상세 계획은 [Site Briefing Pipeline Plan](../.omx/plans/site-briefing-pipeline.md)을 기준으로 한다.

### 완료 기준

- 사용자가 원값, 기간, coverage와 source를 보고 대지의 주요 조건을 설명할 수 있다.
- 같은 snapshot과 versioned policy에서 동일한 Site Brief가 재생성된다.
- 결측, stale cache, 낮은 coverage와 mapping assumption이 정상 값처럼 숨지 않는다.
- Site Brief 생성과 Inspector 변경은 origin network를 호출하지 않는다.
- 프로그램이나 종합 점수를 제안하지 않고, 확인된 내용·해석 주의·현재 모르는 것·추가 확인 자료를 구분한다.
- Windows Rhino 8에서 기존 GH 문서와 새 Inspector가 함께 동작한다.

### 후속 검증 질문

- ~~v1 SiteScope는 법정동 ID 선택으로 시작할지, Rhino point/boundary를 즉시 지원할지?~~
  `결정 완료`: 사용자는 도로명·지번 주소를 입력하고, pipeline이 좌표와 canonical 법정동을 resolve한다. Rhino point/boundary는 후속 입력 방식이다.
- ~~relative position은 percentile, range position 또는 둘 다 중 무엇이 학생에게 가장 잘 이해되는가?~~
  `결정 완료`: 원값·단위를 중심에 두고 descending-value rank, empirical midrank percentile과 min/median/max를 보조로 함께 제공한다.
- ~~첫 산출물은 Inspector tree, profile table와 export bundle 중 무엇이 실제 과제 workflow에서 중심인가?~~
  `결정 완료`: Grasshopper Inspector의 profile table을 1차 산출물로 두고 provenance drill-down과 export bundle은 같은 canonical brief의 다른 표현으로 둔다.

---

## 2026-07-22 — 결정 보강: Address-to-Urban-Context Brief

**상태:** `결정`

### 한 문장 제품 정의

> **URSUS는 주소를 입력하면 해당 대지의 위치를 확인하고, 주변의 인구·사회경제·이동 활동·토지가치·토지이용 맥락을 출처와 데이터 품질이 명확한 Site Brief로 정리하는 Rhino/Grasshopper 도구다.**

짧은 제품 범주는 **주소 기반 도시 맥락 브리핑 도구**, 내부 제품 흐름은 `Address-to-Urban-Context Brief`로 부른다.

### 주소가 제품 계약의 시작점인 이유

- 사용자는 법정동 ID를 알 필요가 없어야 한다.
- 도로명·지번 주소를 정규화하고 좌표와 canonical 법정동을 resolve한 근거가 결과에 남아야 한다.
- query fingerprint만으로는 주소 해석 과정을 복원할 수 없으므로 `ResolvedSite` provenance가 필요하다.
- 빈 주소, 복수 후보, 지원 범위 밖과 불확실한 해석은 기본 주소로 조용히 대체하지 않고 fail-closed 또는 명시적 선택 상태로 처리한다.
- 주소 이후의 Site Brief/Inspector 계산은 immutable snapshot에서 수행해 origin network 0을 유지한다.

```text
User Address
  → Address Resolution
  → ResolvedSite + Reference Cohort
  → AnalysisSnapshot
  → Urban Context Profiles
  → Site Brief
  → Inspector / Export Bundle
```

### v1이 설명하는 다섯 맥락

| Section | 현재 evidence | 말할 수 있는 것 | 말하면 안 되는 것 |
|---|---|---|---|
| `People` | 상주인구 | 법정동 상주인구와 비교권역 내 상대 위치 | 실제 대지 이용자·프로그램 수요 |
| `SocioeconomicContext` | 월평균 추정 소득 | 사회경제적 proxy와 상대 위치 | 구매력 전체·임대료 지불능력·상업성 |
| `MovementActivity` | 대중교통 일평균 승차 | 대중교통 이용 활동의 규모와 상대 위치 | 접근성·환승 편의·보행성 |
| `LandValueContext` | 표준지 공시지가 평균·표본 수 | 공시지가 기반 토지가치 맥락 | 시장가격·매입비·개발수익성 |
| `PlanningContext` | 용도지역 category histogram | 법정동 수준 토지이용 구성 | 필지별 허용 용도·건폐율·용적률·법적 적합성 |

`SiteIdentity`는 입력/정규화 주소, 주소 유형, 좌표, 법정동 코드·이름, resolver source/time과 reference cohort를 보존한다. `EvidenceConfidence`는 모든 section에 기간, coverage, missing, sample, acquisition/delivery origin, cache age, mapping assumption과 known limitation을 붙인다.

### v1이 설명하지 않는 갈래

- 지형·경사·일조·바람과 환경 위험
- 건축물 밀도, 도로·블록과 도시 형태
- 학교·병원·공원 등 생활 서비스 접근
- parcel 수준 건축 규제와 법적 buildability
- 시계열 성장·쇠퇴와 사업성

이 항목들은 현재 데이터로 설명할 수 없으며 Site Brief의 `UnknownsFromCurrentEvidence / NextEvidenceChecks`에 표시한다. 데이터셋 추가는 사용자 검증 후 별도 capability로 승인한다.

### Site Brief v1 정보 구조

```text
SiteBrief
├── SiteIdentity
├── UrbanContext
│   ├── People
│   ├── SocioeconomicContext
│   ├── MovementActivity
│   ├── LandValueContext
│   └── PlanningContext
├── EvidenceConfidence
└── UnknownsFromCurrentEvidence / NextEvidenceChecks
```

원값이 중심이고 `rank / cohort count`, percentile과 `min / median / max`를 보조로 사용한다. 0–1 min-max 값은 시각화 내부에서만 사용할 수 있으며 적합도나 품질 점수처럼 표시하지 않는다. 모든 상대 위치는 reference cohort identity와 함께 저장한다.

### Zoneomics와 비교해 선택한 사업 방향

Zoneomics와 URSUS는 `위치 입력 → 파편화된 공공정보 표준화 → 사람이 읽는 profile/report`라는 제품 구조가 같다. 차이는 Zoneomics가 parcel 규제와 거래 due diligence 위험을 줄이는 반면, URSUS는 설계 초기 도시 맥락 리서치와 근거 정리 시간을 줄인다는 점이다.

- 학생은 첫 UX와 설명 가능성을 검증하는 사용자다.
- 장기 지불 고객 가설은 설계사무소, 도시설계·리서치 조직과 초기 사업기획팀이다.
- 판매할 가치는 공공데이터 자체가 아니라 반복 가능한 Site Brief, 검증된 descriptor, 보고서 workflow와 업데이트 신뢰성이다.
- 상업적 성과는 “더 좋은 설계”가 아니라 대지 리서치 시간, 근거 정리 시간, 재작업과 출처 오류 감소로 측정한다.
- parcel 규제·후보지 lead generation으로 즉시 확장해 Zoneomics와 coverage 경쟁을 하지 않는다.

따라서 Site Briefing은 최종 사업 전체가 아니라, `설계 초기 리서치를 검증 가능한 evidence workflow로 표준화`하는 첫 제품 wedge다.

### 현재 우선순위

1. 주소 해석과 `ResolvedSite` provenance
2. 다섯 Urban Context section의 의미 계약과 golden brief
3. snapshot에서 network 0으로 profile과 confidence 파생
4. profile table 중심 Inspector
5. 동일 Site Brief의 portable export bundle
6. 학생 사용성 검증 후 설계사무소의 시간 절감·반복 사용 검증

### 사용자 피드백 반영: 비교권역, 설명 문구와 주소 export

**상태:** `결정 수정`

#### 비교권역

- 기존 `URSUSSolver.DEFAULT_RADIUS_KM=15`와 하위 `VworldApiParser`의 5km 기본값 모두에 사용자 연구나 도시분석 근거가 없다. 둘 다 legacy 경계 조회 기본값이지 Site Brief의 비교권역 정책이 아니다.
- Address-to-Urban-Context v1의 비교권역은 **대상 법정동과 같은 시군구의 법정동 전체**로 변경한다. “중구 내 법정동 중”처럼 사용자가 비교 범위를 설명할 수 있기 때문이다.
- 기존 `SeoulExpectedDistricts`는 426개 행정동 집합이므로 비교권역에 재사용하지 않는다. Stage 1에서 embedded mapping의 법정동 value를 정규화·중복 제거한 별도 `SeoulLegalDistrictCatalog`를 만든다. 예상 invariant는 서울 467개, 중구 74개다.
- boundary 수집은 현재 검증된 VWorld BBOX/pagination 요청 형태를 재사용한다. versioned 서울 운반 envelope로 한 번 수집한 뒤 cohort membership으로 filter하며, 이 envelope를 비교권역으로 해석하지 않는다. 중구 74/74가 아니면 성공 Site Brief를 만들지 않는다. 누락 관측값은 cohort에서 제거하지 않고 missing으로 남긴다.
- 통계의 상대 비교권역과 도로·시설·건물 같은 물리적 주변 조사 거리는 분리한다. 후자는 데이터가 추가될 때 별도 `ProximityScope`로 검증한다.

#### 사용자 설명 문구

`Fact / Caution / Gap / NextInvestigation`을 사용자에게 그대로 노출하지 않는다.

1. **확인된 내용**
2. **해석할 때 주의**
3. **현재 데이터로 모르는 것**
4. **추가로 확인할 자료**

serialization field도 각각 `ConfirmedFinding`, `InterpretationCaution`, `UnknownFromCurrentEvidence`, `NextEvidenceCheck`로 의미를 드러낸다.

#### 주소 export

- 사용자가 직접 실행한 기본 Site Brief export에는 전체 대지 주소와 precise coordinate를 포함한다.
- 로그, 예외와 cache filename에는 전체 주소를 남기지 않는다.
- 주소와 precise coordinate를 제외하고 법정동 수준으로 내보내는 `Anonymized` export를 사용자의 명시적 선택으로 제공한다.

### 사용자 피드백 반영: `DataGoKrKey` deprecation

**상태:** `결정 수정`

- 현재 제품 경로의 credential은 `VWorldKey`와 `SeoulKey`만 요구한다. Setup과 Grasshopper 설정 UI에서 `DataGoKrKey` 입력을 받지 않는다.
- 기존 `appsettings.json`/`.env`와 명시적으로 호출하는 data.go.kr 공시지가·용도지역 adapter를 깨뜨리지 않기 위해 loader와 public 이름은 deprecated compatibility surface로 남긴다.
- 위 두 legacy adapter는 기본 dataset에 포함하지 않으며, `DataGoKrKey`의 존재를 Site Brief 성공 조건으로 삼지 않는다.
- VWorld catalog에서 용도지역·지구 layer는 확인할 수 있지만, 기존 표준지 공시지가 adapter와 동일한 의미·coverage를 보장하는 대체 service는 아직 검증하지 않았다. 따라서 단순히 URL 또는 key만 바꾸는 migration은 하지 않는다.
- 향후 VWorld 대체는 endpoint/service ID, field semantics, 서울 coverage, pagination, rate/cache 정책과 sanitized live fixture를 별도 contract로 검증한 뒤 진행한다.
- 그 전까지 검증된 snapshot이 없는 `LandValueContext`와 `PlanningContext`는 값을 추정하지 않고 `Unavailable`과 다음 조사 항목을 반환한다.

참고: [VWorld 2D Data API catalog](https://www.vworld.kr/dev/v4dv_2ddataguide2_s001.do)

### 2026-07-22 진행 기록: Stage 1 acquisition gate 통과

**상태:** `검증 완료 — Stage 1 구현은 진행 중`

- 로컬 VWorld key를 최소 `lt_c_ademd_info` WFS 요청으로 다시 검증했고 HTTP 200과 실제 feature 반환을 확인했다.
- 독립 사전 리뷰의 medium finding 2건을 반영하고 재리뷰 `APPROVE`를 받은 뒤 서울 고정 envelope를 실제 조회했다.
- WFS pagination은 1 page, 758/758로 완결됐고 canonical ID 중복은 없었다. embedded 중구 membership을 post-filter한 결과 required 74, matched 74, missing 0이었다.
- key·full request URL·geometry·exact address·machine-local path는 저장하지 않고 page count, canonical ID, response hash와 74/74 결과만 sanitized fixture로 남겼다.
- 독립 사전 리뷰는 DataGoKr deprecation과 legacy 호환을 요구사항에 부합한다고 판단했다.
- 리뷰의 medium finding에 따라 machine-local `.omx/state/`를 git ignore하고, plan/review/journal에서 “key valid”와 “74/74 gate passed”를 분리했다.
- acquisition gate가 통과했으므로 다음 행동은 Stage 1 contract-lock tests를 먼저 추가하고 `AddressResolutionResult`/`ResolvedSite`/`ReferenceCohort` production 구현을 시작하는 것이다.

상세 구현 및 리뷰 순서는 [Site Briefing Pipeline Plan](../.omx/plans/site-briefing-pipeline.md)을 따른다.

---

## 2026-07-21 — vNext PRD 초안: 설명 가능한 초기 대지분석

**상태:** `제안`
**이 항목의 목적:** 다음 버전의 기능 목록보다 제품이 해결할 한 가지 일을 먼저 정의한다.

### 문제

현재 URSUS는 데이터를 안전하게 가져오고 공간적으로 표현할 수 있다. 하지만 사용자가 최종 overlay나 mesh를 보고 다음을 즉시 답하기는 어렵다.

- 왜 이 지역의 값이 높거나 낮은가?
- 어느 원천의 어느 기간 데이터인가?
- 표본과 coverage는 충분한가?
- 가중치 또는 정규화 범위를 바꾸면 결론이 유지되는가?
- 이 결과를 어떤 설계 결정에 써도 되는가?

데이터셋만 더 추가하면 이 문제는 커진다. 따라서 다음 버전의 중심은 “더 많은 데이터”가 아니라 **설명 가능한 시나리오와 결과 profile**이어야 한다.

### 첫 사용자 가설

**가설:** 첫 핵심 사용자는 건축 설계 스튜디오에서 초기 대지분석을 수행하는 학생이다.

이 사용자는 전문 GIS 분석을 만들기보다 다음을 원한다.

1. 설치 후 짧은 시간 안에 대지 주변 조건을 본다.
2. 소득·인구·교통·지가 등 서로 다른 지표를 같은 공간에서 비교한다.
3. 발표에서 “왜 이런 판단을 했는가”를 원값과 출처로 설명한다.
4. 가중치나 목적이 달라졌을 때 결과가 어떻게 달라지는지 본다.
5. 이미지와 수치, 사용한 조건을 과제 산출물로 남긴다.

이 가설은 사용자 관찰 전까지 확정하지 않는다.

### 핵심 Job to Be Done 후보

> 대지가 주어졌을 때 주변 도시 조건을 빠르게 이해하고, 설계 전략에 영향을 주는 근거와 불확실성을 설명 가능한 자료로 만든다.

후보지 순위나 범용 적합도 점수는 이 일보다 뒤에 둔다. 점수는 간단해 보이지만 목적과 가치판단을 숨길 위험이 크다.

### 제안하는 핵심 모델: `AnalysisRecipe`

```text
AnalysisRecipe
├── RecipeId / Version
├── Purpose
├── AnalysisBoundary / Cohort
├── ObservationPolicy
├── SelectedLayers
│   ├── LayerId
│   ├── Direction: Positive | Negative | ContextOnly
│   ├── Weight
│   └── RequiredQuality
├── NormalizationPolicy
├── MissingDataPolicy
└── CreatedAt
```

`AnalysisRecipe`가 필요한 이유:

- 동일한 숫자라도 목적에 따라 좋고 나쁨이 바뀐다.
- min-max 정규화는 비교 대상이 바뀌면 점수가 바뀐다.
- weight만 저장하고 normalization cohort를 잃으면 재현할 수 없다.
- categorical layer는 `ContextOnly`로 남기고 숫자 overlay에서 제외할 수 있어야 한다.
- recipe version이 있어야 과거 결과를 다시 설명할 수 있다.

### 제안하는 사용자 결과: Site/District Profile

최종 결과를 단일 숫자로 축약하기 전에 다음을 제공한다.

```text
Profile
├── 원값과 단위
├── 정규화값
├── recipe 내 기여도
├── 관측 기간
├── coverage / missing IDs
├── SampleCount / RawRecordCount
├── AcquisitionOrigin / DeliveryOrigin / CacheAge
├── source warning과 mapping assumption
└── 결과 해석 문장
```

### 제안 기능

#### P0 — 0.3.x Windows Release Candidate

- Windows Rhino 8에서 installer/portable 설치, 업그레이드, 제거 검증
- 기존 `.gh` 문서 open과 GUID/port 연결 회귀
- Run/Cancel/supersede/document close/native mesh dispose 검증
- 실제 provider 응답을 sanitize한 contract fixture
- 권장 설치 경로 하나와 fallback 하나 선정
- 학생 3–5명 설치 관찰 테스트

**Exit 가설:** 새 Windows 사용자 80% 이상이 외부 도움 없이 15분 안에 샘플 결과에 도달한다.

#### P1 — Explainable Scenario

- `AnalysisRecipe`와 versioned serialization
- Quality/Scenario Inspector 컴포넌트
- layer별 원값·정규화값·기여도 출력
- 같은 snapshot에서 recipe A/B 비교
- provenance와 recipe를 포함한 CSV/report sidecar
- missing, low coverage, stale cache를 캔버스에서 구별

**Exit 가설:** 사용자가 “왜 이 결과가 나왔는가”를 원값과 recipe만으로 설명하고 같은 snapshot에서 재현한다.

#### P2 — 올바른 표현 타입

- category composition/legend renderer
- missing을 0과 다르게 표현
- color-blind-safe palette
- raw unit-aware legend
- IDW trend 가정과 적용 조건을 UI에 표시

#### P3 — 검증된 지역·시간·데이터 확장

- provider capability matrix 작성
- 사용자 대지/경계로 지원 행정구역 자동 판정
- 정의가 유지되는 source부터 snapshot time comparison
- 사용자 과제가 분명한 환경/POI source만 추가

### 비목표

- QGIS/ArcGIS 기능의 광범위한 복제
- 모든 도시·지역을 형식상 선택할 수 있게 만드는 것
- 근거가 숨겨진 “URSUS 공식 종합 점수”
- 데이터 종류 수를 제품 완성도의 지표로 삼는 것
- 실시간 collaboration이나 cloud 계정 시스템

### 측정할 것

| 항목 | 첫 측정안 |
|---|---|
| Time to First Result | 새 사용자, 설치 시작부터 sample visualization까지 |
| 도움 요청 | 진행 중 외부 설명이 필요했던 횟수와 지점 |
| 결과 설명성 | 사용자가 상·하위 결과의 이유를 source/recipe로 설명 가능한가 |
| 재현성 | 저장한 recipe로 같은 snapshot 결과를 다시 만들 수 있는가 |
| 안정성 | cancel, provider failure, missing, stale cache가 조용한 오답이 아닌가 |
| 반복 속도 | cache hit 및 recipe 변경 시 network 0, 체감 지연 측정 |

### 아직 결정하지 않은 것

- 핵심 산출물이 GH geometry, quality report, CSV, 이미지, recipe bundle 중 무엇인가?
- 학생의 첫 실제 결정은 프로그램 배치, 대지 비교, 접근 전략, mass 방향 중 무엇인가?
- profile을 기존 Solver 출력에 append할지 별도 Inspector로 둘지?
- recipe를 GH document 안에 저장할지 외부 JSON으로 교환할지?
- 서울의 분석 깊이를 먼저 완성할지, 전국 boundary 입력을 먼저 열지?
- Setup.exe, Inno, Yak 중 주 설치 경로를 무엇으로 할지?

---

## 2026-07-21 — 방향 논의: 무엇을 어디에 둘까, 어디에 무엇을 둘까

**상태:** `제안`
**출발 경험:** 도심 UAM 정류장의 잠재 수요, 운임 지불 능력과 입지를 정량화·시각화하기 위해 최초 로직을 만들었다.

### 두 접근의 차이

#### Program-first: “설계할 X가 어디에 들어가면 좋은가?”

- intervention과 목적이 먼저 정해져 있어 지표 선택 이유를 설명하기 쉽다.
- UAM 정류장, 공공도서관, 청년주택 등 목적별 recipe를 만들 수 있다.
- 결과는 후보 지역, 근거, trade-off와 검증이 필요한 빈칸이다.
- weighted overlay를 쓰더라도 score가 어떤 질문의 답인지 명확하다.

#### Place-first: “이 지역에는 무엇이 들어가면 좋은가?”

- 탐색적 아이디어 발상에는 매력적이다.
- 그러나 가능한 프로그램의 범위가 무한하고, 공공 데이터만으로 프로그램을 추천하면 가치판단과 사업성 가정이 숨는다.
- 같은 현상도 프로그램에 따라 의미가 반대다. 높은 지가는 구매력 신호이면서 동시에 사업비 위험이다.
- 충분히 검증된 program recipe catalog가 없으면 그럴듯하지만 방어하기 어려운 추천기가 된다.

### 제3의 관점: Decision-question-first

제품의 상위 개념을 Program-first나 Place-first로 고정하기보다 **“공간적 설계 의사결정 질문을 증거로 검토한다”**로 정의한다.

첫 두 workflow는 다음이 적합하다.

1. **Site Screening — 프로그램은 있고 후보지를 찾는다.**
   “UAM 정류장이라는 intervention과 운영 시나리오가 있을 때 어떤 지역을 우선 조사할 것인가?”
2. **Site Briefing — 대지는 있고 설계가 무엇에 반응해야 하는지 찾는다.**
   “이 대지의 수요, 접근성, 비용, 형평성과 제약 중 설계에 중요한 조건은 무엇인가?”

Place-first program recommendation은 나중에 검증된 recipe 여러 개를 같은 지역에 실행해 “가능성 탐색”으로 제공할 수 있다. URSUS가 무엇을 지으라고 단정하는 기능은 우선하지 않는다.

### 현재 권고

- 첫 flagship workflow는 origin story와 가장 잘 맞는 **Program-first Site Screening**으로 한다.
- 제품 전체의 정체성은 더 넓은 **Decision-question-first spatial evidence workbench**로 둔다.
- 두 번째 workflow로 Site Briefing을 추가한다. Rhino/Grasshopper 사용자는 이미 대지나 geometry를 가진 경우가 많아 자연스럽게 이어진다.
- Place-first는 `아이디어`로 유지하되, 임의 추천이 아니라 versioned recipe catalog가 쌓인 뒤의 opportunity scan으로 제한한다.

### UAM 사례에서 지표를 보는 방식

UAM 입지는 하나의 종합 점수보다 서로 충돌하는 여러 축으로 보는 편이 정직하다.

| 축 | proxy 예시 | 해석 주의 |
|---|---|---|
| 잠재 수요 | 생활/상주인구, 업무·방문 유입 | 인구는 실제 UAM 수요가 아니다 |
| 지불 능력 | 소득, 소비, 지가 | 높은 지가는 구매력과 높은 사업비를 동시에 뜻한다 |
| 연결성 | 대중교통 승차, 환승 거점 | 높은 교통은 연계 수요일 수도, 대체재가 충분하다는 뜻일 수도 있다 |
| 물리적 타당성 | 면적, 용도지역, 건축물/장애물 | 현재 source만으로 비행·안전 규정을 판정할 수 없다 |
| 비용 | 지가, 취득 난이도 | 수요 축과 방향이 반대일 수 있다 |
| 공공성/형평성 | 이동 취약성, 서비스 공백 | 구매력 중심 recipe와 다른 목적 함수가 필요하다 |
| 외부효과 | 소음, 환경, 안전 영향 | 현재 데이터 빈칸을 명시해야 한다 |

교통량을 무조건 `positive`, 지가를 무조건 `positive`로 두면 안 된다. 같은 layer가 recipe 안에서 `Benefit`, `Cost`, `Constraint`, `Risk`, `ContextOnly` 중 어떤 역할인지 선언해야 한다.

### `AnalysisRecipe` 보강안

```text
AnalysisRecipe
├── Intervention
├── DecisionMode: SiteScreening | SiteBriefing | OpportunityScan
├── Scenario: commercial | public-service | equity-first | ...
├── Criteria
│   ├── LayerId
│   ├── Role: Constraint | Benefit | Cost | Risk | ContextOnly
│   ├── Direction / Transform
│   ├── Threshold
│   ├── Weight
│   └── EvidenceNote
├── NormalizationCohort
├── MissingDataPolicy
└── Version
```

hard constraint와 context-only layer는 weighted score에 넣지 않는다. Benefit/Cost도 가능한 한 축별 profile을 먼저 보여주고, 최종 score는 특정 scenario 안에서만 선택적으로 만든다.

### 제안 결과물

Site Screening의 결과는 단일 1등 지역보다 다음 묶음이 적합하다.

- constraint를 통과한 후보 지역
- 수요/지불능력/연결성/비용/위험별 profile
- 서로 우열을 단정할 수 없는 Pareto 후보
- recipe와 weight 변화에도 유지되는 robust 후보
- 순위가 쉽게 뒤집히는 sensitive 후보
- 현재 데이터로 판단할 수 없는 항목과 추가 조사 목록
- 각 값의 기간, coverage, sample과 source provenance

### 남은 질문

- 첫 UAM recipe는 상업성, 공공 교통망, 형평성 중 어느 운영 시나리오를 가정했는가?
- 당시 실제로 후보지를 선택할 때 마지막까지 중요했던 두세 조건은 무엇이었는가?
- 분석 결과가 바꾼 설계 판단과, 데이터가 부족해 결국 사람이 결정한 판단은 무엇이었는가?
- UAM 외에 같은 workflow를 검증할 두 번째 program은 무엇이 적합한가?

---

## 2026-07-21 — 아이디어: LLM을 자연어 분석 compiler로 사용

**상태:** `가설`
**아이디어:** 사용자가 설계 목적을 자연어로 설명하면 LLM이 관련 공공데이터, 조건 역할, 기간, 가중치와 한계를 근거와 함께 제안하고, URSUS의 deterministic engine이 검증된 recipe만 실행한다.

### 제품 경험 초안

```text
학생
  “도심 UAM 정류장을 설계하려고 한다.
   잠재 수요와 지불 능력을 바탕으로 후보 입지를 보고 싶다.”
          │
          ▼
URSUS Assistant
  목적·운영 시나리오·필수 제약을 추가 질문
          │
          ▼
Evidence Plan 제안
  - 사용할 dataset과 실제 source
  - Benefit / Cost / Constraint / Risk / ContextOnly
  - 기간·공간 범위·단위·aggregation
  - weight 또는 threshold와 제안 근거
  - 현재 데이터로 판단할 수 없는 항목
          │
          ▼ 사용자 검토·수정·명시 승인
Typed AnalysisRecipe
          │
          ▼
URSUS deterministic engine가 fetch/cache/map/analyze
          │
          ▼
후보 profile, sensitivity, provenance와 추가 조사 항목
```

### 핵심 정의

LLM이 직접 분석 엔진이 되는 것이 아니라 **자연어 요구를 실행 가능한 typed analysis plan으로 compile하는 planner**가 된다.

```text
Natural-language brief
  → EvidencePlan
  → validated AnalysisRecipe
  → deterministic URSUS execution
  → result interpretation
```

이 경계를 지키면 LLM의 유연성과 기존 URSUS의 신뢰성 계약을 함께 사용할 수 있다.

### “서울 열린데이터의 어떤 데이터든”의 현실적 의미

HTTP/XML/JSON pagination 자체는 generic transport/parser로 일반화할 수 있다. 하지만 dataset의 의미까지 LLM이 응답을 보고 즉석에서 추론하게 하면 안 된다.

데이터마다 다음이 다르다.

- 공간 key와 행정동/법정동 체계
- 관측 기간 field와 latest closed의 정의
- 값이 평균, 총량, 비율, category 중 무엇인지
- 중복 identity를 구성하는 field
- 0, null, suppressed value의 의미
- complete coverage 조건
- 행정동→법정동 mapping 방식
- 값의 단위와 유효 범위
- 갱신 주기, 편향과 사용 조건

따라서 “어떤 데이터든”은 **LLM이 임의 endpoint를 실행한다**가 아니라 **검증된 `DatasetDescriptor`를 가진 데이터라면 공통 engine이 실행한다**는 뜻으로 제한한다.

### 제안 모델: `DatasetDescriptor`

```text
DatasetDescriptor
├── Id / Version
├── Provider / ServiceId
├── SourceUrl / DocumentationUrl
├── GeographyField / GeographyType
├── PeriodField / PeriodSemantics
├── ValueFields
│   ├── Field
│   ├── Unit
│   ├── MetricSemantics: Mean | Sum | Ratio | Category
│   └── ValidRange
├── StableIdentityFields
├── SupportedAggregations
├── MappingPolicy
├── CompletenessPolicy
├── Coverage / UpdateCadence
├── KnownLimitations
└── AllowedRoles
```

LLM은 이 catalog에서 검색하고 descriptor가 허용하는 field와 transform만 recipe에 넣을 수 있다. 새로운 source는 descriptor와 fixture가 검증되기 전까지 실행 불가로 처리한다.

### LLM이 해도 되는 일

- 모호한 설계 목적을 구체적인 decision question으로 바꾸기
- 목적과 운영 시나리오를 확인하는 추가 질문
- catalog에서 관련 dataset 후보 검색
- 각 dataset을 고른 이유와 proxy 한계 설명
- `Constraint`, `Benefit`, `Cost`, `Risk`, `ContextOnly` 역할 제안
- weight/threshold 초안과 대안 scenario 제안
- 서로 충돌하는 지표와 데이터 공백 표시
- 실행 결과의 provenance를 근거로 사람이 읽을 수 있는 요약 작성

### LLM이 단독으로 하면 안 되는 일

- catalog에 없는 endpoint나 field를 발명
- API key를 prompt 또는 외부 model에 전달
- 사용자의 승인 없이 유료/평문/대량 network fetch 실행
- hard constraint를 높은 benefit 점수로 상쇄
- 지원되지 않는 지역을 비슷한 데이터로 조용히 대체
- 실제 계산, 정규화, mapping과 ranking을 비결정적으로 수행
- 근거 없는 weight를 객관적 권장값으로 확정
- missing/low coverage 결과를 정상 데이터처럼 해석
- “이곳이 최적 입지”라고 단정

### Weight에 대한 원칙

LLM은 weight를 **확정**하기보다 다음을 제안해야 한다.

1. 왜 이 criterion이 필요한가?
2. 어떤 방향으로 작용하는가?
3. weight가 어떤 가치판단을 포함하는가?
4. 다른 합리적 scenario에서는 어떻게 달라지는가?
5. weight 변화에도 결론이 유지되는가?

기본 결과는 단일 weight set보다 commercial, public-service, equity-first 같은 복수 scenario와 sensitivity를 보여주는 편이 안전하다. 사용자가 최종 recipe를 승인해야 실행 가능하다.

### Tool boundary 제안

LLM이 호출할 수 있는 tool은 작고 typed해야 한다.

```text
search_dataset_catalog(query, geography, period)
get_dataset_descriptor(dataset_id, version)
validate_recipe(recipe)
preview_recipe(recipe)
execute_recipe(approved_recipe_hash)
get_result_profile(run_id)
compare_scenarios(run_ids)
```

`execute_recipe`만 origin network를 사용할 수 있고 명시적 사용자 승인/Run edge를 요구한다. 실제 API key와 raw URL 조립은 URSUS engine 내부에 남긴다.

### Rhino/Grasshopper UX 초안

별도 `URSUS Assistant` panel 또는 component를 둔다.

```text
입력
- Design Brief
- Site / Boundary (optional)
- Decision Mode
- Available Data Policy
- Ask / Revise / Approve / Run

출력
- Evidence Plan
- Recipe Preview
- Required Credentials
- Unsupported Evidence
- Warnings
- Approved Recipe
- Run Id / Snapshot
```

대화 내용이 component solution마다 model을 다시 호출하지 않도록 conversation과 approved recipe의 lifecycle을 분리한다. 기존 Run/Cancel 계약은 유지한다.

### 보안·재현성 계약

- API key는 LLM context에 넣지 않는다.
- 가능하면 raw provider row도 보내지 않고 descriptor와 집계 profile만 제공한다.
- model, system prompt, catalog version과 recipe hash를 run provenance에 기록한다.
- LLM 응답이 달라도 같은 approved recipe는 같은 deterministic pipeline을 실행한다.
- 외부 model 사용 여부, 전송되는 project text와 보관 정책을 사용자에게 설명한다.
- LLM 없이 recipe를 직접 작성·수정하는 manual fallback을 유지한다.
- provider metadata나 문서 text는 prompt instruction이 아니라 untrusted reference data로 취급한다.

### 이 아이디어가 바꾸는 제품 정의

```text
기존
공공데이터를 가져와 overlay하는 GH plugin

제안
자연어 설계 의도를 검증 가능한 공간 분석 recipe로 compile하고,
그 recipe를 provenance-first engine으로 실행하는 design evidence workbench
```

### 첫 prototype 범위 제안

전체 서울 data catalog를 바로 연결하지 않는다.

1. UAM Site Screening 한 가지 prompt family
2. 이미 검증된 income/resident/transit/land-price/zoning descriptor
3. descriptor 검색 → evidence plan → 사용자 승인 → recipe JSON 생성까지만 LLM 사용
4. 기존 engine으로 실행
5. rule-based validator가 hallucinated source/field/role을 거부
6. LLM 없이 만든 reference recipe와 결과를 비교

### 평가 질문

- dataset 선택이 domain reviewer의 reference plan과 얼마나 일치하는가?
- proxy의 한계와 반대 해석을 빠뜨리지 않는가?
- catalog에 없는 source/field 사용률이 0인가?
- 같은 brief에서 recipe가 달라져도 결론의 민감도를 설명하는가?
- 사용자가 제안을 이해하고 수정할 수 있는가?
- LLM 없이 직접 만드는 것보다 실제 시간이 줄어드는가?
- 결과 신뢰가 과도하게 높아지지 않는가?

### 남은 결정

- Assistant를 GH component, Rhino panel 또는 외부 web UI 중 어디에 둘 것인가?
- model provider를 고정할지 사용자가 자신의 endpoint/key를 연결하게 할지?
- dataset catalog를 사람이 version-control할지, 공식 문서 수집으로 반자동 생성할지?
- recipe 승인을 어떤 UI와 provenance로 남길지?
- LLM 비용과 network 사용을 run별로 어떻게 보여줄지?
- UAM reference recipe를 검토할 domain expert를 누구로 둘지?

---

## 2026-07-21 — 0.3.0 Hardening 결과

**상태:** 코드/자동 검증 `완료`, Windows 실환경 `검증 필요`

### 수집과 보안

- 서울 pagination은 total 누락/변경, 조기 종료, total 불일치, 페이지 경계 중복을 `URS309`로 fail-closed한다.
- 페이지당 1,000행, 1,000페이지, 전체 1,000,000행, XML 16MiB 상한이 있다.
- 교통 데이터는 최신 닫힌 월만 유지하고 최소 28일, 매일 예상 서울 지역 95% 이상이어야 complete다.
- retained aggregate는 20,000 entries로 제한한다. 중복 검출 identity set은 최대 100만 행 동안 유지될 수 있다.
- 서울 키 원격 검증은 기본적으로 평문 HTTP를 사용하지 않는다. Setup의 명시적 위험 동의가 있어야 전송한다.

### 공시지가 provenance

- 공시지가를 `Mean + SampleCount`로 보존한다.
- canonical legal ID 병합은 표본 수 가중 평균이다.
- SampleCount가 cache → `DistrictDataSet` → solver → snapshot까지 전달된다.
- `RawRecordCount`는 실제 기여 표본 수 합계다.
- 의미적으로 손상된 typed cache는 강제 재수집하고 0건은 `URS305`를 유지한다.

### CI와 배포

- SDK 8.0.129를 고정한다.
- main push/PR, tag, manual workflow에서 테스트를 필수 gate로 실행한다.
- `installer/package-manifest.json`이 runtime payload의 단일 계약이다.
- package verifier가 실제 파일, C# contract, Inno Source/DestDir, post-install 목록과 deps runtime targets를 비교한다.
- Windows RID 구현 DLL 두 개를 하위 경로 그대로 Setup/Inno/ZIP에 포함한다.
- 제품, assembly, Inno 버전은 0.3.0으로 정렬했다.

### 검증

- 자동 회귀 테스트: **102/102**
- Core 및 GH Release build: 성공
- package contract: **10 runtime files + 2 samples**
- workflow YAML, NuGet known vulnerability, diff check: 통과
- Setup 포함 solution/Inno compile: Linux WindowsDesktop SDK 부재로 미실행
- 실제 credential, Rhino native geometry, 설치/업그레이드/제거: 미실행

---

## 2026-07-21 — Phase 0–2 안정화 회고

**상태:** `완료`, 실환경 항목은 `검증 필요`

### Phase 0 — 신뢰 가능한 기준선

- 의존성 없는 실행형 회귀 테스트 기준선을 만들었다.
- 법정동 8자리/10자리/PNU를 canonical legal ID로 통합하고 행정동 매핑을 분리했다.
- coverage 0%는 실패하고 partial은 결측을 유지한다.
- 존재하는 layer만 district별로 재정규화한다.
- 서울 범위 밖 요청은 서울 API 전에 차단한다.
- Solver는 `Run` false→true edge에서만 실행한다.
- 오류 코드는 안정된 ID와 저장소 내 해결 문서를 사용한다.

### Phase 1 — Open API, cache와 geometry truth

- 평균·합계·category의 집계 의미를 분리했다.
- additive 지표의 법정동 분배는 총합을 보존한다.
- 서울 통계는 versioned expected set과 latest closed period로 기간 혼합을 막는다.
- XML은 필요한 field만 streaming projection한다.
- 공유 HTTP pipeline은 동시 요청 8, deadline, bounded retry, cancellation과 response dispose를 갖는다.
- cache는 LocalApplicationData 아래 query-keyed envelope, coalescing, atomic replace, TTL/schema/corruption fallback을 갖는다.
- snapshot은 raw mapped layer, topology, normalization stats, observation, quality와 origin을 보존한다.
- Polygon/MultiPolygon, island와 hole을 유지하고 EPSG:5179 meter/m²를 canonical 단위로 사용한다.

### Phase 2 — 반응형 GH와 bounded visualization

- async/cancellation과 generation 기반 `RunCoordinator`를 도입했다.
- stale generation은 최신 결과를 덮지 못한다.
- GH task 취소가 실제 source/network 실행까지 전파된다.
- weight/view 변경은 snapshot derived 계산으로 끝나며 HTTP와 CSV side effect가 없다.
- choropleth/extrusion이 기본이고 IDW는 선택적 trend surface다.
- document unit 변환은 adapter 경계에서 길이에 한 번 적용하고 면적은 m²를 유지한다.
- preview 50k vertices, final 250k vertices/500k faces, 예상 192MiB 상한을 검사한다.
- visual LRU는 2 entries/256MiB이며 native mesh dispose ownership을 갖는다.

### 이 단계에서 제거한 고위험 경로

- 행정동/법정동/PNU 혼동으로 생기는 조용한 0건
- 실측값이 없는데 평균값으로 정상처럼 보이는 layer
- additive 값을 복제해 총합을 부풀리는 mapping
- 서로 다른 관측 기간 혼합
- API key가 진단/cache identity에 남는 경로
- force refresh 실패가 이전 cache를 파괴하는 경로
- MultiPolygon/hole 손실과 CRS/단위 이중 변환
- stale generation과 cancel/fault 경쟁
- CSV 저장 직후 derived recompute가 Saved Path를 지우는 경로
- 무제한 resolution/cache로 UI/native memory가 폭주하는 경로

---

## 2026-04-02 — 초기 제품 로드맵 인터뷰

**상태:** 역사적 `가설`; 일부는 현재 방향으로 대체

### 당시 비전

> 건축 설계 초기 단계에서 대지의 모든 공간 데이터를 한 번에 분석하는 원스톱 도구

### 당시 합의

- 학생 우선, 이후 설계사무소로 확장
- 부동산 → 환경 → 생활 인프라 순으로 데이터 확장
- 서울에서 전국으로 확대
- 사용자 가중치, site score, time series를 추가
- Food4Rhino 배포
- 예제와 데이터 해석 가이드 제공

### 당시 평가 기준

| 원칙 | 당시 비중 |
|---|---:|
| student first | 30% |
| data extensibility | 25% |
| analysis depth | 25% |
| reliability | 10% |
| performance | 10% |

### 현재 재평가

- `학생 우선`, 해석 가이드, 가중치, source abstraction은 유지한다.
- “대지의 모든 데이터”는 범위가 무한하고 성공 기준이 없다. `폐기` 후보 표현이다.
- 범용 `SiteScore`는 가정과 가치판단을 숨길 수 있어 `AnalysisRecipe + Profile` 검증 뒤로 미룬다.
- 전국 확장은 architecture 문제가 아니라 provider coverage/semantic validation 문제다.
- 데이터셋 확대보다 explainability와 실제 사용자 여정 검증을 먼저 한다.

---

## 2026-03-31 — v0.2 사용성 PRD

**상태:** 역사적 PRD; 대부분 완료 또는 다른 방식으로 대체

### 당시 목표

> 처음 받은 사용자가 5분 안에 결과를 볼 수 있게 한다.

- 설치 3단계 이내
- Solver 필수 외부 입력 0개
- 매핑 파일을 사용자가 관리하지 않음
- CSV 원클릭 출력
- 해결 가능한 한글 오류 메시지

### 기능별 현재 상태

| 항목 | 당시 제안 | 현재 상태 |
|---|---|---|
| F-01 Setup | self-contained Setup.exe | `완료`, 실제 Windows 사용자 검증 필요 |
| F-02 mapping | DLL embedded resource | `대체`: 외부 파일을 단일 package contract로 자동 배포 |
| F-03 API key | component → settings → environment | `완료`; 현재 우선순위와 이름은 `docs/api-keys.md`가 기준 |
| F-04 DataSet 기본값 | 미입력 시 기본 세트 | `완료` |
| F-05 CSV | 별도/원클릭 exporter | `완료` |
| F-06 오류 | 한글과 해결 URL | `완료`; version-controlled guide로 연결 |
| F-07 edge falloff 제거 | Visualizer port 감소 | `폐기`: 기존 GH 문서 port 호환성을 우선하고 새 mode/output은 append-only |

### 당시 사용자 여정과 현재 차이

초기 PRD는 컴포넌트를 놓으면 바로 실행되는 경험을 상정했다. 현재는 문서 재열기와 입력 변경이 자동 네트워크 호출을 일으키지 않도록 `Run` edge를 요구한다. 한 단계가 늘었지만 비용·credential·재현성을 명시적으로 통제하기 위한 의도된 변경이다.

---

## 2026-03-20 — 최초 TODO 메모

**상태:** 역사적 메모, 원문 요약

### 관찰했던 문제

- Solver가 사용자에게 너무 많은 입력을 요구했다.
- API를 매 동작마다 다시 호출하면 다중 dataset overlay가 느려진다.
- 행정동↔법정동 mapping 파일의 배포 위치가 불명확했다.
- 결과 metric을 Panel 외에 Excel/CSV로 전달할 방법이 필요했다.
- 플러그인 설치 시 DLL 복사와 차단 해제를 자동화하고 싶었다.

### 이후 해결

- API key 자동 탐색, DataSet 기본값과 mapping 자동 배포
- query-keyed cache와 snapshot derived recompute
- CSV export
- Setup/Inno/portable 패키징 계약
- 오류 가이드와 onboarding 문서

### 남은 본질적 질문

최초 TODO는 설치 자동화와 데이터 추가에 초점을 맞췄다. 이제 병목은 파일 배치가 아니라 **사용자가 분석 결과를 이해하고 실제 설계 결정으로 연결하는 방법**이다.

---

## Rough Ideas Inbox

아직 roadmap에 넣지 않은 생각을 날짜와 함께 쌓는다. 검증 전에는 구현 약속으로 보지 않는다.

### 2026-07-21

- `아이디어` Inspector에서 각 layer의 “이 값은 무엇을 뜻하는가 / 언제 수집됐는가 / 얼마나 완전한가”를 접을 수 있는 tree로 보여준다.
- `아이디어` recipe A/B 비교 시 값 차이뿐 아니라 순위가 뒤집힌 district와 그 원인을 보여준다.
- `아이디어` 결과 이미지 옆에 자동 provenance caption을 만들어 발표 슬라이드에 붙일 수 있게 한다.
- `아이디어` “높을수록 좋음”을 기본으로 추론하지 않고 source metadata가 direction을 요구하도록 한다.
- `아이디어` 민감도 분석으로 weight를 조금 바꿔도 결론이 유지되는지 보여준다.
- `아이디어` 사용자 입력 boundary가 지원 지역과 겹치지 않으면 가능한 source capability를 먼저 설명한다.
- `아이디어` 네트워크 없는 수업을 위해 검증된 teaching snapshot bundle을 제공한다.
- `아이디어` telemetry보다 먼저 사용자가 직접 export할 수 있는 secret-free diagnostic bundle을 만든다.
- `아이디어` Yak를 주 배포 경로로 선택한다면 Setup/Inno는 개발·오프라인 fallback으로 격하한다.
- `아이디어` profile의 해석 문장은 자동 설계 결론이 아니라 데이터 사실과 주의사항만 생성한다.

### 새 메모 템플릿

```markdown
### YYYY-MM-DD

- `아이디어` ...
- `가설` ...
- `결정` ...
- `폐기` ... — 이유: ...
```

---

## 현재 수동 검증 체크리스트

### Windows / Rhino 8

- [ ] installer와 portable 각각 새 설치가 성공한다.
- [ ] 이전 버전 위에 설치하고 제거해도 다른 파일을 손상하지 않는다.
- [ ] 기존 샘플 `.gh`가 GUID와 기존 port 연결을 유지한다.
- [ ] 문서를 열 때 저장된 Run=true로 자동 fetch하지 않는다.
- [ ] Run false→true 한 번에 generation 하나만 시작한다.
- [ ] Cancel은 현재 실행만 취소하고 마지막 성공 결과를 유지한다.
- [ ] 늦은 이전 generation이 최신 결과를 덮지 않는다.
- [ ] weight만 바꾸면 HTTP와 CSV write가 발생하지 않는다.
- [ ] mm/m 전환 시 길이만 변하고 Areas는 m²를 유지한다.
- [ ] multipart island와 hole이 모든 visualization mode에서 보존된다.
- [ ] missing district는 0과 구분된다.
- [ ] 과대 geometry/resolution은 OOM 대신 budget 오류를 반환한다.
- [ ] document/component 제거 중 cancel과 mesh dispose가 crash 없이 끝난다.

### 실제 데이터 계약

- [ ] VWorld 정상/0건/error response fixture를 만든다.
- [ ] 서울 total mismatch/duplicate/schema drift fixture를 만든다.
- [ ] DataGoKr 정상/0건/auth/error response fixture를 만든다.
- [ ] fixture에 credential, 사용자 경로와 개인정보가 없음을 검사한다.

---

## 관련 현행 문서

- [Getting Started](getting-started.md)
- [Installation](installation.md)
- [API Keys](api-keys.md)
- [Troubleshooting](troubleshooting.md)
- [CSV Export](csv-export.md)
- [Dataset Interpretation Guide](dataset_interpretation_guide.md)
- [Data Source Specifications](data/README.md)
- [IDW Analysis](IDW_analysis.md)
