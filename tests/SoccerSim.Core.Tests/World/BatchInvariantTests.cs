using SoccerSim.Core.World;
using SoccerSim.Core.World.Serialization;
using SoccerSim.Core.World.Validation;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// ROADMAP.md Sprint 8's test: the sweep finds deliberately planted violations, and the real
/// batch produces exactly the known set of findings.
/// </summary>
public sealed class BatchInvariantTests
{
    private static readonly Lazy<WorldSnapshot> Source =
        new(() => WorldDerivations.Recalculate(WorldJsonReader.Read(WorldFixture.Json)));

    private static WorldSnapshot World => Source.Value;

    private static BatchAuditReport Run(WorldSnapshot world) => BatchAudit.Run(world);

    // --------------------------------------------------------------- the golden

    /// <summary>
    /// The roadmap's golden, and the only assertion in this file that is allowed to be updated
    /// without a reason: 1 error and 4 warnings over 5 clubs, the other 15 clean.
    /// </summary>
    [Fact]
    public void TheRealBatch_ProducesExactlyTheKnownFindings()
    {
        BatchAuditReport report = Run(World);

        Assert.Equal(1, report.Errors);
        Assert.Equal(4, report.Warnings);
        Assert.Equal(5, report.ClubsAffected);
        Assert.Equal(World.Clubs.Count - 5, World.Clubs.Count - report.ClubsAffected);
        Assert.False(report.Released);
    }

    [Fact]
    public void TheOneError_IsTheKnownPhoneticViolation()
    {
        BatchFinding error = Assert.Single(Run(World).Findings, f => f.Level == FindingLevel.Error);

        // clb_bra_bel_001 (Remolar), NamingRule = Phonetic, similarity 0.867 — a real, recorded
        // violation that ADR-0003 deliberately lets into the database so this sweep can report it.
        Assert.Equal("PHONETIC_WINDOW", error.Code);
        Assert.Equal("clb_bra_bel_001", error.EntityId);
        Assert.Contains("0,867", error.Detail.Replace('.', ','));
    }

    [Fact]
    public void TheFourWarnings_AreAllOneWayDerbies()
    {
        var warnings = Run(World).Findings.Where(f => f.Level == FindingLevel.Warning).ToList();

        Assert.Equal(4, warnings.Count);
        Assert.All(warnings, warning => Assert.Equal("DERBY_ONE_WAY", warning.Code));

        Assert.Equal(
            ["clb_bra_rio_002", "clb_bra_rio_004", "clb_bra_san_001", "clb_bra_sao_003"],
            warnings.Select(warning => warning.EntityId).Order());
    }

    [Fact]
    public void TheRealBatch_HasNoStaleEconomy()
    {
        // The Sprint 1 gate proved the formulas reproduce every stored value; this is the same
        // claim made from the other side, over the whole batch at once.
        Assert.DoesNotContain(Run(World).Findings, finding => finding.Code == "ECONOMY_STALE");
    }

    [Fact]
    public void TheSweep_ReportsOnlyFailures()
    {
        // 20 clubs × 6 passing checks would bury the four rows that matter.
        Assert.DoesNotContain(Run(World).Findings, finding => finding.Level == FindingLevel.Ok);
    }

    [Fact]
    public void TheCountersDescribeTheBatch()
    {
        BatchAuditReport report = Run(World);

        Assert.Equal(20, report.ClubsScanned);
        Assert.Equal(688, report.PlayersScanned);
        Assert.Equal(27, report.SourcesRegistered);
    }

    // ------------------------------------------------------- planted violations

    [Fact]
    public void ADuplicateDisplayCode_IsAnErrorOnBothClubs()
    {
        WorldSnapshot world = WithClub(0, club => club with { DisplayCode = World.Clubs[1].DisplayCode });

        var findings = Run(world).Findings.Where(f => f.Code == "CODE_DUP").ToList();

        Assert.Equal(2, findings.Count);
        Assert.All(findings, finding => Assert.Equal(FindingLevel.Error, finding.Level));
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("ABCD")]
    [InlineData("Ab1")]
    public void ADisplayCodeThatIsNotThreeCapitals_IsAWarning(string code)
    {
        WorldSnapshot world = WithClub(0, club => club with { DisplayCode = code });

        BatchFinding finding = Assert.Single(Run(world).Findings, f => f.Code == "CODE_FORMAT");
        Assert.Equal(FindingLevel.Warning, finding.Level);
        Assert.Contains(code, finding.Detail);
    }

    [Fact]
    public void AFoundingYearEqualToTheAnchor_IsAWarning()
    {
        // The anchor exists so the world can deviate from it; an identical year quietly asserts
        // a real fact about a real club.
        WorldSnapshot world = WithClub(0, club => club with
        {
            Audit = club.Audit with { GeneratedFoundingYear = club.Audit.AnchorFoundingYear },
        });

        BatchFinding finding = Assert.Single(Run(world).Findings, f => f.Code == "FOUNDING_EQ");
        Assert.Equal(FindingLevel.Warning, finding.Level);
    }

    /// <summary>
    /// The deviation may move the year but not the decade — and <c>foundingDecadePreserved</c>
    /// records which decade that is. All twenty clubs in the batch agree with the years beside
    /// them, so any disagreement is authored, not inherited.
    /// </summary>
    [Fact]
    public void AGeneratedYearOutsideTheDeclaredDecade_IsAWarning()
    {
        WorldSnapshot world = WithClub(0, club => club with
        {
            Audit = club.Audit with { GeneratedFoundingYear = club.Audit.FoundingDecadePreserved + 45 },
        });

        BatchFinding finding = Assert.Single(Run(world).Findings, f => f.Code == "FOUNDING_DECADE");
        Assert.Equal(FindingLevel.Warning, finding.Level);
        Assert.Contains("generatedFoundingYear", finding.Detail);
        Assert.Contains("fora da década declarada", finding.Detail);
    }

    [Fact]
    public void ADeclaredDecadeThatMatchesNeitherYear_IsReportedTwice()
    {
        WorldSnapshot world = WithClub(0, club => club with
        {
            Audit = club.Audit with { FoundingDecadePreserved = 1700 },
        });

        var findings = Run(world).Findings.Where(f => f.Code == "FOUNDING_DECADE").ToList();

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Detail.Contains("anchorFoundingYear"));
        Assert.Contains(findings, f => f.Detail.Contains("generatedFoundingYear"));
    }

    [Fact]
    public void AnUnverifiedAnchor_IsReportedByBothTheClubCheckAndTheBatchCode()
    {
        WorldSnapshot world = WithClub(0, club => club with
        {
            Audit = club.Audit with { AnchorFactsVerified = false },
        });

        var codes = Run(world).Findings
            .Where(f => f.EntityId == World.Clubs[0].ClubId)
            .Select(f => f.Code)
            .ToList();

        // The club page check and the batch code are the same fact seen from two screens; the
        // batch one carries the consequence ("cannot enter the batch") in its detail.
        Assert.Contains("ANCHOR_VERIFIED", codes);
        Assert.Contains("ANCHOR_UNVERIFIED", codes);
    }

    [Theory]
    [InlineData("foundingSourceCitation")]
    [InlineData("nicknameEvidence")]
    [InlineData("crest_sourceCitation")]
    [InlineData("districtSourceCitation")]
    [InlineData("tacticalStyleEvidence")]
    public void AnyOfTheFiveCitationsMissing_IsAnError(string field)
    {
        WorldSnapshot world = WithClub(0, club => club with { Audit = Blank(club.Audit, field) });

        BatchFinding finding = Assert.Single(Run(world).Findings, f => f.Code == "CITATION_MISSING");
        Assert.Equal(FindingLevel.Error, finding.Level);
        Assert.Contains(field, finding.Detail);
    }

    [Fact]
    public void ADerbyPointingAtNothing_IsAnError()
    {
        WorldSnapshot world = WithClub(0, club => club with
        {
            AiProfile = club.AiProfile with { DerbyRivalClubId = "clb_bra_ghost_001" },
        });

        BatchFinding finding = Assert.Single(Run(world).Findings, f => f.Code == "DERBY_DANGLING");
        Assert.Equal(FindingLevel.Error, finding.Level);
        Assert.Contains("clb_bra_ghost_001", finding.Detail);
    }

    [Fact]
    public void AClubPointingAtAGeoNodeThatIsNotThere_IsAnError()
    {
        WorldSnapshot world = WithClub(0, club => club with
        {
            Geography = club.Geography with { GeoNodeId = "geo_nowhere" },
        });

        BatchFinding finding = Assert.Single(Run(world).Findings, f => f.Code == "GEO_DANGLING");
        Assert.Equal(FindingLevel.Error, finding.Level);
    }

    /// <summary>
    /// The check that caught the spreadsheet's legend row: a GeoNode with no kind and no name
    /// that an extractor swallowed. An audit that only validates references never finds it.
    /// </summary>
    [Fact]
    public void AMalformedGeoNode_IsFoundByAuditingTheNodesThemselves()
    {
        WorldSnapshot world = World with
        {
            GeoNodes = [.. World.GeoNodes, new GeoNode("geo_legend", (GeoNodeKind)99, "geo_bra", "")],
        };

        BatchFinding finding = Assert.Single(Run(world).Findings, f => f.Code == "GEO_NODE_MALFORMED");
        Assert.Equal(FindingLevel.Error, finding.Level);
        Assert.Equal(FindingScope.GeoNode, finding.Scope);
        Assert.Equal("geo_legend", finding.EntityId);
    }

    [Fact]
    public void ANodeHangingOffTheWrongKind_IsAnError()
    {
        // A city cannot hang off a confederation.
        WorldSnapshot world = World with
        {
            GeoNodes = [.. World.GeoNodes, new GeoNode("geo_odd", GeoNodeKind.City, "geo_conmebol", "Cidade Solta")],
        };

        BatchFinding finding = Assert.Single(Run(world).Findings, f => f.Code == "GEO_NODE_INVALID");
        Assert.Contains("City pendurado em Confederation", finding.Detail);
    }

    [Fact]
    public void AClubWithNoPlayers_IsAnError()
    {
        string clubId = World.Clubs[0].ClubId;
        WorldSnapshot world = World with
        {
            Characters = World.Characters.Where(player => player.ClubId != clubId).ToList(),
        };

        BatchFinding finding = Assert.Single(Run(world).Findings, f => f.Code == "SQUAD_EMPTY");
        Assert.Equal(clubId, finding.EntityId);
    }

    [Fact]
    public void ASquadThatDisagreesWithItsDeclaredSize_IsAWarning()
    {
        WorldSnapshot world = WithClub(0, club => club with
        {
            World = club.World with { SquadSize = club.World.SquadSize + 3 },
        });

        BatchFinding finding = Assert.Single(Run(world).Findings, f => f.Code == "SQUAD_SIZE");
        Assert.Equal(FindingLevel.Warning, finding.Level);
    }

    [Fact]
    public void TwoPlayersWearingTheSameNumber_IsAnError()
    {
        string clubId = World.Clubs[0].ClubId;
        var squad = World.Characters.Where(player => player.ClubId == clubId).ToList();

        WorldSnapshot world = World with
        {
            Characters = World.Characters
                .Select(player => player == squad[1] ? player with { ShirtNumber = squad[0].ShirtNumber } : player)
                .ToList(),
        };

        BatchFinding finding = Assert.Single(Run(world).Findings, f => f.Code == "SHIRT_DUP");
        Assert.Equal(FindingLevel.Error, finding.Level);
        Assert.Contains(squad[0].ShirtNumber.ToString(), finding.Detail);
    }

    [Fact]
    public void AnEconomyThatNoLongerFollowsFromTheCalibration_IsAWarning()
    {
        string clubId = World.Clubs[0].ClubId;

        WorldSnapshot world = World with
        {
            Characters = World.Characters
                .Select(player => player.ClubId == clubId ? player with { Overall = player.Overall + 7 } : player)
                .ToList(),
        };

        BatchFinding finding = Assert.Single(Run(world).Findings, f => f.Code == "ECONOMY_STALE");
        Assert.Equal(FindingLevel.Warning, finding.Level);
        Assert.Equal(clubId, finding.EntityId);
    }

    // --------------------------------------------------------------- the gate

    [Fact]
    public void TheGateBlocksOnAnError_AndWarningsAloneDoNot()
    {
        // The real batch has one error, so it is blocked.
        Assert.False(Run(World).Released);

        // Fix that one club's phonetic similarity and the batch releases, four warnings and all.
        WorldSnapshot fixedWorld = WithClub(
            World.Clubs.ToList().FindIndex(club => club.ClubId == "clb_bra_bel_001"),
            club => club with { Audit = club.Audit with { PhoneticSimilarity = 0.7 } });

        BatchAuditReport report = Run(fixedWorld);
        Assert.True(report.Released);
        Assert.Equal(4, report.Warnings);
    }

    // --------------------------------------------------------------- helpers

    private static WorldSnapshot WithClub(int index, Func<ClubIdentity, ClubIdentity> edit)
    {
        var clubs = World.Clubs.ToList();
        clubs[index] = edit(clubs[index]);
        return World with { Clubs = clubs };
    }

    private static ClubDeviationAudit Blank(ClubDeviationAudit audit, string field) => field switch
    {
        "foundingSourceCitation" => audit with { FoundingSourceCitation = " " },
        "nicknameEvidence" => audit with { NicknameEvidence = " " },
        "crest_sourceCitation" => audit with { CrestSourceCitation = " " },
        "districtSourceCitation" => audit with { DistrictSourceCitation = " " },
        "tacticalStyleEvidence" => audit with { TacticalStyleEvidence = " " },
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "not one of the five citations"),
    };
}
