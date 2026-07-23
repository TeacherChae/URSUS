using URSUS.Caching;
using URSUS.Geometry;

namespace URSUS.SiteBriefing;

internal enum CandidateTopologyStatus { UniqueStrict, Edge, MultipleStrict, Outside }

internal sealed record CandidateTopologyMatch(
    AddressCandidate Candidate,
    CandidateTopologyStatus Status,
    CohortBoundaryFeature? UniqueFeature,
    IReadOnlyList<LegalDistrictAlternative> Alternatives);

internal static class AddressTopologyResolver
{
    internal const double EdgeToleranceMeters = 0.01;
    private const double EdgeToleranceSquared = EdgeToleranceMeters * EdgeToleranceMeters;
    private const double EdgeComparisonSlackSquared = 1e-12;

    internal static CandidateTopologyMatch Match(
        AddressCandidate candidate,
        IReadOnlyList<CohortBoundaryFeature> features)
    {
        ProjectedCoordinate projected = ProjectedCoordinate.From(candidate.Wgs84);
        var point = new Coordinate2D(projected.X, projected.Y);
        var evaluated = features.Select(feature => new FeatureEvaluation(
                feature, MinimumDistanceSquared(point, feature.Topology),
                Contains(point, feature.Topology)))
            .ToArray();

        FeatureEvaluation[] edges = evaluated
            .Where(value => IsWithinEdgeTolerance(value.DistanceSquared))
            .OrderBy(value => value.Feature.CanonicalCode, StringComparer.Ordinal)
            .ToArray();
        if (edges.Length > 0)
            return new CandidateTopologyMatch(candidate, CandidateTopologyStatus.Edge, null,
                Array.AsReadOnly(edges.Select(value => Alternative(candidate, value,
                    DistrictMatchKind.Edge)).ToArray()));

        FeatureEvaluation[] strict = evaluated.Where(value => value.Contains)
            .OrderBy(value => value.Feature.CanonicalCode, StringComparer.Ordinal)
            .ToArray();
        if (strict.Length == 0)
            return new CandidateTopologyMatch(candidate, CandidateTopologyStatus.Outside, null,
                Array.Empty<LegalDistrictAlternative>());
        if (strict.Length == 1)
            return new CandidateTopologyMatch(candidate, CandidateTopologyStatus.UniqueStrict,
                strict[0].Feature, Array.AsReadOnly(new[] { Alternative(candidate, strict[0],
                    DistrictMatchKind.Strict) }));
        return new CandidateTopologyMatch(candidate, CandidateTopologyStatus.MultipleStrict, null,
            Array.AsReadOnly(strict.Select(value => Alternative(candidate, value,
                DistrictMatchKind.Strict)).ToArray()));
    }

    internal static AddressResolutionResult Resolve(
        string inputAddress,
        IReadOnlyList<AddressCandidate> candidates,
        IReadOnlyList<DateTimeOffset> addressCapturedAtUtc,
        ReferenceCohort cohort,
        CohortBoundaryLookup boundaries,
        DateTimeOffset resolvedAtUtc)
    {
        if (candidates.Count is < 1 or > 2 || candidates.Count != addressCapturedAtUtc.Count)
            throw new ArgumentException("Topology resolution evidence cardinality가 유효하지 않습니다.");
        if (!boundaries.Features.Select(feature => feature.CanonicalCode)
                .SequenceEqual(cohort.DistrictIds))
            return GenericBoundaryFailure(candidates, AddressResolutionReason.ProviderSchemaInvalid,
                "Boundary provider result does not match the bootstrap cohort.");

        CandidateTopologyMatch[] matches = candidates
            .Select(candidate => Match(candidate, boundaries.Features)).ToArray();
        AddressResolutionResult? unresolved = UnresolvedByTopology(candidates, matches);
        if (unresolved != null) return unresolved;

        CandidateTopologyMatch[] unique = matches;
        ProviderFailure[] nameFailures = unique.Where(match =>
                AddressQueryNormalizer.Normalize(LegalName(match.Candidate)) !=
                AddressQueryNormalizer.Normalize(match.UniqueFeature!.DisplayName) ||
                AddressQueryNormalizer.Normalize(match.UniqueFeature.FullName) !=
                AddressQueryNormalizer.Normalize(
                    $"{cohort.SigunguName} {match.UniqueFeature.DisplayName}"))
            .Select(_ => new ProviderFailure(ProviderFailureMode.Boundary,
                "LEGAL_NAME_MISMATCH", null, null,
                "Address and boundary providers disagree on the legal-district name."))
            .ToArray();
        if (nameFailures.Length > 0)
            return new AddressResolutionResult(AddressResolutionStatus.ProviderFailure,
                AddressResolutionReason.LegalNameMismatch, null, null, candidates, null,
                Array.Empty<LegalDistrictAlternative>(), nameFailures);

        string[] districtIds = unique.Select(match => match.UniqueFeature!.CanonicalCode)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (districtIds.Length > 1)
            return new AddressResolutionResult(AddressResolutionStatus.NeedsSelection,
                AddressResolutionReason.DistrictDisagreement, null, null, candidates, null,
                unique.Select(match => match.Alternatives[0]).ToArray(),
                Array.Empty<ProviderFailure>());
        if (!cohort.DistrictIds.Contains(districtIds[0], StringComparer.Ordinal))
            return GenericBoundaryFailure(candidates, AddressResolutionReason.ProviderSchemaInvalid,
                "Selected legal district is outside the bootstrap cohort.");

        CohortBoundaryFeature selected = unique[0].UniqueFeature!;
        AddressKind kind = candidates.Count == 2 ? AddressKind.DualEquivalent
            : candidates[0].Kind == AddressCandidateKind.Road ? AddressKind.Road : AddressKind.Parcel;
        var sources = candidates.Select((_, index) => new ResolverSource(
                "VWorld", "address", "2.0", addressCapturedAtUtc[index]))
            .Append(new ResolverSource("VWorld", "WFS", "2.0.0",
                boundaries.CapturedAtUtc, "lt_c_ademd_info"))
            .ToArray();
        var site = new ResolvedSite(inputAddress, candidates[0].RefinedText, kind, candidates,
            new LegalDistrictSelection(selected.CanonicalCode, selected.ProviderCode,
                selected.FullName, selected.DisplayName, "WFS topology point-in-polygon"),
            resolvedAtUtc, sources);
        return new AddressResolutionResult(AddressResolutionStatus.Resolved,
            AddressResolutionReason.None, site, cohort, site.Candidates, null,
            Array.Empty<LegalDistrictAlternative>(), Array.Empty<ProviderFailure>());
    }

    private static AddressResolutionResult? UnresolvedByTopology(
        IReadOnlyList<AddressCandidate> candidates,
        IReadOnlyList<CandidateTopologyMatch> matches)
    {
        foreach (CandidateTopologyStatus status in new[]
                 {
                     CandidateTopologyStatus.Edge,
                     CandidateTopologyStatus.MultipleStrict,
                     CandidateTopologyStatus.Outside,
                 })
        {
            CandidateTopologyMatch[] causes = matches.Where(match => match.Status == status).ToArray();
            if (causes.Length == 0) continue;
            CandidateTopologyMatch representative = causes.FirstOrDefault(match =>
                match.Candidate.Kind == AddressCandidateKind.Road) ?? causes[0];
            var location = new RepresentativeLocation(representative.Candidate.Kind,
                representative.Candidate.Wgs84,
                ProjectedCoordinate.From(representative.Candidate.Wgs84),
                causes.Length == 2
                    ? $"Both candidates triggered {status}; Road is the deterministic representative."
                    : $"{representative.Candidate.Kind} candidate triggered {status}.");
            if (status == CandidateTopologyStatus.Outside)
                return new AddressResolutionResult(AddressResolutionStatus.OutOfCoverage,
                    AddressResolutionReason.OutsideBoundaryCoverage, null, null, candidates,
                    location, Array.Empty<LegalDistrictAlternative>(), Array.Empty<ProviderFailure>());
            AddressResolutionReason reason = status == CandidateTopologyStatus.Edge
                ? AddressResolutionReason.DistrictEdge
                : AddressResolutionReason.MultipleDistrictContainment;
            return new AddressResolutionResult(AddressResolutionStatus.NeedsSelection, reason,
                null, null, candidates, location,
                causes.SelectMany(cause => cause.Alternatives).ToArray(),
                Array.Empty<ProviderFailure>());
        }
        return null;
    }

    private static AddressResolutionResult GenericBoundaryFailure(
        IReadOnlyList<AddressCandidate> candidates,
        AddressResolutionReason reason,
        string message)
        => new(AddressResolutionStatus.ProviderFailure, reason, null, null, candidates, null,
            Array.Empty<LegalDistrictAlternative>(), new[]
            {
                new ProviderFailure(ProviderFailureMode.Boundary,
                    "PROVIDER_SCHEMA_INVALID", null, null, message),
            });

    private static string LegalName(AddressCandidate candidate)
        => candidate.Kind == AddressCandidateKind.Road
            ? candidate.RefinedStructure.Level3
            : candidate.RefinedStructure.Level4L;

    private static LegalDistrictAlternative Alternative(
        AddressCandidate candidate,
        FeatureEvaluation evaluation,
        DistrictMatchKind kind)
        => new(candidate.Kind, evaluation.Feature.ProviderCode,
            evaluation.Feature.CanonicalCode, evaluation.Feature.FullName,
            evaluation.Feature.DisplayName, kind, Math.Sqrt(evaluation.DistanceSquared));

    private static bool Contains(Coordinate2D point, BoundaryTopology topology)
        => topology.Parts.Any(part => PointInRing(point, part.Outer) &&
            !part.Holes.Any(hole => PointInRing(point, hole)));

    private static bool PointInRing(Coordinate2D point, BoundaryRing ring)
    {
        bool inside = false;
        for (int index = 0; index < ring.Points.Count - 1; index++)
        {
            Coordinate2D a = ring.Points[index];
            Coordinate2D b = ring.Points[index + 1];
            if ((a.Y > point.Y) == (b.Y > point.Y)) continue;
            double crossingX = a.X + (point.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
            if (point.X < crossingX) inside = !inside;
        }
        return inside;
    }

    private static double MinimumDistanceSquared(Coordinate2D point, BoundaryTopology topology)
    {
        double minimum = double.PositiveInfinity;
        foreach (BoundaryRing ring in topology.Parts.SelectMany(part =>
                     new[] { part.Outer }.Concat(part.Holes)))
            for (int index = 0; index < ring.Points.Count - 1; index++)
                minimum = Math.Min(minimum,
                    SegmentDistanceSquared(point, ring.Points[index], ring.Points[index + 1]));
        return minimum;
    }

    private static double SegmentDistanceSquared(
        Coordinate2D point,
        Coordinate2D start,
        Coordinate2D end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared == 0)
            return Squared(point.X - start.X) + Squared(point.Y - start.Y);
        double parameter = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) /
            lengthSquared;
        parameter = Math.Clamp(parameter, 0, 1);
        return Squared(point.X - (start.X + parameter * dx)) +
            Squared(point.Y - (start.Y + parameter * dy));
    }

    private static double Squared(double value) => value * value;

    private static bool IsWithinEdgeTolerance(double distanceSquared)
        => distanceSquared <= EdgeToleranceSquared + EdgeComparisonSlackSquared;

    private sealed record FeatureEvaluation(
        CohortBoundaryFeature Feature,
        double DistanceSquared,
        bool Contains);
}

public sealed class VWorldAddressResolver
{
    public const double DualEquivalentDistanceMeters = 150;

    private readonly VWorldResolutionProvider _provider;
    private readonly IClock _clock;

    public VWorldAddressResolver(VWorldResolutionProvider provider, IClock? clock = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _clock = clock ?? SystemClock.Instance;
    }

    public async Task<AddressResolutionResult> ResolveAsync(
        string? inputAddress,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (AddressQueryNormalizer.Normalize(inputAddress).Length == 0)
            return AddressResolutionResult.InvalidInput();

        Task<ModeOutcome> roadTask = CaptureAsync(inputAddress!, AddressCandidateKind.Road,
            forceRefresh, cancellationToken);
        Task<ModeOutcome> parcelTask = CaptureAsync(inputAddress!, AddressCandidateKind.Parcel,
            forceRefresh, cancellationToken);
        ModeOutcome[] outcomes = await Task.WhenAll(roadTask, parcelTask).ConfigureAwait(false);
        ProviderFailure[] addressFailures = outcomes.Where(outcome => outcome.Failure != null)
            .Select(outcome => outcome.Failure!).ToArray();
        AddressCandidate[] candidates = outcomes.Where(outcome => outcome.Lookup?.Candidate != null)
            .Select(outcome => outcome.Lookup!.Candidate!).ToArray();
        if (addressFailures.Length > 0)
            return GenericProviderFailure(candidates, addressFailures);
        if (outcomes.All(outcome => outcome.Lookup!.Status == AddressLookupStatus.NotFound))
            return AddressResolutionResult.NotFound();

        if (candidates.Length == 2 && ExceedsDualDistance(
                ProjectedCoordinate.From(candidates[0].Wgs84),
                ProjectedCoordinate.From(candidates[1].Wgs84)))
            return new AddressResolutionResult(AddressResolutionStatus.NeedsSelection,
                AddressResolutionReason.DualModeDistanceExceeded, null, null, candidates, null,
                Array.Empty<LegalDistrictAlternative>(), Array.Empty<ProviderFailure>());

        BootstrapOutcome[] bootstraps = candidates.Select(Bootstrap).ToArray();
        ProviderFailure[] bootstrapFailures = bootstraps.Where(value => value.Failure != null)
            .Select(value => value.Failure!).ToArray();
        if (bootstrapFailures.Length > 0)
        {
            AddressCandidate[] validCandidates = bootstraps.Where(value => value.Failure == null)
                .Select(value => value.Candidate).ToArray();
            return GenericProviderFailure(validCandidates, bootstrapFailures);
        }
        if (bootstraps.Length == 2 &&
            (bootstraps[0].SigunguCode != bootstraps[1].SigunguCode ||
             bootstraps[0].SigunguName != bootstraps[1].SigunguName))
            return new AddressResolutionResult(AddressResolutionStatus.NeedsSelection,
                AddressResolutionReason.CohortBootstrapDisagreement, null, null, candidates, null,
                Array.Empty<LegalDistrictAlternative>(), Array.Empty<ProviderFailure>());

        ReferenceCohort cohort = SeoulLegalDistrictCatalog.CreateCohortForSigungu(
            bootstraps[0].SigunguCode!, $"서울특별시 {bootstraps[0].SigunguName}");
        CohortBoundaryLookup boundaries;
        try
        {
            boundaries = await _provider.GetLegalDistrictsForCohortAsync(cohort, forceRefresh,
                cancellationToken).ConfigureAwait(false);
        }
        catch (VWorldProviderException ex)
        {
            return new AddressResolutionResult(AddressResolutionStatus.ProviderFailure,
                ex.ReasonCode, null, null, candidates, null,
                Array.Empty<LegalDistrictAlternative>(), new[] { ex.Failure });
        }

        DateTimeOffset[] captured = outcomes.Where(outcome => outcome.Lookup?.Candidate != null)
            .Select(outcome => outcome.Lookup!.CapturedAtUtc).ToArray();
        return AddressTopologyResolver.Resolve(inputAddress!, candidates, captured, cohort,
            boundaries, _clock.UtcNow);
    }

    private async Task<ModeOutcome> CaptureAsync(
        string inputAddress,
        AddressCandidateKind mode,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        try
        {
            VWorldAddressLookup lookup = await _provider.GetAddressAsync(inputAddress, mode,
                forceRefresh, cancellationToken).ConfigureAwait(false);
            return new ModeOutcome(mode, lookup, null);
        }
        catch (VWorldProviderException ex)
        {
            return new ModeOutcome(mode, null, ex.Failure);
        }
    }

    private static BootstrapOutcome Bootstrap(AddressCandidate candidate)
    {
        RefinedAddressStructure structure = candidate.RefinedStructure;
        string level1 = AddressQueryNormalizer.Normalize(structure.Level1);
        string level2 = AddressQueryNormalizer.Normalize(structure.Level2);
        string code = structure.Level4AC;
        bool valid = level1 == "서울특별시" && level2.Length > 0 && code.Length == 10 &&
            code.All(char.IsDigit) && code.StartsWith("11", StringComparison.Ordinal);
        if (valid)
        {
            try { _ = SeoulLegalDistrictCatalog.ForSigungu(code[..5]); }
            catch (ArgumentException) { valid = false; }
        }
        return valid
            ? new BootstrapOutcome(candidate, code[..5], level2, null)
            : new BootstrapOutcome(candidate, null, null,
                new ProviderFailure(candidate.Kind == AddressCandidateKind.Road
                        ? ProviderFailureMode.Road : ProviderFailureMode.Parcel,
                    "PROVIDER_SCHEMA_INVALID", null, null,
                    "Address provider response lacks a supported Seoul sigungu scope."));
    }

    internal static bool ExceedsDualDistance(ProjectedCoordinate first, ProjectedCoordinate second)
    {
        double dx = first.X - second.X;
        double dy = first.Y - second.Y;
        return dx * dx + dy * dy >
            DualEquivalentDistanceMeters * DualEquivalentDistanceMeters;
    }

    private static AddressResolutionResult GenericProviderFailure(
        IReadOnlyList<AddressCandidate> candidates,
        IReadOnlyList<ProviderFailure> failures)
    {
        AddressResolutionReason reason = failures[0].CanonicalCode switch
        {
            "PROVIDER_REPORTED_ERROR" => AddressResolutionReason.ProviderReportedError,
            "PROVIDER_SCHEMA_INVALID" => AddressResolutionReason.ProviderSchemaInvalid,
            _ => AddressResolutionReason.ProviderTransportFailure,
        };
        return new AddressResolutionResult(AddressResolutionStatus.ProviderFailure, reason,
            null, null, candidates, null, Array.Empty<LegalDistrictAlternative>(), failures);
    }

    private sealed record ModeOutcome(
        AddressCandidateKind Mode,
        VWorldAddressLookup? Lookup,
        ProviderFailure? Failure);

    private sealed record BootstrapOutcome(
        AddressCandidate Candidate,
        string? SigunguCode,
        string? SigunguName,
        ProviderFailure? Failure);
}
