# URSUS Phase 0–2 안정화 결과와 발전 방향

## 1. 범위와 결론

이 작업은 **Phase 0 결과 신뢰성**, **Phase 1 Open API·캐시·메모리 파이프라인**, **Phase 2 Rhino/Grasshopper UX·시각화 안정성**까지만 다룬다. 설치 패키지, Yak, 서명, 릴리스 채널, 배포 자동화 등 Phase 3 제품화는 의도적으로 제외했다.

현재 URSUS는 “여러 API 값을 받아 IDW 표면을 만드는 도구”에서 **데이터의 기간·coverage·출처·결측을 보존하는 provenance-first 도시 분석 파이프라인**으로 이동했다. 자동 검증이 닫은 범위에서는 조용히 틀린 값을 만드는 주요 경로, 무제한 HTTP/mesh 자원 사용, Grasshopper UI를 막는 실행 구조를 제거했다. 다만 실제 API credential을 사용한 통합 실행과 Rhino 8 Windows native meshing은 별도 수동 검증이 필요하다.

## 2. 단계별 결과

### Phase 0 — 신뢰 가능한 기준선

- 의존성 없는 실행형 `URSUS.Tests` 회귀 테스트 기준선을 만들었다.
- 법정동 8자리, 법정동 10자리, PNU를 canonical legal ID로 통합하고 행정동은 별도 매핑으로 유지했다.
- coverage 0%는 실패, partial은 결측을 유지하며, 존재하는 레이어만 district별 재정규화한다.
- 서울 범위 밖 요청은 서울 API를 호출하기 전에 차단한다.
- Solver는 배치/문서 재열기 시 자동 실행하지 않고 `Run` false→true edge에서만 실행한다.
- 안정된 오류 코드와 현재 저장소의 오류 가이드 URL을 사용한다.

### Phase 1 — 정확한 Open API·캐시·메모리 파이프라인

- source별 집계 의미를 분리했다. 평균형은 `AssumedUniform`, 합계형은 법정동에 균등 분배해 총합을 보존하며 `EstimatedEqualSplit`, zoning category는 기본적으로 임의 ordinal overlay를 만들지 않는다.
- 서울 통계는 versioned expected leaf set과 latest closed period로 완결성을 판단하며 서로 다른 기간을 섞지 않는다.
- XML은 `XmlReader`로 필요한 필드만 스트리밍하고, pagination total/row/중복 identity가 맞아야 complete로 인정한다.
- process-wide 재사용 `HttpClient` 위에 engine별 공유 HTTP pipeline을 두고 최대 동시 요청 8, 전체 deadline, bounded retry, `Retry-After`, cancellation, retry response 즉시 dispose를 적용했다.
- HTTPS를 fail-closed로 적용하고, 서울 HTTP는 명시 opt-in과 high-severity warning이 있을 때만 허용한다. 진단과 cache key에서 secret을 redaction한다.
- cache는 `LocalApplicationData` 아래 query-keyed envelope로 저장하고, per-key coalescing, waiter cancellation, 원자적 교체, TTL/schema/corruption fallback을 갖는다.
- immutable `AnalysisSnapshot`은 raw mapped layer 한 벌, topology, normalization stats, observation, coverage, typed warning/failure, acquisition/delivery origin을 보존한다. normalized 사본과 Rhino mesh는 snapshot에 넣지 않는다.
- Polygon/MultiPolygon, island, hole을 `BoundaryTopology -> Parts -> Outer + Holes`로 보존하고 EPSG:5179 meter/m²를 canonical 단위로 사용한다. 제거된 invalid part/hole은 typed warning으로 남고 전체 invalid feature는 cache 전에 실패한다.

### Phase 2 — 반응형 Grasshopper와 bounded 시각화

- Core의 진짜 async/cancellation과 generation 기반 `RunCoordinator`를 도입했다.
- 상태는 `Idle/Running/CancelRequested/Canceled/Succeeded/Faulted`, progress, stale, quality, snapshot으로 노출된다.
- GH task cancellation이 현재 generation의 실제 source/network 실행까지 전파된다. 늦은 구세대 완료는 최신 결과를 덮지 못한다.
- task 완료 결과는 pending slot과 scheduled solution으로 회수해 Grasshopper pre/post solve 경쟁에서 유실되지 않는다.
- query 입력과 derived/view 입력을 분리했다. 가중치 변경은 마지막 snapshot으로 즉시 계산하며 HTTP 호출과 CSV side effect를 만들지 않는다.
- 기본 시각화는 district topology-aware choropleth/extrusion이고, exact IDW는 선택적 trend surface다.
- snapshot IDW는 part별 `outer + holes` region을 유지하여 떨어진 섬과 hole 소유권을 섞지 않는다.
- Rhino document 단위 변환은 adapter 경계에서 길이에 한 번 적용하고, 면적 출력은 canonical m²를 유지한다.
- legend는 2–20 step, normalized 소수와 raw unit-aware 표기, all-equal/missing/NaN을 처리한다.
- mesh는 preview 50k vertices, final 250k vertices/500k faces, 예상 working set 192MiB 상한을 검사한다. LRU는 최대 2 entries/256MiB이며 eviction/dispose ownership을 갖는다.

## 3. Open API → Rhino 파이프라인 평가

### 사용자 친화성

| 항목 | 기존 상태 | Phase 2 종료 상태 | 남은 일 |
|---|---|---|---|
| 실행 제어 | 배치/입력 변경이 무거운 작업을 유발할 수 있음 | 명시적 Run edge, Cancel, generation 격리 | Windows Rhino에서 버튼/메시지 체감 QA |
| 진행 가시성 | 블로킹 또는 불명확 | state, stage, monotonic progress, stale 표시 | source별 예상 잔여시간은 향후 항목 |
| 데이터 신뢰 | 결측·기간·대치가 결과에 가려짐 | coverage, 기간, cache age, origin, warning/failure가 snapshot/quality에 남음 | 캔버스용 상세 진단 패널 |
| 반복 탐색 | 가중치 변경이 전체 실행과 얽힘 | weight/view 변경은 snapshot derived 계산, HTTP 0 | 비교 시나리오 preset과 undoable parameter set |
| 시각화 기본값 | IDW가 행정구역 값을 연속장처럼 과해석 가능 | choropleth 기본, IDW는 명시적 trend mode | categorical zoning 전용 renderer |
| 하위호환 | 포트 변경 위험 | 기존 GUID/포트 유지, append-only 확장 | 실제 기존 `.gh` 문서 열기 회귀 |

정성적으로는 **실행 통제와 결과 설명성은 실험용 플러그인에서 반복 분석 가능한 도구 수준으로 개선**되었다. 아직 설치·온보딩·credential UX와 실제 Rhino 상호작용 검증을 하지 않았으므로 “최종 사용자 제품” 수준으로 평가하면 안 된다.

### 메모리·성능 효율

- 전체 XML row dictionary 적재를 스트리밍 projection/aggregation으로 바꿨다.
- engine별 공유 HTTP pipeline과 bounded concurrency로 socket와 동시 응답 메모리를 제한했다.
- 동일 cache key origin fetch는 coalesce되고 마지막 waiter가 이탈하면 실제 fetch를 취소한다.
- snapshot은 raw 값과 topology만 보존하며 normalized layer와 mesh 사본을 중복 저장하지 않는다. 서울 기간 품질에는 expected count뿐 아니라 missing ID 목록도 남긴다.
- mesh 생성 전 추정치와 생성 후 실제 vertex/face를 모두 검사한다.
- visual cache는 entry/byte 이중 상한과 명시적 native mesh dispose를 사용한다.
- exact IDW의 계산 복잡도 `O(vertices × samples)`는 의미 보존을 위해 유지했다. 대신 preview budget, cancellation, cache, choropleth 기본값으로 폭주 위험을 통제한다.

따라서 메모리 사용은 **무제한·중복 중심에서 bounded·소유권 중심으로 전환**되었다. 실제 peak working set은 Rhino native allocator와 문서 복잡도에 좌우되므로 Windows profiler 측정이 다음 근거가 되어야 한다.

## 4. 수정한 고위험 버그/장애 요소

- 행정동 코드를 법정동 접두부로 오인하거나 8/10자리/PNU 비교가 어긋나는 조용한 0건 결과
- 요청 지역 실측값이 없는데 평균 대치값으로 정상처럼 보이는 layer 생성
- additive 지표를 여러 법정동에 복제해 총합이 부풀어 오르는 mapping
- 기간 혼합, pagination 누락/중복, provider total mismatch를 complete로 오인하는 경로
- API key가 로그·예외·cache identity에 남는 경로와 HTTPS 미지원 source의 암묵적 사용
- force refresh 실패가 이전 유효 cache를 파괴하거나 동시 요청이 origin fetch를 중복 실행하는 경로
- MultiPolygon/hole 손실과 잘못된 CRS/단위 적용
- Run true 지속, invalid edge, superseded generation, cancel/fault 경쟁에서 stale/결과가 뒤바뀌는 경로
- GH task 취소가 실제 network execution을 취소하지 않거나 완료 output이 solve scheduling 경쟁으로 유실되는 경로
- CSV 성공 직후 동일 가중치 derived recompute가 Saved Path를 지우는 경로
- 모든 IDW outer/hole을 한 planar set으로 평탄화해 part 소유권이 섞이는 경로
- 무제한 resolution/legend/cache로 UI freeze 또는 native 메모리 폭주가 가능한 경로

## 5. 권장 제품 성격과 개발 우선순위

### 제품 성격

URSUS는 범용 GIS를 복제하기보다 **Rhino/Grasshopper 안에서 공공 도시 데이터를 설계 의사결정에 연결하는, 출처와 불확실성을 설명할 수 있는 분석 workbench**로 발전시키는 편이 적합하다.

핵심 제품 원칙은 다음 네 가지다.

1. **Provenance before pixels** — 지도보다 먼저 기간, 단위, coverage, 원천/캐시, mapping assumption을 보존한다.
2. **Explicit expensive work** — 네트워크·대형 geometry는 명시 실행과 취소가 있고, slider 조작은 가능한 한 snapshot derived 계산으로 끝낸다.
3. **Administrative truth by default** — district 값은 choropleth가 기본이며 IDW는 사용자가 연속장 가정을 선택한 경우에만 사용한다.
4. **Bounded by contract** — HTTP, page, retry, cache, mesh, legend에 명시적 상한과 진단을 둔다.

### Phase 3 전에 더 다듬을 우선순위

1. **Windows Rhino 8 수동 회귀 자동화 준비**: 기존 `.gh` 파일 open, Run/Cancel/supersede, unit 변경, document close/dispose, large topology preview를 체크한다.
2. **실 API contract fixture**: credential을 저장하지 않는 sanitized response fixture와 provider schema drift 테스트를 source별로 만든다.
3. **품질 진단 UX**: coverage/period/cache/provenance/failure를 한눈에 보는 별도 read-only panel 또는 structured tree output을 설계한다.
4. **categorical zoning 표현**: ordinal score 대신 category composition/legend를 보존하는 전용 renderer를 추가한다.
5. **시간 비교**: 동일 지역·동일 source의 두 observation window snapshot diff를 first-class derived operation으로 만든다.
6. **시각 접근성**: color-blind-safe palette, missing hatch/outline, legend localization과 단위 설명을 검증한다.
7. **성능 계측**: source latency, cache hit, parsed rows, topology points, mesh vertices/bytes를 secret-free diagnostic record로 노출한다.

## 6. 검증 결과

- Release 자동 회귀 테스트: **76/76 통과**
- `URSUS.GH` Release build: **0 errors**
- Setup을 포함한 전체 solution Release build: **0 errors**
- `git diff --check`: 통과
- 독립 최종 통합 리뷰: **Blocker 0 / Major 0, APPROVED**

## 7. 남은 위험과 수동 검증 체크리스트

### 알려진 검증 공백

- 실제 credential을 사용한 VWorld/서울/공공데이터 end-to-end 호출은 실행하지 않았다.
- Linux에서는 Rhino native `rhcommon_c`를 사용할 수 없어 실제 Brep/Mesh 생성·dispose를 실행하지 못했다.
- RhinoCommon/Grasshopper/System.Windows.Forms 패키지가 net7 target에서 NU1701 compatibility warning을 낸다.
- `net7.0-windows` target은 SDK에서 지원 종료(EOL) 경고를 낸다. framework 전환은 D-006에 따라 별도 변경으로 격리했다.
- 기존 WinForms 코드의 nullable/Windows-only analyzer warning이 남아 있다.
- 설치, 서명, Yak, 배포, 업그레이드 경로는 Phase 3로 보류했다.

### Rhino 8 Windows 체크리스트

- [ ] 기존 샘플 `.gh` 문서가 GUID/기존 포트 연결을 유지한 채 열린다.
- [ ] 문서를 열 때 Run=true가 저장되어 있어도 자동 network fetch를 시작하지 않는다.
- [ ] Run false→true 한 번에 generation 하나만 시작하고 progress가 감소하지 않는다.
- [ ] Cancel은 현재 실행만 취소하고 마지막 성공 결과를 유지한다.
- [ ] 실행 A 뒤 B를 시작해 A가 늦게 끝나도 B/최신 상태를 덮지 않는다.
- [ ] 실행 중 Run을 false로 바꿔도 성공 completion이 pending recovery를 통해 출력된다.
- [ ] weight만 변경하면 HTTP 0이며 CSV 파일을 다시 쓰지 않는다.
- [ ] Export CSV 성공 직후 Saved Path가 유지된다.
- [ ] mm/m document 전환 시 curve/centroid/mesh 길이만 올바르게 변하고 Areas는 m²를 유지한다.
- [ ] multipart island와 hole이 choropleth, extrusion, IDW에서 보존된다.
- [ ] missing district는 중립색/목록으로 표시되고 raw unit legend가 맞다.
- [ ] 과대 resolution/topology는 OOM 대신 budget 오류를 반환한다.
- [ ] component/document 제거 중 cancel/mesh dispose가 crash 없이 끝난다.

## 8. Phase 3로 명시 이월

다음 항목은 이번 변경에 포함하지 않았다: 설치 패키지 구조, Yak/마켓 배포, 코드 서명, 릴리스 채널, 자동 업데이트, telemetry/consent, credential migration UI, 버전 업그레이드 정책, 배포 CI. 이 절차와 방식은 별도 제품화 논의에서 결정한다.
