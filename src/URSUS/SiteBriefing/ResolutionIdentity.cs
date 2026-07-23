using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace URSUS.SiteBriefing;

public static class AddressQueryNormalizer
{
    public const string Version = "query-normalizer/1";

    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        string normalized = value.Normalize(NormalizationForm.FormC);
        var result = new StringBuilder(normalized.Length);
        bool pendingSpace = false;
        foreach (char character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                if (result.Length > 0) pendingSpace = true;
                continue;
            }

            if (pendingSpace)
            {
                result.Append(' ');
                pendingSpace = false;
            }
            result.Append(character);
        }
        return result.ToString();
    }
}

public static class ResolutionIdentity
{
    public const string ResolverContractVersion = "resolver-contract/1";
    public const string RequestCacheVersion = "resolver-request-cache/1";

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
    };

    public static string BuildRequestCachePreimage(AddressCandidateKind mode, string query)
    {
        string normalized = AddressQueryNormalizer.Normalize(query);
        int byteCount = Encoding.UTF8.GetByteCount(normalized);
        return $"{RequestCacheVersion}|{mode}|{byteCount}:{normalized}";
    }

    public static string ComputeRequestCacheId(AddressCandidateKind mode, string query)
        => Sha256(BuildRequestCachePreimage(mode, query));

    public static string BuildSinglePreimage(
        AddressCandidateKind kind,
        string refinedText,
        Wgs84Coordinate coordinate,
        string legalDistrictCode,
        string responseFingerprint)
    {
        string normalizedText = AddressQueryNormalizer.Normalize(refinedText);
        string canonicalCode = DataSources.DistrictCode.CanonicalizeLegal(legalDistrictCode);
        if (canonicalCode.Length == 0)
            throw new ArgumentException("유효한 8자리 법정동 코드가 필요합니다.", nameof(legalDistrictCode));
        EnsureSha256(responseFingerprint, nameof(responseFingerprint));
        return string.Create(CultureInfo.InvariantCulture,
            $"{ResolverContractVersion}|{kind}|{normalizedText}|{coordinate.Longitude:R},{coordinate.Latitude:R}|{canonicalCode}|{responseFingerprint.ToLowerInvariant()}");
    }

    public static string ComputeSingleId(
        AddressCandidateKind kind,
        string refinedText,
        Wgs84Coordinate coordinate,
        string legalDistrictCode,
        string responseFingerprint)
        => Sha256(BuildSinglePreimage(kind, refinedText, coordinate, legalDistrictCode,
            responseFingerprint));

    public static string BuildDualEquivalentPreimage(string roadPreimage, string parcelPreimage)
    {
        if (string.IsNullOrWhiteSpace(roadPreimage))
            throw new ArgumentException("Road preimage가 필요합니다.", nameof(roadPreimage));
        if (string.IsNullOrWhiteSpace(parcelPreimage))
            throw new ArgumentException("Parcel preimage가 필요합니다.", nameof(parcelPreimage));
        return $"{ResolverContractVersion}|DualEquivalent|" +
            $"{Encoding.UTF8.GetByteCount(roadPreimage)}:{roadPreimage}|" +
            $"{Encoding.UTF8.GetByteCount(parcelPreimage)}:{parcelPreimage}";
    }

    public static string ComputeDualEquivalentId(string roadPreimage, string parcelPreimage)
        => Sha256(BuildDualEquivalentPreimage(roadPreimage, parcelPreimage));

    public static (string CanonicalJson, string Sha256) FingerprintProviderResponse(string json)
    {
        JsonNode root = JsonNode.Parse(json)
            ?? throw new ArgumentException("Provider 응답 JSON이 비어 있습니다.", nameof(json));
        if (root is JsonObject rootObject && rootObject["service"] is JsonObject service)
            service.Remove("time");
        JsonNode ordered = OrderNode(root);
        string canonical = ordered.ToJsonString(CanonicalJsonOptions);
        return (canonical, Sha256(canonical));
    }

    public static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    internal static void EnsureSha256(string value, string parameterName)
    {
        if (value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("SHA-256은 64자리 hexadecimal 문자열이어야 합니다.", parameterName);
    }

    private static JsonNode OrderNode(JsonNode node)
        => node switch
        {
            JsonObject obj => new JsonObject(obj
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => KeyValuePair.Create(pair.Key,
                    pair.Value == null ? null : OrderNode(pair.Value)))),
            JsonArray array => new JsonArray(array
                .Select(item => item == null ? null : OrderNode(item)).ToArray()),
            _ => JsonNode.Parse(node.ToJsonString())!,
        };
}
