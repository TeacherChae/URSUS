using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace URSUS.Parsers
{
    /// <summary>
    /// adstrd_legald_mapping.json → 행정동코드↔법정동코드 매핑 로드.
    ///
    /// 우선순위:
    ///   1. DLL 내장 EmbeddedResource (기본 — 사용자가 파일 존재를 몰라도 동작)
    ///   2. 외부 파일 경로 (Load(string) 오버로드 — 커스텀 매핑 사용 시)
    ///
    /// JSON 구조: [ { "adstrd_cd": "...", "legald_cd": "..." }, ... ]
    /// </summary>
    public static class MappingLoader
    {
        private const string EmbeddedResourceName = "URSUS.adstrd_legald_mapping.json";

        /// <summary>
        /// DLL에 내장된 adstrd_legald_mapping.json을 읽어
        /// adstrd_cd → legald_cd 룩업 딕셔너리를 반환한다.
        /// </summary>
        public static Dictionary<string, List<string>> Load()
        {
            var assembly = typeof(MappingLoader).Assembly;
            using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
                ?? throw new InvalidOperationException(
                    $"내장 매핑 리소스를 찾을 수 없습니다: {EmbeddedResourceName}. "
                    + "DLL이 손상되었거나 빌드가 올바르지 않습니다.");

            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            string json = reader.ReadToEnd();
            return Parse(json);
        }

        /// <summary>
        /// 외부 파일 경로에서 adstrd_legald_mapping.json을 읽어
        /// adstrd_cd → legald_cd 룩업 딕셔너리를 반환한다.
        /// 커스텀 매핑 파일을 사용할 때만 호출.
        /// </summary>
        public static Dictionary<string, List<string>> Load(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException($"매핑 파일을 찾을 수 없습니다: {jsonPath}");

            string json = File.ReadAllText(jsonPath, System.Text.Encoding.UTF8);
            return Parse(json);
        }

        private static Dictionary<string, List<string>> Parse(string json)
        {
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
