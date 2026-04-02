using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace URSUS.Resources
{
    /// <summary>
    /// 에러 코드 → 해결 가이드 URL 매핑 테이블.
    ///
    /// 설계 원칙:
    /// - 각 에러 코드(ErrorCodes.URS*)에 대해 사용자가 참조할 수 있는
    ///   문서/가이드 URL을 매핑한다.
    /// - 학생 사용자가 에러 발생 시 즉시 해결 방법을 찾을 수 있도록
    ///   Grasshopper 컴포넌트 메시지에 URL을 함께 표시한다.
    /// - 새 데이터 소스/에러 코드 추가 시 이 파일만 수정하면 된다.
    ///
    /// 사용 예:
    ///   string url = ErrorGuideMap.GetGuideUrl(ErrorCodes.VWorldKeyMissing);
    ///   string msg = ErrorGuideMap.FormatMessageWithGuide(
    ///       ErrorCodes.VWorldKeyMissing,
    ///       "VWorld API 키가 설정되지 않았습니다.");
    /// </summary>
    public static class ErrorGuideMap
    {
        // ═══════════════════════════════════════════════════════════════
        //  기본 URL 상수
        // ═══════════════════════════════════════════════════════════════

        /// <summary>URSUS GitHub Wiki 기본 경로</summary>
        private const string WikiBase = "https://github.com/DaeguURSUS/URSUS/wiki";

        /// <summary>VWorld 개발자 포털</summary>
        private const string VWorldPortal = "https://www.vworld.kr/dev/v4dv_2ddataguide2_s001.do";

        /// <summary>서울 열린데이터 광장</summary>
        private const string SeoulDataPortal = "https://data.seoul.go.kr/";

        /// <summary>해당 에러 코드에 가이드가 없을 때 반환할 기본 URL</summary>
        private const string DefaultGuideUrl = WikiBase + "/Troubleshooting";

        // ═══════════════════════════════════════════════════════════════
        //  매핑 테이블
        // ═══════════════════════════════════════════════════════════════

        private static readonly Dictionary<string, GuideEntry> _entries = new()
        {
            // ── 1xx: API 키 / 인증 ──────────────────────────────────
            [ErrorCodes.VWorldKeyMissing] = new(
                WikiBase + "/API-Key-Setup#vworld",
                "VWorld API 키를 발급받고 설정하는 방법을 안내합니다."),

            [ErrorCodes.SeoulKeyMissing] = new(
                WikiBase + "/API-Key-Setup#seoul",
                "서울 열린데이터 API 키를 발급받고 설정하는 방법을 안내합니다."),

            [ErrorCodes.VWorldKeyInvalidFormat] = new(
                WikiBase + "/API-Key-Setup#vworld-format",
                "VWorld API 키 형식 요구사항을 확인합니다."),

            [ErrorCodes.SeoulKeyInvalidFormat] = new(
                WikiBase + "/API-Key-Setup#seoul-format",
                "서울 열린데이터 API 키 형식 요구사항을 확인합니다."),

            [ErrorCodes.VWorldKeyRejected] = new(
                WikiBase + "/API-Key-Setup#vworld-rejected",
                "VWorld API 키가 거부된 경우 재발급 또는 갱신 방법을 안내합니다."),

            [ErrorCodes.SeoulKeyRejected] = new(
                WikiBase + "/API-Key-Setup#seoul-rejected",
                "서울 열린데이터 API 키가 거부된 경우 재발급 방법을 안내합니다."),

            [ErrorCodes.ApiKeySaveFailed] = new(
                WikiBase + "/Installation#settings-file",
                "appsettings.json 파일 수동 편집 방법을 안내합니다."),

            // ── 2xx: 네트워크 / API 호출 ────────────────────────────
            [ErrorCodes.VWorldTimeout] = new(
                WikiBase + "/Troubleshooting#network",
                "네트워크 연결을 확인하거나 VWorld 서버 상태를 점검합니다."),

            [ErrorCodes.SeoulTimeout] = new(
                WikiBase + "/Troubleshooting#network",
                "네트워크 연결을 확인하거나 서울 열린데이터 서버 상태를 점검합니다."),

            [ErrorCodes.VWorldServerError] = new(
                WikiBase + "/Troubleshooting#vworld-server",
                "VWorld 서버 점검 여부를 확인합니다. 잠시 후 재시도하세요."),

            [ErrorCodes.SeoulServerError] = new(
                WikiBase + "/Troubleshooting#seoul-server",
                "서울 열린데이터 서버 점검 여부를 확인합니다. 잠시 후 재시도하세요."),

            [ErrorCodes.VWorldConnectionFailed] = new(
                WikiBase + "/Troubleshooting#network",
                "방화벽, 프록시 설정 또는 인터넷 연결을 확인합니다."),

            [ErrorCodes.SeoulConnectionFailed] = new(
                WikiBase + "/Troubleshooting#network",
                "방화벽, 프록시 설정 또는 인터넷 연결을 확인합니다."),

            [ErrorCodes.InvalidJsonResponse] = new(
                WikiBase + "/Troubleshooting#json-parse",
                "API 응답 오류 해결 방법을 안내합니다. 캐시 삭제 후 재시도하세요."),

            [ErrorCodes.ApiResponseError] = new(
                WikiBase + "/Troubleshooting#api-error",
                "API 에러 코드별 대응 방법을 안내합니다."),

            [ErrorCodes.DataCollectionFailed] = new(
                WikiBase + "/Troubleshooting#data-collection",
                "데이터 수집 오류의 일반적인 원인과 해결 방법을 안내합니다."),

            // ── 3xx: 데이터 파싱 / 매핑 ─────────────────────────────
            [ErrorCodes.EmbeddedMappingNotFound] = new(
                WikiBase + "/Installation#reinstall",
                "DLL이 손상되었을 수 있습니다. 플러그인을 재설치하세요."),

            [ErrorCodes.MappingFileNotFound] = new(
                WikiBase + "/Installation#mapping-file",
                "매핑 파일 위치를 확인하거나 재설치합니다."),

            [ErrorCodes.MappingJsonParseFailed] = new(
                WikiBase + "/Troubleshooting#mapping-parse",
                "매핑 파일이 손상되었습니다. 플러그인을 재설치하세요."),

            // ── 4xx: 입력 검증 ──────────────────────────────────────
            [ErrorCodes.InputListLengthMismatch] = new(
                WikiBase + "/Getting-Started#component-wiring",
                "Grasshopper 컴포넌트 입력 연결 방법을 안내합니다."),

            [ErrorCodes.CentroidsEmpty] = new(
                WikiBase + "/Getting-Started#component-wiring",
                "Solver 출력을 Visualizer에 올바르게 연결하는 방법을 안내합니다."),

            [ErrorCodes.ValuesEmpty] = new(
                WikiBase + "/Getting-Started#component-wiring",
                "Solver 출력을 Visualizer에 올바르게 연결하는 방법을 안내합니다."),

            [ErrorCodes.CentroidsValuesMismatch] = new(
                WikiBase + "/Getting-Started#component-wiring",
                "Centroids와 Values의 개수가 일치하도록 연결을 확인합니다."),

            [ErrorCodes.LegalCodesEmpty] = new(
                WikiBase + "/Getting-Started#first-run",
                "Solver를 먼저 실행한 뒤 출력을 연결하세요."),

            // ── 5xx: 파일 I/O / 내보내기 ────────────────────────────
            [ErrorCodes.CsvContentEmpty] = new(
                WikiBase + "/CSV-Export#no-content",
                "CSV 내보내기 전에 데이터가 올바르게 생성되었는지 확인합니다."),

            [ErrorCodes.FilePathEmpty] = new(
                WikiBase + "/CSV-Export#file-path",
                "파일 저장 경로를 지정하는 방법을 안내합니다."),

            [ErrorCodes.FileAccessDenied] = new(
                WikiBase + "/Troubleshooting#file-permission",
                "파일/폴더 쓰기 권한을 확인합니다. 관리자 권한이 필요할 수 있습니다."),

            [ErrorCodes.FileSaveFailed] = new(
                WikiBase + "/Troubleshooting#file-save",
                "파일 저장 실패의 일반적인 원인과 해결 방법을 안내합니다."),

            [ErrorCodes.ExportCancelled] = new(
                WikiBase + "/CSV-Export",
                "CSV 내보내기 사용 방법을 안내합니다."),

            [ErrorCodes.NoDataToExport] = new(
                WikiBase + "/CSV-Export#no-data",
                "Solver 출력을 CSV Export 컴포넌트에 연결하세요."),

            // ── 6xx: 시각화 ─────────────────────────────────────────
            [ErrorCodes.VisualizationFailed] = new(
                WikiBase + "/Troubleshooting#visualization",
                "시각화 오류의 일반적인 원인과 해결 방법을 안내합니다."),

            // ── 7xx: 설치 / 설정 ────────────────────────────────────
            [ErrorCodes.InstallFailed] = new(
                WikiBase + "/Installation#manual-install",
                "자동 설치 실패 시 수동 설치 방법을 안내합니다."),

            [ErrorCodes.InstallFolderError] = new(
                WikiBase + "/Installation#folder-permission",
                "설치 폴더 권한 문제 해결 방법을 안내합니다."),

            [ErrorCodes.SettingsSaveFailed] = new(
                WikiBase + "/Installation#settings-file",
                "설정 파일을 수동으로 편집하는 방법을 안내합니다."),

            // ── 8xx: 캐시 ───────────────────────────────────────────
            [ErrorCodes.CacheReadFailed] = new(
                WikiBase + "/Troubleshooting#cache",
                "캐시 파일 삭제 후 재시도하세요."),

            [ErrorCodes.CacheWriteFailed] = new(
                WikiBase + "/Troubleshooting#cache",
                "캐시 폴더 쓰기 권한을 확인합니다."),

            [ErrorCodes.CacheExpired] = new(
                WikiBase + "/Troubleshooting#cache",
                "캐시가 만료되었습니다. 네트워크 연결 상태에서 재실행하세요."),

            // ── 9xx: 일반 ───────────────────────────────────────────
            [ErrorCodes.Unknown] = new(
                DefaultGuideUrl,
                "알 수 없는 오류입니다. GitHub Issues에 문의해 주세요."),
        };

        /// <summary>읽기 전용 매핑 테이블 공개 (테스트/디버깅용)</summary>
        public static IReadOnlyDictionary<string, GuideEntry> Entries { get; } =
            new ReadOnlyDictionary<string, GuideEntry>(_entries);

        // ═══════════════════════════════════════════════════════════════
        //  조회 API
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 에러 코드에 해당하는 가이드 URL을 반환한다.
        /// 매핑이 없으면 기본 Troubleshooting 페이지 URL을 반환한다.
        /// </summary>
        public static string GetGuideUrl(string errorCode)
        {
            return _entries.TryGetValue(errorCode, out var entry)
                ? entry.Url
                : DefaultGuideUrl;
        }

        /// <summary>
        /// 에러 코드에 해당하는 가이드 설명을 반환한다.
        /// </summary>
        public static string GetGuideDescription(string errorCode)
        {
            return _entries.TryGetValue(errorCode, out var entry)
                ? entry.Description
                : "해결 방법은 가이드 문서를 참조하세요.";
        }

        /// <summary>
        /// 에러 메시지에 가이드 URL을 덧붙인 문자열을 생성한다.
        /// Grasshopper 컴포넌트의 AddRuntimeMessage()에 직접 사용 가능.
        ///
        /// 출력 형식:
        ///   [URS101] VWorld API 키가 설정되지 않았습니다.
        ///   💡 해결 방법: VWorld API 키를 발급받고 설정하는 방법을 안내합니다.
        ///   📖 가이드: https://github.com/DaeguURSUS/URSUS/wiki/API-Key-Setup#vworld
        /// </summary>
        public static string FormatMessageWithGuide(string errorCode, string message)
        {
            string url = GetGuideUrl(errorCode);
            string desc = GetGuideDescription(errorCode);
            return $"[{errorCode}] {message}\n" +
                   $"  해결 방법: {desc}\n" +
                   $"  가이드: {url}";
        }

        /// <summary>
        /// 에러 코드가 매핑 테이블에 등록되어 있는지 확인한다.
        /// </summary>
        public static bool HasGuide(string errorCode) =>
            _entries.ContainsKey(errorCode);

        /// <summary>
        /// 특정 카테고리(접두사)에 해당하는 모든 에러 코드를 반환한다.
        /// 예: GetCodesByPrefix("URS1") → API 키 관련 코드 목록
        /// </summary>
        public static IReadOnlyList<string> GetCodesByPrefix(string prefix)
        {
            var result = new List<string>();
            foreach (var key in _entries.Keys)
            {
                if (key.StartsWith(prefix))
                    result.Add(key);
            }
            return result;
        }

        // ═══════════════════════════════════════════════════════════════
        //  확장 지점
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 런타임에 커스텀 에러 코드-가이드 매핑을 등록한다.
        /// 새 데이터 소스 플러그인이 자체 에러 코드를 추가할 때 사용.
        /// 이미 존재하는 코드는 덮어쓴다.
        /// </summary>
        public static void Register(string errorCode, string guideUrl, string description)
        {
            _entries[errorCode] = new GuideEntry(guideUrl, description);
        }

        /// <summary>
        /// 런타임에 여러 에러 코드-가이드 매핑을 일괄 등록한다.
        /// </summary>
        public static void RegisterAll(IEnumerable<(string Code, string Url, string Description)> entries)
        {
            foreach (var (code, url, desc) in entries)
                _entries[code] = new GuideEntry(url, desc);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  GuideEntry — 가이드 URL + 설명을 묶는 값 타입
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 에러 코드에 대한 해결 가이드 정보.
    /// </summary>
    /// <param name="Url">가이드 문서 URL</param>
    /// <param name="Description">한줄 설명 (사용자에게 표시)</param>
    public record GuideEntry(string Url, string Description);
}
