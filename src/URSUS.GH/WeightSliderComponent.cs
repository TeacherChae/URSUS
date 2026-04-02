using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using URSUS.Config;

namespace URSUS.GH
{
    /// <summary>
    /// 데이터셋별 가중치 슬라이더 컴포넌트.
    ///
    /// 기능:
    ///   - 각 데이터셋에 대한 가중치를 개별 Number 입력으로 받음
    ///   - 합계 100% 자동 정규화 (WeightConfig.Create 활용)
    ///   - 정규화된 가중치와 퍼센트 문자열을 출력
    ///   - URSUSSolverComponent의 Weight 입력 포트에 직접 연결 가능
    ///
    /// Grasshopper 캔버스 UX:
    ///   GH 네이티브 슬라이더(0.0~10.0)를 각 입력 포트에 연결하면
    ///   내부에서 합 = 1.0 정규화를 수행하여 출력한다.
    ///   예: Income=3, Pop=2, Transit=1 → Income=0.50, Pop=0.33, Transit=0.17
    /// </summary>
    public class WeightSliderComponent : GH_Component
    {
        public WeightSliderComponent()
            : base(
                "Weight Slider",
                "WSlider",
                "데이터셋별 가중치를 조절하고 합계 100%로 자동 정규화합니다.\n" +
                "GH 슬라이더(0~10)를 각 입력에 연결하세요.",
                "URSUS",
                "Config")
        { }

        public override Guid ComponentGuid
            => new Guid("e7b3c1d4-5f6a-4e8b-9c2d-1a3b5e7f9d01");

        // ── 입력 파라미터 인덱스 ──────────────────────────────────────
        private const int IN_NAMES       = 0;
        private const int IN_RAW_WEIGHTS = 1;
        private const int IN_RESET       = 2;

        // ── 출력 파라미터 인덱스 ──────────────────────────────────────
        private const int OUT_NAMES       = 0;
        private const int OUT_WEIGHTS     = 1;
        private const int OUT_PERCENTAGES = 2;
        private const int OUT_SUMMARY     = 3;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // [0] DataSet Names
            pManager.AddTextParameter("DataSet Names", "DS",
                "데이터셋 이름 목록.\n" +
                "미입력 시 기본 데이터셋(소득, 인구, 교통)이 사용됩니다.\n" +
                "Panel 또는 Value List로 이름을 입력하세요.",
                GH_ParamAccess.list);
            pManager[IN_NAMES].Optional = true;

            // [1] Raw Weights
            pManager.AddNumberParameter("Raw Weights", "W",
                "각 데이터셋의 원시 가중치 (0 이상).\n" +
                "GH 슬라이더(0.0~10.0)를 연결하세요.\n" +
                "DataSet Names와 같은 순서로 대응됩니다.\n" +
                "합이 1이 아니어도 내부에서 자동 정규화됩니다.\n" +
                "미입력 시 균등 가중치(각 1.0)가 적용됩니다.",
                GH_ParamAccess.list);
            pManager[IN_RAW_WEIGHTS].Optional = true;

            // [2] Reset to Equal
            pManager.AddBooleanParameter("Reset", "R",
                "True로 설정하면 모든 가중치를 균등 배분으로 리셋합니다.\n" +
                "Button 컴포넌트를 연결하면 원클릭 리셋이 가능합니다.",
                GH_ParamAccess.item, false);
            pManager[IN_RESET].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            // [0] DataSet Names (passthrough)
            pManager.AddTextParameter("Names", "N",
                "데이터셋 이름 목록 (입력과 동일 순서)",
                GH_ParamAccess.list);

            // [1] Normalized Weights
            pManager.AddNumberParameter("Weights", "W",
                "정규화된 가중치 (합 = 1.0).\n" +
                "URSUSSolver의 가중치 입력에 직접 연결 가능.",
                GH_ParamAccess.list);

            // [2] Percentages (문자열)
            pManager.AddTextParameter("Percentages", "P",
                "각 데이터셋의 가중치 퍼센트 문자열.\n" +
                "예: \"소득: 50.0%\"\n" +
                "Panel에 연결하면 가시적으로 확인 가능.",
                GH_ParamAccess.list);

            // [3] Summary
            pManager.AddTextParameter("Summary", "S",
                "전체 가중치 요약 문자열.\n" +
                "예: \"소득=50.0%, 인구=33.3%, 교통=16.7% (합계: 100.0%)\"",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ── 1. 데이터셋 이름 읽기 ────────────────────────────────
            var names = new List<string>();
            DA.GetDataList(IN_NAMES, names);

            // 미입력 시 기본 데이터셋 사용
            if (names.Count == 0)
            {
                names = new List<string>(URSUSSolver.DefaultDataSets);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "DataSet Names 미입력 — 기본 데이터셋(소득, 인구, 교통) 사용");
            }

            // ── 2. 리셋 플래그 읽기 ─────────────────────────────────
            bool reset = false;
            DA.GetData(IN_RESET, ref reset);

            // ── 3. 원시 가중치 읽기 ─────────────────────────────────
            var rawWeights = new List<double>();
            DA.GetDataList(IN_RAW_WEIGHTS, rawWeights);

            // 리셋 모드이거나 가중치 미입력 시 균등 배분
            if (reset || rawWeights.Count == 0)
            {
                rawWeights = Enumerable.Repeat(1.0, names.Count).ToList();
                if (reset)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        "가중치가 균등 배분으로 리셋되었습니다.");
                }
            }

            // ── 4. 가중치 수 보정 ───────────────────────────────────
            //    이름 수와 가중치 수가 다르면 보정:
            //    - 가중치가 부족하면 1.0으로 채움
            //    - 가중치가 초과하면 이름 수에 맞춰 자름
            if (rawWeights.Count < names.Count)
            {
                int deficit = names.Count - rawWeights.Count;
                rawWeights.AddRange(Enumerable.Repeat(1.0, deficit));
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"가중치 수({rawWeights.Count - deficit})가 데이터셋 수({names.Count})보다 적어 " +
                    $"나머지 {deficit}개에 기본값 1.0을 적용했습니다.");
            }
            else if (rawWeights.Count > names.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"가중치 수({rawWeights.Count})가 데이터셋 수({names.Count})보다 많아 " +
                    $"초과분은 무시됩니다.");
                rawWeights = rawWeights.Take(names.Count).ToList();
            }

            // ── 5. 음수 검증 ────────────────────────────────────────
            for (int i = 0; i < rawWeights.Count; i++)
            {
                if (rawWeights[i] < 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"가중치[{i}] \"{names[i]}\" = {rawWeights[i]:F4} — " +
                        "음수 가중치는 허용되지 않습니다. 슬라이더를 0 이상으로 설정하세요.");
                    return;
                }
            }

            // ── 6. 전체 제로 검증 ───────────────────────────────────
            double sum = rawWeights.Sum();
            if (sum < 1e-9)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "모든 가중치의 합이 0입니다. " +
                    "최소 하나의 데이터셋에 0보다 큰 가중치를 설정하세요.");
                return;
            }

            // ── 7. WeightConfig를 통한 정규화 ───────────────────────
            //    WeightConfig.Create()가 자동 정규화를 수행한다.
            WeightConfig config;
            try
            {
                var weightDict = new Dictionary<string, double>();
                for (int i = 0; i < names.Count; i++)
                {
                    string key = names[i];
                    // 중복 이름 처리: 같은 이름이 있으면 가중치를 합산
                    if (weightDict.ContainsKey(key))
                        weightDict[key] += rawWeights[i];
                    else
                        weightDict[key] = rawWeights[i];
                }

                config = WeightConfig.Create(weightDict);
            }
            catch (ArgumentException ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            // ── 8. 출력 조립 ────────────────────────────────────────
            // 입력 순서와 동일한 순서로 정규화된 가중치 출력
            // (WeightConfig는 SortedDictionary 기반이므로 순서 재매핑 필요)
            var normalizedWeights = new List<double>();
            var percentages = new List<string>();
            var uniqueNames = new List<string>();

            // 중복 제거된 순서 유지 목록 생성
            var seen = new HashSet<string>();
            foreach (string name in names)
            {
                if (seen.Add(name))
                    uniqueNames.Add(name);
            }

            foreach (string name in uniqueNames)
            {
                double w = config.Weights.TryGetValue(name, out double val) ? val : 0.0;
                normalizedWeights.Add(w);
                percentages.Add($"{name}: {w * 100:F1}%");
            }

            // Summary 문자열 조립
            var summaryParts = uniqueNames.Select(name =>
            {
                double w = config.Weights.TryGetValue(name, out double val) ? val : 0.0;
                return $"{name}={w * 100:F1}%";
            });
            double totalPercent = normalizedWeights.Sum() * 100;
            string summary = $"{string.Join(", ", summaryParts)} (합계: {totalPercent:F1}%)";

            // 정규화 피드백
            if (Math.Abs(sum - 1.0) > 1e-6)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"원시 가중치 합({sum:F3}) → 자동 정규화 완료: {summary}");
            }

            if (config.IsEqualDistribution)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "현재 균등 배분 상태입니다.");
            }

            // ── 9. 출력 설정 ────────────────────────────────────────
            DA.SetDataList(OUT_NAMES, uniqueNames);
            DA.SetDataList(OUT_WEIGHTS, normalizedWeights);
            DA.SetDataList(OUT_PERCENTAGES, percentages);
            DA.SetData(OUT_SUMMARY, summary);
        }
    }
}
