using System.Net;
using System.Text.RegularExpressions;
using URSUS.Caching;

namespace URSUS.Net;

public enum ProviderKind { Seoul, DataGoKr, VWorld, Other }

public static class SecretRedactor
{
    private static readonly Regex QuerySecret = new(
        @"(?i)([?&](?:serviceKey|apiKey|key|token)=)[^&\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SeoulPathSecret = new(
        @"(?i)(openapi\.seoul\.go\.kr:8088/)[^/\s]+/",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Redact(string value)
        => SeoulPathSecret.Replace(QuerySecret.Replace(value, "$1***"), "$1***/");
}

public interface IAsyncDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemAsyncDelay : IAsyncDelay
{
    public static SystemAsyncDelay Instance { get; } = new();
    private SystemAsyncDelay() { }
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        => Task.Delay(delay, cancellationToken);
}

public sealed class HttpPipeline
{
    private readonly HttpClient _client;
    private readonly SemaphoreSlim _concurrency;
    private readonly int _maxRetries;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _overallTimeout;
    private readonly IClock _clock;
    private readonly IAsyncDelay _delay;

    public HttpPipeline(HttpClient client, int maxConcurrency = 8, int maxRetries = 2,
        TimeSpan? requestTimeout = null)
        : this(client, maxConcurrency, maxRetries, requestTimeout, null,
            SystemClock.Instance, SystemAsyncDelay.Instance)
    {
    }

    public HttpPipeline(HttpClient client, int maxConcurrency, int maxRetries,
        TimeSpan? requestTimeout, TimeSpan? overallTimeout, IClock clock, IAsyncDelay delay)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (maxConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
        if (maxRetries < 0 || maxRetries > 5) throw new ArgumentOutOfRangeException(nameof(maxRetries));
        _concurrency = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _maxRetries = maxRetries;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        if (_requestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        _overallTimeout = overallTimeout ?? TimeSpan.FromTicks(
            checked(_requestTimeout.Ticks * (maxRetries + 1L) + TimeSpan.FromSeconds(5).Ticks));
        if (_overallTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(overallTimeout));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    public async Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.UtcNow + _overallTimeout;
        for (int attempt = 0; ; attempt++)
        {
            TimeSpan remaining = Remaining(deadline, cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(remaining < _requestTimeout ? remaining : _requestTimeout);
            await _concurrency.WaitAsync(timeout.Token).ConfigureAwait(false);
            bool released = false;
            try
            {
                using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
                if (ShouldRetry(response.StatusCode) && attempt < _maxRetries)
                {
                    TimeSpan delay = GetRetryDelay(response, attempt);
                    // ResponseHeadersRead에서는 response/content dispose가 연결 반환 경계다.
                    // backoff 전에 폐기해야 429/5xx 폭주 시 socket이 대기 시간만큼 누적되지 않는다.
                    response.Dispose();
                    _concurrency.Release();
                    released = true;
                    remaining = Remaining(deadline, cancellationToken);
                    if (delay >= remaining)
                        throw new TimeoutException("HTTP retry가 전체 deadline을 초과합니다.");
                    using var delayTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    delayTimeout.CancelAfter(remaining);
                    await _delay.DelayAsync(delay, delayTimeout.Token).ConfigureAwait(false);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                if (!released) _concurrency.Release();
            }
        }
    }

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is TimeSpan delta) return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (retryAfter?.Date is DateTimeOffset date)
        {
            TimeSpan until = date - _clock.UtcNow;
            return until < TimeSpan.Zero ? TimeSpan.Zero : until;
        }
        return TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt));
    }

    private TimeSpan Remaining(DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TimeSpan remaining = deadline - _clock.UtcNow;
        if (remaining <= TimeSpan.Zero)
            throw new TimeoutException("HTTP 작업의 전체 deadline을 초과했습니다.");
        return remaining;
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
}
