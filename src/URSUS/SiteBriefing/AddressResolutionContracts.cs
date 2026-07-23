using System.Collections.ObjectModel;
using URSUS.DataSources;

namespace URSUS.SiteBriefing;

public enum AddressResolutionStatus
{
    Resolved,
    NeedsSelection,
    OutOfCoverage,
    ProviderFailure,
    NotFound,
    InvalidInput,
}

public enum AddressResolutionReason
{
    None,
    EmptyAddress,
    BothModesNotFound,
    DualModeDistanceExceeded,
    DistrictEdge,
    MultipleDistrictContainment,
    DistrictDisagreement,
    OutsideBoundaryCoverage,
    LegalNameMismatch,
    CohortBoundaryIncomplete,
    ProviderReportedError,
    ProviderSchemaInvalid,
    ProviderTransportFailure,
    CohortBootstrapDisagreement,
}

public enum AddressKind { Road, Parcel, DualEquivalent }
public enum AddressCandidateKind { Road, Parcel }
public enum ProviderFailureMode { Road, Parcel, Boundary }
public enum DistrictMatchKind { Strict, Edge }

public readonly record struct Wgs84Coordinate
{
    public double Longitude { get; }
    public double Latitude { get; }

    public Wgs84Coordinate(double longitude, double latitude)
    {
        if (!double.IsFinite(longitude) || longitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude));
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude));
        Longitude = longitude;
        Latitude = latitude;
    }
}

public readonly record struct ProjectedCoordinate
{
    public const string TransformVersion = "URSUS.Epsg5179/1";
    public double X { get; }
    public double Y { get; }

    public ProjectedCoordinate(double x, double y)
    {
        if (!double.IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
        if (!double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(y));
        X = x;
        Y = y;
    }

    public static ProjectedCoordinate From(Wgs84Coordinate coordinate)
    {
        var projected = Utils.Epsg5179.FromWgs84(coordinate.Longitude, coordinate.Latitude);
        return new ProjectedCoordinate(projected.X, projected.Y);
    }
}

public sealed record RefinedAddressStructure(
    string Level0 = "",
    string Level1 = "",
    string Level2 = "",
    string Level3 = "",
    string Level4L = "",
    string Level4LC = "",
    string Level4A = "",
    string Level4AC = "",
    string Level5 = "",
    string Detail = "");

public sealed class AddressCandidate
{
    public AddressCandidateKind Kind { get; }
    public string InputAddress { get; }
    public string RefinedText { get; }
    public RefinedAddressStructure RefinedStructure { get; }
    public Wgs84Coordinate Wgs84 { get; }
    public string ResponseContract { get; }
    public string ResponseFingerprint { get; }

    public AddressCandidate(
        AddressCandidateKind kind,
        string inputAddress,
        string refinedText,
        RefinedAddressStructure refinedStructure,
        Wgs84Coordinate wgs84,
        string responseContract,
        string responseFingerprint)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (string.IsNullOrWhiteSpace(inputAddress))
            throw new ArgumentException("입력 주소가 필요합니다.", nameof(inputAddress));
        if (string.IsNullOrWhiteSpace(refinedText))
            throw new ArgumentException("정제 주소가 필요합니다.", nameof(refinedText));
        ArgumentNullException.ThrowIfNull(refinedStructure);
        if (string.IsNullOrWhiteSpace(responseContract))
            throw new ArgumentException("응답 계약 버전이 필요합니다.", nameof(responseContract));
        if (!string.Equals(responseContract, "VWorld address/2.0", StringComparison.Ordinal))
            throw new ArgumentException("지원하지 않는 주소 응답 계약입니다.", nameof(responseContract));
        string legalName = kind == AddressCandidateKind.Road
            ? refinedStructure.Level3
            : refinedStructure.Level4L;
        if (string.IsNullOrWhiteSpace(legalName))
            throw new ArgumentException("주소 mode별 법정동명 corroboration field가 필요합니다.",
                nameof(refinedStructure));
        ResolutionIdentity.EnsureSha256(responseFingerprint, nameof(responseFingerprint));

        Kind = kind;
        InputAddress = inputAddress;
        RefinedText = AddressQueryNormalizer.Normalize(refinedText);
        RefinedStructure = refinedStructure;
        Wgs84 = wgs84;
        ResponseContract = responseContract;
        ResponseFingerprint = responseFingerprint.ToLowerInvariant();
    }
}

public sealed record LegalDistrictSelection(
    string CanonicalCode,
    string ProviderCode,
    string FullName,
    string DisplayName,
    string SelectionMethod)
{
    public LegalDistrictSelection Validate()
    {
        if (DistrictCode.CanonicalizeLegal(CanonicalCode) != CanonicalCode)
            throw new ArgumentException("CanonicalCode는 canonical 8자리 법정동 코드여야 합니다.");
        if (string.IsNullOrWhiteSpace(ProviderCode) || string.IsNullOrWhiteSpace(FullName) ||
            string.IsNullOrWhiteSpace(DisplayName) || string.IsNullOrWhiteSpace(SelectionMethod))
            throw new ArgumentException("법정동 선택 근거 필드가 비어 있습니다.");
        return this;
    }
}

public sealed record ResolverSource(
    string Provider,
    string Service,
    string Version,
    DateTimeOffset CapturedAtUtc,
    string? TypeName = null)
{
    public ResolverSource Validate()
    {
        if (string.IsNullOrWhiteSpace(Provider) || string.IsNullOrWhiteSpace(Service) ||
            string.IsNullOrWhiteSpace(Version))
            throw new ArgumentException("Resolver source 식별자가 비어 있습니다.");
        if (CapturedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("CapturedAtUtc는 UTC여야 합니다.");
        return this;
    }
}

public sealed record ProviderFailure(
    ProviderFailureMode Mode,
    string CanonicalCode,
    string? ProviderCode,
    string? ProviderLevel,
    string SafeMessage)
{
    public ProviderFailure Validate()
    {
        if (!Enum.IsDefined(Mode))
            throw new ArgumentOutOfRangeException(nameof(Mode));
        if (string.IsNullOrWhiteSpace(CanonicalCode) || string.IsNullOrWhiteSpace(SafeMessage))
            throw new ArgumentException("Provider failure에는 canonical code와 safe message가 필요합니다.");
        return this;
    }
}

public sealed record LegalDistrictAlternative(
    AddressCandidateKind CandidateKind,
    string ProviderCode,
    string CanonicalCode,
    string FullName,
    string DisplayName,
    DistrictMatchKind MatchKind,
    double DistanceMeters)
{
    public LegalDistrictAlternative Validate()
    {
        if (!Enum.IsDefined(CandidateKind) || !Enum.IsDefined(MatchKind))
            throw new ArgumentException("Alternative enum 값이 유효하지 않습니다.");
        if (string.IsNullOrWhiteSpace(ProviderCode) || string.IsNullOrWhiteSpace(FullName) ||
            string.IsNullOrWhiteSpace(DisplayName))
            throw new ArgumentException("Alternative 법정동 식별 필드가 비어 있습니다.");
        if (DistrictCode.CanonicalizeLegal(CanonicalCode) != CanonicalCode)
            throw new ArgumentException("Alternative code가 canonical 법정동 코드가 아닙니다.");
        if (!double.IsFinite(DistanceMeters) || DistanceMeters < 0)
            throw new ArgumentOutOfRangeException(nameof(DistanceMeters));
        return this;
    }
}

public sealed record RepresentativeLocation(
    AddressCandidateKind CandidateKind,
    Wgs84Coordinate Wgs84,
    ProjectedCoordinate ProjectedCoordinate,
    string SelectionBasis)
{
    public RepresentativeLocation Validate()
    {
        if (!Enum.IsDefined(CandidateKind))
            throw new ArgumentOutOfRangeException(nameof(CandidateKind));
        if (string.IsNullOrWhiteSpace(SelectionBasis))
            throw new ArgumentException("대표 위치 선택 근거가 필요합니다.");
        ProjectedCoordinate expected = ProjectedCoordinate.From(Wgs84);
        if (Math.Abs(expected.X - ProjectedCoordinate.X) > 0.01 ||
            Math.Abs(expected.Y - ProjectedCoordinate.Y) > 0.01)
            throw new ArgumentException("대표 위치의 WGS84와 EPSG:5179 좌표가 일치하지 않습니다.");
        return this;
    }
}

public sealed class ReferenceCohort
{
    public const string PolicyVersion = "same-sigungu-legal-districts/1";
    public const string QueryKind = "SameSigunguLegalDistricts";
    public const string SpatialUnit = "LegalDistrict";

    public string SigunguCode { get; }
    public string SigunguName { get; }
    public string MembershipSourceVersion { get; }
    public IReadOnlyList<string> DistrictIds { get; }
    public string FingerprintInput { get; }
    public string Sha256 { get; }

    public ReferenceCohort(
        string sigunguCode,
        string sigunguName,
        string membershipSourceVersion,
        IEnumerable<string> districtIds)
    {
        if (sigunguCode.Length != 5 || !sigunguCode.All(char.IsDigit))
            throw new ArgumentException("시군구 코드는 5자리 숫자여야 합니다.", nameof(sigunguCode));
        if (string.IsNullOrWhiteSpace(sigunguName))
            throw new ArgumentException("시군구 이름이 필요합니다.", nameof(sigunguName));
        if (string.IsNullOrWhiteSpace(membershipSourceVersion))
            throw new ArgumentException("membership source version이 필요합니다.", nameof(membershipSourceVersion));
        string[] copiedIds = districtIds?.ToArray()
            ?? throw new ArgumentNullException(nameof(districtIds));
        if (copiedIds.Length == 0 || copiedIds.Any(id =>
                DistrictCode.CanonicalizeLegal(id) != id ||
                !id.StartsWith(sigunguCode, StringComparison.Ordinal)) ||
            copiedIds.Distinct(StringComparer.Ordinal).Count() != copiedIds.Length)
            throw new ArgumentException("Cohort에는 해당 시군구의 중복 없는 canonical 법정동이 필요합니다.",
                nameof(districtIds));

        SigunguCode = sigunguCode;
        SigunguName = sigunguName;
        MembershipSourceVersion = membershipSourceVersion;
        DistrictIds = Array.AsReadOnly(copiedIds.OrderBy(id => id, StringComparer.Ordinal).ToArray());
        FingerprintInput = $"{PolicyVersion}|{QueryKind}|{SigunguCode}|" +
            $"{MembershipSourceVersion}|{string.Join(',', DistrictIds)}";
        Sha256 = ResolutionIdentity.Sha256(FingerprintInput);
    }
}

public sealed class ResolvedSite
{
    public string InputAddress { get; }
    public string NormalizedAddress { get; }
    public AddressKind AddressKind { get; }
    public IReadOnlyList<AddressCandidate> Candidates { get; }
    public Wgs84Coordinate Wgs84 { get; }
    public ProjectedCoordinate ProjectedCoordinate { get; }
    public LegalDistrictSelection LegalDistrict { get; }
    public IReadOnlyList<string> ResolutionWarnings { get; }
    public string ResolutionCanonicalInput { get; }
    public string ResolutionId { get; }
    public DateTimeOffset ResolvedAt { get; }
    public IReadOnlyList<ResolverSource> ResolverSources { get; }

    public ResolvedSite(
        string inputAddress,
        string normalizedAddress,
        AddressKind addressKind,
        IReadOnlyList<AddressCandidate> candidates,
        LegalDistrictSelection legalDistrict,
        DateTimeOffset resolvedAt,
        IEnumerable<ResolverSource> resolverSources,
        IEnumerable<string>? resolutionWarnings = null)
    {
        if (!Enum.IsDefined(addressKind))
            throw new ArgumentOutOfRangeException(nameof(addressKind));
        if (string.IsNullOrWhiteSpace(inputAddress))
            throw new ArgumentException("입력 주소가 필요합니다.", nameof(inputAddress));
        if (string.IsNullOrWhiteSpace(normalizedAddress))
            throw new ArgumentException("정규화 주소가 필요합니다.", nameof(normalizedAddress));
        ArgumentNullException.ThrowIfNull(candidates);
        int expectedCount = addressKind == AddressKind.DualEquivalent ? 2 : 1;
        if (candidates.Count != expectedCount || candidates.Any(candidate => candidate == null))
            throw new ArgumentException("주소 종류와 candidate 수가 일치하지 않습니다.", nameof(candidates));
        if (addressKind == AddressKind.Road && candidates[0].Kind != AddressCandidateKind.Road ||
            addressKind == AddressKind.Parcel && candidates[0].Kind != AddressCandidateKind.Parcel ||
            addressKind == AddressKind.DualEquivalent &&
            (candidates[0].Kind != AddressCandidateKind.Road || candidates[1].Kind != AddressCandidateKind.Parcel))
            throw new ArgumentException("Candidate 순서는 Road, Parcel 계약을 따라야 합니다.", nameof(candidates));

        InputAddress = inputAddress;
        NormalizedAddress = AddressQueryNormalizer.Normalize(normalizedAddress);
        AddressKind = addressKind;
        Candidates = Array.AsReadOnly(candidates.ToArray());
        if (Candidates.Any(candidate => !string.Equals(candidate.InputAddress, InputAddress,
                StringComparison.Ordinal)))
            throw new ArgumentException("모든 candidate는 같은 verbatim input을 보존해야 합니다.",
                nameof(candidates));
        Wgs84 = candidates[0].Wgs84;
        ProjectedCoordinate = ProjectedCoordinate.From(Wgs84);
        LegalDistrict = legalDistrict.Validate();
        ResolutionWarnings = Array.AsReadOnly(resolutionWarnings?.ToArray() ?? Array.Empty<string>());
        if (resolvedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("ResolvedAt은 UTC여야 합니다.", nameof(resolvedAt));
        ResolvedAt = resolvedAt;
        ResolverSources = Array.AsReadOnly(resolverSources?.Select(source => source.Validate()).ToArray()
            ?? throw new ArgumentNullException(nameof(resolverSources)));
        if (ResolverSources.Count == 0)
            throw new ArgumentException("하나 이상의 resolver source가 필요합니다.", nameof(resolverSources));

        if (addressKind == AddressKind.DualEquivalent)
        {
            string road = ResolutionIdentity.BuildSinglePreimage(candidates[0].Kind,
                candidates[0].RefinedText, candidates[0].Wgs84, LegalDistrict.CanonicalCode,
                candidates[0].ResponseFingerprint);
            string parcel = ResolutionIdentity.BuildSinglePreimage(candidates[1].Kind,
                candidates[1].RefinedText, candidates[1].Wgs84, LegalDistrict.CanonicalCode,
                candidates[1].ResponseFingerprint);
            ResolutionCanonicalInput = ResolutionIdentity.BuildDualEquivalentPreimage(road, parcel);
        }
        else
        {
            AddressCandidate candidate = candidates[0];
            ResolutionCanonicalInput = ResolutionIdentity.BuildSinglePreimage(candidate.Kind,
                candidate.RefinedText, candidate.Wgs84, LegalDistrict.CanonicalCode,
                candidate.ResponseFingerprint);
        }
        ResolutionId = ResolutionIdentity.Sha256(ResolutionCanonicalInput);
    }
}

public sealed class AddressResolutionResult
{
    public AddressResolutionStatus Status { get; }
    public AddressResolutionReason ReasonCode { get; }
    public ResolvedSite? ResolvedSite { get; }
    public ReferenceCohort? ReferenceCohort { get; }
    public IReadOnlyList<AddressCandidate> Candidates { get; }
    public RepresentativeLocation? RepresentativeLocation { get; }
    public IReadOnlyList<LegalDistrictAlternative> LegalDistrictAlternatives { get; }
    public IReadOnlyList<ProviderFailure> ProviderFailures { get; }

    public AddressResolutionResult(
        AddressResolutionStatus status,
        AddressResolutionReason reasonCode,
        ResolvedSite? resolvedSite,
        ReferenceCohort? referenceCohort,
        IReadOnlyList<AddressCandidate> candidates,
        RepresentativeLocation? representativeLocation,
        IReadOnlyList<LegalDistrictAlternative> legalDistrictAlternatives,
        IReadOnlyList<ProviderFailure> providerFailures)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(legalDistrictAlternatives);
        ArgumentNullException.ThrowIfNull(providerFailures);
        if (candidates.Any(candidate => candidate == null) ||
            legalDistrictAlternatives.Any(alternative => alternative == null) ||
            providerFailures.Any(failure => failure == null))
            throw new ArgumentException("Result collection에는 null을 넣을 수 없습니다.");
        if (candidates.Count == 2 &&
            (candidates[0].Kind != AddressCandidateKind.Road ||
             candidates[1].Kind != AddressCandidateKind.Parcel))
            throw new ArgumentException("두 candidate는 Road, Parcel 순서여야 합니다.",
                nameof(candidates));
        if (candidates.Count == 2 && !string.Equals(candidates[0].InputAddress,
                candidates[1].InputAddress, StringComparison.Ordinal))
            throw new ArgumentException("두 candidate는 같은 verbatim input provenance를 가져야 합니다.",
                nameof(candidates));

        Status = status;
        ReasonCode = reasonCode;
        ResolvedSite = resolvedSite;
        ReferenceCohort = referenceCohort;
        Candidates = status == AddressResolutionStatus.Resolved && resolvedSite != null
            ? resolvedSite.Candidates
            : candidates.Count == 0
                ? Array.Empty<AddressCandidate>()
                : Array.AsReadOnly(candidates.ToArray());
        RepresentativeLocation = representativeLocation?.Validate();
        LegalDistrictAlternatives = legalDistrictAlternatives.Count == 0
            ? Array.Empty<LegalDistrictAlternative>()
            : Array.AsReadOnly(legalDistrictAlternatives.Select(item => item.Validate()).ToArray());
        ProviderFailures = providerFailures.Count == 0
            ? Array.Empty<ProviderFailure>()
            : Array.AsReadOnly(providerFailures.Select(item => item.Validate()).ToArray());

        ValidateMatrix(candidates);
    }

    public static AddressResolutionResult InvalidInput()
        => Empty(AddressResolutionStatus.InvalidInput, AddressResolutionReason.EmptyAddress);

    public static AddressResolutionResult NotFound()
        => Empty(AddressResolutionStatus.NotFound, AddressResolutionReason.BothModesNotFound);

    private static AddressResolutionResult Empty(
        AddressResolutionStatus status,
        AddressResolutionReason reason)
        => new(status, reason, null, null, Array.Empty<AddressCandidate>(), null,
            Array.Empty<LegalDistrictAlternative>(), Array.Empty<ProviderFailure>());

    private void ValidateMatrix(IReadOnlyList<AddressCandidate> sourceCandidates)
    {
        bool noSuccess = ResolvedSite == null && ReferenceCohort == null;
        switch (ReasonCode)
        {
            case AddressResolutionReason.None when Status == AddressResolutionStatus.Resolved:
                ResolvedSite? site = ResolvedSite;
                ReferenceCohort? cohort = ReferenceCohort;
                Require(site != null && cohort != null && Candidates.Count is 1 or 2 &&
                    RepresentativeLocation == null && LegalDistrictAlternatives.Count == 0 &&
                    ProviderFailures.Count == 0, "Resolved payload가 계약과 일치하지 않습니다.");
                Require(ReferenceEquals(sourceCandidates, site!.Candidates),
                    "Result와 ResolvedSite는 같은 ordered immutable candidate 값을 공유해야 합니다.");
                Require(cohort!.DistrictIds.Contains(
                    site.LegalDistrict.CanonicalCode, StringComparer.Ordinal),
                    "Resolved 법정동이 reference cohort에 포함되어야 합니다.");
                return;
            case AddressResolutionReason.DualModeDistanceExceeded
                when Status == AddressResolutionStatus.NeedsSelection:
                Require(noSuccess && Candidates.Count == 2 && RepresentativeLocation == null &&
                    LegalDistrictAlternatives.Count == 0 && ProviderFailures.Count == 0,
                    "Dual distance payload가 계약과 일치하지 않습니다.");
                return;
            case AddressResolutionReason.CohortBootstrapDisagreement
                when Status == AddressResolutionStatus.NeedsSelection:
                Require(noSuccess && Candidates.Count == 2 && RepresentativeLocation == null &&
                    LegalDistrictAlternatives.Count == 0 && ProviderFailures.Count == 0,
                    "Cohort bootstrap disagreement payload가 계약과 일치하지 않습니다.");
                return;
            case AddressResolutionReason.DistrictEdge
                when Status == AddressResolutionStatus.NeedsSelection:
                Require(noSuccess && Candidates.Count is 1 or 2 && RepresentativeLocation != null &&
                    LegalDistrictAlternatives.Count >= 1 &&
                    LegalDistrictAlternatives.All(x => x.MatchKind == DistrictMatchKind.Edge) &&
                    ProviderFailures.Count == 0, "District edge payload가 계약과 일치하지 않습니다.");
                ValidateAmbiguityLinks();
                return;
            case AddressResolutionReason.MultipleDistrictContainment
                when Status == AddressResolutionStatus.NeedsSelection:
                Require(noSuccess && Candidates.Count is 1 or 2 && RepresentativeLocation != null &&
                    LegalDistrictAlternatives.Count >= 2 &&
                    LegalDistrictAlternatives.All(x => x.MatchKind == DistrictMatchKind.Strict) &&
                    ProviderFailures.Count == 0, "Multiple containment payload가 계약과 일치하지 않습니다.");
                ValidateAmbiguityLinks();
                return;
            case AddressResolutionReason.DistrictDisagreement
                when Status == AddressResolutionStatus.NeedsSelection:
                Require(noSuccess && Candidates.Count == 2 && RepresentativeLocation == null &&
                    LegalDistrictAlternatives.Count == 2 &&
                    LegalDistrictAlternatives.All(x => x.MatchKind == DistrictMatchKind.Strict) &&
                    LegalDistrictAlternatives.Select(x => x.CandidateKind).Distinct().Count() == 2 &&
                    ProviderFailures.Count == 0, "District disagreement payload가 계약과 일치하지 않습니다.");
                Require(LegalDistrictAlternatives.All(alternative =>
                        Candidates.Any(candidate => candidate.Kind == alternative.CandidateKind)),
                    "District alternative가 authoritative candidate에 연결되지 않습니다.");
                Require(LegalDistrictAlternatives.Select(x => x.CanonicalCode)
                        .Distinct(StringComparer.Ordinal).Count() == 2,
                    "District disagreement는 서로 다른 canonical 법정동이어야 합니다.");
                return;
            case AddressResolutionReason.OutsideBoundaryCoverage
                when Status == AddressResolutionStatus.OutOfCoverage:
                Require(noSuccess && Candidates.Count is 1 or 2 && RepresentativeLocation != null &&
                    LegalDistrictAlternatives.Count == 0 && ProviderFailures.Count == 0,
                    "Out-of-coverage payload가 계약과 일치하지 않습니다.");
                Require(Candidates.Any(candidate =>
                        candidate.Kind == RepresentativeLocation!.CandidateKind &&
                        candidate.Wgs84 == RepresentativeLocation.Wgs84),
                    "RepresentativeLocation은 원인 candidate 좌표여야 합니다.");
                return;
            case AddressResolutionReason.LegalNameMismatch
                when Status == AddressResolutionStatus.ProviderFailure:
                RequireProviderFailure(noSuccess && Candidates.Count is 1 or 2 &&
                    ProviderFailures.Count is 1 or 2 &&
                    ProviderFailures.All(x => x.Mode == ProviderFailureMode.Boundary));
                return;
            case AddressResolutionReason.CohortBoundaryIncomplete
                when Status == AddressResolutionStatus.ProviderFailure:
                RequireProviderFailure(noSuccess && Candidates.Count is 1 or 2 &&
                    ProviderFailures.Count == 1 &&
                    ProviderFailures[0].Mode == ProviderFailureMode.Boundary &&
                    ProviderFailures[0].CanonicalCode == "COHORT_BOUNDARY_INCOMPLETE");
                return;
            case AddressResolutionReason.ProviderReportedError or
                 AddressResolutionReason.ProviderSchemaInvalid or
                 AddressResolutionReason.ProviderTransportFailure
                when Status == AddressResolutionStatus.ProviderFailure:
                RequireProviderFailure(noSuccess && ValidateGenericProviderFailures());
                return;
            case AddressResolutionReason.BothModesNotFound
                when Status == AddressResolutionStatus.NotFound:
            case AddressResolutionReason.EmptyAddress
                when Status == AddressResolutionStatus.InvalidInput:
                Require(noSuccess && Candidates.Count == 0 && RepresentativeLocation == null &&
                    LegalDistrictAlternatives.Count == 0 && ProviderFailures.Count == 0,
                    "Empty result payload가 계약과 일치하지 않습니다.");
                return;
            default:
                throw new ArgumentException("Status와 reason 조합이 닫힌 계약에 없습니다.");
        }
    }

    private void RequireProviderFailure(bool condition)
        => Require(condition && RepresentativeLocation == null &&
            LegalDistrictAlternatives.Count == 0, "Provider failure payload가 계약과 일치하지 않습니다.");

    private bool ValidateGenericProviderFailures()
    {
        if (Candidates.Count > 2 || ProviderFailures.Count is < 1 or > 2)
            return false;
        ProviderFailureMode[] modes = ProviderFailures.Select(failure => failure.Mode).ToArray();
        if (!modes.SequenceEqual(modes.OrderBy(mode => mode)) ||
            modes.Distinct().Count() != modes.Length ||
            ProviderFailures.Any(failure => GenericReason(failure.CanonicalCode) == null) ||
            GenericReason(ProviderFailures[0].CanonicalCode) != ReasonCode)
            return false;

        bool hasBoundary = modes.Contains(ProviderFailureMode.Boundary);
        if (hasBoundary)
            return ProviderFailures.Count == 1 && Candidates.Count is 1 or 2;
        if (Candidates.Count == 2) return false;
        if (Candidates.Count == 1)
        {
            if (ProviderFailures.Count != 1) return false;
            ProviderFailureMode expectedFailure = Candidates[0].Kind == AddressCandidateKind.Road
                ? ProviderFailureMode.Parcel
                : ProviderFailureMode.Road;
            return ProviderFailures[0].Mode == expectedFailure;
        }
        return modes.All(mode => mode is ProviderFailureMode.Road or ProviderFailureMode.Parcel);
    }

    private static AddressResolutionReason? GenericReason(string canonicalCode)
        => canonicalCode switch
        {
            "PROVIDER_REPORTED_ERROR" => AddressResolutionReason.ProviderReportedError,
            "PROVIDER_SCHEMA_INVALID" => AddressResolutionReason.ProviderSchemaInvalid,
            "PROVIDER_TRANSPORT_FAILURE" => AddressResolutionReason.ProviderTransportFailure,
            _ => null,
        };

    private void ValidateAmbiguityLinks()
    {
        AddressCandidateKind[] triggerKinds = LegalDistrictAlternatives
            .Select(alternative => alternative.CandidateKind)
            .Distinct()
            .ToArray();
        Require(triggerKinds.All(kind => Candidates.Any(candidate => candidate.Kind == kind)),
            "District alternative가 authoritative candidate에 연결되지 않습니다.");
        AddressCandidateKind representativeKind = triggerKinds.Contains(AddressCandidateKind.Road)
            ? AddressCandidateKind.Road
            : triggerKinds[0];
        AddressCandidate expected = Candidates.Single(candidate =>
            candidate.Kind == representativeKind);
        Require(RepresentativeLocation!.CandidateKind == representativeKind &&
                RepresentativeLocation.Wgs84 == expected.Wgs84,
            "RepresentativeLocation은 reason을 일으킨 candidate 좌표여야 합니다.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new ArgumentException(message);
    }
}
