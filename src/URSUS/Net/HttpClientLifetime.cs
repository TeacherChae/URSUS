namespace URSUS.Net;

/// <summary>
/// API key와 query state는 source/요청에만 두고 socket handler만 process 수명으로 재사용한다.
/// 호출자는 이 client의 default headers를 변경하거나 dispose하지 않는다.
/// </summary>
internal static class HttpClientLifetime
{
    internal static HttpClient Shared { get; } = new();
}
