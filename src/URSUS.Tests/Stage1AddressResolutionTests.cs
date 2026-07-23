using System.Text;
using System.Text.Json;
using URSUS.DataSources;
using URSUS.Parsers;
using URSUS.SiteBriefing;

namespace URSUS.Tests;

internal static class Stage1AddressResolutionTests
{
    private const string RoadFingerprint =
        "702f1e6f788434921910ab4ce1ec479cd18c096a1d20a22acb1402194cbcb13f";

    [Test]
    internal static void QueryNormalizerAndRequestCache_MatchIdentityFixture()
    {
        string[] inputs =
        {
            "서울특별시 중구 세종대로 110",
            "\t서울특별시  중구　세종대로 110\n",
            "서울특별시 중구 세종대로 110",
        };

        foreach (string input in inputs)
        {
            AssertEx.Equal("서울특별시 중구 세종대로 110",
                AddressQueryNormalizer.Normalize(input));
            AssertEx.Equal(
                "a695286bce728a878a5771ef280ec3ff7551a425d8226bdb198b42281b7d1f9b",
                ResolutionIdentity.ComputeRequestCacheId(AddressCandidateKind.Road, input));
        }
        AssertEx.Equal(
            "dfc2cfa303acc64701b9f035c47becdf50d3b5751c5a51aa8df4e7f1bb2cc68c",
            ResolutionIdentity.ComputeRequestCacheId(AddressCandidateKind.Parcel, inputs[0]));
    }

    [Test]
    internal static void ResolutionIds_MatchRoadParcelAndDualIdentityFixture()
    {
        AddressCandidate road = RoadCandidate();
        AddressCandidate parcel = ParcelCandidate();
        string roadPreimage = ResolutionIdentity.BuildSinglePreimage(road.Kind,
            road.RefinedText, road.Wgs84, "11140103", road.ResponseFingerprint);
        string parcelPreimage = ResolutionIdentity.BuildSinglePreimage(parcel.Kind,
            parcel.RefinedText, parcel.Wgs84, "11140103", parcel.ResponseFingerprint);

        AssertEx.Equal("b2d94baf2d69fa85d9ff33503bdb29f19a9ba7f1beb92ed539a8274ea3e24c86",
            ResolutionIdentity.Sha256(roadPreimage));
        AssertEx.Equal("91380e9550fedeedf497763f2c1357c98628fb1cc1ab22a71cdd70d34146b7bf",
            ResolutionIdentity.Sha256(parcelPreimage));
        string dual = ResolutionIdentity.BuildDualEquivalentPreimage(roadPreimage, parcelPreimage);
        AssertEx.True(dual.Contains("|191:", StringComparison.Ordinal));
        AssertEx.True(dual.Contains("|176:", StringComparison.Ordinal));
        AssertEx.Equal("c125bc7c506ef8049432fec17dc43d8a2c3d887f9ecc03c9511202aa816fca17",
            ResolutionIdentity.Sha256(dual));
    }

    [Test]
    internal static void ProviderFingerprint_RemovesOnlyRootServiceTimeAndOrdersProperties()
    {
        const string input = """
            {"status":"OK","service":{"version":"2.0","time":"2026-07-22T06:12:01Z","name":"address"},"result":{"time":"preserve-this-field","point":{"y":"37.0","x":"127.0"}}}
            """;
        var result = ResolutionIdentity.FingerprintProviderResponse(input);
        AssertEx.Equal(
            "{\"result\":{\"point\":{\"x\":\"127.0\",\"y\":\"37.0\"},\"time\":\"preserve-this-field\"},\"service\":{\"name\":\"address\",\"version\":\"2.0\"},\"status\":\"OK\"}",
            result.CanonicalJson);
        AssertEx.Equal(
            "71cab8630c94b69ff20bb36a6612b44ee7f3a6bcc7ee64bb12be3b3d40c3fb7b",
            result.Sha256);
    }

    [Test]
    internal static void ResolvedRoad_UsesFixtureIdentityAndSharedImmutableCandidates()
    {
        AddressCandidate candidate = RoadCandidate();
        var site = CreateResolvedSite(new[] { candidate });
        ReferenceCohort cohort = JungGuCohort();
        var result = new AddressResolutionResult(
            AddressResolutionStatus.Resolved,
            AddressResolutionReason.None,
            site,
            cohort,
            site.Candidates,
            null,
            Array.Empty<LegalDistrictAlternative>(),
            Array.Empty<ProviderFailure>());

        AssertEx.Equal(
            "resolver-contract/1|Road|서울특별시 중구 세종대로 110 (태평로1가)|126.97834678005411,37.56670100709671|11140103|" + RoadFingerprint,
            site.ResolutionCanonicalInput);
        AssertEx.Equal("b2d94baf2d69fa85d9ff33503bdb29f19a9ba7f1beb92ed539a8274ea3e24c86",
            site.ResolutionId);
        AssertEx.True(ReferenceEquals(result.Candidates, site.Candidates));
        AssertEx.True(result.ReferenceCohort!.DistrictIds.Contains("11140103"));
        AssertEx.Near(953931.914144946, site.ProjectedCoordinate.X, 0.001);
        AssertEx.Near(1952054.2116652473, site.ProjectedCoordinate.Y, 0.001);
    }

    [Test]
    internal static void ReferenceCohort_IsSortedDefensiveAndMatchesJungGuFixture()
    {
        string[] input = MappingLoader.Load().Values.SelectMany(ids => ids)
            .Select(DistrictCode.CanonicalizeLegal)
            .Where(id => id.StartsWith("11140", StringComparison.Ordinal) &&
                !id.EndsWith("000", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(id => id, StringComparer.Ordinal)
            .ToArray();
        ReferenceCohort cohort = new("11140", "서울특별시 중구",
            SeoulExpectedDistricts.Version, input);
        input[0] = "11199999";

        AssertEx.Equal(74, cohort.DistrictIds.Count);
        AssertEx.Equal("11140101", cohort.DistrictIds[0]);
        AssertEx.Equal("11140174", cohort.DistrictIds[^1]);
        AssertEx.Equal(
            "0a6144d5067f73cc9c4f510b76a11d08058015bd49101b60260bf091004f2fa8",
            cohort.Sha256);
    }

    [Test]
    internal static void SeoulLegalDistrictCatalog_SeparatesAdministrativeAndLegalMembership()
    {
        AssertEx.Equal(426, SeoulExpectedDistricts.Ids.Count);
        AssertEx.Equal(467, SeoulLegalDistrictCatalog.Ids.Count);
        AssertEx.True(SeoulLegalDistrictCatalog.Ids.SequenceEqual(
            SeoulLegalDistrictCatalog.Ids.OrderBy(id => id, StringComparer.Ordinal)));
        AssertEx.True(SeoulLegalDistrictCatalog.Ids.All(id => id.Length == 8 &&
            id.StartsWith("11", StringComparison.Ordinal) &&
            !id.EndsWith("000", StringComparison.Ordinal)));

        ReferenceCohort cohort = SeoulLegalDistrictCatalog.CreateCohort(
            "11140103", "서울특별시 중구");
        AssertEx.Equal(74, cohort.DistrictIds.Count);
        AssertEx.True(cohort.DistrictIds.Contains("11140103"));
        AssertEx.Equal(
            "0a6144d5067f73cc9c4f510b76a11d08058015bd49101b60260bf091004f2fa8",
            cohort.Sha256);
    }

    [Test]
    internal static void AnalysisRequest_PreservesVerbatimAddressAndKeepsSemanticFingerprint()
    {
        var verbatim = new Analysis.AnalysisRequest(new[] { "resident_pop" },
            address1: "\t서울특별시  중구　세종대로 110\n");
        var trimmed = new Analysis.AnalysisRequest(new[] { "resident_pop" },
            address1: "서울특별시 중구 세종대로 110");

        AssertEx.Equal("\t서울특별시  중구　세종대로 110\n", verbatim.InputAddress1);
        AssertEx.Equal("서울특별시  중구　세종대로 110", verbatim.Address1);
        AssertEx.Equal(trimmed.QueryFingerprint, verbatim.QueryFingerprint);
    }

    [Test]
    internal static void ClosedResultMatrix_AcceptsTypedFailuresAndRejectsDivergence()
    {
        AddressResolutionResult invalid = AddressResolutionResult.InvalidInput();
        AddressResolutionResult notFound = AddressResolutionResult.NotFound();
        AssertEx.Equal(AddressResolutionReason.EmptyAddress, invalid.ReasonCode);
        AssertEx.Equal(AddressResolutionReason.BothModesNotFound, notFound.ReasonCode);

        AddressCandidate candidate = RoadCandidate();
        var failure = new ProviderFailure(ProviderFailureMode.Boundary,
            "COHORT_BOUNDARY_INCOMPLETE", "MEMBERSHIP_MISMATCH", null,
            "Boundary provider returned 73 of 74 required legal districts.");
        var incomplete = new AddressResolutionResult(
            AddressResolutionStatus.ProviderFailure,
            AddressResolutionReason.CohortBoundaryIncomplete,
            null, null, new[] { candidate }, null,
            Array.Empty<LegalDistrictAlternative>(), new[] { failure });
        AssertEx.Equal(1, incomplete.ProviderFailures.Count);

        var dualCandidates = new[] { candidate, ParcelCandidateWithInput(candidate.InputAddress) };
        var boundaryFailure = new ProviderFailure(ProviderFailureMode.Boundary,
            "PROVIDER_SCHEMA_INVALID", null, null,
            "Boundary provider response did not satisfy contract VWorld WFS/2.0.0.");
        var dualBoundaryFailure = new AddressResolutionResult(
            AddressResolutionStatus.ProviderFailure,
            AddressResolutionReason.ProviderSchemaInvalid,
            null, null, dualCandidates, null,
            Array.Empty<LegalDistrictAlternative>(), new[] { boundaryFailure });
        AssertEx.Equal(2, dualBoundaryFailure.Candidates.Count);
        AssertEx.Throws<ArgumentException>(() => new AddressResolutionResult(
            AddressResolutionStatus.ProviderFailure,
            AddressResolutionReason.ProviderSchemaInvalid,
            null, null, dualCandidates, null,
            Array.Empty<LegalDistrictAlternative>(),
            new[] { boundaryFailure with { Mode = ProviderFailureMode.Road } }));

        ResolvedSite site = CreateResolvedSite(new[] { candidate });
        ReferenceCohort cohort = JungGuCohort();
        AssertEx.Throws<ArgumentException>(() => new AddressResolutionResult(
            AddressResolutionStatus.Resolved,
            AddressResolutionReason.None,
            site,
            cohort,
            new[] { candidate },
            null,
            Array.Empty<LegalDistrictAlternative>(),
            Array.Empty<ProviderFailure>()));
        AssertEx.Throws<ArgumentException>(() => new AddressResolutionResult(
            AddressResolutionStatus.ProviderFailure,
            AddressResolutionReason.CohortBoundaryIncomplete,
            null, null, new[] { candidate }, null,
            Array.Empty<LegalDistrictAlternative>(),
            new[] { failure with { Mode = ProviderFailureMode.Road } }));
    }

    [Test]
    internal static void ResultCollections_DefensivelyCopyNonResolvedPayloads()
    {
        AddressCandidate candidate = RoadCandidate();
        var source = new[] { candidate, ParcelCandidateWithInput(candidate.InputAddress) };
        var result = new AddressResolutionResult(
            AddressResolutionStatus.NeedsSelection,
            AddressResolutionReason.DualModeDistanceExceeded,
            null, null, source, null,
            Array.Empty<LegalDistrictAlternative>(),
            Array.Empty<ProviderFailure>());
        source[0] = ParcelCandidate();

        AssertEx.Equal(AddressCandidateKind.Road, result.Candidates[0].Kind);
        AssertEx.Throws<ArgumentNullException>(() => new AddressResolutionResult(
            AddressResolutionStatus.NotFound,
            AddressResolutionReason.BothModesNotFound,
            null, null, null!, null,
            Array.Empty<LegalDistrictAlternative>(), Array.Empty<ProviderFailure>()));
    }

    [Test]
    internal static void ValueContracts_RejectOutOfRangeCoordinatesAndNonUtcTimestamps()
    {
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new Wgs84Coordinate(181, 37));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new Wgs84Coordinate(127, -91));

        AddressCandidate candidate = RoadCandidate();
        AssertEx.Throws<ArgumentException>(() => new ResolvedSite(
            candidate.InputAddress,
            candidate.RefinedText,
            AddressKind.Road,
            new[] { candidate },
            new LegalDistrictSelection("11140103", "11140103", "서울특별시 중구 태평로1가",
                "태평로1가", "WFS topology point-in-polygon"),
            new DateTimeOffset(2026, 7, 22, 15, 0, 0, TimeSpan.FromHours(9)),
            new[] { new ResolverSource("VWorld", "address", "2.0", DateTimeOffset.UtcNow) }));
        AssertEx.Throws<ArgumentException>(() => new AddressCandidate(
            AddressCandidateKind.Road, "입력", "정제", new RefinedAddressStructure(),
            new Wgs84Coordinate(127, 37), "VWorld address/2.0", RoadFingerprint));
        AssertEx.Throws<ArgumentException>(() => new AddressCandidate(
            AddressCandidateKind.Parcel, "입력", "정제",
            new RefinedAddressStructure(Level4L: "법정동"),
            new Wgs84Coordinate(127, 37), "VWorld address/1.0", RoadFingerprint));
        AssertEx.Throws<ArgumentException>(() => new ResolvedSite(
            candidate.InputAddress, candidate.RefinedText, (AddressKind)999,
            new[] { candidate },
            new LegalDistrictSelection("11140103", "11140103", "서울특별시 중구 태평로1가",
                "태평로1가", "WFS topology point-in-polygon"),
            DateTimeOffset.UtcNow,
            new[] { new ResolverSource("VWorld", "address", "2.0", DateTimeOffset.UtcNow) }));
        AssertEx.Throws<ArgumentException>(() => new AddressResolutionResult(
            AddressResolutionStatus.ProviderFailure,
            AddressResolutionReason.ProviderSchemaInvalid, null, null, [], null, [],
            new[] { new ProviderFailure((ProviderFailureMode)999, "PROVIDER_SCHEMA_INVALID",
                null, null, "safe") }));
        AssertEx.Throws<ArgumentException>(() => new AddressResolutionResult(
            AddressResolutionStatus.NeedsSelection,
            AddressResolutionReason.DistrictEdge, null, null, new[] { candidate },
            RepresentativeFor(candidate),
            new[] { new LegalDistrictAlternative(AddressCandidateKind.Road, "", "11140103",
                "", "", DistrictMatchKind.Edge, 0) }, []));
    }

    [Test]
    internal static void ResultFixture_AllMappedCasesSatisfyClosedMatrix()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(
            FindRepositoryFile("docs", "fixtures", "address-resolution-result-cases-v1.json")));
        JsonElement cases = document.RootElement.GetProperty("cases");
        AssertEx.Equal(22, cases.GetArrayLength());

        foreach (JsonElement item in cases.EnumerateArray())
        {
            JsonElement expected = item.GetProperty("expected");
            AddressResolutionStatus status = Enum.Parse<AddressResolutionStatus>(
                expected.GetProperty("status").GetString()!);
            AddressResolutionReason reason = Enum.Parse<AddressResolutionReason>(
                expected.GetProperty("reasonCode").GetString()!);
            AddressCandidate[] candidates = expected.GetProperty("candidates")
                .EnumerateArray().Select(ParseCandidate).ToArray();
            ResolvedSite? site = expected.GetProperty("resolvedSite").ValueKind == JsonValueKind.Null
                ? null
                : ParseResolvedSite(expected.GetProperty("resolvedSite"), candidates);
            ReferenceCohort? cohort = expected.GetProperty("referenceCohort").ValueKind == JsonValueKind.Null
                ? null
                : ParseCohort(expected.GetProperty("referenceCohort"));
            RepresentativeLocation? representative =
                expected.GetProperty("representativeLocation").ValueKind == JsonValueKind.Null
                    ? null
                    : ParseRepresentative(expected.GetProperty("representativeLocation"));
            LegalDistrictAlternative[] alternatives = expected
                .GetProperty("legalDistrictAlternatives").EnumerateArray()
                .Select(ParseAlternative).ToArray();
            ProviderFailure[] failures = expected.GetProperty("providerFailures").EnumerateArray()
                .Select(ParseFailure).ToArray();

            var result = new AddressResolutionResult(status, reason, site, cohort,
                site?.Candidates ?? candidates, representative, alternatives, failures);
            string caseId = item.GetProperty("caseId").GetString()!;
            AssertEx.Equal(status, result.Status, caseId);
            AssertEx.Equal(reason, result.ReasonCode, caseId);
            AssertEx.Equal(candidates.Length, result.Candidates.Count, caseId);
            AssertEx.Equal(alternatives.Length, result.LegalDistrictAlternatives.Count, caseId);
            AssertEx.Equal(failures.Length, result.ProviderFailures.Count, caseId);
            AssertEx.Equal(site != null, result.ResolvedSite != null, caseId);
            AssertEx.Equal(cohort != null, result.ReferenceCohort != null, caseId);
            AssertEx.Equal(representative != null, result.RepresentativeLocation != null, caseId);
            AssertEx.True(result.Candidates.Select(value => value.Kind).SequenceEqual(
                candidates.Select(value => value.Kind)), caseId);
            AssertEx.True(result.ProviderFailures.Select(value => (value.Mode, value.CanonicalCode))
                .SequenceEqual(failures.Select(value => (value.Mode, value.CanonicalCode))), caseId);
            AssertEx.True(result.LegalDistrictAlternatives
                .Select(value => (value.CandidateKind, value.CanonicalCode, value.MatchKind))
                .SequenceEqual(alternatives
                    .Select(value => (value.CandidateKind, value.CanonicalCode, value.MatchKind))), caseId);
            if (representative != null)
                AssertEx.Equal(representative.CandidateKind,
                    result.RepresentativeLocation!.CandidateKind, caseId);
            if (site != null)
            {
                AssertEx.Equal(site.ResolutionId, result.ResolvedSite!.ResolutionId, caseId);
                AssertEx.Equal(cohort!.Sha256, result.ReferenceCohort!.Sha256, caseId);
            }
        }
    }

    [Test]
    internal static void ConstructorRejectionFixture_AllDeclaredMutationsThrow()
    {
        AddressCandidate road = RoadCandidate();
        AddressCandidate parcel = ParcelCandidateWithInput(road.InputAddress);
        ResolvedSite site = CreateResolvedSite(new[] { road });
        ReferenceCohort cohort = JungGuCohort();
        RepresentativeLocation roadRepresentative = RepresentativeFor(road);
        LegalDistrictAlternative roadEdge = Alternative(
            AddressCandidateKind.Road, DistrictMatchKind.Edge, "11140103");
        LegalDistrictAlternative roadStrict = Alternative(
            AddressCandidateKind.Road, DistrictMatchKind.Strict, "11140103");
        ProviderFailure schema = new(ProviderFailureMode.Road,
            "PROVIDER_SCHEMA_INVALID", null, null, "Address provider schema invalid.");
        ProviderFailure incomplete = new(ProviderFailureMode.Boundary,
            "COHORT_BOUNDARY_INCOMPLETE", "MEMBERSHIP_MISMATCH", null,
            "Boundary provider returned 73 of 74 required legal districts.");
        ProviderFailure transport = new(ProviderFailureMode.Parcel,
            "PROVIDER_TRANSPORT_FAILURE", null, null,
            "Address provider request timed out.");

        var actions = new Dictionary<string, Action>(StringComparer.Ordinal)
        {
            ["null-candidates-collection"] = () => New(AddressResolutionStatus.NotFound,
                AddressResolutionReason.BothModesNotFound, null, null, null!, null, [], []),
            ["resolved-missing-site"] = () => New(AddressResolutionStatus.Resolved,
                AddressResolutionReason.None, null, cohort, new[] { road }, null, [], []),
            ["resolved-has-representative"] = () => New(AddressResolutionStatus.Resolved,
                AddressResolutionReason.None, site, cohort, site.Candidates,
                roadRepresentative, [], []),
            ["resolved-candidates-diverge"] = () => New(AddressResolutionStatus.Resolved,
                AddressResolutionReason.None, site, cohort, new[] { road }, null, [], []),
            ["status-reason-mismatch"] = () => New(AddressResolutionStatus.NotFound,
                AddressResolutionReason.EmptyAddress, null, null, [], null, [], []),
            ["distance-exceeded-one-candidate"] = () => New(AddressResolutionStatus.NeedsSelection,
                AddressResolutionReason.DualModeDistanceExceeded, null, null,
                new[] { road }, null, [], []),
            ["edge-without-representative"] = () => New(AddressResolutionStatus.NeedsSelection,
                AddressResolutionReason.DistrictEdge, null, null,
                new[] { road }, null, new[] { roadEdge }, []),
            ["edge-with-strict-alternative"] = () => New(AddressResolutionStatus.NeedsSelection,
                AddressResolutionReason.DistrictEdge, null, null,
                new[] { road }, roadRepresentative, new[] { roadStrict }, []),
            ["multiple-with-one-alternative"] = () => New(AddressResolutionStatus.NeedsSelection,
                AddressResolutionReason.MultipleDistrictContainment, null, null,
                new[] { road }, roadRepresentative, new[] { roadStrict }, []),
            ["disagreement-without-kind-pair"] = () => New(AddressResolutionStatus.NeedsSelection,
                AddressResolutionReason.DistrictDisagreement, null, null,
                new[] { road, parcel }, null, new[] { roadStrict, roadStrict }, []),
            ["out-of-coverage-with-alternative"] = () => New(AddressResolutionStatus.OutOfCoverage,
                AddressResolutionReason.OutsideBoundaryCoverage, null, null,
                new[] { road }, roadRepresentative, new[] { roadEdge }, []),
            ["provider-failure-with-representative"] = () => New(AddressResolutionStatus.ProviderFailure,
                AddressResolutionReason.ProviderSchemaInvalid, null, null,
                [], roadRepresentative, [], new[] { schema }),
            ["not-found-with-candidate"] = () => New(AddressResolutionStatus.NotFound,
                AddressResolutionReason.BothModesNotFound, null, null,
                new[] { road }, null, [], []),
            ["empty-input-with-failure"] = () => New(AddressResolutionStatus.InvalidInput,
                AddressResolutionReason.EmptyAddress, null, null,
                [], null, [], new[] { schema }),
            ["cohort-incomplete-wrong-failure-mode"] = () => New(
                AddressResolutionStatus.ProviderFailure,
                AddressResolutionReason.CohortBoundaryIncomplete, null, null,
                new[] { road }, null, [], new[] { incomplete with { Mode = ProviderFailureMode.Road } }),
            ["generic-two-candidates-with-address-mode-failure"] = () => New(
                AddressResolutionStatus.ProviderFailure,
                AddressResolutionReason.ProviderSchemaInvalid, null, null,
                new[] { road, parcel }, null, [], new[] { schema }),
            ["outside-representative-wrong-candidate-kind"] = () => New(
                AddressResolutionStatus.OutOfCoverage,
                AddressResolutionReason.OutsideBoundaryCoverage, null, null,
                new[] { road, parcel },
                new RepresentativeLocation(AddressCandidateKind.Road, parcel.Wgs84,
                    ProjectedCoordinate.From(parcel.Wgs84), "Wrong typed linkage."), [], []),
            ["disagreement-same-canonical-code"] = () => New(
                AddressResolutionStatus.NeedsSelection,
                AddressResolutionReason.DistrictDisagreement, null, null,
                new[] { road, parcel }, null,
                new[] { roadStrict, Alternative(AddressCandidateKind.Parcel,
                    DistrictMatchKind.Strict, "11140103") }, []),
            ["dual-candidates-different-input-provenance"] = () => New(
                AddressResolutionStatus.ProviderFailure,
                AddressResolutionReason.ProviderSchemaInvalid, null, null,
                new[] { road, ParcelCandidate() }, null, [],
                new[] { schema with { Mode = ProviderFailureMode.Boundary } }),
            ["mixed-provider-failures-wrong-primary-reason"] = () => New(
                AddressResolutionStatus.ProviderFailure,
                AddressResolutionReason.ProviderTransportFailure, null, null,
                [], null, [], new[] { schema, transport }),
            ["mixed-provider-failures-wrong-order"] = () => New(
                AddressResolutionStatus.ProviderFailure,
                AddressResolutionReason.ProviderTransportFailure, null, null,
                [], null, [], new[] { transport, schema }),
            ["generic-provider-duplicate-failure-mode"] = () => New(
                AddressResolutionStatus.ProviderFailure,
                AddressResolutionReason.ProviderSchemaInvalid, null, null,
                [], null, [], new[] { schema, schema }),
            ["generic-provider-address-boundary-mixed"] = () => New(
                AddressResolutionStatus.ProviderFailure,
                AddressResolutionReason.ProviderSchemaInvalid, null, null,
                [], null, [], new[] { schema, transport with { Mode = ProviderFailureMode.Boundary } }),
            ["generic-provider-unknown-canonical-code"] = () => New(
                AddressResolutionStatus.ProviderFailure,
                AddressResolutionReason.ProviderSchemaInvalid, null, null,
                [], null, [], new[] { schema with { CanonicalCode = "UNKNOWN_FAILURE" } }),
        };

        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(
            FindRepositoryFile("docs", "fixtures", "address-resolution-result-cases-v1.json")));
        string[] declared = fixture.RootElement.GetProperty("constructorRejectionCases")
            .EnumerateArray().Select(item => item.GetProperty("caseId").GetString()!).ToArray();
        AssertEx.Equal(24, declared.Length);
        AssertEx.True(declared.OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(
            actions.Keys.OrderBy(id => id, StringComparer.Ordinal)));
        foreach (string caseId in declared)
            AssertEx.Throws<ArgumentException>(actions[caseId]);
    }

    private static AddressCandidate RoadCandidate() => new(
        AddressCandidateKind.Road,
        "서울특별시 중구 세종대로 110",
        "서울특별시 중구 세종대로 110 (태평로1가)",
        new RefinedAddressStructure("대한민국", "서울특별시", "중구", "태평로1가",
            "세종대로", "", "명동", "1114055000", "110", "서울특별시 청사 신관"),
        new Wgs84Coordinate(126.97834678005411, 37.56670100709671),
        "VWorld address/2.0",
        RoadFingerprint);

    private static AddressCandidate ParcelCandidate() => new(
        AddressCandidateKind.Parcel,
        "서울특별시 중구 태평로1가 31",
        "서울특별시 중구 태평로1가 31",
        new RefinedAddressStructure("대한민국", "서울특별시", "중구", "", "태평로1가",
            "1114010300100310000", "명동", "1114055000", "31", "서울특별시청 본관동"),
        new Wgs84Coordinate(126.9782290751147, 37.56657117348658),
        "VWorld address/2.0",
        "2fa7acf1d6d8079fcbd98d7213d9c6cbcb01113ce5a95fb19965ecd9eb4af6e3");

    private static AddressCandidate ParcelCandidateWithInput(string input) => new(
        AddressCandidateKind.Parcel,
        input,
        "서울특별시 중구 태평로1가 31",
        new RefinedAddressStructure("대한민국", "서울특별시", "중구", "", "태평로1가",
            "1114010300100310000", "명동", "1114055000", "31", "서울특별시청 본관동"),
        new Wgs84Coordinate(126.9782290751147, 37.56657117348658),
        "VWorld address/2.0",
        "2fa7acf1d6d8079fcbd98d7213d9c6cbcb01113ce5a95fb19965ecd9eb4af6e3");

    private static ResolvedSite CreateResolvedSite(IReadOnlyList<AddressCandidate> candidates)
        => new(
            "서울특별시 중구 세종대로 110",
            candidates[0].RefinedText,
            AddressKind.Road,
            candidates,
            new LegalDistrictSelection("11140103", "11140103", "서울특별시 중구 태평로1가",
                "태평로1가", "WFS topology point-in-polygon"),
            DateTimeOffset.Parse("2026-07-22T06:12:01Z"),
            new[]
            {
                new ResolverSource("VWorld", "address", "2.0",
                    DateTimeOffset.Parse("2026-07-22T06:12:01Z")),
                new ResolverSource("VWorld", "WFS", "2.0.0",
                    DateTimeOffset.Parse("2026-07-22T06:12:01Z"), "lt_c_ademd_info"),
            });

    private static ReferenceCohort JungGuCohort()
    {
        string[] ids = MappingLoader.Load().Values.SelectMany(value => value)
            .Select(DistrictCode.CanonicalizeLegal)
            .Where(id => id.StartsWith("11140", StringComparison.Ordinal) &&
                !id.EndsWith("000", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new ReferenceCohort("11140", "서울특별시 중구",
            SeoulExpectedDistricts.Version, ids);
    }

    private static AddressCandidate ParseCandidate(JsonElement element)
    {
        JsonElement structure = element.GetProperty("refinedStructure");
        string Read(string name) => structure.TryGetProperty(name, out JsonElement value)
            ? value.GetString() ?? string.Empty : string.Empty;
        JsonElement point = element.GetProperty("wgs84");
        return new AddressCandidate(
            Enum.Parse<AddressCandidateKind>(element.GetProperty("kind").GetString()!),
            element.GetProperty("inputAddress").GetString()!,
            element.GetProperty("refinedText").GetString()!,
            new RefinedAddressStructure(Read("level0"), Read("level1"), Read("level2"),
                Read("level3"), Read("level4L"), Read("level4LC"), Read("level4A"),
                Read("level4AC"), Read("level5"), Read("detail")),
            new Wgs84Coordinate(point.GetProperty("longitude").GetDouble(),
                point.GetProperty("latitude").GetDouble()),
            element.GetProperty("responseContract").GetString()!,
            element.GetProperty("responseFingerprint").GetString()!);
    }

    private static ResolvedSite ParseResolvedSite(
        JsonElement element,
        IReadOnlyList<AddressCandidate> authoritativeCandidates)
    {
        JsonElement legal = element.GetProperty("legalDistrict");
        ResolverSource[] sources = element.GetProperty("resolverSources").EnumerateArray()
            .Select(source => new ResolverSource(
                source.GetProperty("provider").GetString()!,
                source.GetProperty("service").GetString()!,
                source.GetProperty("version").GetString()!,
                source.GetProperty("capturedAtUtc").GetDateTimeOffset(),
                source.TryGetProperty("typeName", out JsonElement typeName)
                    ? typeName.GetString() : null)).ToArray();
        return new ResolvedSite(
            element.GetProperty("inputAddress").GetString()!,
            element.GetProperty("normalizedAddress").GetString()!,
            Enum.Parse<AddressKind>(element.GetProperty("addressKind").GetString()!),
            authoritativeCandidates,
            new LegalDistrictSelection(
                legal.GetProperty("canonicalCode").GetString()!,
                legal.GetProperty("providerCode").GetString()!,
                legal.GetProperty("fullName").GetString()!,
                legal.GetProperty("displayName").GetString()!,
                legal.GetProperty("selectionMethod").GetString()!),
            element.GetProperty("resolvedAt").GetDateTimeOffset(),
            sources,
            element.GetProperty("resolutionWarnings").EnumerateArray()
                .Select(value => value.GetString()!).ToArray());
    }

    private static ReferenceCohort ParseCohort(JsonElement element)
        => new(
            element.GetProperty("sigunguCode").GetString()!,
            element.GetProperty("sigunguName").GetString()!,
            element.GetProperty("membershipSourceVersion").GetString()!,
            element.GetProperty("districtIds").EnumerateArray()
                .Select(value => value.GetString()!).ToArray());

    private static RepresentativeLocation ParseRepresentative(JsonElement element)
    {
        JsonElement wgs = element.GetProperty("wgs84");
        JsonElement projected = element.GetProperty("projectedCoordinate");
        return new RepresentativeLocation(
            Enum.Parse<AddressCandidateKind>(element.GetProperty("candidateKind").GetString()!),
            new Wgs84Coordinate(wgs.GetProperty("longitude").GetDouble(),
                wgs.GetProperty("latitude").GetDouble()),
            new ProjectedCoordinate(projected.GetProperty("x").GetDouble(),
                projected.GetProperty("y").GetDouble()),
            element.GetProperty("selectionBasis").GetString()!);
    }

    private static LegalDistrictAlternative ParseAlternative(JsonElement element)
        => new(
            Enum.Parse<AddressCandidateKind>(element.GetProperty("candidateKind").GetString()!),
            element.GetProperty("providerCode").GetString()!,
            element.GetProperty("canonicalCode").GetString()!,
            element.GetProperty("fullName").GetString()!,
            element.GetProperty("displayName").GetString()!,
            Enum.Parse<DistrictMatchKind>(element.GetProperty("matchKind").GetString()!),
            element.GetProperty("distanceMeters").GetDouble());

    private static ProviderFailure ParseFailure(JsonElement element)
        => new(
            Enum.Parse<ProviderFailureMode>(element.GetProperty("mode").GetString()!),
            element.GetProperty("canonicalCode").GetString()!,
            element.GetProperty("providerCode").ValueKind == JsonValueKind.Null
                ? null : element.GetProperty("providerCode").GetString(),
            element.GetProperty("providerLevel").ValueKind == JsonValueKind.Null
                ? null : element.GetProperty("providerLevel").GetString(),
            element.GetProperty("safeMessage").GetString()!);

    private static AddressResolutionResult New(
        AddressResolutionStatus status,
        AddressResolutionReason reason,
        ResolvedSite? site,
        ReferenceCohort? cohort,
        IReadOnlyList<AddressCandidate> candidates,
        RepresentativeLocation? representative,
        IReadOnlyList<LegalDistrictAlternative> alternatives,
        IReadOnlyList<ProviderFailure> failures)
        => new(status, reason, site, cohort, candidates, representative, alternatives, failures);

    private static RepresentativeLocation RepresentativeFor(AddressCandidate candidate)
        => new(candidate.Kind, candidate.Wgs84, ProjectedCoordinate.From(candidate.Wgs84),
            "Synthetic reason candidate.");

    private static LegalDistrictAlternative Alternative(
        AddressCandidateKind kind,
        DistrictMatchKind match,
        string code)
        => new(kind, code, code, "서울특별시 중구 태평로1가", "태평로1가", match,
            match == DistrictMatchKind.Edge ? 0.004 : 8);

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Repository file not found: {Path.Combine(parts)}");
    }
}
