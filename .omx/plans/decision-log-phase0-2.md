# URSUS Phase 0–2 의사결정 로그

결정 상태: `Proposed`, `Accepted`, `Deferred`, `Rejected`.

## D-001 — 제품화/배포 범위 분리

- 상태: Accepted
- 결정: Phase 0–2에서는 코드와 로컬 빌드 기준선만 다룬다. 설치 프로그램, CI 릴리스, Yak/배포 방식은 Phase 3 이후 사용자와 별도로 결정한다.
- 영향: 현재 `bin/dist`와 배포 스크립트 경로 불일치는 기록만 유지하고 이 작업에서 배포 방식을 확정하지 않는다.

## D-002 — 결측 데이터 기본 정책

- 상태: Accepted
- 결정: coverage는 finite 값이 있는 요청 법정동 수/요청 법정동 수다. 0% 레이어는 실패, 부분 coverage는 warning과 결측 유지, 100%는 complete다. 셀별 overlay는 존재하는 레이어만 가중치 재정규화하며 값이 전혀 없는 셀은 NaN이다.
- 영향: 기존 결과가 달라질 수 있으나 잘못된 확신 방지가 하위호환보다 우선한다.

## D-003 — 합계형 행정동 값의 법정동 배분

- 상태: Accepted
- 결정: 인구·승하차 같은 합계형 값은 매핑 대상 법정동에 균등 분배하여 총합을 보존하고 `EstimatedEqualSplit`을 남긴다. 평균/비율은 동일값을 전파하고 `AssumedUniform`을 남긴다. 여러 행정동이 한 법정동에 합쳐지면 합계는 합산, 평균/비율은 단순 평균한다. 범주는 임의 숫자화하지 않는다.
- 이유: 현재 매핑에는 면적/인구/시설 가중치가 없으므로 정확한 분배는 불가능하지만, 기존의 전량 복제보다 총합 보존과 불확실성 표기가 안전하다.

## D-004 — GH 비동기 구현 방식

- 상태: Accepted
- 결정: 현재 참조된 Grasshopper 8.17 공식 어셈블리 계약의 `GH_TaskCapableComponent<T>`를 사용하고 Core에는 별도 async+cancellation API를 둔다. 세대 번호로 stale result를 폐기한다. 기존 0~9 입력, 0~10 출력과 ComponentGuid를 보존하고 새 포트는 append한다.
- 실행 의미: Run/Cancel은 false를 관찰한 뒤의 rising-edge에서만 query/network 작업을 시작한다. 문서 reopen 당시 true 또는 query-changing 입력(address/dataset/period/radius/key) 변경은 실행하지 않는다. derived/view 입력(weights/style/resolution/legend)은 마지막 immutable snapshot에서 즉시 재계산하며 HTTP 0을 보장한다.

## D-005 — 기본 시각화

- 상태: Accepted
- 결정: 행정구역 기반 데이터의 기본 표현은 choropleth/extrusion이며 IDW는 선택적 trend surface로 유지한다.
- 영향: 기존 IDW 컴포넌트는 제거하지 않고 의미와 안전 제한을 명확히 한다.

## D-006 — 프레임워크 업그레이드

- 상태: Deferred
- 이유: net8/Rhino 8 전환은 타당하지만 현재 Phase 0–2의 데이터 정확성·반응성 변경과 섞으면 회귀 원인 분리가 어려워진다. 제품화 이전 별도 변경으로 수행한다.

## D-007 — 새 의존성

- 상태: Accepted
- 결정: Phase 0–2에서 새 NuGet 의존성을 추가하지 않는다. `URSUS.Tests`는 현재 개발 환경에 설치된 `net8.0` console executable과 자체 assertion/exit code를 사용하며 fixture와 fake `HttpMessageHandler`를 포함한다. net8 test runner는 net7 제품 assembly를 참조하되 제품 타깃 업그레이드와 분리한다. 검증 명령은 `dotnet run --project src/URSUS.Tests/URSUS.Tests.csproj -c Release`다.

## D-008 — Boundary topology와 호환성

- 상태: Accepted
- 결정: 내부 모델은 `BoundaryTopology -> Parts[] -> Outer + Holes[]`를 사용한다. 기존 단일 Geometry는 가장 큰 outer의 legacy projection으로 유지한다. 기존 GH 포트/ComponentGuid는 유지하고 topology 출력은 append한다. 캐시 schema를 올려 구형 geometry cache는 재사용하지 않는다.

## D-009 — 좌표계

- 상태: Accepted
- 결정: 분석 CRS는 전국 단일 평면 좌표계 EPSG:5179로 고정하고 snapshot/cache identity에 기록한다. Rhino 출력 시 문서 단위로 변환한다.

## D-010 — 관측 기간

- 상태: Accepted
- 결정: 각 source는 D-013의 expected-set/closed-period 규칙으로 단일 기간을 선택한다. 선택 기간과 complete/partial 판정은 `ObservationWindow`, cache identity, provenance에 포함한다. 명시 기간 조회는 Core query 계약으로 제공하되 GH UI 노출은 Phase 3 이후 결정한다.

## D-011 — Phase 0의 배포 부채

- 상태: Deferred
- 결정: `bin/dist`, CI, 설치 스크립트, 버전 혼선은 known deployment debt로만 기록한다. Phase 0에서는 테스트 프로젝트와 로컬 빌드 가능성만 복구하고 배포 경로나 릴리스 방식을 바꾸지 않는다.

## D-012 — HTTP transport와 API 키 노출

- 상태: Accepted
- 근거: 서울 열린데이터 공식 가이드와 FAQ는 `http://openapi.seoul.go.kr:8088`을 제시하고 2026-07-21 실제 TLS handshake도 실패한다. data.go.kr의 두 현재 endpoint는 HTTPS 연결이 가능하다.
- 결정: `TransportPolicy` typed record에 `AllowInsecureSeoulHttp`를 둔다. precedence는 explicit `DataQuery.TransportPolicy` > user environment/config `URSUS_ALLOW_INSECURE_SEOUL_HTTP` > false다. Solver는 정책을 모든 source query에 전달한다. GH는 기존 포트를 유지하며 input 11에 `Allow Insecure Seoul HTTP`(default false)를 append한다. false면 parser/HTTP 0과 source error, true면 HTTP 허용 및 매 실행 `AnalysisSnapshot` high-severity warning이다. LandPrice/Zoning은 HTTPS만 사용하며 production 묵시 HTTP fallback은 금지한다. 모든 URI/로그는 key를 마스킹한다.

## D-013 — Source별 관측기간 완결 규칙

- 상태: Accepted
- 기대집합: embedded mapping에서 서울 prefix `11`, 상위 행정구역 코드(`...000`)를 제외한 versioned leaf ID 426개를 사용한다. source별 예외가 생기면 별도 versioned expected-ID fixture로만 변경한다. observed/expected count와 missing IDs를 quality에 기록한다.
- 월평균 소득/상주인구: `STDR_YYQU_CD`; injectable UTC clock 기준 아직 닫히지 않은 현재 분기는 제외한다. latest closed quarter 중 expected 426개의 finite 값이 모두 있으면 Complete, 아니면 최신 closed quarter 하나만 Partial로 반환하고 기간 혼합은 금지한다.
- 대중교통: `CRTR_DD`; injectable clock 기준 현재 월 제외. 최신 closed calendar month에서 28일 이상 존재하고 각 포함 날짜가 expected set의 95% 이상 finite coverage여야 Complete다. 그렇지 않으면 최신 closed month 하나를 Partial로 반환하며 관측일 수/coverage를 기록한다.
- 생활인구(비활성): `STDR_DE_ID`; 현재 UTC 날짜 이전의 최신 날짜 하나를 선택하고, expected ID별 24개 `TMZON_PD_SE`가 모두 있을 때만 그 ID를 complete로 센다.
- 공시지가: 명시 `stdrYear`가 없으면 injectable clock의 현재연도-1, 응답/결과에 기준연도를 기록한다.
- 용도지역/경계: snapshot period를 꾸미지 않고 `effectiveAt=null`, `retrievedAt=UTC`를 기록한다.
- 완결 기간이 없으면 이전 기간과 섞지 않고 source failure/partial warning으로 반환한다.

## D-014 — 평문 key 저장

- 상태: Deferred
- 결정: OS별 보안 저장소/credential migration은 Phase 3 제품화에서 사용자와 결정한다. Phase 0–2에서는 키 삭제 지원, 파일 권한 최소화, 경로/값 로그 제거, transport redaction까지만 수행한다.

## D-015 — Phase 2 query와 derived 입력

- 상태: Accepted
- 결정: address/dataset/period/radius/key는 query-changing이므로 새 Run edge가 필요하다. weights/style/resolution/legend는 derived/view 입력이므로 마지막 snapshot에서 즉시 재계산하고 HTTP를 호출하지 않는다. 따라서 P2-05는 계산 결과 변경과 fetch count 0을 함께 검증한다.

## D-016 — Cache coalescing과 cancellation

- 상태: Accepted
- 결정: persistent cache key에는 refresh mode를 넣지 않는다. in-flight coalescing key만 `(PersistentCacheKey, RefreshMode)`이며 force/non-force는 같은 fetch를 공유하지 않는다. 동일 persistent key의 origin operation 전체는 per-key async lock으로 직렬화한다. non-force miss가 먼저면 뒤의 force가 완료 후 같은 entry를 최종 교체하고, force가 먼저면 뒤의 non-force는 갱신 entry를 읽는다. 실패한 force는 기존 entry를 보존한다. `ForceRefresh`는 persistent read를 우회하고 성공 시 동일 persistent entry를 atomic replace한다. 각 waiter cancellation은 해당 waiter를 즉시 종료한다. 다른 waiter가 남아 있으면 origin fetch는 계속되고, 마지막 waiter가 이탈하면 shared CTS로 origin HTTP를 취소한다. bounded timeout은 항상 적용하며 성공한 origin만 atomic cache write를 수행한다.

## D-017 — Phase 1 구현 순서와 snapshot 메모리

- 상태: Accepted
- 구현 순서: (1) 현재 집계 characterization + aggregation/period/CRS 계약, (2) injected shared HttpClient와 true async/cancellation, (3) bounded paging/retry, (4) `XmlReader` incremental aggregation, (5) query-keyed cache/coalescing/atomic store, (6) source adapter/registry, minimal snapshot, topology integration.
- snapshot: immutable raw layer dictionary 한 벌과 normalization stats/provenance/quality만 저장한다. normalized dictionary는 weight 계산 시 derived하고 geometry topology/mesh는 snapshot과 분리된 cache가 소유한다.

## D-018 — Cache lookup intent와 envelope

- 상태: Accepted
- 결정: lookup에 쓰는 `PersistentCacheKey`는 source/schema, `QueryIntent(latest|explicit period)`, culture/order 독립 canonical query, CRS를 포함하고 secret과 refresh mode를 제외한다. resolved `ObservationWindow`, original `retrievedAt`, cache origin/age는 cache envelope에 저장한다. cache hit은 `retrievedAt`을 현재 시각으로 덮지 않는다.
- 공간 identity: geocoded address는 정규화된 BBOX와 radius, 직접 BBOX는 좌표 순서가 정규화된 bounds를 사용한다. 통계 district subset은 정렬된 canonical legal IDs를 사용한다.

## D-019 — Source registry 생명주기

- 상태: Accepted
- 결정: 각 `AnalysisEngine` 인스턴스가 fresh `DataSourceRegistry`를 소유한다. engine 내부에서 shared HTTP pipeline/cache를 주입하되 key-bound source instance를 process-wide singleton에 저장하지 않는다. 기존 static provider는 legacy compatibility만 담당하며 신규 실행 경로에서는 사용하지 않는다.

## D-020 — 지표별 집계·매핑 의미

- 상태: Accepted
- `avg_income`: 행정동 행을 기간 내 산술평균하고 법정동에 동일값을 전파하며 `AssumedUniform`을 기록한다.
- `resident_pop`: 기간 내 행정동 합계를 사용하고 연결된 법정동에 균등 분배해 총합을 보존하며 `EstimatedEqualSplit`을 기록한다.
- `transit`: 법정동/일자별 승하차 합계를 먼저 계산하고 선택한 closed month의 관측일 산술평균을 사용한다. 행정동 원천이면 법정동에 균등 분배해 총합을 보존한다.
- `land_price`: 요청 법정동의 유효 필지 가격 산술평균과 표본 수를 함께 기록한다.
- `zoning`: 법정동별 immutable category-count histogram과 total sample count를 raw 값으로 보존한다. overlay 기본은 off다. 명시 opt-in 시 versioned category→ordinal 표를 알려진 category count에 적용한 count-weighted mean `ZoningOrdinalScore`를 계산한다. transform 이름/버전/범주표를 provenance에 남기며 unknown은 점수 계산에서 제외하고 알려진 표본이 0이면 결측이다.

## D-021 — 최소 snapshot과 legacy projection

- 상태: Accepted
- 결정: `AnalysisSnapshot`은 canonical code ordinal ascending district index, immutable boundary topology reference와 legacy projection reference, raw mapped values 한 벌, normalization stats, coverage/typed failures/warning severity, observation/source/unit, original retrievedAt/cache age, `AcquisitionOrigin`과 `DeliveryOrigin`을 가진다. normalized layer dictionary와 mesh는 저장하지 않고 derived cache에서 계산한다. 기존 `SolverResult`는 ordered index와 legacy largest-outer projection에서 결정적으로 생성한다.

## D-022 — Boundary topology 계산 계약

- 상태: Accepted
- 결정: `BoundaryTopology -> Parts[] -> Outer + Holes[]`를 사용한다. ring은 닫혀 있고 점이 최소 4개이며 면적이 0이 아니어야 한다. projected XY에서 outer는 CCW, hole은 CW로 parser 경계에서 정규화한다. invalid hole은 해당 hole drop+typed warning, invalid outer는 해당 part drop+typed warning, 모든 part가 invalid면 feature failure다. 면적은 모든 part의 outer 합에서 hole 합을 빼고, centroid도 동일 signed-area 가중식으로 계산한다. legacy Geometry는 가장 큰 outer만 투영하며 면적/중심 계산에는 쓰지 않는다.

## D-023 — HTTP 동시성·재시도·진단

- 상태: Accepted
- 결정: engine당 공유 pipeline의 global in-flight request-attempt 상한은 8이다. 각 retry attempt만 semaphore를 점유하고 backoff 동안은 반환한다. timeout/clock/delay는 주입 가능해야 하며 retry count와 전체 deadline은 bounded다. redaction은 로그·예외·cache identity에 적용한다. provider가 query/path key를 요구하면 실제 전송 URI에는 포함될 수 있으나 진단 surface에는 원문을 남기지 않는다.

## D-024 — 페이지 완결성과 cache 파일

- 상태: Accepted
- 결정: paged response는 provider total-count와 수신 page/row count가 정확히 맞고 모든 page가 성공하며 source별 stable composite row identity가 중복되지 않은 경우에만 complete 후보가 된다. stable identity는 source schema version과 함께 정의하고, 자연키가 없으면 필요한 projected fields의 canonical tuple을 사용한다. truncation/duplicate는 Partial/failure다. cache root는 LocalApplicationData이며 테스트 override를 제공한다. per-key lock 아래 temp file을 쓰고 flush 후 atomic replace하며 실패 temp는 정리한다.

## D-025 — Typed spatial bounds

- 상태: Accepted
- 결정: 신규 `SpatialBounds`는 좌표와 `CoordinateReferenceSystem(Epsg5179|Wgs84)`를 함께 요구한다. 분석/cache canonical bounds는 EPSG:5179, VWorld WFS 송신 bounds는 EPSG:4326으로 변환한다. 기존 `DataQuery.Bounds`는 legacy EPSG:5179로 명시하고 신규 실행은 typed bounds를 사용한다. cache identity에는 정규화된 EPSG:5179 bounds를 넣는다.

## D-026 — Phase 2 async coordinator와 supersede

- 상태: Accepted
- 결정: Core `RunAsync`가 실제 source/geometry cancellation을 소유하고, 순수 `RunCoordinator`가 generation/state/query fingerprint/last success를 소유한다. Run false 관찰 뒤 true edge가 실행 중이면 현재 generation을 cancel하고 새 generation으로 supersede한다. old completion/fault/cancel은 새 state나 last success를 변경하지 않는다. GH pre/post solve는 coordinator generation을 한 번만 시작/commit한다.

## D-027 — Phase 2 append-only port manifest

- 상태: Accepted
- Solver: input 0~11과 output 0~10 보존, `Cancel` input 12 append, `Status/Quality/Progress/Snapshot` output 11~14 append.
- Visualizer: input 0~10과 output 0~5 및 Guid 보존. input11 `Mode` integer/item/default0(0=Choropleth,1=Extrusion,2=IDW), input12 `Snapshot` generic/item/optional, input13 `Layer Id` text/item/optional(empty=기존 Values overlay), output6 `Missing Codes` list, output7 `Status` item을 append한다. Snapshot이 topology/code/raw layer/unit/provenance를 전달하며 snapshot이 없는 기존 연결은 legacy IDW fallback warning이다.

## D-028 — IDW 정확도와 resource budget

- 상태: Accepted
- 결정: global exact IDW 의미를 유지하고 kNN/radius 근사는 Phase 2에서 거부한다. preview 50k vertices, final 250k vertices/500k faces, estimated mesh working set 192MiB 상한을 적용한다. meshing 전 추정과 생성 후 actual count를 모두 검사하고 CPU loop에 token check를 둔다. legend steps 2..20이다.

## D-029 — 시각화 단위·choropleth·side effect

- 상태: Accepted
- 결정: snapshot은 meter/m² canonical이다. Rhino adapter는 native geometry 길이에 document-unit scale을 한 번 적용하며 그 geometry에서 유도되는 면적은 scale²를 따른다. Solver의 `Areas (m²)` scalar는 문서 단위와 무관하게 canonical m²로 유지한다. 문서 unit/style/resolution/weights 변경은 last snapshot derived recompute이며 HTTP 0이다. 기본은 topology-aware choropleth/extrusion, IDW는 optional exact trend mode다. CSV Export/Path는 다음 Run 성공 때만 실행하는 side effect이며 idle 변경만으로 쓰지 않는다.

## D-030 — Coordinator progress와 derived cache budget

- 상태: Accepted
- 결정: cancel terminal은 `Canceled`이며 last successful snapshot을 유지한다. progress는 generation-gated/monotonic이고 GH message update는 10Hz 이하로 제한한다. query fingerprint는 transport/force/period/spatial/dataset/key fingerprint를 포함하고 Run edge에서 side-effect inputs까지 immutable capture한다. mesh LRU는 최대 2 entries 및 총 256MiB이며 eviction 시 Rhino mesh reference를 해제한다.

## D-031 — Retry response 자원 수명

- 상태: Accepted
- 결정: `ResponseHeadersRead` retry 응답은 retry delay와 semaphore 반환 전에 response/content를 폐기한다. backoff는 connection을 점유하지 않으며 injected delay 테스트가 폐기 순서를 고정한다.

## D-032 — Phase 1 구현 승인

- 상태: Accepted
- 근거: 4차 구현 리뷰에서 BLOCKER/MAJOR 0, Release 자동 테스트 45/45, solution build 0 errors, `git diff --check` 통과. 비차단 항목은 canonical provider identity, sample/missing-ID metadata, per-key lock 회수이며 Phase 2의 독립 작업을 막지 않는다.

## D-033 — Coordinator cancellation ownership

- 상태: Accepted
- 결정: generation별 `CancellationOwner`가 active cancel count와 retire/dispose를 단일 gate에서 직렬화한다. completion은 retire만 요청하고 supersede/cancel/component dispose는 cancel만 수행하여 동일 CTS의 `Cancel`/`Dispose` 동시 실행을 구조적으로 금지한다.

## D-034 — Phase 2.0/2.1 구현 승인

- 상태: Accepted
- 근거: cancel terminal, generation 격리, stale 보존, no-op event 억제, 10Hz monotonic progress, true async/no fallback, CPU loop cancellation을 4차 리뷰에서 승인. 자동 테스트 58/58, solution build 0 errors.

## D-035 — Phase 2.2 시각화 core 구현 승인

- 상태: Accepted
- 근거: topology-aware choropleth/extrusion planner, raw/overlay 정렬, missing neutral color, unit-aware legend, exact IDW cancellation, preview/final resource budget, 소유권이 분명한 2-entry/256MiB mesh LRU를 4차 독립 리뷰에서 승인했다. Rhino native mesh 실행은 Linux에서 `rhcommon_c`를 로드할 수 없어 Windows 수동 검증으로 격리한다.

## D-036 — Phase 2.3 GH adapter 완료·취소·도메인 계약

- 상태: Accepted
- 결정: GH `CancelToken`은 generation-scoped coordinator cancellation에 연결하고, 완료 output은 pending slot에 저장한 뒤 document solution을 재예약해 pre/post solve 경쟁에서도 회수한다. query identity와 weight identity를 분리하여 실제 가중치 변경에만 derived CSV를 재계산하고, 취소/실패한 최신 세대 뒤에도 마지막 성공 snapshot을 재사용하되 표시 중 snapshot fingerprint를 coordinator에 복원한다.
- 시각화: snapshot IDW는 모든 curve를 전역 평면화하지 않고 topology part마다 `outer + holes` region을 독립 planar domain으로 만든다. 생성 전 topology budget과 cancellation을 검사하며 invalid document unit 경고를 status에 보존한다.
- 호환성: 기존 Solver/Visualizer GUID와 포트 인덱스는 유지하고 새 포트만 append한다. nullable union boundary는 GH 출력 경계에서 허용한다.

## D-037 — Phase 2 구현 승인

- 상태: Accepted
- 근거: Phase 2.0/2.1 coordinator/core async, Phase 2.2 visualization core, Phase 2.3 GH adapter를 각각 독립 리뷰 루프로 닫았다. 마지막 GH adapter 리뷰는 5차에서 BLOCKER/MAJOR 0으로 승인되었고 자동 테스트 70/70, GH Release build 0 errors, `git diff --check`를 통과했다.
- 비차단 공백: Linux에서는 실제 Rhino/Grasshopper task scheduling과 native Brep/Mesh 생성을 실행하지 못했으므로 `docs/phase0-2-outcome-and-roadmap.md`의 Windows Rhino 8 체크리스트를 Phase 3 착수 전 수행한다.

## D-038 — 최종 통합 리뷰 피드백 폐쇄

- 상태: Accepted
- 수정: built-in source cancellation 재전파, process-wide `HttpClient` 수명, zoning categorical default-off/explicit ordinal opt-in, 공시지가 explicit `stdrYear`, invalid boundary part/hole warning 및 all-invalid typed failure, canonical `Areas (m²)` 계약을 코드·PRD·테스트에서 일치시켰다.
- 추가 회귀: cache corruption/atomic recovery, normal↔force ordering, source cancellation, zoning source opt-in, LandPrice provider year, topology warning/failure, snapshot/topology immutability, 서울 missing-ID quality를 고정했다.
- 근거: 최종 통합 재리뷰에서 BLOCKER 0/MAJOR 0으로 승인. Release 자동 테스트 76/76, GH 및 전체 solution build 0 errors, `git diff --check` 통과. 실제 credential API E2E와 Windows Rhino native task/mesh는 문서화된 수동 검증 공백으로 유지한다.
