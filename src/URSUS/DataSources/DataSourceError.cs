namespace URSUS.DataSources
{
    /// <summary>
    /// 데이터 소스 에러 정보.
    ///
    /// ErrorCodes 체계(URS1xx~9xx)와 연동되며,
    /// 사용자에게 보여줄 한글 메시지와 해결 가이드를 포함한다.
    /// </summary>
    public class DataSourceError
    {
        /// <summary>
        /// 에러 코드 (ErrorCodes 상수, 예: "URS201").
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// 사용자에게 표시할 한글 메시지.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// 에러 심각도.
        /// </summary>
        public ErrorSeverity Severity { get; }

        /// <summary>
        /// 원본 예외 (디버깅용, null 가능).
        /// </summary>
        public Exception? InnerException { get; }

        public DataSourceError(string code, string message,
                               ErrorSeverity severity = ErrorSeverity.Error,
                               Exception? innerException = null)
        {
            Code           = code;
            Message        = message;
            Severity       = severity;
            InnerException = innerException;
        }

        // ─────────────────────────────────────────────────────────────
        //  팩토리 메서드 — 자주 발생하는 에러 유형
        // ─────────────────────────────────────────────────────────────

        /// <summary>API 키 누락</summary>
        public static DataSourceError ApiKeyMissing(string keyName, string errorCode)
            => new(errorCode,
                   $"API 키가 설정되지 않았습니다: {keyName}\n" +
                   "→ URSUS.Setup.exe를 실행하거나, GH 컴포넌트의 입력에 직접 연결하세요.");

        /// <summary>네트워크 타임아웃</summary>
        public static DataSourceError Timeout(string sourceName, string errorCode, Exception? ex = null)
            => new(errorCode,
                   $"{sourceName} 서버 연결 시간 초과.\n" +
                   "→ 네트워크 연결을 확인하고 다시 시도해주세요.",
                   ErrorSeverity.Error, ex);

        /// <summary>HTTP 서버 에러</summary>
        public static DataSourceError HttpError(string sourceName, int statusCode, string errorCode)
            => new(errorCode,
                   $"{sourceName} 서버 응답 오류 (HTTP {statusCode}).\n" +
                   "→ 잠시 후 다시 시도해주세요.");

        /// <summary>데이터 파싱 실패</summary>
        public static DataSourceError ParseError(string detail, string errorCode, Exception? ex = null)
            => new(errorCode,
                   $"데이터 파싱 실패: {detail}",
                   ErrorSeverity.Error, ex);

        /// <summary>데이터 없음 (0건 수집)</summary>
        public static DataSourceError NoData(string sourceName, string errorCode)
            => new(errorCode,
                   $"{sourceName}에서 수집된 데이터가 없습니다.\n" +
                   "→ 분석 범위와 API 키를 확인해주세요.",
                   ErrorSeverity.Warning);

        /// <summary>캐시 읽기 실패 (복구 가능)</summary>
        public static DataSourceError CacheCorrupted(string cachePath, string errorCode, Exception? ex = null)
            => new(errorCode,
                   $"캐시 파일이 손상되었습니다: {cachePath}\n" +
                   "→ 해당 파일을 삭제하고 다시 실행하면 API에서 재수집합니다.",
                   ErrorSeverity.Warning, ex);

        public override string ToString()
            => $"[{Code}] {Message}";
    }

    /// <summary>에러 심각도</summary>
    public enum ErrorSeverity
    {
        /// <summary>정보성 (로그만 남김)</summary>
        Info,

        /// <summary>경고 (계속 진행 가능, 일부 데이터 누락 가능)</summary>
        Warning,

        /// <summary>에러 (해당 데이터 소스 결과 사용 불가)</summary>
        Error
    }
}
