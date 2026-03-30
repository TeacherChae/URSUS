using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Grasshopper.Kernel;

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

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("VWorld Key", "VK",
                "VWorld API 키 (미입력 시 설정 파일 사용)",
                GH_ParamAccess.item);
            pManager[0].Optional = true;

            pManager.AddTextParameter("Seoul Key", "SK",
                "서울 열린데이터 API 키 (미입력 시 설정 파일 사용)",
                GH_ParamAccess.item);
            pManager[1].Optional = true;

            pManager.AddTextParameter("DataSet", "DS",
                "사용할 데이터셋 이름 목록 (URSUSSolver.DS_* 상수 참고)",
                GH_ParamAccess.list);
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
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string vk = "", sk = "";
            DA.GetData(0, ref vk);
            DA.GetData(1, ref sk);

            var dataSet = new List<string>();
            if (!DA.GetDataList(2, dataSet)) return;

            var config = LoadConfig();

            if (string.IsNullOrEmpty(vk)) vk = config.VWorldKey;
            if (string.IsNullOrEmpty(sk)) sk = config.SeoulKey;

            if (string.IsNullOrEmpty(vk) || string.IsNullOrEmpty(sk))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "API 키가 설정되지 않았습니다. " +
                    "URSUS.Setup.exe를 실행하여 API 키를 설정하거나, " +
                    "VK/SK 입력에 직접 연결하세요.");
                return;
            }

            try
            {
                var solver = new URSUSSolver(vk, sk);
                var result = solver.Run(dataSet);

                DA.SetDataList(0, result.LegalCodes);
                DA.SetDataList(1, result.Names);
                DA.SetDataList(2, result.Geometries);
                DA.SetDataList(3, result.Centroids);
                DA.SetDataList(4, result.Areas);
                DA.SetDataList(5, result.Values);
                DA.SetData(6, result.UnionBoundary);
            }
            catch (Exception ex)
            {
                LogError(ex);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"데이터 수집 중 오류가 발생했습니다: {ex.Message}");
            }
        }

        private AppSettings LoadConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(
                    typeof(URSUSSolverComponent).Assembly.Location)!;
                string path = Path.Combine(dir, "appsettings.json");

                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<AppSettings>(json)
                           ?? new AppSettings();
                }
            }
            catch { /* 설정 파일 읽기 실패 시 빈 설정 반환 */ }

            return new AppSettings();
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

    public class AppSettings
    {
        public string VWorldKey { get; set; } = "";
        public string SeoulKey { get; set; } = "";
        public string CacheDir { get; set; } = "";
        public string MappingJsonPath { get; set; } = "";
    }
}
