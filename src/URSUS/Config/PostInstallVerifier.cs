using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace URSUS.Config
{
    /// <summary>
    /// 설치 후 검증 — 파일 존재, JSON 유효성, API 키 존재를 3단계로 확인.
    ///
    /// 3-Step UX 검증 흐름:
    ///   Step 1: 핵심 파일 존재 확인 (GHA, DLL, JSON)
    ///   Step 2: appsettings.json 파싱 및 구조 검증
    ///   Step 3: API 키 존재 및 로드 가능 여부 확인
    ///
    /// 각 단계는 독립적으로 실행 가능하며, 실패 시 명확한 복구 가이드를 제공한다.
    /// </summary>
    public sealed class PostInstallVerifier
    {
        // ── 핵심 파일 목록 ──────────────────────────────────────────────
        private static readonly string[] REQUIRED_FILES = new[]
        {
            "URSUS.GH.gha",
            "URSUS.dll",
        };

        private static readonly string[] OPTIONAL_FILES = new[]
        {
            "Clipper2Lib.dll",
            "appsettings.json",
            "adstrd_legald_mapping.json",
            "URSUS.GH.deps.json",
            "URSUS.GH.runtimeconfig.json",
        };

        private const string SETTINGS_FILENAME = "appsettings.json";

        // ── 검증 결과 모델 ──────────────────────────────────────────────

        /// <summary>개별 검증 항목의 결과</summary>
        public sealed record CheckItem(
            string Name,
            bool Passed,
            string Message,
            CheckSeverity Severity = CheckSeverity.Info);

        public enum CheckSeverity
        {
            Info,
            Warning,
            Error
        }

        /// <summary>전체 검증 결과 (3단계 합산)</summary>
        public sealed class VerificationReport
        {
            public List<CheckItem> Step1_Files { get; } = new();
            public List<CheckItem> Step2_Config { get; } = new();
            public List<CheckItem> Step3_ApiKeys { get; } = new();

            public bool Step1Passed => Step1_Files.All(c => c.Passed || c.Severity != CheckSeverity.Error);
            public bool Step2Passed => Step2_Config.All(c => c.Passed || c.Severity != CheckSeverity.Error);
            public bool Step3Passed => Step3_ApiKeys.All(c => c.Passed || c.Severity != CheckSeverity.Error);

            public bool AllPassed => Step1Passed && Step2Passed && Step3Passed;
            public bool HasWarnings => GetAllChecks().Any(c => !c.Passed && c.Severity == CheckSeverity.Warning);
            public int ErrorCount => GetAllChecks().Count(c => !c.Passed && c.Severity == CheckSeverity.Error);
            public int WarningCount => GetAllChecks().Count(c => !c.Passed && c.Severity == CheckSeverity.Warning);

            public IEnumerable<CheckItem> GetAllChecks()
                => Step1_Files.Concat(Step2_Config).Concat(Step3_ApiKeys);

            /// <summary>
            /// 사용자에게 표시할 요약 메시지를 생성한다.
            /// Grasshopper 런타임 메시지 또는 Setup UI에 사용.
            /// </summary>
            public string ToSummary()
            {
                var lines = new List<string>();

                if (AllPassed && !HasWarnings)
                {
                    lines.Add("[URSUS] 설치 검증 완료 ✓");
                    lines.Add("  모든 항목이 정상입니다.");
                    return string.Join(Environment.NewLine, lines);
                }

                lines.Add($"[URSUS] 설치 검증 결과: 오류 {ErrorCount}건, 경고 {WarningCount}건");
                lines.Add("");

                if (!Step1Passed)
                {
                    lines.Add("── Step 1: 파일 검사 ──");
                    foreach (var c in Step1_Files.Where(c => !c.Passed))
                        lines.Add($"  {SeverityIcon(c.Severity)} {c.Message}");
                    lines.Add("");
                }

                if (!Step2Passed)
                {
                    lines.Add("── Step 2: 설정 파일 검사 ──");
                    foreach (var c in Step2_Config.Where(c => !c.Passed))
                        lines.Add($"  {SeverityIcon(c.Severity)} {c.Message}");
                    lines.Add("");
                }

                if (!Step3Passed || Step3_ApiKeys.Any(c => !c.Passed))
                {
                    lines.Add("── Step 3: API 키 검사 ──");
                    foreach (var c in Step3_ApiKeys.Where(c => !c.Passed))
                        lines.Add($"  {SeverityIcon(c.Severity)} {c.Message}");
                    lines.Add("");
                }

                // 복구 가이드
                lines.Add("── 해결 방법 ──");
                if (!Step1Passed)
                    lines.Add("  • 설치 프로그램을 다시 실행하거나, 파일을 수동으로 복사하세요.");
                if (!Step2Passed)
                    lines.Add("  • appsettings.json 파일의 JSON 형식을 확인하세요.");
                if (Step3_ApiKeys.Any(c => !c.Passed && c.Severity == CheckSeverity.Warning))
                {
                    lines.Add("  • API 키 설정 방법 (택 1):");
                    lines.Add("    (1) GH 컴포넌트의 VK/SK 입력에 직접 연결");
                    lines.Add("    (2) 환경변수: set URSUS_VWORLD_KEY=<키>");
                    lines.Add("    (3) appsettings.json 파일에 작성");
                }

                return string.Join(Environment.NewLine, lines);
            }

            /// <summary>
            /// Grasshopper 런타임 메시지 레벨에 맞는 짧은 한줄 요약.
            /// </summary>
            public string ToShortSummary()
            {
                if (AllPassed && !HasWarnings)
                    return "설치 검증 완료 — 모든 항목 정상 ✓";
                if (AllPassed && HasWarnings)
                    return $"설치 정상 (경고 {WarningCount}건 — API 키 미설정 등)";
                return $"설치 문제 발견: 오류 {ErrorCount}건, 경고 {WarningCount}건";
            }

            private static string SeverityIcon(CheckSeverity s) => s switch
            {
                CheckSeverity.Error   => "✗",
                CheckSeverity.Warning => "⚠",
                _                     => "ℹ",
            };
        }

        // ── 검증 실행 ───────────────────────────────────────────────────

        /// <summary>
        /// 지정한 설치 디렉토리를 기준으로 전체 3단계 검증을 실행한다.
        /// installDir이 null이면 DLL 위치를 자동 감지.
        /// </summary>
        public static VerificationReport Verify(string? installDir = null)
        {
            string resolvedDir = installDir
                ?? Path.GetDirectoryName(typeof(PostInstallVerifier).Assembly.Location)
                ?? ".";

            var report = new VerificationReport();

            // Step 1: 핵심 파일 존재 확인
            VerifyFiles(resolvedDir, report);

            // Step 2: appsettings.json 파싱 및 구조 검증
            VerifyConfig(resolvedDir, report);

            // Step 3: API 키 로드 가능 여부 확인
            VerifyApiKeys(report);

            return report;
        }

        // ── Step 1: 파일 존재 확인 ──────────────────────────────────────

        private static void VerifyFiles(string installDir, VerificationReport report)
        {
            // 설치 디렉토리 존재
            if (!Directory.Exists(installDir))
            {
                report.Step1_Files.Add(new CheckItem(
                    "InstallDir",
                    false,
                    $"설치 디렉토리를 찾을 수 없습니다: {installDir}",
                    CheckSeverity.Error));
                return;
            }

            report.Step1_Files.Add(new CheckItem(
                "InstallDir",
                true,
                $"설치 디렉토리 확인: {installDir}"));

            // 필수 파일
            foreach (string file in REQUIRED_FILES)
            {
                string path = Path.Combine(installDir, file);
                bool exists = File.Exists(path);
                report.Step1_Files.Add(new CheckItem(
                    file,
                    exists,
                    exists
                        ? $"{file} 확인 ✓ ({new FileInfo(path).Length:N0} bytes)"
                        : $"{file} 파일이 없습니다 — 설치가 불완전합니다.",
                    exists ? CheckSeverity.Info : CheckSeverity.Error));
            }

            // 선택 파일 (경고만)
            foreach (string file in OPTIONAL_FILES)
            {
                string path = Path.Combine(installDir, file);
                bool exists = File.Exists(path);
                report.Step1_Files.Add(new CheckItem(
                    file,
                    exists,
                    exists
                        ? $"{file} 확인 ✓"
                        : $"{file} 파일이 없습니다 (기능 제한 가능).",
                    exists ? CheckSeverity.Info : CheckSeverity.Warning));
            }

            // Windows 파일 차단(Zone.Identifier) 확인
            foreach (string file in REQUIRED_FILES)
            {
                string path = Path.Combine(installDir, file);
                if (!File.Exists(path)) continue;

                bool blocked = IsFileBlocked(path);
                if (blocked)
                {
                    report.Step1_Files.Add(new CheckItem(
                        $"{file}_Unblock",
                        false,
                        $"{file}이 Windows에 의해 차단되었습니다. " +
                        "파일 속성에서 '차단 해제'를 클릭하세요.",
                        CheckSeverity.Error));
                }
            }
        }

        /// <summary>
        /// Windows Zone.Identifier ADS가 존재하는지 확인한다.
        /// (차단된 파일은 .NET 로더가 어셈블리를 로드하지 못함)
        /// </summary>
        private static bool IsFileBlocked(string filePath)
        {
            try
            {
                // Zone.Identifier ADS 존재 여부를 확인
                // .NET에서는 직접 접근이 제한적이므로, 파일 스트림으로 시도
                string adsPath = filePath + ":Zone.Identifier";
                // FileStream으로 ADS를 열 수 있으면 차단된 것
                using var fs = new FileStream(adsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return fs.Length > 0;
            }
            catch
            {
                // ADS가 없거나 접근 불가 → 차단되지 않음
                return false;
            }
        }

        // ── Step 2: appsettings.json 유효성 검증 ────────────────────────

        private static void VerifyConfig(string installDir, VerificationReport report)
        {
            // DLL 인접 설정 파일 확인
            string dllSettingsPath = Path.Combine(installDir, SETTINGS_FILENAME);
            VerifySingleConfigFile(dllSettingsPath, "DLL 인접", report);

            // 사용자 프로필 설정 파일 확인
            string userProfilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "URSUS", SETTINGS_FILENAME);
            VerifySingleConfigFile(userProfilePath, "사용자 프로필", report);

            // 둘 다 없으면 경고
            if (!File.Exists(dllSettingsPath) && !File.Exists(userProfilePath))
            {
                report.Step2_Config.Add(new CheckItem(
                    "NoConfigFile",
                    false,
                    "appsettings.json 파일이 어디에도 없습니다. " +
                    "API 키를 GH 컴포넌트에서 직접 입력하거나 설정 파일을 생성하세요.",
                    CheckSeverity.Warning));
            }
        }

        private static void VerifySingleConfigFile(
            string path, string label, VerificationReport report)
        {
            if (!File.Exists(path))
            {
                report.Step2_Config.Add(new CheckItem(
                    $"Config_{label}",
                    false,
                    $"{label} appsettings.json 없음: {path}",
                    CheckSeverity.Info));
                return;
            }

            try
            {
                string json = File.ReadAllText(path, System.Text.Encoding.UTF8);

                // 빈 파일 체크
                if (string.IsNullOrWhiteSpace(json))
                {
                    report.Step2_Config.Add(new CheckItem(
                        $"Config_{label}",
                        false,
                        $"{label} appsettings.json이 비어 있습니다: {path}",
                        CheckSeverity.Warning));
                    return;
                }

                // JSON 파싱 시도
                var settings = JsonSerializer.Deserialize<UrsusSettings>(json);
                if (settings == null)
                {
                    report.Step2_Config.Add(new CheckItem(
                        $"Config_{label}",
                        false,
                        $"{label} appsettings.json 파싱 실패 (null 반환): {path}",
                        CheckSeverity.Error));
                    return;
                }

                // 구조 유효 — 키 존재 여부 요약
                var presentKeys = new List<string>();
                if (!string.IsNullOrWhiteSpace(settings.VWorldKey))
                    presentKeys.Add("VWorldKey");
                if (!string.IsNullOrWhiteSpace(settings.SeoulKey))
                    presentKeys.Add("SeoulKey");

                string keyInfo = presentKeys.Count > 0
                    ? $"키: {string.Join(", ", presentKeys)}"
                    : "키 없음";

                report.Step2_Config.Add(new CheckItem(
                    $"Config_{label}",
                    true,
                    $"{label} appsettings.json 유효 ✓ ({keyInfo}): {path}"));
            }
            catch (JsonException ex)
            {
                report.Step2_Config.Add(new CheckItem(
                    $"Config_{label}",
                    false,
                    $"{label} appsettings.json JSON 형식 오류: {ex.Message}",
                    CheckSeverity.Error));
            }
            catch (Exception ex)
            {
                report.Step2_Config.Add(new CheckItem(
                    $"Config_{label}",
                    false,
                    $"{label} appsettings.json 읽기 오류: {ex.Message}",
                    CheckSeverity.Error));
            }
        }

        // ── Step 3: API 키 로드 가능 여부 확인 ──────────────────────────

        private static void VerifyApiKeys(VerificationReport report)
        {
            try
            {
                var provider = new ApiKeyProvider();

                // VWorld 키
                string? vwKey = provider.VWorldKey;
                if (!string.IsNullOrWhiteSpace(vwKey))
                {
                    string source = provider.KeySources.TryGetValue(ApiKeyProvider.KEY_VWORLD, out var s)
                        ? s : "unknown";
                    report.Step3_ApiKeys.Add(new CheckItem(
                        "VWorldKey",
                        true,
                        $"VWorld API 키 로드 ✓ (출처: {source}, 길이: {vwKey.Length}자)"));
                }
                else
                {
                    report.Step3_ApiKeys.Add(new CheckItem(
                        "VWorldKey",
                        false,
                        "VWorld API 키가 설정되지 않았습니다. " +
                        "GH 컴포넌트의 VK 입력에 직접 연결하거나, " +
                        "appsettings.json에 \"VWorldKey\" 항목을 추가하세요.",
                        CheckSeverity.Warning));
                }

                // Seoul 키
                string? skKey = provider.SeoulKey;
                if (!string.IsNullOrWhiteSpace(skKey))
                {
                    string source = provider.KeySources.TryGetValue(ApiKeyProvider.KEY_SEOUL, out var s)
                        ? s : "unknown";
                    report.Step3_ApiKeys.Add(new CheckItem(
                        "SeoulKey",
                        true,
                        $"서울 열린데이터 API 키 로드 ✓ (출처: {source}, 길이: {skKey.Length}자)"));
                }
                else
                {
                    report.Step3_ApiKeys.Add(new CheckItem(
                        "SeoulKey",
                        false,
                        "서울 열린데이터 API 키가 설정되지 않았습니다. " +
                        "GH 컴포넌트의 SK 입력에 직접 연결하거나, " +
                        "appsettings.json에 \"SeoulKey\" 항목을 추가하세요.",
                        CheckSeverity.Warning));
                }
            }
            catch (Exception ex)
            {
                report.Step3_ApiKeys.Add(new CheckItem(
                    "ApiKeyProvider",
                    false,
                    $"API 키 로드 중 오류: {ex.Message}",
                    CheckSeverity.Error));
            }
        }

        // ── 설정 파일 자동 생성 (복구용) ────────────────────────────────

        /// <summary>
        /// appsettings.json이 존재하지 않을 때 빈 템플릿을 생성한다.
        /// 사용자가 키를 수동으로 입력할 수 있도록 가이드 역할.
        /// </summary>
        public static string CreateTemplateIfMissing(string? installDir = null)
        {
            string resolvedDir = installDir
                ?? Path.GetDirectoryName(typeof(PostInstallVerifier).Assembly.Location)
                ?? ".";

            string settingsPath = Path.Combine(resolvedDir, SETTINGS_FILENAME);

            if (File.Exists(settingsPath))
                return settingsPath;

            try
            {
                var template = new UrsusSettings
                {
                    VWorldKey = "",
                    SeoulKey = "",
                };

                string json = JsonSerializer.Serialize(template, new JsonSerializerOptions
                {
                    WriteIndented = true,
                });

                File.WriteAllText(settingsPath, json, System.Text.Encoding.UTF8);
                return settingsPath;
            }
            catch
            {
                // 파일 생성 실패 — 사용자 프로필 경로로 폴백
                string userPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "URSUS", SETTINGS_FILENAME);

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(userPath)!);
                    var template = new UrsusSettings
                    {
                        VWorldKey = "",
                        SeoulKey = "",
                    };
                    string json = JsonSerializer.Serialize(template, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                    });
                    File.WriteAllText(userPath, json, System.Text.Encoding.UTF8);
                    return userPath;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }
    }
}
