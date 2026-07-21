# URSUS Product Journal

> 제품 아이디어, 임시 가설, PRD 초안, 결정과 검증 결과를 날짜별로 쌓는 living document
> 마지막 갱신: 2026-07-21 / 현재 제품 버전: 0.3.0

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
3. **Administrative truth by default** — district 값은 choropleth가 기본이다. IDW는 연속장 가정을 사용자가 선택한 trend 표현이다.
4. **Bounded by contract** — HTTP, page, retry, cache, mesh, legend와 배포 payload에 명시적 상한/계약/진단을 둔다.
5. **No universal score** — overlay를 객관적 정답처럼 제시하지 않는다. 목적과 가정을 포함한 versioned recipe의 결과로 다룬다.
6. **Expand by validated capability** — 지역 선택 UI부터 열지 않는다. 데이터 범위와 의미가 검증된 capability만 노출한다.

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
