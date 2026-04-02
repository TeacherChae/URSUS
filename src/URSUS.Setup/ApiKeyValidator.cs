using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace URSUS.Setup
{
    /// <summary>
    /// API 키 유효성 검증 — 실제 API 엔드포인트에 경량 요청을 보내 키가 동작하는지 확인.
    /// </summary>
    public sealed class ApiKeyValidator : IDisposable
    {
        private readonly HttpClient _http;

        public ApiKeyValidator()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        }

        // ── 검증 결과 ────────────────────────────────────────────────────

        public sealed record ValidationResult(
            bool IsValid,
            string Message,
            ValidationErrorKind ErrorKind = ValidationErrorKind.None);

        public enum ValidationErrorKind
        {
            None,
            Empty,
            FormatError,
            NetworkError,
            Unauthorized,
            ServerError,
            Unknown
        }

        // ── VWorld API 키 검증 ───────────────────────────────────────────

        /// <summary>
        /// VWorld WFS API에 최소 요청을 보내 키 유효성을 확인한다.
        /// 서울 종로구 청운동 1건만 조회하여 빠르게 응답.
        /// </summary>
        public async Task<ValidationResult> ValidateVWorldKeyAsync(
            string apiKey, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return new ValidationResult(false, "VWorld API 키를 입력해주세요.", ValidationErrorKind.Empty);

            string trimmed = apiKey.Trim();

            // 기본 형식 검사: VWorld 키는 영숫자+하이픈, 보통 32~40자
            if (trimmed.Length < 10)
                return new ValidationResult(false,
                    "VWorld API 키 형식이 올바르지 않습니다. (너무 짧음)",
                    ValidationErrorKind.FormatError);

            try
            {
                // WFS GetFeature — maxFeatures=1 로 최소 부하 요청
                string url =
                    $"https://api.vworld.kr/req/wfs?service=WFS&version=2.0.0&request=GetFeature" +
                    $"&typeName=lt_c_lisdregistsystem&maxFeatures=1&srsName=EPSG:4326" +
                    $"&output=application/json&key={trimmed}";

                using var response = await _http.GetAsync(url, ct);

                if (response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync(ct);
                    // VWorld는 잘못된 키에도 200을 반환하고 JSON 에러 메시지를 보내는 경우가 있음
                    if (body.Contains("\"features\"") || body.Contains("\"numberMatched\""))
                        return new ValidationResult(true, "VWorld API 키가 유효합니다.");

                    if (body.Contains("\"error\"") || body.Contains("Unauthorized") ||
                        body.Contains("인증") || body.Contains("NotAuthorized"))
                        return new ValidationResult(false,
                            "VWorld API 키가 유효하지 않습니다. 키를 다시 확인해주세요.",
                            ValidationErrorKind.Unauthorized);

                    // 200이지만 예상치 못한 응답 — 일단 통과 (false negative 방지)
                    return new ValidationResult(true,
                        "VWorld 응답을 수신했습니다. (키가 유효한 것으로 간주)");
                }

                return response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized or
                    System.Net.HttpStatusCode.Forbidden =>
                        new ValidationResult(false,
                            "VWorld API 키가 거부되었습니다. 키를 다시 확인해주세요.",
                            ValidationErrorKind.Unauthorized),
                    _ =>
                        new ValidationResult(false,
                            $"VWorld 서버 응답 오류 (HTTP {(int)response.StatusCode}). 잠시 후 다시 시도해주세요.",
                            ValidationErrorKind.ServerError)
                };
            }
            catch (TaskCanceledException)
            {
                return new ValidationResult(false,
                    "VWorld 서버 연결 시간 초과. 네트워크를 확인해주세요.",
                    ValidationErrorKind.NetworkError);
            }
            catch (HttpRequestException ex)
            {
                return new ValidationResult(false,
                    $"VWorld 서버에 연결할 수 없습니다: {ex.Message}",
                    ValidationErrorKind.NetworkError);
            }
            catch (Exception ex)
            {
                return new ValidationResult(false,
                    $"VWorld 키 검증 중 오류: {ex.Message}",
                    ValidationErrorKind.Unknown);
            }
        }

        // ── 서울 열린데이터 API 키 검증 ──────────────────────────────────

        /// <summary>
        /// 서울 열린데이터광장 API에 최소 요청을 보내 키 유효성을 확인한다.
        /// tbGiGanByAdongW (기간별 행정동 통계) 1건 조회.
        /// </summary>
        public async Task<ValidationResult> ValidateSeoulKeyAsync(
            string apiKey, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return new ValidationResult(false, "서울 열린데이터 API 키를 입력해주세요.", ValidationErrorKind.Empty);

            string trimmed = apiKey.Trim();

            // 기본 형식 검사: 서울 열린데이터 키는 영숫자, 보통 30~50자
            if (trimmed.Length < 10)
                return new ValidationResult(false,
                    "서울 열린데이터 API 키 형식이 올바르지 않습니다. (너무 짧음)",
                    ValidationErrorKind.FormatError);

            try
            {
                // 월평균소득 데이터 1건만 조회
                string url =
                    $"http://openapi.seoul.go.kr:8088/{trimmed}/json/tbGiGanByAdongW/1/1/";

                using var response = await _http.GetAsync(url, ct);

                if (response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync(ct);

                    // 정상 응답: "tbGiGanByAdongW" 키가 포함됨
                    if (body.Contains("\"tbGiGanByAdongW\""))
                        return new ValidationResult(true, "서울 열린데이터 API 키가 유효합니다.");

                    // 에러 응답: 인증 실패
                    if (body.Contains("INFO-200") || body.Contains("해당하는 데이터가 없습니다"))
                        return new ValidationResult(true,
                            "서울 열린데이터 API 키가 유효합니다. (데이터 0건이지만 인증 성공)");

                    if (body.Contains("INFO-300") || body.Contains("인증키가 유효하지"))
                        return new ValidationResult(false,
                            "서울 열린데이터 API 키가 유효하지 않습니다. 키를 다시 확인해주세요.",
                            ValidationErrorKind.Unauthorized);

                    // 기타 에러 코드
                    if (body.Contains("\"RESULT\"") && body.Contains("\"CODE\""))
                        return new ValidationResult(false,
                            "서울 열린데이터 API 응답에 오류가 있습니다. 키를 다시 확인해주세요.",
                            ValidationErrorKind.Unauthorized);

                    // 예상 외 응답 — 키는 유효한 것으로 간주
                    return new ValidationResult(true,
                        "서울 열린데이터 응답을 수신했습니다. (키가 유효한 것으로 간주)");
                }

                return new ValidationResult(false,
                    $"서울 열린데이터 서버 응답 오류 (HTTP {(int)response.StatusCode}). 잠시 후 다시 시도해주세요.",
                    ValidationErrorKind.ServerError);
            }
            catch (TaskCanceledException)
            {
                return new ValidationResult(false,
                    "서울 열린데이터 서버 연결 시간 초과. 네트워크를 확인해주세요.",
                    ValidationErrorKind.NetworkError);
            }
            catch (HttpRequestException ex)
            {
                return new ValidationResult(false,
                    $"서울 열린데이터 서버에 연결할 수 없습니다: {ex.Message}",
                    ValidationErrorKind.NetworkError);
            }
            catch (Exception ex)
            {
                return new ValidationResult(false,
                    $"서울 열린데이터 키 검증 중 오류: {ex.Message}",
                    ValidationErrorKind.Unknown);
            }
        }

        public void Dispose() => _http.Dispose();
    }
}
