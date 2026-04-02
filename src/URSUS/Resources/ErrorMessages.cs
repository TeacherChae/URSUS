namespace URSUS.Resources
{
    /// <summary>
    /// URSUS 전체에서 사용하는 한글 에러/상태 메시지 중앙 리소스.
    ///
    /// 설계 원칙:
    /// - 모든 사용자-facing 메시지를 한 곳에서 관리 (i18n 대비)
    /// - 카테고리별 내부 static class로 구조화
    /// - 포맷 파라미터가 필요한 메시지는 static 메서드로 제공
    /// - 학생 사용자가 이해할 수 있는 쉬운 한글 표현 사용
    ///
    /// 새 메시지 추가 시:
    ///   1. 해당 카테고리 클래스에 const 또는 static 메서드 추가
    ///   2. 호출부에서 ErrorMessages.Category.MESSAGE_NAME 으로 참조
    /// </summary>
    public static class ErrorMessages
    {
        // ═════════════════════════════════════════════════════════════════
        //  API 키 관련
        // ═════════════════════════════════════════════════════════════════

        public static class ApiKey
        {
            // ── 누락 ────────────────────────────────────────────────────
            public const string MissingPrefix =
                "[URSUS] 다음 API 키가 설정되지 않았습니다: ";

            public const string LoadComplete =
                "[URSUS] API 키 로드 완료:";

            // ── 설정 안내 ───────────────────────────────────────────────
            public const string SetupGuideHeader =
                "설정 방법 (택 1):";

            public const string SetupGuideWire =
                "  (1) GH 컴포넌트의 VK/SK 입력에 직접 연결";

            public const string SetupGuideEnv =
                "  (2) 환경변수 설정:";

            public const string SetupGuideFile =
                "  (3) 설정 파일에 작성:";

            public const string SetupGuideFileAlt =
                "      또는 DLL 옆: ";

            // ── 발급 안내 ───────────────────────────────────────────────
            public const string IssuanceHeader =
                "API 키 발급:";

            public const string VWorldIssuanceUrl =
                "  VWorld: https://www.vworld.kr/dev/v4dv_2ddataguide2_s001.do (무료 회원가입 후 발급)";

            public const string SeoulIssuanceUrl =
                "  서울 열린데이터: https://data.seoul.go.kr/ (무료 회원가입 후 발급)";

            // ── 저장 ────────────────────────────────────────────────────
            public static string SaveFailed(string detail) =>
                $"API 키 저장 중 오류가 발생했습니다:\n{detail}\n\n" +
                "설치 후 appsettings.json에서 직접 설정할 수 있습니다.";

            public const string ExistingKeyLoaded =
                "기존 설정에서 키를 불러왔습니다.";

            public const string EnterVWorldKey =
                "키를 입력해주세요.";

            public const string EnterSeoulKey =
                "키를 입력해주세요.";
        }

        // ═════════════════════════════════════════════════════════════════
        //  API 키 설정 (GH 컴포넌트)
        // ═════════════════════════════════════════════════════════════════

        public static class ApiKeySettings
        {
            public static string MissingRequired(string keyNames) =>
                $"필수 API 키 미설정: {keyNames}\n" +
                "Open 입력을 True로 설정하여 키를 입력하세요.";

            public const string LoadComplete =
                "API 키 로드 완료";

            public const string NoKeysToSave =
                "저장할 키가 없습니다. 최소 하나의 키를 입력해주세요.";

            public static string SaveComplete(int count) =>
                $"API 키 {count}개가 저장되었습니다.\n" +
                "Solver 컴포넌트에서 자동으로 로드됩니다.";

            public static string SaveFailed(string detail) =>
                $"저장 중 오류:\n{detail}\n\n" +
                "appsettings.json을 직접 편집하거나,\n" +
                "Solver 컴포넌트의 VK/SK 입력에 직접 연결하세요.";

            public const string LoadedFromExisting =
                "기존 설정에서 로드됨";
        }

        // ═════════════════════════════════════════════════════════════════
        //  API 키 검증 (Setup)
        // ═════════════════════════════════════════════════════════════════

        public static class Validation
        {
            // ── VWorld ──────────────────────────────────────────────────
            public const string VWorldKeyEmpty =
                "VWorld API 키를 입력해주세요.";

            public const string VWorldKeyTooShort =
                "VWorld API 키 형식이 올바르지 않습니다. (너무 짧음)";

            public const string VWorldKeyValid =
                "VWorld API 키가 유효합니다.";

            public const string VWorldKeyAccepted =
                "VWorld 응답을 수신했습니다. (키가 유효한 것으로 간주)";

            public const string VWorldKeyInvalid =
                "VWorld API 키가 유효하지 않습니다. 키를 다시 확인해주세요.";

            public const string VWorldKeyRejected =
                "VWorld API 키가 거부되었습니다. 키를 다시 확인해주세요.";

            public static string VWorldServerError(int statusCode) =>
                $"VWorld 서버 응답 오류 (HTTP {statusCode}). 잠시 후 다시 시도해주세요.";

            public const string VWorldTimeout =
                "VWorld 서버 연결 시간 초과. 네트워크를 확인해주세요.";

            public static string VWorldConnectionFailed(string detail) =>
                $"VWorld 서버에 연결할 수 없습니다: {detail}";

            public static string VWorldValidationError(string detail) =>
                $"VWorld 키 검증 중 오류: {detail}";

            // ── 서울 열린데이터 ─────────────────────────────────────────
            public const string SeoulKeyEmpty =
                "서울 열린데이터 API 키를 입력해주세요.";

            public const string SeoulKeyTooShort =
                "서울 열린데이터 API 키 형식이 올바르지 않습니다. (너무 짧음)";

            public const string SeoulKeyValid =
                "서울 열린데이터 API 키가 유효합니다.";

            public const string SeoulKeyValidNoData =
                "서울 열린데이터 API 키가 유효합니다. (데이터 0건이지만 인증 성공)";

            public const string SeoulKeyAccepted =
                "서울 열린데이터 응답을 수신했습니다. (키가 유효한 것으로 간주)";

            public const string SeoulKeyInvalid =
                "서울 열린데이터 API 키가 유효하지 않습니다. 키를 다시 확인해주세요.";

            public const string SeoulKeyResponseError =
                "서울 열린데이터 API 응답에 오류가 있습니다. 키를 다시 확인해주세요.";

            public static string SeoulServerError(int statusCode) =>
                $"서울 열린데이터 서버 응답 오류 (HTTP {statusCode}). 잠시 후 다시 시도해주세요.";

            public const string SeoulTimeout =
                "서울 열린데이터 서버 연결 시간 초과. 네트워크를 확인해주세요.";

            public static string SeoulConnectionFailed(string detail) =>
                $"서울 열린데이터 서버에 연결할 수 없습니다: {detail}";

            public static string SeoulValidationError(string detail) =>
                $"서울 열린데이터 키 검증 중 오류: {detail}";

            // ── 공통 ────────────────────────────────────────────────────
            public const string Validating =
                "검증 중...";
        }

        // ═════════════════════════════════════════════════════════════════
        //  API 호출 / 네트워크
        // ═════════════════════════════════════════════════════════════════

        public static class Api
        {
            public const string InvalidJsonResponse =
                "API 응답이 올바른 JSON 형식이 아닙니다.";

            public static string ApiError(string code, string message) =>
                $"API 오류: {code} - {message}";

            public static string DataCollectionFailed(string detail) =>
                $"데이터 수집 중 오류가 발생했습니다: {detail}";
        }

        // ═════════════════════════════════════════════════════════════════
        //  데이터 / 파싱
        // ═════════════════════════════════════════════════════════════════

        public static class DataSource
        {
            public const string IdEmpty =
                "데이터 소스의 Id가 비어있습니다.";
        }

        public static class Data
        {
            // ── 매핑 파일 ───────────────────────────────────────────────
            public static string EmbeddedMappingNotFound(string resourceName) =>
                $"내장 매핑 리소스를 찾을 수 없습니다: {resourceName}. " +
                "DLL이 손상되었거나 빌드가 올바르지 않습니다.";

            public static string MappingFileNotFound(string path) =>
                $"매핑 파일을 찾을 수 없습니다: {path}";

            public const string MappingJsonParseFailed =
                "매핑 JSON 파싱 실패";

            // ── 데이터셋 기본값 ─────────────────────────────────────────
            public const string DefaultDataSetUsed =
                "DataSet 미입력 — 기본 데이터셋(소득, 인구, 대중교통) 전체를 사용합니다.";

            // ── 입력 검증 ───────────────────────────────────────────────
            public static string InputListLengthMismatch(
                int codesCount, int namesCount, int areasCount, int valuesCount) =>
                $"입력 리스트의 길이가 일치하지 않습니다: " +
                $"codes={codesCount}, names={namesCount}, " +
                $"areas={areasCount}, values={valuesCount}";

            public const string CentroidsEmpty =
                "centroids가 비어 있습니다.";

            public const string ValuesEmpty =
                "values가 비어 있습니다.";

            public const string CentroidsValuesMismatch =
                "centroids와 values의 개수가 다릅니다.";

            public const string LegalCodesEmpty =
                "Legal Codes가 비어 있습니다. Solver 출력을 연결해 주세요.";

            public static string InputListLengthMismatchShort(
                int lc, int n, int a, int v) =>
                $"입력 리스트 길이가 일치하지 않습니다: " +
                $"LC={lc}, N={n}, A={a}, V={v}";
        }

        // ═════════════════════════════════════════════════════════════════
        //  가중치 검증
        // ═════════════════════════════════════════════════════════════════

        public static class Weight
        {
            public static string NegativeWeight(int index, double value) =>
                $"가중치[{index}] = {value:F4} — 음수 가중치는 허용되지 않습니다. " +
                "슬라이더를 0 이상으로 설정하세요.";

            public const string AllZeroWeights =
                "모든 가중치의 합이 0입니다. " +
                "최소 하나의 데이터셋에 0보다 큰 가중치를 설정하세요.";

            public static string AutoNormalized(double weightSum, string pairs) =>
                $"가중치 합({weightSum:F3})이 1이 아니므로 자동 정규화됩니다 → {pairs}";
        }

        // ═════════════════════════════════════════════════════════════════
        //  CSV 내보내기
        // ═════════════════════════════════════════════════════════════════

        public static class CsvExport
        {
            public const string ContentEmpty =
                "CSV 내용이 비어 있습니다.";

            public const string FilePathEmpty =
                "파일 경로가 지정되지 않았습니다.";

            public static string SaveComplete(int rowCount, string fileName) =>
                $"CSV 저장 완료: {rowCount}행 -> {fileName}";

            public static string SaveCompleteShort(int rowCount, string fileName) =>
                $"저장 완료: {rowCount}행 -> {fileName}";

            public static string AccessDenied(string path) =>
                $"CSV 파일 접근 권한이 없습니다: {path}";

            public static string WriteFailed(string detail) =>
                $"CSV 파일 저장 실패: {detail}";

            public static string FileAccessDenied(string path) =>
                $"파일 접근 권한이 없습니다: {path}";

            public static string FileSaveFailed(string detail) =>
                $"파일 저장 실패: {detail}";

            public static string ExportError(string detail) =>
                $"내보내기 오류: {detail}";

            public static string ExportFailed(string detail) =>
                $"내보내기 실패: {detail}";

            public const string ExportCancelled =
                "내보내기가 취소되었습니다.";

            public const string NoDataToExport =
                "내보낼 데이터가 없습니다.\nSolver 출력을 연결해 주세요.";

            public const string WaitingForExport =
                "대기 중 -- Export를 True로 설정하세요.";

            public static string ExportComplete(string filePath, int rowCount) =>
                $"CSV 내보내기 완료!\n\n파일: {filePath}\n행 수: {rowCount}행";

            public static string DialogFallback(string detail) =>
                $"SaveFileDialog를 표시할 수 없어 기본 경로를 사용합니다: {detail}";

            public static string CannotOpenFile(string detail) =>
                $"파일을 열 수 없습니다: {detail}";
        }

        // ═════════════════════════════════════════════════════════════════
        //  CSV 엔드포인트 / 서버
        // ═════════════════════════════════════════════════════════════════

        public static class CsvEndpoint
        {
            public const string WaitingForEnable =
                "대기 중 — Enable을 True로 설정하세요.";

            public const string ServerStopped =
                "서버 중지됨 — Enable을 True로 설정하세요.";

            public static string ServerStartFailed(string detail) =>
                $"서버 시작 실패: {detail}";

            public static string ServerRunning(int rowCount, string baseUrl) =>
                $"서버 실행 중 — {rowCount}행 제공 중\n" +
                $"URL: {baseUrl}/api/csv/download";

            public static string DataUpdateFailed(string detail) =>
                $"데이터 업데이트 실패: {detail}";
        }

        // ═════════════════════════════════════════════════════════════════
        //  시각화
        // ═════════════════════════════════════════════════════════════════

        public static class Visualization
        {
            public static string VisualizationFailed(string detail) =>
                $"시각화 중 오류: {detail}";
        }

        // ═════════════════════════════════════════════════════════════════
        //  설치 (Setup)
        // ═════════════════════════════════════════════════════════════════

        public static class Setup
        {
            public const string ProgressCreatingFolder =
                "설치 폴더 생성 중...";

            public static string ProgressCopying(string fileName) =>
                $"복사 중: {fileName}";

            public const string ProgressSavingSettings =
                "설정 파일 저장 중...";

            public const string InstallComplete =
                "설치 완료!";

            public static string InstallFailed(string detail) =>
                $"설치 실패: {detail}";

            public static string InstallErrorDialog(string detail, string targetDir) =>
                $"설치 중 오류가 발생했습니다:\n\n{detail}\n\n" +
                $"수동으로 파일을 복사해주세요:\n  대상 폴더: {targetDir}";

            public const string InstallSuccess =
                "URSUS가 성공적으로 설치되었습니다.";

            public const string ApiKeyMissingWarning =
                "일부 API 키가 설정되지 않았습니다.\n" +
                "  Grasshopper에서 VK/SK 입력에 직접 연결하거나,\n" +
                "  설치 폴더의 appsettings.json을 편집하세요.";

            public const string ApiKeyReady =
                "API 키가 설정되어 있습니다. 바로 사용할 수 있습니다!";

            public const string VWorldKeyNotSet =
                "미설정 (나중에 설정 가능)";

            public const string KeyValidated =
                "검증 완료";

            public const string KeyEnteredNotValidated =
                "입력됨 (미검증)";
        }

        // ═════════════════════════════════════════════════════════════════
        //  캐시
        // ═════════════════════════════════════════════════════════════════

        public static class Cache
        {
            public static string UsingCache(string name, double remainingDays) =>
                $"[CACHE] {name} 캐시 사용 (만료까지 {remainingDays:F1}일)";

            public static string FetchingApi(string name) =>
                $"[CACHE] {name} API 호출 중...";

            public static string SaveComplete(string name, int count) =>
                $"[CACHE] {name} 저장 완료 ({count}건)";
        }

        // ═════════════════════════════════════════════════════════════════
        //  Solver 로그
        // ═════════════════════════════════════════════════════════════════

        public static class Solver
        {
            public static string DistrictsCollected(int count) =>
                $"[Solver] 법정동 {count}개 수집";

            public static string OverlayComplete(int datasetCount) =>
                $"[Solver] overlay 값 계산 완료 ({datasetCount}개 데이터셋)";

            public static string UnionResult(bool success) =>
                $"[Solver] Union 외곽선 생성 {(success ? "성공" : "실패")}";

            public static string PageProgress(int completed, int total) =>
                $"[INFO] {completed}/{total} 페이지 완료";

            public static string TotalCount(int count) =>
                $"[INFO] list_total_count = {count}, 병렬 페이지 요청 시작";
        }
    }
}
