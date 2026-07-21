# URSUS Phase 0–2 테스트 명세

## 1. 테스트 계층

### A. 순수 단위 테스트 — 항상 실행

- 법정동 8자리/10자리/PNU canonicalization과 행정동↔법정동 fixture 매핑
- min-max 정규화, all-equal, NaN/Infinity, 결측 coverage
- 데이터셋 집계 의미와 행정동→법정동 배분
- 가중치 정규화 및 활성 레이어 대응
- GeoJSON Polygon/MultiPolygon/hole 변환
- observation window 선택, 캐시 identity/envelope/TTL/schema/corruption/atomic replacement
- 범례 값 포맷 및 해상도 clamp

### B. 계약 테스트 — 로컬 fixture와 fake handler 사용

- 서울 XML streaming parser: paging, 필드 투영, latest closed period와 completeness 보고, 오류 응답, cancellation
- VWorld/공공데이터 파서: URL 인코딩, 8/10자리 코드, rate limit/retry
- 공유 HTTP 파이프라인: 동시성 상한, timeout, cancellation, bounded retry, HTTPS, URI/로그 secret redaction
- Registry: 서로 다른 키 provider 간 오염 없음
- Solver: coverage 수식/partial policy, 서울 외 preflight network 0, snapshot provenance

### C. 프로젝트 검증 — 항상 실행

1. `dotnet run --project src/URSUS.Tests/URSUS.Tests.csproj -c Release`
2. `dotnet build src/URSUS/URSUS.csproj -c Release`
3. `dotnet build src/URSUS.GH/URSUS.GH.csproj -c Release`
4. 가능한 환경에서는 `dotnet build URSUS.sln -c Release`

### D. Windows + Rhino 8 수동/통합 검증 — 환경 있을 때 실행

- Solver 배치 직후 Run=false, 네트워크 0, UI freeze 없음
- Run=true → 진행 상태 → 결과 → Run=false 안정 상태
- 실행 중 취소와 입력 변경 시 stale result 폐기
- Rhino 문서 단위(m/mm)에 따른 동일 실세계 거리
- choropleth, optional IDW, 범례, 결측 표시
- 대형 영역/고해상도 입력 clamp와 경고

### E. 실제 API smoke — 키가 있을 때만 실행

- VWorld 주소/경계 1건
- 서울 소득/상주인구/교통 각 소규모 페이지
- 공시지가/용도지역은 지원 상태일 때 1개 법정동
- 응답 로그에 API 키가 노출되지 않는지 확인

## 2. Phase 0 RED 목록

| ID | 테스트 | 실패 기준 |
|---|---|---|
| P0-01 | `Legal8_Legal10_AndPnuCanonicalizeToSameLegalId` | 같은 법정동 표현이 불일치 |
| P0-02 | `AdministrativeCode_IsNotTreatedAsLegalPrefix` | 행정동을 법정동 접두부로 동일시 |
| P0-03 | `EmbeddedMapping_MapsAdministrativeToDeclaredLegalIds` | fixture와 다른 행정↔법정 매핑 |
| P0-04 | `PartialCoverage_PreservesMissingAndRenormalizesPerDistrict` | 평균 대치 또는 전역 가중치 왜곡 |
| P0-05 | `ZeroCoverage_RejectsLayer` | 0% 레이어가 활성 처리 |
| P0-06 | `EqualValues_NormalizeToNeutral` | NaN/최대색/불안정 결과 |
| P0-07 | `MissingRequestedLayer_IsReported` | 레이어가 조용히 제외됨 |
| P0-08 | `NonSeoulRequest_DoesNotFetchSeoulSource` | 서울 외 요청이 네트워크 호출 |
| P0-09 | `RunFalse_DoesNotCreateSolver` | GH 추출 가능한 실행 정책이 true |
| P0-10 | `ErrorGuide_UsesCurrentRepository` | 오래된 조직 URL 유지 |
| P0-11 | `SourceErrorCode_IsStableAcrossProcesses` | `GetHashCode()` 기반 코드 |

## 3. Phase 1 RED 목록

| ID | 테스트 | 실패 기준 |
|---|---|---|
| P1-01 | `SumMetric_IsNotDuplicatedAcrossLegalDistricts` | 원값이 각 법정동에 복제 |
| P1-02 | `MeanMetric_MapsWithDeclaredPolicy` | 정책과 다른 평균/가중 처리 |
| P1-03 | `LatestClosedPeriod_IsSelectedAndCompletenessReported` | 기간 전체 무차별 평균/기간 누락 또는 older complete로 후퇴 |
| P1-04 | `XmlParser_ProjectsOnlyRequiredFields` | 전체 행 dictionary/list 적재 |
| P1-05 | `HttpPipeline_RespectsMaxConcurrency` | 동시 요청이 상한 초과 |
| P1-06 | `Cancellation_StopsInFlightRequest` | 호출자가 취소 후 요청 계속 |
| P1-07 | `Retry_IsBoundedAndHonorsRetryAfter` | 무한/즉시 재시도 |
| P1-08 | `DataGoKrFailsClosedWithoutHttpsAndAllSecretsAreRedacted` | data.go.kr HTTP fallback 또는 URI/로그/예외의 키 원문 노출 |
| P1-09 | `CacheIdentity_IncludesQueryPeriodAndCrs` | 다른 요청이 같은 캐시 공유 |
| P1-10 | `ForceRefresh_ReplacesPersistentEntryUsedByNextNormalRead` | force가 stale 캐시를 읽거나 이후 non-force가 갱신 전 값을 재사용 |
| P1-11 | `ConcurrentSameKeyFetch_IsCoalesced` | stampede/동시 writer |
| P1-12 | `CorruptCacheRefetchesAtomicallyWithoutTemporaryFiles` | 예외로 전체 분석 중단 |
| P1-13 | `CorruptCacheRefetchesAtomicallyWithoutTemporaryFiles` | 부분 파일이 유효 캐시로 노출 |
| P1-14 | `ChangedKeys_DoNotReuseOldRegistrySources` | 이전 키 URL 호출 |
| P1-15 | `GeoJson_PreservesMultiPolygonAndHoles` | 섬/hole 손실 또는 depth 예외 |
| P1-16 | `Snapshot_ReportsCoveragePeriodUnitOriginAndWarnings` | Phase 2용 품질 계약 누락 |
| P1-17 | `Wgs84_ToEpsg5179_MatchesKnownControlPointsWithinOneMeter` | 전국 좌표계 왜곡/zone 불연속 |
| P1-18 | `SnapshotAndCache_AreTaggedEpsg5179` | CRS provenance/identity 누락 |
| P1-19 | `SeoulPeriodPolicy_PrefersLatestClosedPartialOverOlderComplete` | older complete로 후퇴하거나 서로 다른 기간 혼합 |
| P1-20 | `CurrentStateSources_UseRetrievedAtInsteadOfFakePeriod` | boundary/zoning에 허위 관측기간 |
| P1-21 | `ProductionHttp_IsHttpsOrExplicitlyOptedIn` | HTTP 묵시 fallback |
| P1-22 | `UnusedSourceKey_IsNotRequired` | 선택하지 않은 dataset 키 누락으로 차단 |
| P1-23 | `InvalidRadiusOrNumericBounds_FailBeforeFetch_AndAddressOrderNormalizes` | NaN/0/100km 초과 WFS 호출 또는 역순 주소 거부 |
| P1-24 | `PeriodCompleteness_UsesEmbeddedExpectedLeafSetAndInjectableClock` | 부분 응답을 complete로 오판/current open period 선택 |
| P1-25 | `InsecureSeoulOptIn_PropagatesQueryToSnapshotWarning` | 기본 HTTP 호출 또는 경고 유실 |
| P1-26 | `CacheKey_CanonicalizesAllQueryFieldsIndependentOfOrderAndCulture` | parameter 순서/locale에 따른 cache miss/collision |
| P1-27 | `BoundaryCacheKey_DiffersByAddressRadiusAndBbox` | 서로 다른 공간 요청 cache collision |
| P1-28 | `RefreshModes_DoNotCoalesceAndLastWaiterCancelsOrigin` | force/non-force 혼합 또는 취소된 orphan fetch |
| P1-29 | `TruncatedPagination_CannotProduceCompletePeriod` | page 누락/row count 불일치 후 complete 판정 |
| P1-30 | `ZoningOrdinalScore_IsExplicitlyDerivedAndNotRawCategory` | 범주를 무근거 double로 취급 |
| P1-31 | `TopologyAreaAndCentroid_UseAllPartsMinusHoles` | largest outer만 면적/중심 계산 |
| P1-32 | `Topology_RejectsOpenOrDegenerateRingsAndNormalizesOrientation` | 잘못된 ring 수용 |
| P1-33 | `CacheEnvelope_PreservesOriginalRetrievedAtOnHit` | cache hit마다 데이터가 새것처럼 표시 |
| P1-34 | `RegistryInstances_DoNotShareKeyBoundSources` | engine 간 API key/source 오염 |
| P1-35 | `SnapshotProjectsDeterministicallyToLegacySolverResult` | district 순서/legacy GH 출력 변화 |
| P1-36 | `NormalAndForceOriginsSerializeWithRefreshWinningPersistentState` | 늦게 완료된 stale normal fetch가 force 결과를 덮음 |
| P1-37 | `FailedForceRefreshPreservesPreviousValidEntry` | force 실패로 기존 정상 cache 손실 |
| P1-38 | `DuplicateStableRowIdentityMakesPageSetIncomplete` | 중복 page/row가 total count만 맞춰 complete 오판 |
| P1-39 | `ZoningHistogramTransform_IsVersionedDefaultOffAndUnknownMissing` | 다중 범주 손실/묵시 overlay/unknown 숫자화 |
| P1-40 | `TypedBounds_NormalizeToWgs84WithoutCrsAmbiguity` | EPSG:5179 bounds를 4326으로 오인 |
| P1-41 | `SnapshotSeparatesAcquisitionAndDeliveryOrigin` | cache hit이 API 신선 데이터처럼 표시 |
| P1-42 | `BuiltInSourceCancellationPropagatesAndDefaultClientsAreShared` | source가 cancel을 실패 결과로 삼키거나 반복 Run이 handler/socket 누적 |
| P1-43 | `LandSourceExplicitPeriodControlsProviderYearAndObservation` | explicit `stdrYear`가 cache key에만 있고 provider/observation은 현재연도-1 |
| P1-44 | `GeoJson_PreservesMultiPolygonAndHoles` invalid topology branch | invalid part/hole의 조용한 drop 또는 전체 invalid feature cache |
| P1-45 | `SnapshotAndTopologyCollectionsCannotBeMutatedThroughArrayCasts` | IReadOnly surface를 array/list cast로 변경 |
| P1-46 | `ZoningSourcePreservesCategoriesAndRequiresExplicitOrdinalOptIn` | default-off 선언과 실제 source numeric overlay 동작 불일치 |

## 4. Phase 2 RED 목록

| ID | 테스트 | 실패 기준 |
|---|---|---|
| P2-01 | `RunGate_DefaultsFalse` | 배치 즉시 실행 가능 |
| P2-02 | `NewGeneration_DiscardsOldResult` | 이전 결과가 최신 출력 덮음 |
| P2-03 | `RunStateMachine_RequiresFalseToTrueEdge` | reopen/입력 변경으로 자동 실행 |
| P2-04 | `ExistingPortsAndComponentGuid_AreStable` | 기존 GH 문서 연결 손상 |
| P2-05 | `WeightsOnlyChange_RecomputesOverlayWithoutFetch` | overlay 미갱신 또는 HTTP 호출 발생 |
| P2-06 | `Resolution_IsClamped` | 무제한 정점 생성 |
| P2-07 | `LegendSteps_AreClamped` | 비정상 메모리/시간 사용 |
| P2-08 | `NormalizedLegend_ShowsDecimals` | 0/1만 표시 |
| P2-09 | `AllEqual_UsesNeutralColor` | 최대색 또는 divide-by-zero |
| P2-10 | `ExactIdwOptimizedPath_MatchesReference` | 계산 의미 변화 |
| P2-11 | `InvalidModelUnits_ReportsWarning` | 단위 무시 |
| P2-12 | `RunAsync_CancellationPropagatesWithoutFallback` | 취소가 legacy 재조회/partial success로 변환 |
| P2-13 | `TaskAdapter_PreAndPostSolveStartsExactlyOnce` | GH 2-pass에서 edge 소비/중복 실행 |
| P2-14 | `CancelOnlyCurrentGeneration` | old cancel이 새 generation 취소 |
| P2-15 | `CancelledOrFaultedGenerationCannotCommitOrEraseLastSuccess` | 마지막 정상 출력 소실 |
| P2-16 | `FalseTrueWhileRunningSupersedesPreviousGeneration` | 동시 결과 경합 |
| P2-17 | `QueryChangeWhileTrueDoesNotFetchAndMarksSnapshotStale` | 입력 변경 자동 네트워크/오래된 결과 오인 |
| P2-18 | `ComponentRemovalCancelsCurrentGeneration` | orphan 작업 |
| P2-19 | `SolverAndVisualizerPortManifestsRemainAppendOnly` | index/name/type/access/default/Guid 회귀 |
| P2-20 | `ChoroplethPreservesPartsHolesOrderAndMissingStyle` | topology 손실/코드-값 misalignment |
| P2-21 | `MeterToDocumentUnitScalesNativeGeometryAndKeepsAreaScalarM2WithoutFetch` | mm/cm/inch geometry 왜곡, `Areas (m²)` scalar 오변환, 재조회 |
| P2-22 | `IdwRejectsEstimatedOrActualVertexFaceMemoryCap` | OOM 전 안전장치 없음 |
| P2-23 | `IdwCpuLoopObservesCancellation` | 취소 후 CPU 계속 점유 |
| P2-24 | `LegendFormatsNormalizedRawMissingAndAllEqual` | 0/1 정수화/NaN label/동률 불일치 |
| P2-25 | `CsvPathChangeWhileIdleDoesNotWrite` | derived 변경만으로 파일 side effect |
| P2-26 | `OldGenerationProgressIsIgnoredAndCurrentProgressIsMonotonic` | stale progress가 최신 상태 덮음 |
| P2-27 | `CanceledStatePreservesLastSuccessfulSnapshot` | cancel 후 정상 결과 소실 |
| P2-28 | `MeshCacheEvictsByTwoEntryAnd256MiBBudget` | derived cache 누적 메모리 폭증 |
| P2-29 | `VisualizerSnapshotPortSuppliesTopologyRawUnitAndProvenance` | legacy curve/overlay만으로 hole/raw legend 손실 |
| P2-30 | `QueryFingerprintIncludesTransportPeriodForceAndSpatialFields` | data-affecting 변경 stale 오판 |

## 5. 리뷰 체크리스트

각 계획 리뷰어는 다음을 판정한다.

- 실패 모드가 테스트 가능한 인수 조건으로 연결되는가?
- Phase 경계를 넘는 숨은 의존성이 있는가?
- 배포/제품화가 Phase 0–2에 침투하지 않았는가?
- 외부 API/Rhino 없이 검증 가능한 순수 로직 경계가 충분한가?
- 과도한 리팩터링 없이 P0 위험을 먼저 줄이는가?

각 구현 리뷰어는 다음을 판정한다.

- 테스트가 구현 세부가 아니라 계약을 고정하는가?
- 오류를 0/평균/빈 리스트로 숨기지 않는가?
- 취소, 동시성, 캐시, 파일 I/O가 실패 안전한가?
- 키/개인 경로가 로그와 URL에 노출되지 않는가?
- 새 코드가 기존 중복을 늘리지 않는가?
- 자동 검증 결과와 미검증 범위가 정직하게 기록됐는가?
