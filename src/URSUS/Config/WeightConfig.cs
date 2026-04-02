using System;
using System.Collections.Generic;
using System.Linq;

namespace URSUS.Config
{
    /// <summary>
    /// 데이터셋별 가중치를 저장/관리하는 불변 모델.
    ///
    /// 설계 원칙:
    /// - 불변(immutable) record — 스레드 안전, 값 비교 지원
    /// - 균등 배분 기본값: 데이터셋 목록만 주면 자동으로 1/N 배분
    /// - 정규화 보장: 생성 시점에 합 = 1 정규화 수행
    /// - 음수/전체 제로 방어: 생성자에서 검증하여 잘못된 상태 방지
    ///
    /// 사용 예:
    /// <code>
    /// // 균등 배분 (3개 데이터셋 → 각 0.333...)
    /// var equal = WeightConfig.CreateEqual(new[] { "소득", "인구", "교통" });
    ///
    /// // 명시적 가중치 (자동 정규화)
    /// var custom = WeightConfig.Create(new Dictionary&lt;string, double&gt;
    /// {
    ///     { "소득", 3.0 }, { "인구", 2.0 }, { "교통", 1.0 }
    /// });
    /// // → 소득=0.5, 인구=0.333, 교통=0.167
    ///
    /// // 특정 데이터셋 가중치 변경 (새 인스턴스 반환)
    /// var updated = equal.WithWeight("소득", 0.5);
    /// </code>
    /// </summary>
    public sealed record WeightConfig
    {
        // ─────────────────────────────────────────────────────────────
        //  상태
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 데이터셋 Id → 정규화된 가중치 (합 = 1).
        /// 순서가 보장되는 SortedDictionary 기반 복사본.
        /// </summary>
        public IReadOnlyDictionary<string, double> Weights { get; }

        /// <summary>
        /// 포함된 데이터셋 수.
        /// </summary>
        public int Count => Weights.Count;

        /// <summary>
        /// 균등 배분으로 생성되었는지 여부.
        /// UI에서 "기본값 사용 중" 표시에 활용.
        /// </summary>
        public bool IsEqualDistribution { get; }

        // ─────────────────────────────────────────────────────────────
        //  생성자 (private — 팩토리 메서드 사용)
        // ─────────────────────────────────────────────────────────────

        private WeightConfig(IReadOnlyDictionary<string, double> weights, bool isEqual)
        {
            Weights = weights;
            IsEqualDistribution = isEqual;
        }

        // ─────────────────────────────────────────────────────────────
        //  팩토리 메서드
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 균등 배분 가중치를 생성한다.
        /// N개 데이터셋이면 각각 1/N.
        /// </summary>
        /// <param name="dataSetIds">데이터셋 Id 목록 (1개 이상)</param>
        /// <exception cref="ArgumentException">빈 목록</exception>
        public static WeightConfig CreateEqual(IEnumerable<string> dataSetIds)
        {
            var ids = dataSetIds?.ToList()
                ?? throw new ArgumentNullException(nameof(dataSetIds));

            if (ids.Count == 0)
                throw new ArgumentException(
                    "데이터셋 목록이 비어있습니다. 최소 1개의 데이터셋이 필요합니다.",
                    nameof(dataSetIds));

            double w = 1.0 / ids.Count;
            var dict = new SortedDictionary<string, double>();
            foreach (string id in ids)
            {
                if (string.IsNullOrWhiteSpace(id))
                    throw new ArgumentException("데이터셋 Id가 비어있습니다.", nameof(dataSetIds));
                dict[id] = w;
            }

            return new WeightConfig(dict, isEqual: true);
        }

        /// <summary>
        /// 명시적 가중치로 생성한다.
        /// 합이 1이 아니면 자동 정규화된다.
        /// </summary>
        /// <param name="weights">데이터셋 Id → 가중치 (0 이상)</param>
        /// <exception cref="ArgumentException">
        /// 빈 딕셔너리, 음수 가중치, 또는 모든 가중치가 0인 경우
        /// </exception>
        public static WeightConfig Create(IDictionary<string, double> weights)
        {
            if (weights == null)
                throw new ArgumentNullException(nameof(weights));
            if (weights.Count == 0)
                throw new ArgumentException(
                    "가중치 목록이 비어있습니다.", nameof(weights));

            // 검증
            foreach (var (id, w) in weights)
            {
                if (string.IsNullOrWhiteSpace(id))
                    throw new ArgumentException("데이터셋 Id가 비어있습니다.", nameof(weights));
                if (w < 0)
                    throw new ArgumentException(
                        $"가중치[{id}] = {w:F4} — 음수 가중치는 허용되지 않습니다.",
                        nameof(weights));
            }

            double sum = weights.Values.Sum();
            if (sum < 1e-9)
                throw new ArgumentException(
                    "모든 가중치의 합이 0입니다. " +
                    "최소 하나의 데이터셋에 0보다 큰 가중치를 설정하세요.",
                    nameof(weights));

            // 정규화
            var normalized = new SortedDictionary<string, double>();
            foreach (var (id, w) in weights)
                normalized[id] = w / sum;

            // 균등 배분인지 판정
            bool isEqual = IsApproximatelyEqual(normalized);

            return new WeightConfig(normalized, isEqual);
        }

        /// <summary>
        /// 단일 데이터셋의 가중치를 변경한 새 WeightConfig를 반환한다.
        /// 변경 후 자동 정규화된다.
        /// </summary>
        /// <param name="dataSetId">변경할 데이터셋 Id</param>
        /// <param name="newWeight">새 가중치 (0 이상, 정규화 전 원시 값)</param>
        /// <exception cref="KeyNotFoundException">해당 데이터셋이 없을 때</exception>
        /// <exception cref="ArgumentException">음수 가중치</exception>
        public WeightConfig WithWeight(string dataSetId, double newWeight)
        {
            if (!Weights.ContainsKey(dataSetId))
                throw new KeyNotFoundException(
                    $"데이터셋 '{dataSetId}'이(가) WeightConfig에 존재하지 않습니다.");
            if (newWeight < 0)
                throw new ArgumentException(
                    $"가중치 {newWeight:F4} — 음수 가중치는 허용되지 않습니다.",
                    nameof(newWeight));

            var raw = new Dictionary<string, double>();
            foreach (var (id, w) in Weights)
                raw[id] = id == dataSetId ? newWeight : w;

            return Create(raw);
        }

        /// <summary>
        /// 데이터셋을 추가하고 균등 배분으로 재설정한 새 WeightConfig를 반환한다.
        /// </summary>
        public WeightConfig WithAddedDataSet(string dataSetId)
        {
            if (string.IsNullOrWhiteSpace(dataSetId))
                throw new ArgumentException("데이터셋 Id가 비어있습니다.", nameof(dataSetId));

            var ids = Weights.Keys.ToList();
            if (!ids.Contains(dataSetId))
                ids.Add(dataSetId);

            return CreateEqual(ids);
        }

        /// <summary>
        /// 데이터셋을 제거하고 나머지를 재정규화한 새 WeightConfig를 반환한다.
        /// </summary>
        /// <exception cref="InvalidOperationException">마지막 1개를 제거하려 할 때</exception>
        public WeightConfig WithRemovedDataSet(string dataSetId)
        {
            if (!Weights.ContainsKey(dataSetId))
                return this; // 없으면 변경 없음

            if (Weights.Count <= 1)
                throw new InvalidOperationException(
                    "마지막 데이터셋은 제거할 수 없습니다. 최소 1개의 데이터셋이 필요합니다.");

            var remaining = new Dictionary<string, double>();
            foreach (var (id, w) in Weights)
            {
                if (id != dataSetId)
                    remaining[id] = w;
            }

            return Create(remaining);
        }

        // ─────────────────────────────────────────────────────────────
        //  변환 (Solver 호환)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 주어진 데이터셋 순서에 맞춰 가중치 리스트를 반환한다.
        /// URSUSSolver.Run()의 weights 파라미터로 직접 전달 가능.
        ///
        /// WeightConfig에 없는 데이터셋은 0.0으로 처리된다.
        /// </summary>
        /// <param name="orderedDataSetIds">데이터셋 Id 순서</param>
        public List<double> ToOrderedList(IEnumerable<string> orderedDataSetIds)
        {
            return orderedDataSetIds
                .Select(id => Weights.TryGetValue(id, out double w) ? w : 0.0)
                .ToList();
        }

        /// <summary>
        /// 활성 데이터셋만 필터링하여 (가중치 > 0) 새 WeightConfig를 반환한다.
        /// 실제 수집에 성공한 레이어에 맞춰 슬라이싱할 때 사용.
        /// </summary>
        /// <param name="activeDataSetIds">활성 데이터셋 Id 목록</param>
        public WeightConfig SliceFor(IEnumerable<string> activeDataSetIds)
        {
            var activeSet = new HashSet<string>(activeDataSetIds);
            var sliced = new Dictionary<string, double>();

            foreach (var (id, w) in Weights)
            {
                if (activeSet.Contains(id))
                    sliced[id] = w;
            }

            if (sliced.Count == 0)
                throw new ArgumentException(
                    "활성 데이터셋과 WeightConfig 간에 일치하는 항목이 없습니다.");

            return Create(sliced);
        }

        // ─────────────────────────────────────────────────────────────
        //  유틸리티
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 모든 가중치가 대략 균등한지 판정 (epsilon = 1e-6).
        /// </summary>
        private static bool IsApproximatelyEqual(SortedDictionary<string, double> weights)
        {
            if (weights.Count <= 1) return true;

            double expected = 1.0 / weights.Count;
            const double epsilon = 1e-6;
            return weights.Values.All(w => Math.Abs(w - expected) < epsilon);
        }

        /// <summary>
        /// 사람이 읽을 수 있는 형태로 표시.
        /// 예: "WeightConfig { 소득=0.50, 인구=0.33, 교통=0.17 }"
        /// </summary>
        public override string ToString()
        {
            var entries = Weights.Select(kv => $"{kv.Key}={kv.Value:F2}");
            return $"WeightConfig {{ {string.Join(", ", entries)} }}";
        }
    }
}
