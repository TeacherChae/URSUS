namespace URSUS.Resources
{
    /// <summary>
    /// URSUS 전체에서 사용하는 구조화된 에러 코드.
    ///
    /// 코드 체계:
    ///   URSxxx — 접두사 URS (URSUS)
    ///   1xx    — API 키 / 인증 관련
    ///   2xx    — 네트워크 / API 호출 관련
    ///   3xx    — 데이터 파싱 / 매핑 관련
    ///   4xx    — 입력 검증 관련
    ///   5xx    — 파일 I/O / 내보내기 관련
    ///   6xx    — 시각화 관련
    ///   7xx    — 설치 / 설정 관련
    ///   8xx    — 캐시 관련
    ///   9xx    — 일반 / 미분류
    ///
    /// 새 에러 코드 추가 시:
    ///   1. 해당 카테고리 범위 내에서 번호 할당
    ///   2. ErrorGuideMap 에 가이드 URL 매핑 추가
    ///   3. ErrorMessages 에 사용자-facing 메시지 추가
    /// </summary>
    public static class ErrorCodes
    {
        // ═══════════════════════════════════════════════════════════════
        //  1xx — API 키 / 인증
        // ═══════════════════════════════════════════════════════════════

        /// <summary>VWorld API 키 미설정</summary>
        public const string VWorldKeyMissing = "URS101";

        /// <summary>서울 열린데이터 API 키 미설정</summary>
        public const string SeoulKeyMissing = "URS102";

        /// <summary>VWorld API 키 형식 오류 (너무 짧음 등)</summary>
        public const string VWorldKeyInvalidFormat = "URS103";

        /// <summary>서울 열린데이터 API 키 형식 오류</summary>
        public const string SeoulKeyInvalidFormat = "URS104";

        /// <summary>VWorld API 키 인증 실패 (서버 거부)</summary>
        public const string VWorldKeyRejected = "URS105";

        /// <summary>서울 열린데이터 API 키 인증 실패</summary>
        public const string SeoulKeyRejected = "URS106";

        /// <summary>API 키 저장 실패</summary>
        public const string ApiKeySaveFailed = "URS107";

        // ═══════════════════════════════════════════════════════════════
        //  2xx — 네트워크 / API 호출
        // ═══════════════════════════════════════════════════════════════

        /// <summary>VWorld 서버 연결 시간 초과</summary>
        public const string VWorldTimeout = "URS201";

        /// <summary>서울 열린데이터 서버 연결 시간 초과</summary>
        public const string SeoulTimeout = "URS202";

        /// <summary>VWorld 서버 HTTP 오류 (5xx 등)</summary>
        public const string VWorldServerError = "URS203";

        /// <summary>서울 열린데이터 서버 HTTP 오류</summary>
        public const string SeoulServerError = "URS204";

        /// <summary>VWorld 서버 연결 불가</summary>
        public const string VWorldConnectionFailed = "URS205";

        /// <summary>서울 열린데이터 서버 연결 불가</summary>
        public const string SeoulConnectionFailed = "URS206";

        /// <summary>API 응답이 유효한 JSON이 아님</summary>
        public const string InvalidJsonResponse = "URS207";

        /// <summary>API 응답 내 오류 코드 반환</summary>
        public const string ApiResponseError = "URS208";

        /// <summary>데이터 수집 중 일반 오류</summary>
        public const string DataCollectionFailed = "URS209";

        // ═══════════════════════════════════════════════════════════════
        //  3xx — 데이터 파싱 / 매핑
        // ═══════════════════════════════════════════════════════════════

        /// <summary>내장 매핑 리소스를 찾을 수 없음 (DLL 손상)</summary>
        public const string EmbeddedMappingNotFound = "URS301";

        /// <summary>외부 매핑 파일을 찾을 수 없음</summary>
        public const string MappingFileNotFound = "URS302";

        /// <summary>매핑 JSON 파싱 실패</summary>
        public const string MappingJsonParseFailed = "URS303";

        // ═══════════════════════════════════════════════════════════════
        //  4xx — 입력 검증
        // ═══════════════════════════════════════════════════════════════

        /// <summary>입력 리스트 길이 불일치</summary>
        public const string InputListLengthMismatch = "URS401";

        /// <summary>centroids 비어 있음</summary>
        public const string CentroidsEmpty = "URS402";

        /// <summary>values 비어 있음</summary>
        public const string ValuesEmpty = "URS403";

        /// <summary>centroids와 values 개수 불일치</summary>
        public const string CentroidsValuesMismatch = "URS404";

        /// <summary>Legal Codes 비어 있음</summary>
        public const string LegalCodesEmpty = "URS405";

        // ═══════════════════════════════════════════════════════════════
        //  5xx — 파일 I/O / 내보내기
        // ═══════════════════════════════════════════════════════════════

        /// <summary>CSV 내용 비어 있음</summary>
        public const string CsvContentEmpty = "URS501";

        /// <summary>파일 경로 미지정</summary>
        public const string FilePathEmpty = "URS502";

        /// <summary>파일 접근 권한 없음</summary>
        public const string FileAccessDenied = "URS503";

        /// <summary>파일 저장 실패</summary>
        public const string FileSaveFailed = "URS504";

        /// <summary>내보내기 취소됨</summary>
        public const string ExportCancelled = "URS505";

        /// <summary>내보낼 데이터 없음</summary>
        public const string NoDataToExport = "URS506";

        // ═══════════════════════════════════════════════════════════════
        //  6xx — 시각화
        // ═══════════════════════════════════════════════════════════════

        /// <summary>시각화 중 오류</summary>
        public const string VisualizationFailed = "URS601";

        // ═══════════════════════════════════════════════════════════════
        //  7xx — 설치 / 설정
        // ═══════════════════════════════════════════════════════════════

        /// <summary>설치 실패</summary>
        public const string InstallFailed = "URS701";

        /// <summary>설치 폴더 접근 오류</summary>
        public const string InstallFolderError = "URS702";

        /// <summary>설정 파일 저장 실패</summary>
        public const string SettingsSaveFailed = "URS703";

        // ═══════════════════════════════════════════════════════════════
        //  8xx — 캐시
        // ═══════════════════════════════════════════════════════════════

        /// <summary>캐시 읽기 실패</summary>
        public const string CacheReadFailed = "URS801";

        /// <summary>캐시 쓰기 실패</summary>
        public const string CacheWriteFailed = "URS802";

        /// <summary>캐시 만료됨 — API 재호출 필요</summary>
        public const string CacheExpired = "URS803";

        // ═══════════════════════════════════════════════════════════════
        //  9xx — 일반
        // ═══════════════════════════════════════════════════════════════

        /// <summary>알 수 없는 오류</summary>
        public const string Unknown = "URS999";
    }
}
