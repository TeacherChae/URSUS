using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using URSUS.Caching;
using URSUS.DataSources;
using URSUS.Geometry;
using URSUS.Net;
using URSUS.Parsers;

namespace URSUS.SiteBriefing;

public enum AddressLookupStatus { Success, NotFound }

public sealed class VWorldAddressLookup
{
    public AddressLookupStatus Status { get; }
    public AddressCandidate? Candidate { get; }
    public DateTimeOffset CapturedAtUtc { get; }
    public DateTimeOffset DeliveredAtUtc { get; }
    public DeliveryOrigin DeliveryOrigin { get; }

    internal VWorldAddressLookup(
        AddressLookupStatus status,
        AddressCandidate? candidate,
        DateTimeOffset capturedAtUtc,
        DateTimeOffset deliveredAtUtc,
        DeliveryOrigin deliveryOrigin)
    {
        if ((status == AddressLookupStatus.Success) != (candidate != null))
            throw new ArgumentException("Address lookup status와 candidate가 일치하지 않습니다.");
        Status = status;
        Candidate = candidate;
        CapturedAtUtc = capturedAtUtc;
        DeliveredAtUtc = deliveredAtUtc;
        DeliveryOrigin = deliveryOrigin;
    }
}

public sealed class VWorldProviderException : Exception
{
    public AddressResolutionReason ReasonCode { get; }
    public ProviderFailure Failure { get; }

    public VWorldProviderException(AddressResolutionReason reasonCode, ProviderFailure failure,
        Exception? inner = null)
        : base(failure.Validate().SafeMessage, inner)
    {
        ReasonCode = reasonCode;
        Failure = failure;
    }
}

public sealed class CohortBoundaryFeature
{
    public string ProviderCode { get; }
    public string CanonicalCode { get; }
    public string FullName { get; }
    public string DisplayName { get; }
    public BoundaryTopology Topology { get; }
    public IReadOnlyList<string> Warnings { get; }

    public CohortBoundaryFeature(
        string providerCode,
        string canonicalCode,
        string fullName,
        string displayName,
        BoundaryTopology topology,
        IEnumerable<string>? warnings = null)
    {
        if (string.IsNullOrWhiteSpace(providerCode) ||
            DistrictCode.CanonicalizeLegal(canonicalCode) != canonicalCode ||
            string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Boundary feature identity가 유효하지 않습니다.");
        ProviderCode = providerCode;
        CanonicalCode = canonicalCode;
        FullName = fullName;
        DisplayName = displayName;
        Topology = topology ?? throw new ArgumentNullException(nameof(topology));
        Warnings = Array.AsReadOnly(warnings?.ToArray() ?? Array.Empty<string>());
    }
}

public sealed class CohortBoundaryLookup
{
    public IReadOnlyList<CohortBoundaryFeature> Features { get; }
    public int ProviderExtraCount { get; }
    public string CacheId { get; }
    public DateTimeOffset CapturedAtUtc { get; }
    public DateTimeOffset DeliveredAtUtc { get; }
    public DeliveryOrigin DeliveryOrigin { get; }

    internal CohortBoundaryLookup(
        IEnumerable<CohortBoundaryFeature> features,
        int providerExtraCount,
        string cacheId,
        DateTimeOffset capturedAtUtc,
        DateTimeOffset deliveredAtUtc,
        DeliveryOrigin deliveryOrigin)
    {
        CohortBoundaryFeature[] copied = features.ToArray();
        if (copied.Length == 0 || providerExtraCount < 0)
            throw new ArgumentException("Boundary lookup payload가 유효하지 않습니다.");
        Features = Array.AsReadOnly(copied);
        ProviderExtraCount = providerExtraCount;
        CacheId = cacheId;
        CapturedAtUtc = capturedAtUtc;
        DeliveredAtUtc = deliveredAtUtc;
        DeliveryOrigin = deliveryOrigin;
    }
}

public static class CohortBoundaryCacheIdentity
{
    public const string CacheVersion = "cohort-boundary-cache/1";
    public const string AcquisitionPolicyVersion = "seoul-wfs-acquisition-envelope/1";
    public const string Envelope = "126.7,37.4,127.3,37.72";

    public static string BuildPreimage(ReferenceCohort cohort)
        => $"{CacheVersion}|{AcquisitionPolicyVersion}|{Envelope}|{cohort.Sha256}";

    public static string Compute(ReferenceCohort cohort)
        => ResolutionIdentity.Sha256(BuildPreimage(cohort));
}

public sealed class VWorldResolutionProvider
{
    private const string AddressEndpoint = "https://api.vworld.kr/req/address";
    private const string WfsEndpoint = "https://api.vworld.kr/req/wfs";
    private static readonly TimeSpan SuccessAddressTtl = TimeSpan.FromDays(30);
    private static readonly TimeSpan NotFoundAddressTtl = TimeSpan.FromDays(1);
    private static readonly TimeSpan BoundaryTtl = TimeSpan.FromDays(30);

    private readonly string _apiKey;
    private readonly HttpPipeline _http;
    private readonly AtomicCacheStore _cache;
    private readonly IClock _clock;

    public VWorldResolutionProvider(
        string apiKey,
        HttpPipeline? http = null,
        AtomicCacheStore? cache = null,
        IClock? clock = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("VWorld API key가 필요합니다.", nameof(apiKey));
        _apiKey = apiKey;
        _http = http ?? new HttpPipeline(HttpClientLifetime.Shared, maxConcurrency: 8);
        _clock = clock ?? SystemClock.Instance;
        _cache = cache ?? new AtomicCacheStore(clock: _clock);
    }

    public async Task<VWorldAddressLookup> GetAddressAsync(
        string inputAddress,
        AddressCandidateKind mode,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        string query = AddressQueryNormalizer.Normalize(inputAddress);
        if (query.Length == 0)
            throw new ArgumentException("주소가 비어 있습니다.", nameof(inputAddress));

        string cacheId = ResolutionIdentity.ComputeRequestCacheId(mode, query);
        CacheRead<AddressCacheEntry> read;
        try
        {
            read = await _cache.GetOrFetchAsync(
                new PersistentCacheKey(cacheId),
                forceRefresh,
                entry => AddressCacheTtl(entry, mode),
                token => FetchAddressAsync(query, mode, token),
                cancellationToken).ConfigureAwait(false);
        }
        catch (VWorldProviderException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (TimeoutException)
        {
            throw TransportFailure(ToFailureMode(mode), "Address provider request timed out.");
        }
        catch (HttpRequestException)
        {
            throw TransportFailure(ToFailureMode(mode), "Address provider request failed.");
        }

        AddressCandidate? candidate = read.Value.Status == AddressLookupStatus.Success
            ? read.Value.ToCandidate(inputAddress)
            : null;
        return new VWorldAddressLookup(read.Value.Status, candidate, read.RetrievedAt,
            _clock.UtcNow, read.DeliveryOrigin);
    }

    public async Task<CohortBoundaryLookup> GetLegalDistrictsForCohortAsync(
        ReferenceCohort cohort,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cohort);
        string cacheId = CohortBoundaryCacheIdentity.Compute(cohort);
        CacheRead<BoundaryCacheEntry> read;
        try
        {
            read = await _cache.GetOrFetchAsync(
                new PersistentCacheKey(cacheId),
                forceRefresh,
                entry => BoundaryCacheTtl(entry, cohort),
                token => FetchCohortBoundariesAsync(cohort, token),
                cancellationToken).ConfigureAwait(false);
        }
        catch (VWorldProviderException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (TimeoutException)
        {
            throw TransportFailure(ProviderFailureMode.Boundary,
                "Boundary provider request timed out.");
        }
        catch (HttpRequestException)
        {
            throw TransportFailure(ProviderFailureMode.Boundary,
                "Boundary provider request failed.");
        }

        IReadOnlyList<CohortBoundaryFeature> features = read.Value.Features
            .Select(feature => feature.ToFeature()).ToArray();
        return new CohortBoundaryLookup(features, read.Value.ProviderExtraCount, cacheId,
            read.RetrievedAt, _clock.UtcNow, read.DeliveryOrigin);
    }

    private async Task<AddressCacheEntry> FetchAddressAsync(
        string normalizedQuery,
        AddressCandidateKind mode,
        CancellationToken cancellationToken)
    {
        Uri uri = BuildAddressUri(normalizedQuery, mode);
        string body = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        try
        {
            JsonObject root = JsonNode.Parse(body)?.AsObject()
                ?? throw new InvalidOperationException("Address provider JSON이 비어 있습니다.");
            JsonObject response = root["response"] as JsonObject ?? root;
            JsonObject service = response["service"] as JsonObject
                ?? throw new InvalidOperationException("Address service contract가 없습니다.");
            if (ReadString(service, "name") != "address" ||
                ReadString(service, "version") != "2.0" ||
                ReadString(service, "operation") != "getcoord")
                throw new InvalidOperationException("Address service contract가 일치하지 않습니다.");
            string status = response["status"]?.GetValue<string>() ?? string.Empty;
            if (status == "NOT_FOUND")
            {
                if (response["record"]?["total"]?.ToString() != "0")
                    throw new InvalidOperationException("NOT_FOUND record.total이 0이 아닙니다.");
                return AddressCacheEntry.NotFound(mode);
            }
            if (status == "ERROR")
                throw ReportedFailure(ToFailureMode(mode), response["error"] as JsonObject);
            if (status != "OK")
                throw new InvalidOperationException("지원하지 않는 address status입니다.");

            JsonObject input = response["input"] as JsonObject
                ?? throw new InvalidOperationException("Address input contract가 없습니다.");
            if (ReadString(input, "type") != mode.ToString().ToLowerInvariant() ||
                AddressQueryNormalizer.Normalize(ReadString(input, "address")) != normalizedQuery)
                throw new InvalidOperationException("Address response input이 request와 일치하지 않습니다.");
            string refinedText = response["refined"]?["text"]?.GetValue<string>() ?? string.Empty;
            JsonObject structure = response["refined"]?["structure"] as JsonObject
                ?? throw new InvalidOperationException("Address refined.structure가 없습니다.");
            JsonObject point = response["result"]?["point"] as JsonObject
                ?? throw new InvalidOperationException("Address result.point가 없습니다.");
            if (response["result"]?["crs"]?.GetValue<string>() != "EPSG:4326")
                throw new InvalidOperationException("Address result CRS가 EPSG:4326이 아닙니다.");
            if (!double.TryParse(point["x"]?.ToString(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double longitude) ||
                !double.TryParse(point["y"]?.ToString(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double latitude))
                throw new InvalidOperationException("Address point가 finite number가 아닙니다.");

            var fingerprint = ResolutionIdentity.FingerprintProviderResponse(
                response.ToJsonString());
            var refined = new RefinedAddressStructure(
                ReadString(structure, "level0"), ReadString(structure, "level1"),
                ReadString(structure, "level2"), ReadString(structure, "level3"),
                ReadString(structure, "level4L"), ReadString(structure, "level4LC"),
                ReadString(structure, "level4A"), ReadString(structure, "level4AC"),
                ReadString(structure, "level5"), ReadString(structure, "detail"));
            var coordinate = new Wgs84Coordinate(longitude, latitude);
            _ = new AddressCandidate(mode, normalizedQuery, refinedText, refined, coordinate,
                "VWorld address/2.0", fingerprint.Sha256);
            return AddressCacheEntry.Success(mode, refinedText, refined, coordinate,
                fingerprint.Sha256);
        }
        catch (VWorldProviderException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or
                                   System.Text.Json.JsonException)
        {
            throw SchemaFailure(ToFailureMode(mode),
                "Address provider response did not satisfy contract VWorld address/2.0.", ex);
        }
    }

    private async Task<BoundaryCacheEntry> FetchCohortBoundariesAsync(
        ReferenceCohort cohort,
        CancellationToken cancellationToken)
    {
        try
        {
            var all = new List<CohortBoundaryFeature>();
            var stableIdentities = new HashSet<string>(StringComparer.Ordinal);
            var canonicalIds = new HashSet<string>(StringComparer.Ordinal);
            int start = 0;
            int received = 0;
            int? expectedTotal = null;
            const int count = 1000;
            const int maxPages = 100;

            for (int page = 0; page < maxPages; page++)
            {
                WfsBatch batch = await FetchWfsBatchAsync(start, count, cancellationToken)
                    .ConfigureAwait(false);
                if (expectedTotal == null) expectedTotal = batch.NumberMatched;
                else if (expectedTotal.Value != batch.NumberMatched)
                    throw new InvalidOperationException("numberMatched changed between pages.");
                if (batch.NumberReturned != batch.Features.Count)
                    throw new InvalidOperationException("numberReturned did not match feature count.");
                if (batch.Features.Count == 0)
                {
                    if (received == expectedTotal.Value) break;
                    throw new InvalidOperationException("pagination terminated before numberMatched.");
                }

                foreach (JsonObject rawFeature in batch.Features)
                {
                    JsonObject properties = rawFeature["properties"] as JsonObject
                        ?? throw new InvalidOperationException("WFS feature properties missing.");
                    string providerCode = ReadString(properties, "emd_cd");
                    string canonicalCode = DistrictCode.CanonicalizeLegal(providerCode);
                    string fullName = ReadString(properties, "full_nm");
                    string displayName = ReadString(properties, "emd_kor_nm");
                    if (canonicalCode.Length == 0 || fullName.Length == 0 || displayName.Length == 0)
                        throw new InvalidOperationException("WFS identity fields missing.");
                    string stable = ResolutionIdentity.Sha256(
                        $"{providerCode}|{rawFeature["geometry"]?.ToJsonString()}");
                    if (!stableIdentities.Add(stable) || !canonicalIds.Add(canonicalCode))
                        throw new InvalidOperationException("duplicate WFS feature identity.");
                    BoundaryTopology topology = ParseProjectedTopology(rawFeature["geometry"],
                        out IReadOnlyList<string> warnings);
                    all.Add(new CohortBoundaryFeature(providerCode, canonicalCode, fullName,
                        displayName, topology, warnings));
                }

                received += batch.Features.Count;
                if (received > expectedTotal.Value)
                    throw new InvalidOperationException("received exceeded numberMatched.");
                if (received == expectedTotal.Value) break;
                if (batch.Features.Count < count)
                    throw new InvalidOperationException("pagination terminated on a short page.");
                start += batch.Features.Count;
                if (page == maxPages - 1)
                    throw new InvalidOperationException("pagination exceeded safety limit.");
            }

            if (expectedTotal == null || received != expectedTotal.Value)
                throw new InvalidOperationException("pagination did not complete.");

            var required = cohort.DistrictIds.ToHashSet(StringComparer.Ordinal);
            CohortBoundaryFeature[] filtered = all.Where(feature =>
                    required.Contains(feature.CanonicalCode))
                .OrderBy(feature => feature.CanonicalCode, StringComparer.Ordinal)
                .ToArray();
            string[] missing = cohort.DistrictIds.Except(
                filtered.Select(feature => feature.CanonicalCode), StringComparer.Ordinal).ToArray();
            if (missing.Length > 0)
                throw new VWorldProviderException(
                    AddressResolutionReason.CohortBoundaryIncomplete,
                    new ProviderFailure(ProviderFailureMode.Boundary,
                        "COHORT_BOUNDARY_INCOMPLETE", "MEMBERSHIP_MISMATCH", null,
                        $"Boundary provider returned {filtered.Length} of {cohort.DistrictIds.Count} required legal districts."));
            return BoundaryCacheEntry.Create(cohort, filtered, all.Count - filtered.Length);
        }
        catch (VWorldProviderException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or
                                   System.Text.Json.JsonException or BoundaryTopologyParseException)
        {
            throw SchemaFailure(ProviderFailureMode.Boundary,
                "Boundary provider response did not satisfy contract VWorld WFS/2.0.0.", ex);
        }
    }

    private async Task<WfsBatch> FetchWfsBatchAsync(
        int start,
        int count,
        CancellationToken cancellationToken)
    {
        Uri uri = BuildWfsUri(start, count);
        string body = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        JsonObject root;
        try
        {
            root = JsonNode.Parse(body)?.AsObject()
                ?? throw new InvalidOperationException("WFS JSON이 비어 있습니다.");
        }
        catch (System.Text.Json.JsonException) when (
            body.Contains("ExceptionReport", StringComparison.OrdinalIgnoreCase))
        {
            throw ReportedFailure(ProviderFailureMode.Boundary, null, allowMissingCode: true);
        }
        JsonObject response = root["response"] as JsonObject ?? root;
        if (response["status"]?.GetValue<string>() == "ERROR")
            throw ReportedFailure(ProviderFailureMode.Boundary,
                response["error"] as JsonObject);
        JsonArray features = response["features"] as JsonArray
            ?? throw new InvalidOperationException("WFS features가 없습니다.");
        int matched = ReadCount(response, "numberMatched");
        int returned = ReadCount(response, "numberReturned");
        if (features.Count != returned || features.Any(node => node is not JsonObject))
            throw new InvalidOperationException("WFS features 배열 shape가 numberReturned와 일치하지 않습니다.");
        return new WfsBatch(features.Cast<JsonObject>().ToArray(), matched, returned);
    }

    private Uri BuildAddressUri(string normalizedQuery, AddressCandidateKind mode)
    {
        string url = AddressEndpoint +
            "?service=address&request=getcoord&crs=EPSG:4326" +
            $"&address={Uri.EscapeDataString(normalizedQuery)}" +
            $"&format=json&type={mode.ToString().ToLowerInvariant()}" +
            $"&key={Uri.EscapeDataString(_apiKey)}";
        return new Uri(url);
    }

    private Uri BuildWfsUri(int start, int count)
    {
        string url = WfsEndpoint +
            "?SERVICE=WFS&REQUEST=GetFeature&TYPENAME=lt_c_ademd_info" +
            "&BBOX=126.7,37.4,127.3,37.72" +
            $"&VERSION=2.0.0&COUNT={count}&STARTINDEX={start}" +
            "&SRSNAME=EPSG:4326&OUTPUT=application/json&EXCEPTIONS=text/xml" +
            $"&KEY={Uri.EscapeDataString(_apiKey)}";
        return new Uri(url);
    }

    private static BoundaryTopology ParseProjectedTopology(
        JsonNode? geometry,
        out IReadOnlyList<string> warnings)
    {
        ValidateStrictGeoJsonRings(geometry);
        BoundaryTopology topology = GeoJsonBoundaryParser.Parse(geometry, out warnings);
        if (warnings.Count > 0)
            throw new BoundaryTopologyParseException(
                "Address resolution boundary topology cannot drop invalid parts or holes.");
        return topology;
    }

    private static void ValidateStrictGeoJsonRings(JsonNode? geometry)
    {
        string type = geometry?["type"]?.GetValue<string>() ?? string.Empty;
        JsonArray coordinates = geometry?["coordinates"] as JsonArray
            ?? throw new BoundaryTopologyParseException("Boundary geometry coordinates missing.");
        IEnumerable<JsonArray> polygons = type switch
        {
            "Polygon" => new[] { coordinates },
            "MultiPolygon" => coordinates.Select(node => node?.AsArray()
                ?? throw new BoundaryTopologyParseException("Boundary polygon missing.")),
            _ => throw new BoundaryTopologyParseException("Boundary geometry type unsupported."),
        };
        foreach (JsonArray polygon in polygons)
        {
            if (polygon.Count == 0)
                throw new BoundaryTopologyParseException("Boundary polygon has no outer ring.");
            foreach (JsonNode? ringNode in polygon)
            {
                JsonArray ring = ringNode?.AsArray()
                    ?? throw new BoundaryTopologyParseException("Boundary ring missing.");
                if (ring.Count < 4)
                    throw new BoundaryTopologyParseException("Boundary ring has fewer than four coordinates.");
                (double X, double Y)? first = null;
                (double X, double Y)? last = null;
                foreach (JsonNode? coordinateNode in ring)
                {
                    JsonArray pair = coordinateNode?.AsArray()
                        ?? throw new BoundaryTopologyParseException("Boundary coordinate missing.");
                    if (pair.Count < 2 ||
                        !double.TryParse(pair[0]?.ToString(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double longitude) ||
                        !double.TryParse(pair[1]?.ToString(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double latitude) ||
                        !double.IsFinite(longitude) || !double.IsFinite(latitude) ||
                        longitude is < -180 or > 180 || latitude is < -90 or > 90)
                        throw new BoundaryTopologyParseException("Boundary coordinate invalid.");
                    first ??= (longitude, latitude);
                    last = (longitude, latitude);
                }
                if (first != last)
                    throw new BoundaryTopologyParseException("Boundary ring is not explicitly closed.");
            }
        }
    }

    private static int ReadCount(JsonObject root, string name)
    {
        if (!int.TryParse(root[name]?.ToString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int value) || value < 0)
            throw new InvalidOperationException($"WFS {name}가 유효하지 않습니다.");
        return value;
    }

    private static string ReadString(JsonObject source, string name)
        => source[name]?.GetValue<string>() ?? string.Empty;

    private static TimeSpan AddressCacheTtl(AddressCacheEntry entry, AddressCandidateKind mode)
    {
        if (!entry.IsValid(mode)) return TimeSpan.Zero;
        return entry.Status == AddressLookupStatus.Success ? SuccessAddressTtl : NotFoundAddressTtl;
    }

    private static TimeSpan BoundaryCacheTtl(BoundaryCacheEntry entry, ReferenceCohort cohort)
        => entry.IsValid(cohort) ? BoundaryTtl : TimeSpan.Zero;

    private static ProviderFailureMode ToFailureMode(AddressCandidateKind mode)
        => mode == AddressCandidateKind.Road ? ProviderFailureMode.Road : ProviderFailureMode.Parcel;

    private static VWorldProviderException ReportedFailure(
        ProviderFailureMode mode,
        JsonObject? error,
        bool allowMissingCode = false)
    {
        string? code = error?["code"]?.GetValue<string>();
        string? level = error?["level"]?.GetValue<string>();
        if (!allowMissingCode && string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Provider ERROR envelope code missing.");
        string message = code switch
        {
            "INVALID_KEY" => "Address provider authentication failed.",
            "PARAM_REQUIRED" => "Address provider rejected the request because a required parameter was missing.",
            _ => mode == ProviderFailureMode.Boundary
                ? "Boundary provider rejected the request."
                : "Address provider rejected the request.",
        };
        return new VWorldProviderException(AddressResolutionReason.ProviderReportedError,
            new ProviderFailure(mode, "PROVIDER_REPORTED_ERROR", code, level, message));
    }

    private static VWorldProviderException SchemaFailure(
        ProviderFailureMode mode,
        string message,
        Exception? inner = null)
        => new(AddressResolutionReason.ProviderSchemaInvalid,
            new ProviderFailure(mode, "PROVIDER_SCHEMA_INVALID", null, null, message), inner);

    private static VWorldProviderException TransportFailure(
        ProviderFailureMode mode,
        string message)
        => new(AddressResolutionReason.ProviderTransportFailure,
            new ProviderFailure(mode, "PROVIDER_TRANSPORT_FAILURE", null, null, message));

    private sealed record WfsBatch(
        IReadOnlyList<JsonObject> Features,
        int NumberMatched,
        int NumberReturned);

    private sealed record AddressCacheEntry(
        int SchemaVersion,
        AddressLookupStatus Status,
        AddressCandidateKind Mode,
        string? RefinedText,
        RefinedAddressStructure? RefinedStructure,
        Wgs84Coordinate? Wgs84,
        string? ResponseFingerprint)
    {
        public static AddressCacheEntry NotFound(AddressCandidateKind mode)
            => new(1, AddressLookupStatus.NotFound, mode, null, null, null, null);

        public static AddressCacheEntry Success(
            AddressCandidateKind mode,
            string refinedText,
            RefinedAddressStructure refinedStructure,
            Wgs84Coordinate wgs84,
            string responseFingerprint)
            => new(1, AddressLookupStatus.Success, mode, refinedText, refinedStructure,
                wgs84, responseFingerprint);

        public bool IsValid(AddressCandidateKind expectedMode)
        {
            if (SchemaVersion != 1 || Mode != expectedMode || !Enum.IsDefined(Status)) return false;
            if (Status == AddressLookupStatus.NotFound)
                return RefinedText == null && RefinedStructure == null && Wgs84 == null &&
                    ResponseFingerprint == null;
            if (RefinedText == null || RefinedStructure == null || Wgs84 == null ||
                ResponseFingerprint == null) return false;
            try
            {
                _ = new AddressCandidate(Mode, "cache-probe", RefinedText, RefinedStructure,
                    Wgs84.Value, "VWorld address/2.0", ResponseFingerprint);
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        public AddressCandidate ToCandidate(string inputAddress)
        {
            if (Status != AddressLookupStatus.Success || RefinedText == null ||
                RefinedStructure == null || Wgs84 == null || ResponseFingerprint == null)
                throw new InvalidOperationException("Success address cache payload가 유효하지 않습니다.");
            return new AddressCandidate(Mode, inputAddress, RefinedText, RefinedStructure,
                Wgs84.Value, "VWorld address/2.0", ResponseFingerprint);
        }
    }

    private sealed record CachedCoordinate(double X, double Y);
    private sealed record CachedPart(
        IReadOnlyList<CachedCoordinate> Outer,
        IReadOnlyList<IReadOnlyList<CachedCoordinate>> Holes);

    private sealed record BoundaryCacheFeature(
        string ProviderCode,
        string CanonicalCode,
        string FullName,
        string DisplayName,
        IReadOnlyList<CachedPart> Parts,
        IReadOnlyList<string> Warnings)
    {
        public static BoundaryCacheFeature FromFeature(CohortBoundaryFeature feature)
            => new(feature.ProviderCode, feature.CanonicalCode, feature.FullName,
                feature.DisplayName, feature.Topology.Parts.Select(part => new CachedPart(
                    part.Outer.Points.Select(point => new CachedCoordinate(point.X, point.Y)).ToArray(),
                    part.Holes.Select(hole => (IReadOnlyList<CachedCoordinate>)hole.Points
                        .Select(point => new CachedCoordinate(point.X, point.Y)).ToArray()).ToArray()))
                    .ToArray(), feature.Warnings);

        public CohortBoundaryFeature ToFeature()
        {
            var topology = BoundaryTopology.Create(Parts.Select(part => new BoundaryPart(
                new BoundaryRing(part.Outer.Select(point => new Coordinate2D(point.X, point.Y)).ToArray()),
                part.Holes.Select(hole => new BoundaryRing(hole
                    .Select(point => new Coordinate2D(point.X, point.Y)).ToArray())).ToArray())));
            return new CohortBoundaryFeature(ProviderCode, CanonicalCode, FullName,
                DisplayName, topology, Warnings);
        }
    }

    private sealed record BoundaryCacheEntry(
        int SchemaVersion,
        string AcquisitionPolicyVersion,
        string CohortSha256,
        int ProviderExtraCount,
        IReadOnlyList<BoundaryCacheFeature> Features)
    {
        public static BoundaryCacheEntry Create(
            ReferenceCohort cohort,
            IReadOnlyList<CohortBoundaryFeature> features,
            int providerExtraCount)
            => new(1, CohortBoundaryCacheIdentity.AcquisitionPolicyVersion, cohort.Sha256,
                providerExtraCount, features.Select(BoundaryCacheFeature.FromFeature).ToArray());

        public bool IsValid(ReferenceCohort cohort)
        {
            if (SchemaVersion != 1 ||
                AcquisitionPolicyVersion != CohortBoundaryCacheIdentity.AcquisitionPolicyVersion ||
                CohortSha256 != cohort.Sha256 || ProviderExtraCount < 0 || Features == null)
                return false;
            try
            {
                CohortBoundaryFeature[] features = Features.Select(value => value.ToFeature()).ToArray();
                return features.Select(value => value.CanonicalCode)
                    .SequenceEqual(cohort.DistrictIds) &&
                    features.Select(value => value.CanonicalCode)
                        .Distinct(StringComparer.Ordinal).Count() == features.Length;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return false;
            }
        }
    }
}
