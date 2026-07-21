# URSUS Phase 0–2 안정화 PRD

## 1. 목표

Open API 원천 데이터가 Rhino/Grasshopper 결과로 변환되는 과정에서 **조용히 틀린 결과를 만들지 않고**, 대용량 입력에서도 **메모리와 UI 반응성을 통제**하며, 사용자가 실행·취소·품질 상태를 이해할 수 있는 상태까지 개선한다.

이 문서의 완료 범위는 Phase 0, Phase 1, Phase 2다. Phase 3 제품화, 설치 패키지, Yak/배포 채널, 릴리스 절차는 포함하지 않는다.

## 2. 성공 기준

- 의존성 없는 실행형 회귀 테스트 프로젝트로 핵심 데이터 계약과 회귀 시나리오를 재현할 수 있다.
- VWorld 법정동 8자리, 공공 API 법정동 10자리 및 PNU가 동일한 canonical legal-district ID로 비교된다. 행정동과 법정동은 별도 매핑 엔터티로 유지한다.
- 조회 대상 지역에 실측 데이터가 없을 때 평균값으로 정상처럼 보이는 오버레이를 만들지 않는다.
- 데이터 소스별 집계 의미가 명시되며, 합계형 지표를 단순 복제하지 않는다.
- HTTP 요청은 공유 클라이언트, 취소 토큰, 제한된 동시성, 스트리밍/필드 투영을 사용한다.
- 캐시는 쓰기 가능한 사용자 경로, 원자적 저장, TTL/스키마/손상 복구 계약을 갖는다.
- Grasshopper Solver는 배치 즉시 네트워크를 호출하지 않고 명시적 Run에서만 실행한다.
- 장시간 실행은 UI 스레드를 점유하지 않으며 취소와 상태 메시지를 제공한다.
- 시각화는 단위, 모델 스케일, 값 범위, 결측/동률 상태를 오해 없이 표시한다.
- Phase별 계획 및 구현이 독립 리뷰를 통과하고, 리뷰 지적이 해결되거나 의사결정 로그에 격리된다.

## 3. 비목표

- 설치 프로그램/CI 배포 아티팩트/Yak 패키지 설계와 배포 실행
- 신규 외부 데이터셋 추가
- 사용자 계정, 클라우드 동기화, 텔레메트리
- Rhino 7 호환성 확대
- 완전한 디자인 시스템이나 다국어 제품화

## 4. 실행 원칙

1. 각 Phase는 `계획 → 리뷰 → 수정 → 테스트(RED) → 구현(GREEN) → 정리 → 리뷰 → 검증`으로 닫는다.
2. 기존 동작이 불명확한 리팩터링은 먼저 특성화 테스트를 추가한다.
3. 사용자에게 정상처럼 보이는 잘못된 결과는 실패보다 위험한 P0로 취급한다.
4. 새 패키지는 추가하지 않고 BCL 및 기존 의존성을 우선한다.
5. 외부 API 키 또는 Rhino Windows 런타임이 필요한 검증은 별도 수동/통합 체크리스트로 남기되, 순수 로직은 테스트 가능한 경계로 추출한다. 테스트 기반선은 새 패키지를 추가하지 않는 `net8.0` console runner와 fixture/fake `HttpMessageHandler`다. 제품 타깃 net7 변경과는 분리한다.
6. 결정이 필요한 항목은 `.omx/plans/decision-log-phase0-2.md`에 기록하고, 해당 결정과 무관한 작업은 계속한다.

## 5. Phase 0 — 결과 신뢰성과 개발 기준선

### 5.1 문제

- 솔루션이 존재하지 않는 테스트 프로젝트를 참조한다.
- 법정동 코드 자릿수/PNU 해석 불일치가 공시지가/용도지역 값을 조용히 0건으로 만든다.
- 서울 데이터가 없는 지역에 평균 대치값을 채워 유효한 분석처럼 보일 수 있다.
- 실행 기본값만으로 컴포넌트 배치 시 네트워크 작업이 시작된다.
- 잘못된 오류 가이드 URL, 캐시 저장 위치, 버전/출력 경로 혼선이 진단을 방해한다.

### 5.2 범위

- 실제 `URSUS.Tests` 프로젝트를 만들고 핵심 순수 로직 회귀 테스트를 시작한다.
- 법정동 코드의 canonical form과 비교 규칙을 한 곳에 정의하고, 행정동↔법정동은 embedded mapping fixture로 별도 검증한다.
- coverage는 `유효 finite raw 값이 있는 요청 법정동 수 / 요청 법정동 수`로 계산한다. 0% 레이어는 실패, 0% 초과 100% 미만은 partial warning과 결측 유지, 100%는 complete다. 결측 셀의 오버레이는 존재하는 레이어 가중치만 재정규화하고, 어떤 값도 없으면 NaN과 경고를 반환한다.
- Solver 컴포넌트에 `Run` 입력(기본 false)과 빈 지역 조기 반환을 추가한다.
- 서울 데이터 소스는 요청 코드가 서울 범위가 아니면 네트워크 호출 전에 `UnsupportedCoverage`로 종료한다.
- 오류 가이드 링크와 테스트 가능한 고정 오류 코드 계약을 바로잡고 런타임 `GetHashCode()` 기반 코드를 제거한다.
- 빌드 기준선은 로컬 프로젝트/솔루션 빌드까지로 한정한다. 설치·배포 경로 통합은 Phase 3으로 보류한다.

### 5.3 인수 조건

- 솔루션에서 테스트 프로젝트가 실제로 복원·빌드·실행된다.
- VWorld 법정동 8자리와 법정동 10자리/PNU가 동일 지역일 때 일치하며, 행정동 코드를 접두부로 오인하지 않는다.
- 데이터가 없는 대상 지역은 값 `0.5`의 균질 레이어로 생성되지 않는다.
- 서울 외 요청은 서울 원천 데이터 다운로드를 시작하지 않는다.
- Run=false에서 Solver/HTTP/파일 캐시 작업이 발생하지 않는다.
- Phase 0 대상 테스트가 통과하고 Core/GH 프로젝트가 빌드된다.

## 6. Phase 1 — 데이터 정확성, API 및 메모리 효율

### 6.1 문제

- 행정동 값을 법정동마다 복제하여 합계형 지표가 부풀 수 있다.
- 서울 XML 전체 행과 모든 필드를 메모리에 적재하고 최대 40개 요청을 블로킹 실행한다.
- 파서별 `HttpClient` 생성, 취소 미전파, 재시도/동시성 정책 부재가 있다.
- 캐시가 DLL 디렉터리에 있고 원자성, 스키마, TTL, 잠금, 손상 복구가 없다.
- Registry 싱글턴이 최초 API 키를 캡처한다.
- Polygon/MultiPolygon 깊이, hole/island, 좌표계/단위 계약이 약하다.
- 기간별 행을 무차별 평균하고 관측시점이 결과/캐시 키에 없다.

### 6.2 범위

- 데이터셋마다 집계 의미(`Mean`, `Sum`, `Rate`, `Category`)와 행정동→법정동 배분 정책을 명시한다. 합계형은 매핑 대상에 균등 분배하여 총합을 보존하고 `EstimatedEqualSplit`, 평균/비율은 동일값을 전파하되 `AssumedUniform`, 범주는 임의 숫자화를 금지한다. 여러 행정동이 한 법정동에 모일 때 합계는 합산하고 평균/비율은 단순 평균을 사용해 품질 플래그를 남긴다.
- `ObservationWindow`와 source별 latest-closed-period 선택 정책을 도입한다. 완결성은 응답 내부 최대치가 아니라 versioned embedded 서울 행정동 leaf 기대집합(현재 426개)과 finite observed ID를 비교한다. 선택 기간은 query, cache identity, provenance에 포함하며 기간 행 전체를 무차별 평균하지 않는다. 상세 규칙은 D-013을 따른다.
- XML 처리를 `XmlReader` 기반 스트리밍과 필요한 필드 투영으로 변경한다.
- pagination은 `list_total_count`와 모든 계획 page range의 성공/총 row count 일치를 확인한 뒤에만 기간 selector를 실행한다. 페이지 누락/중복/절단은 complete로 판정하지 않는다.
- 공유 `HttpClient`, `CancellationToken`, 제한된 동시성, timeout, 429/5xx bounded retry 계약을 도입한다. LandPrice/Zoning은 HTTPS만 사용한다. TLS를 제공하지 않는 서울 공식 endpoint는 기본 차단하고 `allowInsecureSeoulHttp` 명시 opt-in에서만 사용하며 매 실행 high-severity warning을 snapshot에 남긴다. URI/진단 로그에서 키를 마스킹한다.
- 캐시를 사용자 LocalApplicationData 아래로 옮긴다. persistent lookup identity는 `source + schema + QueryIntent(latest|explicit period) + canonicalized all data-affecting query fields + CRS`이며 refresh mode는 포함하지 않는다. in-flight coalescing key만 `(PersistentCacheKey, RefreshMode)`로 분리한다. 동일 persistent key의 origin read/fetch/write 전체는 직렬화한다: non-force miss 뒤 force가 대기하면 non-force 완료 후 force가 다시 fetch하여 최종값을 교체하고, force 뒤 non-force는 갱신 cache를 읽는다. 실패한 force는 기존 valid entry를 보존한다. `ForceRefresh`는 persistent read를 우회하지만 성공한 응답으로 같은 persistent entry를 원자 교체하므로, 이후 non-force 조회도 갱신값을 읽는다. resolved `ObservationWindow`, 최초 `retrievedAt`, `AcquisitionOrigin`, 현재 `DeliveryOrigin`, cache age는 envelope에 저장하고 cache hit에서 다시 쓰지 않는다. boundary는 geocoded normalized BBOX/radius 또는 ordered BBOX, 통계는 sorted district IDs, typed transport policy와 source parameters를 포함하며 culture/parameter order에 독립적이다. secret은 identity에서 제외한다. per-key async lock, 요청 coalescing, 원자적 쓰기, TTL, schema version, corruption fallback을 구현한다.
- Registry는 `AnalysisEngine` 인스턴스마다 새 `DataSourceRegistry`를 구성한다. `HttpPipeline`과 `CacheService`만 engine 내부에서 공유한다. static `DataSourceRegistryProvider`는 legacy catalog/명시 주입에만 남기고 기본 Solver 실행 경로에서 사용하지 않는다.
- Phase 2가 소비할 최소 immutable `AnalysisSnapshot`을 선행한다. snapshot은 ordered canonical district index, immutable boundary topology reference와 legacy projection, layer별 mapped raw 값 한 벌, normalization stats, coverage, typed source failure, warning severity, observation window, source, unit, 최초 retrievedAt/cache age/origin, quality flags를 가진다. normalized dictionary는 저장하지 않고 derived 계산하며 Rhino mesh는 별도 geometry cache에 둔다. 기존 `SolverResult`는 snapshot의 legacy projection이다.
- boundary topology는 `BoundaryTopology -> Parts[] -> Outer + Holes[]`로 표현한다. 기존 단일 `Geometry`는 가장 큰 outer를 반환하는 legacy projection으로 유지하고, 기존 GH 출력 인덱스/ComponentGuid는 보존한다. 캐시 schema를 올려 구형 geometry cache는 재조회한다.
- GeoJSON Polygon/MultiPolygon을 topology 모델로 안전하게 파싱하고 외곽/내곽 ring과 다중 섬을 보존한다.
- 분석 CRS는 EPSG:5179로 고정하고 snapshot에 기록하며, Rhino 출력 경계에서 문서 단위로 변환한다.
- Solver는 선택된 데이터셋과 boundary source의 `RequiredApiKeys` 합집합만 요구한다. 사용하지 않는 source의 키는 실행 차단 조건이 아니다.
- radius는 finite `0 < r <= 100km`여야 한다. numeric `DataQuery.Bounds`의 NaN/empty/invalid는 WFS 전에 실패한다. address pair의 순서는 오류가 아니며 geocode 2회 후 min/max BBOX로 정규화한다.

### 6.3 인수 조건

- 스트리밍 파서는 전체 응답 행 딕셔너리 목록을 만들지 않는다.
- 동시 HTTP 요청 수가 구성된 상한을 넘지 않고 취소가 실제 요청까지 전파된다.
- 캐시 파일이 부분 기록돼도 다음 실행에서 복구하거나 재조회한다.
- 서로 다른 query subset/기간/CRS가 같은 cache entry를 공유하지 않고, 동시 요청은 한 번만 원천을 호출한다.
- 서로 다른 boundary address/radius/BBOX와 transport policy는 같은 cache entry를 공유하지 않는다.
- `ForceRefresh`는 유효 캐시를 우회한다.
- 키 변경 후 새 요청은 새 키 구성을 사용한다.
- Polygon/MultiPolygon/hole fixture가 손실 없이 파싱된다.
- 데이터 집계 계약 테스트가 additive/non-additive 지표를 구분한다.
- snapshot이 coverage, 기간, 단위, provenance, partial failure를 구조적으로 전달한다.
- 테스트/진단 로그에 API 키 원문이 나타나지 않는다.
- 서울시청 `(126.9784, 37.5665)`이 EPSG:5179 `(953936.490, 1952031.885)`에 1m 이내, 부산시청 `(129.0756, 35.1796)`이 `(1143467.380, 1688281.982)`에 1m 이내로 변환된다.

## 7. Phase 2 — Rhino/Grasshopper UX와 시각화 안정성

### 7.1 문제

- `SolveInstance`에서 네트워크와 기하 연산을 동기 실행해 UI를 멈춘다.
- 실행, 진행, 취소, stale cache, coverage, provenance가 사용자에게 보이지 않는다.
- 가중치/키/CSV 경로가 중복되고 불필요한 재계산이 발생한다.
- IDW는 O(vertices × points), 제한 없는 해상도, 다중 mesh 복제로 메모리/시간 폭주 가능성이 있다.
- 범례가 0–1 값을 `N0`로 표시하고 동률 값 처리와 컬러 정규화가 일관되지 않다.

### 7.2 범위

- P2.0: Core에 `RunAsync(AnalysisRequest, CancellationToken, IProgress<AnalysisProgress>)`를 추가하고 모든 source/boundary await와 CPU loop에 token을 전달한다. cancellation은 fallback/partial success로 바꾸지 않고 재throw한다. snapshot은 defensive-copy된 성공/실패/품질/provenance를 보존한다.
- P2.1: GH와 독립된 `RunCoordinator`가 `Idle/Running/CancelRequested/Canceled/Succeeded/Faulted`, generation, query fingerprint, stale flag, last successful snapshot을 소유한다. 새 유효 Run edge는 진행 중 generation을 취소하고 supersede하며 old completion/cancellation/progress는 최신 상태나 마지막 성공 출력을 변경할 수 없다. cancel terminal은 `Canceled`이며 last success를 유지한다. progress는 generation-gated, monotonic, UI update는 최대 10Hz로 throttle한다. component dispose/document removal도 현재 generation을 취소한다. GH task adapter는 pre-solve에서 한 번 시작하고 post-solve에서 같은 generation 결과만 commit한다.
- `GH_TaskCapableComponent<T>`와 Core async API를 사용해 네트워크/연산을 UI 스레드 밖에서 수행하고 취소/세대 번호로 오래된 결과를 폐기한다.
- Solver 기존 입력 0~11과 출력 0~10, `ComponentGuid`는 그대로 유지한다. `Cancel`은 input 12에만 append하고 status/quality/progress/snapshot 출력은 11 이후에 append한다. Visualizer도 기존 입력 0~10, 출력 0~5, `ComponentGuid`를 보존하고 input11 `Mode` integer/item/default0(0=Choropleth,1=Extrusion,2=IDW), input12 `Snapshot` generic/item/optional, input13 `Layer Id` text/item/optional(empty=기존 Values overlay)를 append한다. Snapshot이 topology/codes/raw layer/unit/provenance를 전달한다. output6 `Missing Codes` list, output7 `Status` item을 append한다.
- `Run`은 false를 관찰한 뒤 true로 바뀌는 rising-edge에서만 원천/query 분석을 실행한다. 문서 reopen 당시 true나 query-changing 입력(address/dataset/period/radius/key) 변경은 자동 원천 조회하지 않으며 새 Run edge가 필요하다. derived/view 입력(weights/style/resolution/legend)은 마지막 immutable snapshot에서 즉시 재계산하며 HTTP 호출은 0이다. `Cancel`도 rising-edge이며 현재 generation만 취소한다.
- 입력 변경 debounce, 명시적 Run, 진행 상태, 오류/경고/coverage/cache age/provenance 출력을 제공한다.
- 원천 수집/매핑/가중 오버레이/메시 생성을 분리 캐시하여 가중치 변경은 네트워크를 유발하지 않는다.
- 기본 지도 표현은 기존 Visualizer 안의 행정구역 choropleth/extrusion 모드로 하고 IDW는 선택적 trend surface로 위치시킨다. 신규 패키지/템플릿/마이그레이션 UI는 만들지 않는다.
- IDW는 정확한 global 계산을 유지한다. 근접 후보 축소는 수치 의미가 달라져 Phase 2에서 사용하지 않는다. preview는 최대 50,000 vertices, final은 250,000 vertices/500,000 faces/추정 192MiB이며 meshing 전 추정 초과를 거부하고 생성 후 actual count도 재검증한다. loop는 cancellation을 검사하고 snapshot/topology/mode/resolution/unit-scale key로 LRU mesh를 재사용한다. LRU는 최대 2 entries/총 256MiB이고 초과 시 oldest mesh를 dispose/evict한다.
- query fingerprint는 transport policy, force, period, address/radius/bounds, dataset/key fingerprint 등 모든 data-affecting field를 포함한다. Run edge 시 request와 CSV side-effect input을 immutable capture한다.
- 기본 Mode는 choropleth/extrusion이다. district code ordinal 정렬과 topology part/hole, missing/NaN 중립색, raw/overlay 선택, 음수·높이 clamp를 검증한다. district geometry가 없는 legacy 연결은 IDW fallback warning을 낸다.
- snapshot 좌표/면적은 EPSG:5179 meter/m²로 유지한다. Rhino native geometry의 길이는 adapter 경계에서 document-unit scale을 한 번 적용하므로 그 geometry에서 유도한 면적은 자연히 scale²가 된다. 반면 Solver의 명시적 `Areas (m²)` scalar 출력은 문서 단위와 무관하게 canonical m²를 유지한다. 문서 unit 변경은 HTTP 0으로 geometry만 재생성한다.
- legend steps는 2..20, normalized 값은 소수, raw 값은 unit-aware adaptive invariant format을 쓴다. empty/NaN/missing/all-equal을 구조적으로 처리한다.
- 범례 숫자 형식, all-equal/empty/NaN 값, 단위와 모델 스케일 처리를 수정한다.
- 중복 기능 정리는 기존 파일 삭제보다 먼저 호출 경로와 직렬화 하위호환을 테스트한다.

### 7.3 인수 조건

- 컴포넌트 배치 및 Run=false 상태에서 UI와 네트워크가 유휴다.
- 실행 중 취소 후 완료된 이전 작업이 최신 출력을 덮어쓰지 않는다.
- 문서 reopen, Run=true 상태의 다른 입력 변경, cancel/re-run 상태 전이가 정의된 테스트를 통과한다.
- 기존 GH 입력/출력 인덱스와 ComponentGuid가 유지된다.
- 가중치만 변경할 때 마지막 snapshot으로 overlay가 다시 계산되고 HTTP 요청 수가 0이다.
- 해상도/범례 입력 극단값이 clamp되며 OOM 유발 크기를 거부한다.
- 0–1 값 범례가 의미 있는 소수로 표시되고 all-equal 값은 일관된 중립색을 사용한다.
- IDW 최적화 결과가 기존 naive 계산과 epsilon 내에서 일치한다.
- Rhino Windows 수동 체크리스트에서 캔버스 반응, 상태 메시지, 모델 단위를 검증할 수 있다.
- CSV Export/Path는 query나 derived 계산이 아니라 Run completion에 결합된 side effect다. idle 상태에서 값만 바꿔 파일을 쓰지 않으며 다음 Run 성공 시에만 적용한다.

## 8. 최종 인수 산출물

- 변경 코드와 테스트
- Phase별 리뷰 기록 및 해결 상태
- 의사결정 로그
- 자동 검증 결과와 Rhino/외부 API 미검증 항목
- Phase 3 제품화로 이월할 항목 목록(배포 방식 결정은 포함하지 않음)
