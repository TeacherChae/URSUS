using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace URSUS.Config
{
    /// <summary>
    /// API 키 자동 로드 — 환경변수 → DLL 인접 설정 파일 → 사용자 프로필 설정 파일 순으로 탐색.
    ///
    /// 우선순위 (높은 것이 이김):
    ///   1. 명시적 입력 (GH 와이어 직접 연결)
    ///   2. 환경변수 (URSUS_VWORLD_KEY, URSUS_SEOUL_KEY 등)
    ///   3. DLL 인접 appsettings.json
    ///   4. 사용자 프로필 %APPDATA%/URSUS/appsettings.json
    ///
    /// 새 API 키를 추가하려면:
    ///   1. ApiKeyName 상수 추가
    ///   2. UrsusSettings에 프로퍼티 추가
    ///   3. ENV_MAP에 환경변수 매핑 추가
    /// </summary>
    public sealed class ApiKeyProvider
    {
        // ── API 키 이름 상수 ─────────────────────────────────────────────
        public const string KEY_VWORLD     = "VWorldKey";
        public const string KEY_SEOUL      = "SeoulKey";
        public const string KEY_DATA_GO_KR = "DataGoKrKey";

        // ── 환경변수 이름 매핑 ───────────────────────────────────────────
        private static readonly Dictionary<string, string> ENV_MAP = new()
        {
            { KEY_VWORLD,     "URSUS_VWORLD_KEY"     },
            { KEY_SEOUL,      "URSUS_SEOUL_KEY"      },
            { KEY_DATA_GO_KR, "URSUS_DATA_GO_KR_KEY" },
        };

        private const string SETTINGS_FILENAME = "appsettings.json";

        private readonly Dictionary<string, string> _resolvedKeys = new();
        private readonly Dictionary<string, string> _keySources   = new();

        /// <summary>키가 어디서 로드됐는지 출처 정보 (디버깅/로그용)</summary>
        public IReadOnlyDictionary<string, string> KeySources => _keySources;

        /// <summary>
        /// 모든 설정 소스를 탐색하여 API 키를 로드한다.
        /// explicitOverrides에 값이 있으면 최우선 적용.
        /// </summary>
        /// <param name="explicitOverrides">GH 와이어에서 직접 입력된 키 (null/빈문자열이면 무시)</param>
        public ApiKeyProvider(Dictionary<string, string>? explicitOverrides = null)
        {
            // 4단계: 사용자 프로필 설정 파일 (최저 우선순위부터 적재)
            LoadFromSettingsFile(GetUserProfileSettingsPath());

            // 3단계: DLL 인접 설정 파일
            LoadFromSettingsFile(GetDllAdjacentSettingsPath());

            // 2단계: 환경변수
            LoadFromEnvironment();

            // 1단계: 명시적 입력 (최고 우선순위)
            if (explicitOverrides != null)
            {
                foreach (var (key, value) in explicitOverrides)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        _resolvedKeys[key] = value.Trim();
                        _keySources[key]   = "explicit_input";
                    }
                }
            }
        }

        // ── Public API ───────────────────────────────────────────────────

        /// <summary>
        /// 지정한 키 이름에 해당하는 API 키를 반환한다.
        /// 키가 없으면 null 반환.
        /// </summary>
        public string? GetKey(string keyName)
        {
            return _resolvedKeys.TryGetValue(keyName, out string? val) ? val : null;
        }

        /// <summary>VWorld API 키</summary>
        public string? VWorldKey => GetKey(KEY_VWORLD);

        /// <summary>서울 열린데이터 API 키</summary>
        public string? SeoulKey => GetKey(KEY_SEOUL);

        /// <summary>공공데이터포털(data.go.kr) API 키 — 공시지가 등 부동산 데이터용 (선택)</summary>
        public string? DataGoKrKey => GetKey(KEY_DATA_GO_KR);

        /// <summary>
        /// 필수 키가 모두 설정되었는지 확인하고, 누락된 키 목록을 반환한다.
        /// </summary>
        /// <param name="requiredKeys">필수 키 이름 목록</param>
        /// <returns>누락된 키 이름 목록 (비어 있으면 모두 설정됨)</returns>
        public List<string> GetMissingKeys(params string[] requiredKeys)
        {
            return requiredKeys
                .Where(k => string.IsNullOrWhiteSpace(GetKey(k)))
                .ToList();
        }

        /// <summary>
        /// 사용자 친화적 진단 메시지를 생성한다.
        /// 누락된 키에 대해 설정 방법을 안내.
        /// </summary>
        public string GetDiagnosticMessage(params string[] requiredKeys)
        {
            var missing = GetMissingKeys(requiredKeys);
            if (missing.Count == 0)
            {
                var lines = new List<string> { "[URSUS] API 키 로드 완료:" };
                foreach (var (key, source) in _keySources)
                    lines.Add($"  {key} ← {source}");
                return string.Join(Environment.NewLine, lines);
            }

            var msg = new List<string>
            {
                $"[URSUS] 다음 API 키가 설정되지 않았습니다: {string.Join(", ", missing)}",
                "",
                "설정 방법 (택 1):",
                "  (1) GH 컴포넌트의 VK/SK 입력에 직접 연결",
                "  (2) 환경변수 설정:"
            };
            foreach (string key in missing)
            {
                if (ENV_MAP.TryGetValue(key, out string? envName))
                    msg.Add($"      set {envName}=<your_key>");
            }
            msg.Add($"  (3) 설정 파일에 작성:");
            msg.Add($"      {GetUserProfileSettingsPath()}");
            msg.Add($"      또는 DLL 옆: {GetDllAdjacentSettingsPath()}");
            msg.Add("");
            msg.Add("API 키 발급:");
            foreach (string key in missing)
            {
                if (key == KEY_VWORLD)
                    msg.Add("  VWorld: https://www.vworld.kr/dev/v4dv_2ddataguide2_s001.do (무료 회원가입 후 발급)");
                else if (key == KEY_SEOUL)
                    msg.Add("  서울 열린데이터: https://data.seoul.go.kr/ (무료 회원가입 후 발급)");
                else if (key == KEY_DATA_GO_KR)
                    msg.Add("  공공데이터포털: https://www.data.go.kr/data/15058747/openapi.do (무료, 공시지가 조회용)");
            }

            return string.Join(Environment.NewLine, msg);
        }

        // ── 설정 파일 저장 (Setup 도구에서 사용) ─────────────────────────

        /// <summary>
        /// 사용자 프로필 경로에 API 키를 저장한다.
        /// 기존 설정이 있으면 병합.
        /// </summary>
        public static void SaveToUserProfile(Dictionary<string, string> keys)
        {
            string path = GetUserProfileSettingsPath();
            SaveSettings(path, keys);
        }

        /// <summary>
        /// DLL 인접 경로에 API 키를 저장한다.
        /// 기존 설정이 있으면 병합.
        /// </summary>
        public static void SaveToDllDirectory(Dictionary<string, string> keys)
        {
            string path = GetDllAdjacentSettingsPath();
            SaveSettings(path, keys);
        }

        // ── Private: 각 소스에서 키 로드 ────────────────────────────────

        private void LoadFromEnvironment()
        {
            foreach (var (keyName, envName) in ENV_MAP)
            {
                string? val = Environment.GetEnvironmentVariable(envName);
                if (!string.IsNullOrWhiteSpace(val))
                {
                    _resolvedKeys[keyName] = val.Trim();
                    _keySources[keyName]   = $"env:{envName}";
                }
            }
        }

        private void LoadFromSettingsFile(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            try
            {
                string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
                var settings = JsonSerializer.Deserialize<UrsusSettings>(json);
                if (settings == null) return;

                string source = $"file:{path}";

                if (!string.IsNullOrWhiteSpace(settings.VWorldKey))
                {
                    _resolvedKeys[KEY_VWORLD] = settings.VWorldKey!.Trim();
                    _keySources[KEY_VWORLD]   = source;
                }
                if (!string.IsNullOrWhiteSpace(settings.SeoulKey))
                {
                    _resolvedKeys[KEY_SEOUL] = settings.SeoulKey!.Trim();
                    _keySources[KEY_SEOUL]   = source;
                }
                if (!string.IsNullOrWhiteSpace(settings.DataGoKrKey))
                {
                    _resolvedKeys[KEY_DATA_GO_KR] = settings.DataGoKrKey!.Trim();
                    _keySources[KEY_DATA_GO_KR]   = source;
                }
            }
            catch
            {
                // 설정 파일 파싱 실패 — 다음 소스로 진행
            }
        }

        // ── Private: 경로 헬퍼 ──────────────────────────────────────────

        private static string GetDllAdjacentSettingsPath()
        {
            string? dllDir = Path.GetDirectoryName(
                typeof(ApiKeyProvider).Assembly.Location);
            return dllDir != null
                ? Path.Combine(dllDir, SETTINGS_FILENAME)
                : SETTINGS_FILENAME;
        }

        private static string GetUserProfileSettingsPath()
        {
            string appData = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "URSUS", SETTINGS_FILENAME);
        }

        // ── Private: 설정 저장 ──────────────────────────────────────────

        private static void SaveSettings(string path, Dictionary<string, string> keys)
        {
            // 기존 파일이 있으면 로드 후 병합
            UrsusSettings settings;
            if (File.Exists(path))
            {
                try
                {
                    string existing = File.ReadAllText(path, System.Text.Encoding.UTF8);
                    settings = JsonSerializer.Deserialize<UrsusSettings>(existing)
                               ?? new UrsusSettings();
                }
                catch
                {
                    settings = new UrsusSettings();
                }
            }
            else
            {
                settings = new UrsusSettings();
            }

            // 키 병합
            foreach (var (keyName, value) in keys)
            {
                switch (keyName)
                {
                    case KEY_VWORLD:     settings.VWorldKey   = value; break;
                    case KEY_SEOUL:      settings.SeoulKey    = value; break;
                    case KEY_DATA_GO_KR: settings.DataGoKrKey = value; break;
                }
            }

            // 저장
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true,
            });
            File.WriteAllText(path, json, System.Text.Encoding.UTF8);
        }
    }

    /// <summary>
    /// appsettings.json 직렬화/역직렬화 모델.
    /// 새 API 키를 추가할 때 여기에 프로퍼티를 추가한다.
    /// </summary>
    public class UrsusSettings
    {
        [JsonPropertyName("VWorldKey")]
        public string? VWorldKey { get; set; }

        [JsonPropertyName("SeoulKey")]
        public string? SeoulKey { get; set; }

        [JsonPropertyName("DataGoKrKey")]
        public string? DataGoKrKey { get; set; }

        [JsonPropertyName("CacheDir")]
        public string? CacheDir { get; set; }

        [JsonPropertyName("MappingJsonPath")]
        public string? MappingJsonPath { get; set; }
    }
}
