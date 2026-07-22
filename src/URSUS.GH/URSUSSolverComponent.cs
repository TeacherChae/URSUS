using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using URSUS.Config;
using URSUS.Export;
using URSUS.Execution;
using URSUS.Resources;
using URSUS.DataSources;
using URSUS.Analysis;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Rhino;
using Rhino.Geometry;
using Grasshopper;
using URSUS.Visualization;

namespace URSUS.GH
{
    public sealed record SolverTaskOutput(
        long Generation, SolverResult? Result, string Csv, string SavedPath,
        int RowCount, string WeightSummary, string QueryFingerprint,
        string ObservedIdentity, string WeightFingerprint, string? Error);

    public class URSUSSolverComponent : GH_TaskCapableComponent<SolverTaskOutput>
    {
        public URSUSSolverComponent()
            : base(
                "URSUS Solver",
                "Solver",
                "법정동 경계 + 소득 데이터 수집 파이프라인",
                "URSUS",
                "Data")
        {
            UseTasks = true;
            _coordinator = new RunCoordinator((request, token, progress) =>
            {
                var solver = _solverForStart ?? throw new InvalidOperationException("Solver context missing.");
                return solver.RunAsync(request, token, progress);
            });
            _coordinator.Changed += status => RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                Message = status.State == RunState.Running
                    ? $"{status.Stage ?? "running"} {status.Progress:P0}"
                    : status.State.ToString();
                Instances.RedrawCanvas();
                if (status.State is RunState.Canceled or RunState.Succeeded or RunState.Faulted)
                    OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(false));
            }));
        }

        public override Guid ComponentGuid
            => new Guid("794d034a-069d-4790-a220-1293dd3328cf");

        // ── 입력 파라미터 인덱스 상수 ──────────────────────────────────
        //    인덱스를 상수로 관리해 SolveInstance와의 동기화를 보장한다.
        private const int IN_VWORLD_KEY   = 0;
        private const int IN_SEOUL_KEY    = 1;
        private const int IN_DATASET      = 2;
        private const int IN_W_INCOME     = 3;
        private const int IN_W_POP        = 4;
        private const int IN_W_TRANSIT    = 5;
        private const int IN_EXPORT_CSV   = 6;
        private const int IN_CSV_PATH     = 7;
        private const int IN_ADDRESS1     = 8;
        private const int IN_ADDRESS2     = 9;
        private const int IN_RUN          = 10;
        private const int IN_ALLOW_INSECURE_SEOUL = 11;
        private const int IN_CANCEL = 12;

        private readonly RunCoordinator _coordinator;
        private readonly object _pendingSync = new();
        private URSUSSolver? _solverForStart;
        private CachedOutput? _lastOutput;
        private SolverTaskOutput? _pendingOutput;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // ── 모든 입력은 Optional — 캔버스에 놓기만 해도 실행됩니다 ──

            // [0] VWorld Key
            pManager.AddTextParameter("VWorld Key", "VK",
                "VWorld API 키 (미입력 시 환경변수/설정 파일에서 자동 로드)",
                GH_ParamAccess.item, "");
            pManager[IN_VWORLD_KEY].Optional = true;

            // [1] Seoul Key
            pManager.AddTextParameter("Seoul Key", "SK",
                "서울 열린데이터 API 키 (미입력 시 환경변수/설정 파일에서 자동 로드)",
                GH_ParamAccess.item, "");
            pManager[IN_SEOUL_KEY].Optional = true;

            // [2] DataSet
            pManager.AddTextParameter("DataSet", "DS",
                "사용할 데이터셋 이름 목록 (URSUSSolver.DS_* 상수 참고)\n" +
                "미입력 시 전체 데이터셋(소득, 인구, 대중교통)이 자동 선택됩니다.",
                GH_ParamAccess.list);
            pManager[IN_DATASET].Optional = true;

            // [3] Weight: 월평균 소득
            pManager.AddNumberParameter("W Income", "WI",
                "월평균 소득 데이터의 가중치.\n" +
                "GH 슬라이더(0.0~1.0)를 연결하세요.\n" +
                "미입력 시 기본값 1.0 (균등 가중치).\n" +
                "합이 1이 아니어도 내부에서 자동 정규화됩니다.",
                GH_ParamAccess.item, 1.0);
            pManager[IN_W_INCOME].Optional = true;

            // [4] Weight: 상주인구
            pManager.AddNumberParameter("W Population", "WP",
                "상주인구 데이터의 가중치.\n" +
                "GH 슬라이더(0.0~1.0)를 연결하세요.\n" +
                "미입력 시 기본값 1.0 (균등 가중치).\n" +
                "합이 1이 아니어도 내부에서 자동 정규화됩니다.",
                GH_ParamAccess.item, 1.0);
            pManager[IN_W_POP].Optional = true;

            // [5] Weight: 대중교통
            pManager.AddNumberParameter("W Transit", "WT",
                "대중교통 승차 승객 수 데이터의 가중치.\n" +
                "GH 슬라이더(0.0~1.0)를 연결하세요.\n" +
                "미입력 시 기본값 1.0 (균등 가중치).\n" +
                "합이 1이 아니어도 내부에서 자동 정규화됩니다.",
                GH_ParamAccess.item, 1.0);
            pManager[IN_W_TRANSIT].Optional = true;

            // [6] Export CSV
            pManager.AddBooleanParameter("Export CSV", "EX",
                "True로 설정 시 CSV 파일을 저장합니다.\n" +
                "Button 컴포넌트를 연결하면 원클릭 내보내기가 가능합니다.",
                GH_ParamAccess.item, false);
            pManager[IN_EXPORT_CSV].Optional = true;

            // [7] CSV Path
            pManager.AddTextParameter("CSV Path", "CP",
                "CSV 저장 경로. 비워 두면 바탕화면에 자동 저장됩니다.\n" +
                "(예: C:\\Users\\user\\Desktop\\site_analysis.csv)",
                GH_ParamAccess.item, "");
            pManager[IN_CSV_PATH].Optional = true;

            // [8] Address 1 (분석 중심 주소 또는 BBOX 좌하단)
            pManager.AddTextParameter("Address 1", "A1",
                "분석 영역 중심 주소 (전국 어디든 가능).\n" +
                "Address 2가 비어 있으면 이 주소를 중심으로 반경 검색.\n" +
                "Address 2를 함께 입력하면 BBOX 모드로 동작.\n" +
                "(예: 부산 해운대구 우동, 대전 유성구 궁동)",
                GH_ParamAccess.item, "");
            pManager[IN_ADDRESS1].Optional = true;

            // [9] Address 2 (BBOX 우상단 — 선택)
            pManager.AddTextParameter("Address 2", "A2",
                "BBOX 모드의 우상단 주소 (선택).\n" +
                "비워 두면 Address 1 중심 반경 검색 모드.\n" +
                "입력 시 Address 1~2를 꼭짓점으로 하는 사각형 검색.\n" +
                "(예: 부산 기장군 장안읍, 대전 동구 용전동)",
                GH_ParamAccess.item, "");
            pManager[IN_ADDRESS2].Optional = true;

            // [10] Run — 기존 입력 뒤에 append하여 저장된 GH 문서의 포트 인덱스를 보존한다.
            pManager.AddBooleanParameter("Run", "R",
                "False→True 전환 시에만 분석을 한 번 실행합니다.\n" +
                "컴포넌트를 배치하거나 문서를 다시 열 때는 자동 실행하지 않습니다.",
                GH_ParamAccess.item, false);
            pManager[IN_RUN].Optional = true;

            // [11] 서울 공식 endpoint가 HTTP만 제공하는 동안의 명시적 opt-in.
            pManager.AddBooleanParameter("Allow Insecure Seoul HTTP", "HTTP",
                "서울 열린데이터의 평문 HTTP endpoint 사용을 명시적으로 허용합니다. " +
                "기본값은 False이며, True일 때 결과에 높은 심각도 경고가 남습니다.",
                GH_ParamAccess.item, false);
            pManager[IN_ALLOW_INSECURE_SEOUL].Optional = true;

            pManager.AddBooleanParameter("Cancel", "CXL",
                "False→True 전환 시 현재 실행 세대만 취소합니다.", GH_ParamAccess.item, false);
            pManager[IN_CANCEL].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Legal Codes", "LC",
                "법정동 코드", GH_ParamAccess.list);
            pManager.AddTextParameter("Names", "N",
                "법정동 이름", GH_ParamAccess.list);
            pManager.AddCurveParameter("Geometries", "G",
                "법정동 경계 커브", GH_ParamAccess.list);
            pManager.AddPointParameter("Centroids", "C",
                "법정동 중심점", GH_ParamAccess.list);
            pManager.AddNumberParameter("Areas", "A",
                "법정동 면적 (m²)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Values", "V",
                "Weighted overlay 값 (0~1 정규화)", GH_ParamAccess.list);
            pManager.AddCurveParameter("Union Boundary", "U",
                "전체 외곽선", GH_ParamAccess.item);

            // CSV 내보내기 출력 포트 (PRD F-05)
            pManager.AddTextParameter("CSV", "CSV",
                "분석 결과 CSV 문자열.\n" +
                "Panel에 연결하면 데이터를 미리 볼 수 있고,\n" +
                "별도 CsvExport 컴포넌트 없이도 결과를 확인할 수 있습니다.",
                GH_ParamAccess.item);
            pManager.AddTextParameter("Saved Path", "SP",
                "CSV 파일이 저장된 경로 (Export CSV=True일 때만 출력)",
                GH_ParamAccess.item);
            pManager.AddIntegerParameter("Row Count", "RC",
                "CSV 데이터 행 수 (헤더 제외)",
                GH_ParamAccess.item);

            // [10] Weight Summary
            pManager.AddTextParameter("Weight Summary", "WS",
                "실제 적용된 가중치 요약 문자열.\n" +
                "예: \"WeightConfig { 소득=0.50, 인구=0.33, 교통=0.17 }\"",
                GH_ParamAccess.item);
            pManager.AddTextParameter("Status", "ST", "Idle/Running/Canceled/Succeeded/Faulted",
                GH_ParamAccess.item);
            pManager.AddTextParameter("Quality", "Q", "coverage, cache, warning 요약",
                GH_ParamAccess.item);
            pManager.AddNumberParameter("Progress", "P", "현재 실행 진행률 0~1",
                GH_ParamAccess.item);
            pManager.AddGenericParameter("Snapshot", "S", "immutable AnalysisSnapshot",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool run = false;
            bool cancel = false;
            DA.GetData(IN_RUN, ref run);
            DA.GetData(IN_CANCEL, ref cancel);
            bool runEdge = _coordinator.ObserveRunSignal(run);
            if (_coordinator.ObserveCancel(cancel))
                base.RequestTaskCancellation();
            if (!run)
            {
                SolverTaskOutput? completed = TryTakePending();
                if (completed?.Result != null)
                {
                    CommitTaskOutput(completed);
                    foreach (string warning in completed.Result.Warnings)
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
                }
                if (!string.IsNullOrWhiteSpace(completed?.Error))
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, completed.Error);
                if (_lastOutput != null)
                    WriteCachedOutputs(DA, _lastOutput);
                WriteStatusOutputs(DA);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    _lastOutput == null
                            ? "대기 중 — Address 1을 입력하고 Run을 True로 전환하세요."
                            : "대기 중 — 마지막 완료 결과를 유지합니다. 다시 실행하려면 Run을 False→True로 전환하세요.");
                return;
            }

            // ── API 키 읽기 ──────────────────────────────────────────────
            string vk = "", sk = "";
            DA.GetData(IN_VWORLD_KEY, ref vk);
            DA.GetData(IN_SEOUL_KEY, ref sk);

            // ── DataSet 읽기 ─────────────────────────────────────────────
            var dataSet = new List<string>();
            DA.GetDataList(IN_DATASET, dataSet);

            // ── 데이터셋별 가중치 슬라이더 읽기 ─────────────────────────
            double wIncome = 1.0, wPop = 1.0, wTransit = 1.0;
            DA.GetData(IN_W_INCOME,  ref wIncome);
            DA.GetData(IN_W_POP,     ref wPop);
            DA.GetData(IN_W_TRANSIT, ref wTransit);

            // ── CSV 옵션 읽기 ────────────────────────────────────────────
            bool exportCsv = false;
            string csvPath = "";
            DA.GetData(IN_EXPORT_CSV, ref exportCsv);
            DA.GetData(IN_CSV_PATH,   ref csvPath);

            // ── 분석 영역 주소 읽기 ─────────────────────────────────────
            string addr1 = "", addr2 = "";
            bool allowInsecureSeoulHttp = false;
            DA.GetData(IN_ADDRESS1, ref addr1);
            DA.GetData(IN_ADDRESS2, ref addr2);
            DA.GetData(IN_ALLOW_INSECURE_SEOUL, ref allowInsecureSeoulHttp);

            // DataSet 입력이 없으면 전체 데이터셋을 기본 선택
            if (dataSet.Count == 0)
            {
                dataSet = new List<string>(URSUSSolver.DefaultDataSets);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    ErrorMessages.Data.DefaultDataSetUsed);
            }
            string observedIdentity = FingerprintObservedInputs(
                dataSet, addr1, addr2, allowInsecureSeoulHttp, vk, sk);

            // ── 슬라이더 → WeightConfig 조립 ─────────────────────────────
            //    DataSet 목록의 각 항목에 대응하는 가중치를 매핑한다.
            //    알려진 데이터셋은 개별 슬라이더 값을, 미지의 데이터셋은 1.0(균등)을 사용.
            //    WeightConfig를 통해 검증/정규화를 일원화한다.
            var sliderWeightMap = new Dictionary<string, double>
            {
                { URSUSSolver.DS_AVG_INCOME,   wIncome  },
                { URSUSSolver.DS_RESIDENT_POP, wPop     },
                { URSUSSolver.DS_TRANSIT,      wTransit },
            };

            var rawWeightDict = new Dictionary<string, double>();
            foreach (string ds in dataSet)
            {
                double w = sliderWeightMap.TryGetValue(ds, out double val) ? val : 1.0;
                rawWeightDict[ds] = w;
            }

            // ── WeightConfig를 통한 검증/정규화 ─────────────────────────
            WeightConfig weightConfig;
            try
            {
                weightConfig = WeightConfig.Create(rawWeightDict);
            }
            catch (ArgumentException ex)
            {
                _coordinator.MarkObservedInvalid();
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                if (_lastOutput != null) WriteCachedOutputs(DA, _lastOutput);
                WriteStatusOutputs(DA);
                return;
            }

            // 정규화 피드백: 원시 합 ≠ 1이면 안내
            double rawSum = rawWeightDict.Values.Sum();
            if (Math.Abs(rawSum - 1.0) > 1e-6)
            {
                var pairs = dataSet.Select(ds =>
                {
                    double nw = weightConfig.Weights.TryGetValue(ds, out double v) ? v : 0.0;
                    return $"{ds}: {nw:F3}";
                });
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"가중치 합({rawSum:F3})이 1이 아니므로 자동 정규화됩니다 → " +
                    string.Join(", ", pairs));
            }

            // WeightConfig → dataSet 순서의 List<double> 변환
            var weights = weightConfig.ToOrderedList(dataSet);
            string weightFingerprint = FingerprintWeights(dataSet, weights);

            SolverTaskOutput? recovered = TryTakePending(observedIdentity);
            bool recoveredResultCommitted = recovered?.Result != null;
            if (recovered?.Result != null)
            {
                CommitTaskOutput(recovered);
                foreach (string warning in recovered.Result.Warnings)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
            }
            if (!string.IsNullOrWhiteSpace(recovered?.Error))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, recovered.Error);

            RunCoordinatorStatus coordinatorStatus = _coordinator.Status;
            bool hasUncommittedSuccess = coordinatorStatus.State == RunState.Succeeded &&
                _lastOutput?.Generation != coordinatorStatus.Generation;
            bool cachedInputsAreCurrent = !runEdge && _lastOutput?.Result.Snapshot != null &&
                coordinatorStatus.State is not (RunState.Running or RunState.CancelRequested) &&
                !hasUncommittedSuccess && _lastOutput.ObservedIdentity == observedIdentity;
            if (cachedInputsAreCurrent)
            {
                _coordinator.MarkObservedCurrent(_lastOutput!.QueryFingerprint);
                if (InPreSolve) return;
                if (_lastOutput.WeightFingerprint != weightFingerprint)
                    _lastOutput = RecomputeDerived(
                        _lastOutput, dataSet, weights, weightFingerprint);
                if (!recoveredResultCommitted)
                    foreach (string warning in _lastOutput.Result.Warnings)
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
                WriteCachedOutputs(DA, _lastOutput!);
                WriteStatusOutputs(DA);
                return;
            }

            if (string.IsNullOrWhiteSpace(addr1))
            {
                _coordinator.MarkObservedInvalid();
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Address 1이 비어 있습니다. 분석 중심 주소를 입력한 뒤 Run을 다시 실행하세요.");
                if (_lastOutput != null) WriteCachedOutputs(DA, _lastOutput);
                WriteStatusOutputs(DA);
                return;
            }

            // ApiKeyProvider: 환경변수 → DLL 인접 파일 → 사용자 프로필 순으로 자동 탐색
            var overrides = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(vk)) overrides[ApiKeyProvider.KEY_VWORLD] = vk;
            if (!string.IsNullOrWhiteSpace(sk)) overrides[ApiKeyProvider.KEY_SEOUL]  = sk;

            var keyProvider = new ApiKeyProvider(overrides);

            // 필수 키 누락 확인
            bool needsSeoul = dataSet.Any(ds =>
                ds == URSUSSolver.DS_AVG_INCOME ||
                ds == URSUSSolver.DS_RESIDENT_POP ||
                ds == URSUSSolver.DS_TRANSIT);
            bool needsDataGoKr = dataSet.Any(ds =>
                ds == URSUSSolver.DS_LAND_PRICE || ds == URSUSSolver.DS_ZONING);
            var requiredKeys = new List<string> { ApiKeyProvider.KEY_VWORLD };
            if (needsSeoul) requiredKeys.Add(ApiKeyProvider.KEY_SEOUL);
            if (needsDataGoKr) requiredKeys.Add(nameof(UrsusSettings.DataGoKrKey));
            var missing = keyProvider.GetMissingKeys(requiredKeys.ToArray());
            if (missing.Count > 0)
            {
                _coordinator.MarkObservedInvalid();
                // 누락된 각 키에 대해 ErrorGuideMap URL 포함 메시지 생성
                string errorCode = missing.Contains(ApiKeyProvider.KEY_VWORLD)
                    ? ErrorCodes.VWorldKeyMissing
                    : missing.Contains(ApiKeyProvider.KEY_SEOUL)
                        ? ErrorCodes.SeoulKeyMissing
                        : ErrorCodes.DataGoKrKeyMissing;
                string diagnostic = keyProvider.GetDiagnosticMessage(requiredKeys.ToArray());
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    ErrorGuideMap.FormatMessageWithGuide(errorCode, diagnostic));
                if (_lastOutput != null) WriteCachedOutputs(DA, _lastOutput);
                WriteStatusOutputs(DA);
                return;
            }

            // 키 출처 로그 (정보 레벨)
            Console.WriteLine(keyProvider.GetDiagnosticMessage(
                ApiKeyProvider.KEY_VWORLD, ApiKeyProvider.KEY_SEOUL));

            string keyFingerprint = FingerprintKeys(keyProvider, requiredKeys);
            var request = new AnalysisRequest(dataSet, weights, addr1,
                string.IsNullOrWhiteSpace(addr2) ? null : addr2, null,
                new TransportPolicy(allowInsecureSeoulHttp), keyFingerprint: keyFingerprint);
            _coordinator.ObserveQuery(request);
            long? generation = null;
            if (runEdge)
            {
                _solverForStart = new URSUSSolver(keyProvider);
                generation = _coordinator.StartObserved(request);
            }

            if (InPreSolve)
            {
                if (generation.HasValue)
                {
                    CancellationToken generationToken = CancelToken;
                    TaskList.Add(CompleteGenerationAsync(generation.Value, exportCsv, csvPath,
                        request.QueryFingerprint, observedIdentity, weightFingerprint,
                        generationToken));
                }
                return;
            }

            SolverTaskOutput? taskOutput = null;
            if (GetSolveResults(DA, out SolverTaskOutput solved))
            {
                taskOutput = solved;
                ClearPending(solved.Generation);
            }
            else if (generation.HasValue)
                taskOutput = CompleteGenerationAsync(generation.Value, exportCsv, csvPath,
                    request.QueryFingerprint, observedIdentity, weightFingerprint,
                    CancellationToken.None)
                    .GetAwaiter().GetResult();

            if (taskOutput?.Result != null)
            {
                var result = taskOutput.Result;
                foreach (string warning in result.Warnings)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
                CommitTaskOutput(taskOutput);
                WriteCachedOutputs(DA, _lastOutput!);
            }
            else if (_lastOutput != null)
                WriteCachedOutputs(DA, _lastOutput);

            if (!string.IsNullOrWhiteSpace(taskOutput?.Error))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, taskOutput.Error);
            WriteStatusOutputs(DA);
        }

        private void WriteCachedOutputs(IGH_DataAccess DA, CachedOutput cached)
        {
            double rawScale = RhinoDoc.ActiveDoc == null ? 1.0 :
                RhinoMath.UnitScale(UnitSystem.Meters, RhinoDoc.ActiveDoc.ModelUnitSystem);
            var unitScale = DocumentUnitScale.FromMetersPerDocumentUnit(
                double.IsFinite(rawScale) && rawScale > 0 ? 1.0 / rawScale : 0);
            if (unitScale.Warning != null)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, unitScale.Warning);
            double scale = unitScale.LengthScale;
            DA.SetDataList(0, cached.Result.LegalCodes);
            DA.SetDataList(1, cached.Result.Names);
            DA.SetDataList(2, cached.Result.Geometries.Select(curve => ScaleCurve(curve, scale)));
            DA.SetDataList(3, cached.Result.Centroids.Select(point =>
                new Point3d(point.X * scale, point.Y * scale, point.Z * scale)));
            DA.SetDataList(4, cached.Result.Areas);
            DA.SetDataList(5, cached.Result.Values);
            DA.SetData(6, ScaleCurve(cached.Result.UnionBoundary, scale));
            DA.SetData(7, cached.Csv);
            DA.SetData(8, cached.SavedPath);
            DA.SetData(9, cached.RowCount);
            DA.SetData(10, cached.WeightSummary);
        }

        private static CachedOutput RecomputeDerived(CachedOutput cached,
            IReadOnlyList<string> dataSet, IReadOnlyList<double> weights,
            string weightFingerprint)
        {
            var derived = SnapshotDerivedCalculator.Recompute(cached.Result.Snapshot!, dataSet, weights);
            var result = cached.Result with
            {
                Values = cached.Result.LegalCodes.Select(code =>
                    derived.Values.TryGetValue(code, out double value) ? value : double.NaN).ToList(),
                EffectiveWeights = derived.EffectiveWeights,
                LayerCoverage = derived.Coverage,
                ActiveLayers = derived.ActiveLayers,
                MissingLayers = derived.MissingLayers,
            };
            string csv = CsvExporter.Serialize(result.LegalCodes, result.Names, result.Areas, result.Values);
            return cached with
            {
                Result = result,
                Csv = csv,
                SavedPath = "",
                WeightSummary = derived.EffectiveWeights?.ToString()
                    ?? "데이터 레이어 없음 (가중치 미적용)",
                WeightFingerprint = weightFingerprint,
            };
        }

        private static Curve? ScaleCurve(Curve? curve, double scale)
        {
            if (curve == null) return null;
            Curve duplicate = curve.DuplicateCurve();
            if (double.IsFinite(scale) && scale > 0 && Math.Abs(scale - 1) > 1e-12)
                duplicate.Transform(Transform.Scale(Point3d.Origin, scale));
            return duplicate;
        }

        private async Task<SolverTaskOutput> CompleteGenerationAsync(
            long generation, bool exportCsv, string csvPath, string queryFingerprint,
            string observedIdentity, string weightFingerprint,
            CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(
                () => _coordinator.CancelGeneration(generation));
            Task? current = _coordinator.CurrentTask;
            try
            {
                if (current != null)
                    await current.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                var canceled = new SolverTaskOutput(generation, null, "", "", 0, "",
                    queryFingerprint, observedIdentity, weightFingerprint, null);
                StorePending(canceled);
                return canceled;
            }
            var status = _coordinator.Status;
            var result = status.Generation == generation && status.State == RunState.Succeeded
                ? _coordinator.LastSuccessfulResult : null;
            if (result == null)
            {
                var terminal = new SolverTaskOutput(generation, null, "", "", 0, "",
                    queryFingerprint, observedIdentity, weightFingerprint, status.Error);
                StorePending(terminal);
                return terminal;
            }

            string csv = CsvExporter.Serialize(result.LegalCodes, result.Names,
                result.Areas, result.Values);
            int rows = result.LegalCodes.Count;
            string summary = result.EffectiveWeights?.ToString()
                ?? "데이터 레이어 없음 (가중치 미적용)";
            string savedPath = "";
            string? error = null;
            if (exportCsv)
            {
                savedPath = ResolveExportPath(csvPath);
                try { CsvExporter.WriteToFile(csv, savedPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { error = ex.Message; savedPath = ""; }
            }
            var output = new SolverTaskOutput(generation, result, csv, savedPath, rows, summary,
                queryFingerprint, observedIdentity, weightFingerprint, error);
            StorePending(output);
            return output;
        }

        private void CommitTaskOutput(SolverTaskOutput output)
        {
            if (output.Result == null) return;
            _lastOutput = new CachedOutput(output.Result, output.Csv, output.SavedPath,
                output.RowCount, output.WeightSummary, output.QueryFingerprint,
                output.ObservedIdentity, output.WeightFingerprint, output.Generation);
        }

        private void StorePending(SolverTaskOutput output)
        {
            lock (_pendingSync)
            {
                if (_pendingOutput == null || output.Generation >= _pendingOutput.Generation)
                    _pendingOutput = output;
            }
            RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                GH_Document? document = OnPingDocument();
                document?.ScheduleSolution(1, _ => ExpireSolution(false));
            }));
        }

        private SolverTaskOutput? TryTakePending(string observedIdentity)
        {
            lock (_pendingSync)
            {
                if (_pendingOutput == null ||
                    !string.Equals(_pendingOutput.ObservedIdentity, observedIdentity,
                        StringComparison.Ordinal)) return null;
                SolverTaskOutput output = _pendingOutput;
                _pendingOutput = null;
                return output;
            }
        }

        private SolverTaskOutput? TryTakePending()
        {
            lock (_pendingSync)
            {
                SolverTaskOutput? output = _pendingOutput;
                _pendingOutput = null;
                return output;
            }
        }

        private void ClearPending(long generation)
        {
            lock (_pendingSync)
            {
                if (_pendingOutput?.Generation == generation) _pendingOutput = null;
            }
        }

        private void WriteStatusOutputs(IGH_DataAccess DA)
        {
            var status = _coordinator.Status;
            DA.SetData(11, status.IsStale ? $"{status.State} (stale)" : status.State.ToString());
            var result = _coordinator.LastSuccessfulResult;
            string quality = result?.Snapshot == null ? "결과 없음" : string.Join("; ",
                result.Snapshot.Layers.Values.Select(layer =>
                    $"{layer.Id}:{layer.Coverage:P0}/{layer.DeliveryOrigin}" +
                    (layer.CacheAge.HasValue ? $" age={layer.CacheAge.Value:g}" : "") +
                    (string.IsNullOrWhiteSpace(layer.Unit) ? "" : $" unit={layer.Unit}"))
                .Concat(new[]
                {
                    $"warnings={result.Snapshot.Warnings.Count}",
                    $"failures={result.Snapshot.Failures.Count}",
                }));
            DA.SetData(12, quality);
            DA.SetData(13, status.Progress);
            DA.SetData(14, result?.Snapshot);
        }

        private static string FingerprintKeys(ApiKeyProvider provider, IEnumerable<string> names)
        {
            string canonical = string.Join("|", names.OrderBy(name => name, StringComparer.Ordinal)
                .Select(name => $"{name}:{provider.GetKey(name) ?? ""}"));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant();
        }

        private static string FingerprintObservedInputs(IEnumerable<string> dataSets,
            string address1, string address2, bool allowInsecure, string vworldKey, string seoulKey)
        {
            string canonical = string.Join("|", dataSets.OrderBy(value => value, StringComparer.Ordinal)) +
                $"|a1:{address1.Trim()}|a2:{address2.Trim()}|http:{allowInsecure}|" +
                $"vk:{vworldKey}|sk:{seoulKey}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant();
        }

        private static string FingerprintWeights(IReadOnlyList<string> dataSets,
            IReadOnlyList<double> weights)
        {
            string canonical = string.Join("|", dataSets.Zip(weights,
                    (dataSet, weight) => (dataSet, weight))
                .OrderBy(pair => pair.dataSet, StringComparer.Ordinal)
                .Select(pair => $"{pair.dataSet}:{pair.weight:R}"));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant();
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            _coordinator.Dispose();
            base.RemovedFromDocument(document);
        }

        private sealed record CachedOutput(
            global::URSUS.SolverResult Result,
            string Csv,
            string SavedPath,
            int RowCount,
            string WeightSummary,
            string QueryFingerprint,
            string ObservedIdentity,
            string WeightFingerprint,
            long Generation);

        /// <summary>
        /// CSV 저장 경로를 결정한다:
        ///   1) 사용자가 명시적으로 경로를 입력한 경우 → 그대로 사용 (.csv 확장자 보장)
        ///   2) 경로가 비어 있으면 → 바탕화면 기본 경로 (타임스탬프 포함)
        /// </summary>
        private static string ResolveExportPath(string userPath)
        {
            if (!string.IsNullOrWhiteSpace(userPath))
            {
                if (!userPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    return userPath + ".csv";
                return userPath;
            }

            // 기본: 바탕화면에 타임스탬프 파일명
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return Path.Combine(desktop, $"URSUS_export_{DateTime.Now:yyyyMMdd_HHmm}.csv");
        }

        private void LogError(Exception ex)
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.ApplicationData),
                    "URSUS", "logs");
                Directory.CreateDirectory(logDir);

                string logFile = Path.Combine(logDir,
                    $"error_{DateTime.Now:yyyyMMdd_HHmmss}.log");

                File.WriteAllText(logFile,
                    $"Time: {DateTime.Now}\n" +
                    $"Version: {typeof(URSUSSolverComponent).Assembly.GetName().Version}\n" +
                    $"Exception: {ex.GetType().Name}\n" +
                    $"Message: {ex.Message}\n" +
                    $"StackTrace:\n{ex.StackTrace}\n");
            }
            catch { /* 로깅 실패는 무시 */ }
        }
    }
}
