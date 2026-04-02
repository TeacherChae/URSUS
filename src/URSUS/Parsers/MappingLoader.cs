using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using URSUS.Resources;

namespace URSUS.Parsers
{
    /// <summary>
    /// adstrd_legald_mapping.json → 행정동코드↔법정동코드 매핑 로드.
    ///
    /// DLL 내장 EmbeddedResource에서 로드 — 사용자가 외부 파일 배치를 몰라도 동작한다.
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
                    ErrorMessages.Data.EmbeddedMappingNotFound(EmbeddedResourceName));

            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            string json = reader.ReadToEnd();
            return Parse(json);
        }

        private static Dictionary<string, List<string>> Parse(string json)
        {
            var entries = JsonSerializer.Deserialize<List<MappingEntry>>(json)
                          ?? throw new InvalidOperationException(ErrorMessages.Data.MappingJsonParseFailed);

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
