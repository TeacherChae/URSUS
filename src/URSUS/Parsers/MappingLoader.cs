using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace URSUS.Parsers
{
    /// <summary>
    /// adstrd_legald_mapping.json → 행정동코드↔법정동코드 매핑 로드.
    /// XlsxToJson.Convert() 출력 파일을 읽는다.
    ///
    /// JSON 구조: [ { "adstrd_cd": "...", "legald_cd": "..." }, ... ]
    /// </summary>
    public static class MappingLoader
    {
        /// <summary>
        /// adstrd_legald_mapping.json을 읽어
        /// adstrd_cd → legald_cd 룩업 딕셔너리를 반환한다.
        /// 동일 adstrd_cd에 여러 legald_cd가 있으면 리스트로 반환.
        /// </summary>
        public static Dictionary<string, List<string>> Load(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException($"매핑 파일을 찾을 수 없습니다: {jsonPath}");

            string json = File.ReadAllText(jsonPath, System.Text.Encoding.UTF8);
            var entries = JsonSerializer.Deserialize<List<MappingEntry>>(json)
                          ?? throw new InvalidOperationException("매핑 JSON 파싱 실패");

            var result = new Dictionary<string, List<string>>();
            foreach (var e in entries)
            {
                if (string.IsNullOrEmpty(e.AdstrdCd) || string.IsNullOrEmpty(e.LegaldCd))
                    continue;
                if (!result.TryGetValue(e.AdstrdCd, out var list))
                    result[e.AdstrdCd] = list = new List<string>();
                if (!list.Contains(e.LegaldCd))
                    list.Add(e.LegaldCd);
            }

            return result;
        }

        private class MappingEntry
        {
            [JsonPropertyName("adstrd_cd")]
            public string? AdstrdCd { get; set; }

            [JsonPropertyName("legald_cd")]
            public string? LegaldCd { get; set; }
        }
    }
}
