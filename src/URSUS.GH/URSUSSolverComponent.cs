using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using URSUS.Config;
using URSUS.Export;
using URSUS.Resources;

namespace URSUS.GH
{
    public class URSUSSolverComponent : GH_Component
    {
        public URSUSSolverComponent()
            : base(
                "URSUS Solver",
                "Solver",
                "법정동 경계 + 소득 데이터 수집 파이프라인",
                "URSUS",
                "Data")
        { }

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
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
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
            DA.GetData(IN_ADDRESS1, ref addr1);
            DA.GetData(IN_ADDRESS2, ref addr2);

            // DataSet 입력이 없으면 전체 데이터셋을 기본 선택
            if (dataSet.Count == 0)
            {
                dataSet = new List<string>(URSUSSolver.DefaultDataSets);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    ErrorMessages.Data.DefaultDataSetUsed);
            }

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
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
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

            // ApiKeyProvider: 환경변수 → DLL 인접 파일 → 사용자 프로필 순으로 자동 탐색
            var overrides = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(vk)) overrides[ApiKeyProvider.KEY_VWORLD] = vk;
            if (!string.IsNullOrWhiteSpace(sk)) overrides[ApiKeyProvider.KEY_SEOUL]  = sk;

            var keyProvider = new ApiKeyProvider(overrides);

            // 필수 키 누락 확인
            var missing = keyProvider.GetMissingKeys(
                ApiKeyProvider.KEY_VWORLD, ApiKeyProvider.KEY_SEOUL);
            if (missing.Count > 0)
            {
                // 누락된 각 키에 대해 ErrorGuideMap URL 포함 메시지 생성
                string errorCode = missing.Contains(ApiKeyProvider.KEY_VWORLD)
                    ? ErrorCodes.VWorldKeyMissing
                    : ErrorCodes.SeoulKeyMissing;
                string diagnostic = keyProvider.GetDiagnosticMessage(
                    ApiKeyProvider.KEY_VWORLD, ApiKeyProvider.KEY_SEOUL);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    ErrorGuideMap.FormatMessageWithGuide(errorCode, diagnostic));
                return;
            }

            // 키 출처 로그 (정보 레벨)
            Console.WriteLine(keyProvider.GetDiagnosticMessage(
                ApiKeyProvider.KEY_VWORLD, ApiKeyProvider.KEY_SEOUL));

            try
            {
                var solver = new URSUSSolver(keyProvider);

                // 가중치 슬라이더 값을 Solver에 전달 (항상 non-null)
                // 주소가 비어 있으면 null로 전달 → Solver 내부에서 기본값 적용
                string? solverAddr1 = string.IsNullOrWhiteSpace(addr1) ? null : addr1;
                string? solverAddr2 = string.IsNullOrWhiteSpace(addr2) ? null : addr2;
                var result = solver.Run(dataSet, weights, solverAddr1, solverAddr2);

                // ── 기존 출력 포트 (0~6) ────────────────────────────
                DA.SetDataList(0, result.LegalCodes);
                DA.SetDataList(1, result.Names);
                DA.SetDataList(2, result.Geometries);
                DA.SetDataList(3, result.Centroids);
                DA.SetDataList(4, result.Areas);
                DA.SetDataList(5, result.Values);
                DA.SetData(6, result.UnionBoundary);

                // ── CSV 직렬화 (항상 출력 — Panel로 미리보기 가능) ──
                string csv = CsvExporter.Serialize(
                    result.LegalCodes, result.Names,
                    result.Areas, result.Values);
                int rowCount = result.LegalCodes.Count;

                DA.SetData(7, csv);       // CSV 문자열
                DA.SetData(9, rowCount);  // Row Count

                // Weight Summary 출력
                string weightSummary = result.EffectiveWeights?.ToString()
                    ?? "데이터 레이어 없음 (가중치 미적용)";
                DA.SetData(10, weightSummary);

                // ── CSV 파일 저장 (Export CSV=True일 때만) ──────────
                if (exportCsv)
                {
                    string resolvedPath = ResolveExportPath(csvPath);
                    try
                    {
                        int written = CsvExporter.WriteToFile(csv, resolvedPath);
                        DA.SetData(8, resolvedPath);  // Saved Path

                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            ErrorMessages.CsvExport.SaveComplete(written, Path.GetFileName(resolvedPath)));
                    }
                    catch (UnauthorizedAccessException)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                            ErrorGuideMap.FormatMessageWithGuide(
                                ErrorCodes.FileAccessDenied,
                                ErrorMessages.CsvExport.AccessDenied(resolvedPath)));
                        DA.SetData(8, string.Empty);
                    }
                    catch (IOException ioEx)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                            ErrorGuideMap.FormatMessageWithGuide(
                                ErrorCodes.FileSaveFailed,
                                ErrorMessages.CsvExport.WriteFailed(ioEx.Message)));
                        DA.SetData(8, string.Empty);
                    }
                }
                else
                {
                    DA.SetData(8, string.Empty);
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    ErrorGuideMap.FormatMessageWithGuide(
                        ErrorCodes.DataCollectionFailed,
                        ErrorMessages.Api.DataCollectionFailed(ex.Message)));
            }
        }

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
