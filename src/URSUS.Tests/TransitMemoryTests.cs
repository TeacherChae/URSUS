using System.Globalization;
using System.Net;
using System.Text;
using URSUS.Caching;
using URSUS.DataSources;
using URSUS.Net;
using URSUS.Parsers;

namespace URSUS.Tests;

internal static class TransitMemoryTests
{
    [Test]
    internal static void TransitLatest_StreamsOutOfOrderHistoryAndRetainsOnlyNewestClosedMonth()
    {
        var rows = new List<TransitRow>();
        for (var month = new DateTime(2024, 1, 1); month <= new DateTime(2026, 1, 1);
             month = month.AddMonths(1))
        {
            int monthOrdinal = (month.Year - 2024) * 12 + month.Month;
            for (int day = 1; day <= 30; day++)
            {
                string date = $"{month:yyyyMM}{day:00}";
                rows.Add(new TransitRow("11110515", date, monthOrdinal * 100 + day));
                rows.Add(new TransitRow("11110530", date, monthOrdinal * 1000 + day));
            }
        }

        // 최신/과거가 page 경계를 넘나드는 순서에서도 선택 월 하나만 유지해야 한다.
        rows = rows.OrderBy(row => StableShuffleKey(row.Date, row.District)).ToList();
        var parser = TransitParser(rows,
            new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));

        SeoulAggregate result = parser.GetTransitBoardingByAdstrdAsync(TransitQuery())
            .GetAwaiter().GetResult();

        AssertEx.Equal("202512", result.Observation.PeriodId);
        AssertEx.Near(2415.5, result.Values["11110515"]);
        AssertEx.Near(24015.5, result.Values["11110530"]);
        AssertEx.Equal(rows.Count, result.RawRecordCount);
        AssertEx.True(result.PaginationComplete);
        AssertEx.True(parser.PeakRetainedAggregateEntries <= 60,
            $"Expected at most 30 days x 2 districts, got {parser.PeakRetainedAggregateEntries}.");
    }

    [Test]
    internal static void TransitExplicitMonth_IsStableWhenNewerAndCurrentRowsArriveFirst()
    {
        var rows = new[]
        {
            new TransitRow("11110515", "20260101", 9000),
            new TransitRow("11110515", "20251201", 8000),
            new TransitRow("11110515", "20241102", 30),
            new TransitRow("11110515", "20241101", 10),
            new TransitRow("11110530", "20241101", 20),
        };
        var parser = TransitParser(rows,
            new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));

        SeoulAggregate result = parser.GetTransitBoardingByAdstrdAsync(TransitQuery("202411"))
            .GetAwaiter().GetResult();

        AssertEx.Equal("202411", result.Observation.PeriodId);
        AssertEx.Near(20, result.Values["11110515"]);
        AssertEx.Near(20, result.Values["11110530"]);
        AssertEx.True(parser.PeakRetainedAggregateEntries <= 3);
    }

    [Test]
    internal static void TransitCompleteness_Requires28DaysAndAtLeast95PercentEveryDay()
    {
        SeoulAggregate boundary = CompleteBoundaryAggregate(405);
        SeoulAggregate belowBoundary = CompleteBoundaryAggregate(404);

        AssertEx.True(boundary.Observation.IsComplete,
            "ceil(426 * 95%) = 405 districts per day must be accepted");
        AssertEx.False(belowBoundary.Observation.IsComplete,
            "a single day with 404 districts must make the month incomplete");
        AssertEx.Equal(0, boundary.Observation.MissingIds.Count);
        AssertEx.Equal(28 * 426 - 21, boundary.RawRecordCount);
    }

    [Test]
    internal static void TransitDuplicateAcrossPageBoundary_FailsClosed()
    {
        var rows = Enumerable.Range(0, 1000)
            .Select(index => new TransitRow($"D{index:0000}", "20251201", index + 1))
            .ToList();
        rows.Add(rows[0]);
        var parser = TransitParser(rows,
            new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));

        AssertThrows<SeoulPaginationException>(() => parser
            .GetTransitBoardingByAdstrdAsync(TransitQuery()).GetAwaiter().GetResult());
    }

    [Test]
    internal static void TransitOversizedPage_FailsAtThePageRowLimit()
    {
        var rows = Enumerable.Range(0, 1001)
            .Select(index => new TransitRow($"D{index:0000}", "20251201", index + 1))
            .ToArray();
        var parser = new DataSeoulApiParser("secret",
            new HttpPipeline(new HttpClient(new OversizedTransitHandler(rows)), maxRetries: 0),
            new FrozenClock(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero)));

        AssertThrows<SeoulPaginationException>(() => parser
            .GetTransitBoardingByAdstrdAsync(TransitQuery()).GetAwaiter().GetResult());
    }

    [Test]
    internal static void AsyncBodyRead_TimeoutAndUserCancellationAbortStreamAndReleaseSemaphore()
    {
        VerifyBlockedReadIsAborted(cancelExplicitly: false);
        VerifyBlockedReadIsAborted(cancelExplicitly: true);
    }

    [Test]
    internal static void StreamCallbackFailure_DisposesBodyAndReleasesSemaphore()
    {
        const string xml = "<root><list_total_count>1</list_total_count>" +
            "<row><DONG_ID>A</DONG_ID></row></root>";
        var tracked = new TrackingMemoryStream(Encoding.UTF8.GetBytes(xml));
        var handler = new FirstStreamThenTextHandler(tracked, "recovered");
        var pipeline = new HttpPipeline(new HttpClient(handler), maxConcurrency: 1, maxRetries: 0,
            requestTimeout: TimeSpan.FromSeconds(2));

        AssertThrows<InvalidOperationException>(() => pipeline.ProcessStreamAsync(
            new Uri("https://example.test/first"),
            (stream, token) => SeoulXmlStreamParser.ParseRowsAsync(stream,
                new[] { "DONG_ID" },
                (_, _) => throw new InvalidOperationException("callback failed"), token),
            CancellationToken.None).GetAwaiter().GetResult());

        AssertEx.True(tracked.IsDisposed);
        string recovery = pipeline.GetStringAsync(
            new Uri("https://example.test/second"), CancellationToken.None)
            .GetAwaiter().GetResult();
        AssertEx.Equal("recovered", recovery);
    }

    private static SeoulAggregate CompleteBoundaryAggregate(int firstDayDistrictCount)
    {
        var rows = new List<TransitRow>();
        for (int day = 1; day <= 28; day++)
        {
            int count = day == 1 ? firstDayDistrictCount : SeoulExpectedDistricts.Ids.Count;
            foreach (string district in SeoulExpectedDistricts.Ids.Take(count))
                rows.Add(new TransitRow(district, $"202512{day:00}", 1));
        }
        var parser = TransitParser(rows,
            new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));
        return parser.GetTransitBoardingByAdstrdAsync(TransitQuery()).GetAwaiter().GetResult();
    }

    private static void VerifyBlockedReadIsAborted(bool cancelExplicitly)
    {
        var blocked = new BlockingStream();
        var handler = new FirstStreamThenTextHandler(blocked, "recovered");
        var pipeline = new HttpPipeline(new HttpClient(handler), maxConcurrency: 1, maxRetries: 0,
            requestTimeout: cancelExplicitly ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(100));
        using var cancellation = new CancellationTokenSource();
        Task<SeoulXmlPageSummary> operation = pipeline.ProcessStreamAsync(
            new Uri("https://example.test/blocked"),
            (stream, token) => SeoulXmlStreamParser.ParseRowsAsync(stream,
                new[] { "DONG_ID" }, (_, _) => ValueTask.CompletedTask, token),
            cancellation.Token);

        AssertEx.True(blocked.ReadStarted.Wait(TimeSpan.FromSeconds(2)),
            "the XML reader never started reading the response body");
        if (cancelExplicitly) cancellation.Cancel();
        Task completed = Task.WhenAny(operation, Task.Delay(TimeSpan.FromSeconds(3)))
            .GetAwaiter().GetResult();
        AssertEx.True(ReferenceEquals(operation, completed),
            "a blocked response body did not abort at cancellation/timeout");
        if (cancelExplicitly)
            AssertEx.True(operation.IsCanceled, "user cancellation must remain cancellation");
        else
            AssertEx.True(operation.Exception?.InnerException is TimeoutException,
                "request deadline must surface as TimeoutException");
        _ = operation.Exception;
        AssertEx.True(blocked.IsDisposed);

        string recovery = pipeline.GetStringAsync(
            new Uri("https://example.test/recovery"), CancellationToken.None)
            .GetAwaiter().GetResult();
        AssertEx.Equal("recovered", recovery);
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static DataSeoulApiParser TransitParser(
        IEnumerable<TransitRow> rows,
        DateTimeOffset now)
        => new("secret",
            new HttpPipeline(new HttpClient(new PagedTransitHandler(rows.ToArray())), maxRetries: 0),
            new FrozenClock(now));

    private static DataQuery TransitQuery(string? period = null) => new()
    {
        QueryIntent = period == null ? QueryIntent.Latest : QueryIntent.ExplicitPeriod,
        ExplicitPeriod = period,
        TransportPolicy = new TransportPolicy(true),
    };

    private static int StableShuffleKey(string date, string district)
    {
        int value = int.Parse(date, CultureInfo.InvariantCulture);
        int districtTail = int.Parse(district[^2..], CultureInfo.InvariantCulture);
        return unchecked((value * 397) ^ districtTail);
    }

    private readonly record struct TransitRow(string District, string Date, double Value);

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class PagedTransitHandler : HttpMessageHandler
    {
        private readonly TransitRow[] _rows;

        public PagedTransitHandler(TransitRow[] rows) => _rows = rows;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string[] path = request.RequestUri!.AbsolutePath.Split('/',
                StringSplitOptions.RemoveEmptyEntries);
            int start = int.Parse(path[^2], CultureInfo.InvariantCulture) - 1;
            int end = int.Parse(path[^1], CultureInfo.InvariantCulture);
            var page = _rows.Skip(start).Take(Math.Min(end, _rows.Length) - start);
            string xml = "<root><list_total_count>" + _rows.Length + "</list_total_count>" +
                string.Concat(page.Select(row =>
                    $"<row><DONG_ID>{row.District}</DONG_ID><CRTR_DD>{row.Date}</CRTR_DD>" +
                    $"<PSNG_NO>{row.Value.ToString(CultureInfo.InvariantCulture)}</PSNG_NO></row>")) +
                "</root>";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml, Encoding.UTF8, "application/xml"),
            });
        }
    }

    private sealed class OversizedTransitHandler : HttpMessageHandler
    {
        private readonly TransitRow[] _rows;

        public OversizedTransitHandler(TransitRow[] rows) => _rows = rows;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string xml = "<root><list_total_count>" + _rows.Length + "</list_total_count>" +
                string.Concat(_rows.Select(row =>
                    $"<row><DONG_ID>{row.District}</DONG_ID><CRTR_DD>{row.Date}</CRTR_DD>" +
                    $"<PSNG_NO>{row.Value.ToString(CultureInfo.InvariantCulture)}</PSNG_NO></row>")) +
                "</root>";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml, Encoding.UTF8, "application/xml"),
            });
        }
    }

    private sealed class FirstStreamThenTextHandler : HttpMessageHandler
    {
        private readonly Stream _first;
        private readonly string _second;
        private int _requestCount;

        public FirstStreamThenTextHandler(Stream first, string second)
        {
            _first = first;
            _second = second;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            int requestNumber = Interlocked.Increment(ref _requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = requestNumber == 1
                    ? new StreamContent(_first)
                    : new StringContent(_second, Encoding.UTF8, "text/plain"),
            });
        }
    }

    private sealed class BlockingStream : Stream
    {
        private readonly TaskCompletionSource<bool> _released =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ManualResetEventSlim ReadStarted { get; } = new();
        public bool IsDisposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadStarted.Set();
            _released.Task.GetAwaiter().GetResult();
            throw new ObjectDisposedException(nameof(BlockingStream));
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadStarted.Set();
            await _released.Task.ConfigureAwait(false);
            throw new ObjectDisposedException(nameof(BlockingStream));
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            _released.TrySetResult(true);
            base.Dispose(disposing);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class TrackingMemoryStream : MemoryStream
    {
        public TrackingMemoryStream(byte[] bytes) : base(bytes) { }
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
