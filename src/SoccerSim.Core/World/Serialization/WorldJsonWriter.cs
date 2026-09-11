using System.Text.Json;
using System.Text.Json.Nodes;

namespace SoccerSim.Core.World.Serialization;

/// <summary>
/// Writes the whole base back out as the document <see cref="WorldJsonReader"/> reads
/// (ALGORITHMS.md §8). The two are inverses on purpose and a test pins it: a format that can only
/// be read is a one-way door, and the tool has to be able to hand the world back.
///
/// <para>Derived values (overall, market value, salary, ΔE, luminance, home advantage, shirt
/// name) are written, because a reader outside this repository has no way to recompute them — but
/// they are recomputed on the way back in, so the document can never reintroduce a stale one.</para>
/// </summary>
public static class WorldJsonWriter
{
    public const string FileName = "terraparalela_base_de_mundo.json";

    public static string Write(WorldSnapshot world)
    {
        var root = new JsonObject
        {
            ["meta"] = new JsonObject
            {
                [WorldMeta.MasterSeedKey] = world.Meta.MasterSeed,
                [WorldMeta.SchemaVersionKey] = world.Meta.SchemaVersion,
                [WorldMeta.SourceFileKey] = world.Meta.SourceFile,
            },
            ["calibration"] = Calibration(world.Calibration),
            ["positionWeights"] = PositionWeights(world.Calibration),
            ["geoNodes"] = Array(world.GeoNodes, GeoNode),
            ["sources"] = Array(world.Sources, Source),
            ["competitions"] = Array(world.Competitions, Competition),
            ["clubs"] = Array(world.Clubs, Club),
            ["players"] = Array(world.Characters, Character),
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonArray Array<T>(IReadOnlyList<T> items, Func<T, JsonNode> map)
    {
        var array = new JsonArray();
        foreach (T item in items)
            array.Add(map(item));
        return array;
    }

    private static JsonNode GeoNode(GeoNode node) => new JsonObject
    {
        ["geoNodeId"] = node.GeoNodeId,
        ["kind"] = node.Kind.ToString(),
        ["parentId"] = node.ParentId,
        ["displayName"] = node.DisplayName,
    };

    private static JsonNode Source(WorldSource source) => new JsonObject
    {
        ["tema"] = source.Tema,
        ["numero"] = source.Numero,
        ["fonte"] = source.Fonte,
        ["url"] = source.Url,
    };

    private static JsonNode Competition(Competition competition) => new JsonObject
    {
        ["competitionId"] = competition.CompetitionId,
        ["name"] = competition.Name,
        ["scope"] = competition.Scope.ToString(),
        ["anchorGeoNodeId"] = competition.AnchorGeoNodeId,
        ["memberPredicateId"] = competition.MemberPredicateId,
        ["prestigeBand"] = competition.PrestigeBand.ToString(),
        ["leagueTierFloat"] = competition.LeagueTierFloat,
        ["format"] = competition.Format,
        ["clubCount"] = competition.ClubCount,
        ["rounds"] = competition.Rounds,
        ["promotedIn"] = competition.PromotedIn,
        ["relegatedOut"] = competition.RelegatedOut,
        ["continentalSlots"] = competition.ContinentalSlots,
        ["editionId"] = competition.EditionId,
        ["season"] = competition.Season,
        ["memberClubIds"] = Array(competition.MemberClubIds, id => (JsonNode)JsonValue.Create(id)!),
    };

    private static JsonNode Club(ClubIdentity club) => new JsonObject
    {
        ["clubId"] = club.ClubId,
        ["displayCode"] = club.DisplayCode,
        ["identity"] = new JsonObject
        {
            ["officialName"] = club.Identity.OfficialName,
            ["shortName"] = club.Identity.ShortName,
            ["nickname"] = club.Identity.Nickname,
            ["foundingYear"] = club.Identity.FoundingYear,
        },
        ["geography"] = new JsonObject
        {
            ["cityName"] = club.Geography.CityName,
            ["uf"] = club.Geography.Uf,
            ["countryId"] = club.Geography.CountryId,
            ["geoNodeId"] = club.Geography.GeoNodeId,
            ["districtArchetype"] = club.Geography.DistrictArchetype.ToString(),
        },
        ["world"] = new JsonObject
        {
            ["prestigeBand"] = club.World.PrestigeBand.ToString(),
            ["clubStrength"] = club.World.ClubStrength,
            ["squadSize"] = club.World.SquadSize,
            ["namingRule"] = club.World.NamingRule.ToString(),
        },
        ["crest"] = new JsonObject
        {
            ["shieldShape"] = club.Crest.ShieldShape.ToString(),
            ["centralCharge"] = club.Crest.CentralCharge,
            ["motto"] = club.Crest.Motto,
            ["colors"] = Array(club.Crest.Colors, colour => (JsonNode)JsonValue.Create(colour)!),
        },
        ["palette"] = new JsonObject
        {
            ["primary"] = club.Palette.Primary,
            ["secondary"] = club.Palette.Secondary,
            ["tertiary"] = club.Palette.Tertiary,
            ["typographyStyle"] = club.Palette.TypographyStyle.ToString(),
        },
        ["kits"] = new JsonObject
        {
            ["collarStyle"] = club.Kits.CollarStyle.ToString(),
            ["fitStyle"] = club.Kits.FitStyle.ToString(),
            ["home"] = new JsonObject
            {
                ["fabricPattern"] = club.Kits.Home.FabricPattern.ToString(),
                ["shirt"] = club.Kits.Home.Shirt,
                ["shorts"] = club.Kits.Home.Shorts,
                ["socks"] = club.Kits.Home.Socks,
                ["luminance"] = club.Kits.Home.Luminance,
            },
            ["away"] = new JsonObject
            {
                ["fabricPattern"] = club.Kits.Away.FabricPattern.ToString(),
                ["shirt"] = club.Kits.Away.Shirt,
                ["shorts"] = club.Kits.Away.Shorts,
                ["socks"] = club.Kits.Away.Socks,
            },
            ["deltaE"] = club.Kits.DeltaE,
            ["deltaEThreshold"] = club.Kits.DeltaEThreshold,
            ["polarityRule"] = club.Kits.PolarityRule,
        },
        ["stadium"] = new JsonObject
        {
            ["name"] = club.Stadium.Name,
            ["capacity"] = club.Stadium.Capacity,
            ["atmosphereArchetype"] = club.Stadium.AtmosphereArchetype.ToString(),
            ["pitchSurface"] = club.Stadium.PitchSurface.ToString(),
        },
        ["aiProfile"] = new JsonObject
        {
            ["defaultTacticalStyle"] = club.AiProfile.DefaultTacticalStyle.ToString(),
            ["tacticalStyleProvenance"] = club.AiProfile.TacticalStyleProvenance.ToString(),
            ["homeAdvantageModifier"] = club.AiProfile.HomeAdvantageModifier,
            ["derbyRivalClubId"] = club.AiProfile.DerbyRivalClubId,
        },
        ["audit"] = ClubAudit(club.Audit),
    };

    private static JsonNode ClubAudit(ClubDeviationAudit audit) => new JsonObject
    {
        ["clubId"] = audit.ClubId,
        ["anchorClubName"] = audit.AnchorClubName,
        ["anchorCityName"] = audit.AnchorCityName,
        ["anchorFoundingYear"] = audit.AnchorFoundingYear,
        ["generatedFoundingYear"] = audit.GeneratedFoundingYear,
        ["foundingDecadePreserved"] = audit.FoundingDecadePreserved,
        ["foundingSourceCitation"] = audit.FoundingSourceCitation,
        ["anchorNickname"] = audit.AnchorNickname,
        ["nicknameCommercialLevel"] = audit.NicknameCommercialLevel,
        ["nicknameTrademarked"] = audit.NicknameTrademarked ? 1 : 0,
        ["nicknameEvidence"] = audit.NicknameEvidence,
        ["namingRule"] = audit.NamingRule.ToString(),
        ["namingRuleReason"] = audit.NamingRuleReason,
        ["phoneticSimilarity"] = audit.PhoneticSimilarity,
        ["crest_originalChargeReplaced"] = audit.CrestOriginalChargeReplaced,
        ["crest_substituteCharge"] = audit.CrestSubstituteCharge,
        ["crest_sourceCitation"] = audit.CrestSourceCitation,
        ["districtSourceCitation"] = audit.DistrictSourceCitation,
        ["tacticalStyleProvenance"] = audit.TacticalStyleProvenance.ToString(),
        ["tacticalStyleEvidence"] = audit.TacticalStyleEvidence,
        ["chromaticPolicy"] = audit.ChromaticPolicy,
        ["anchorFactsVerified"] = audit.AnchorFactsVerified ? 1 : 0,
        ["reviewedBy"] = audit.ReviewedBy,
        ["reviewDate"] = audit.ReviewDate,
        ["note"] = audit.Note,
    };

    private static JsonNode Character(CharacterRecord player)
    {
        var attrs = new JsonObject();
        foreach (Attr attr in Enum.GetValues<Attr>())
            attrs[attr.ToString()] = player.Attrs[attr];

        return new JsonObject
        {
            ["playerId"] = player.PlayerId,
            ["clubId"] = player.ClubId,
            ["shirtNumber"] = player.ShirtNumber,
            ["firstName"] = player.FirstName,
            ["lastName"] = player.LastName,
            ["shirtName"] = player.ShirtName,
            ["nationality"] = player.Nationality,
            ["secondNationality"] = player.SecondNationality,
            ["dateOfBirth"] = CsvValue.Date(player.DateOfBirth),
            ["age"] = player.Age,
            ["phase"] = player.Phase.ToString(),
            ["squadRole"] = player.SquadRole.ToString(),
            ["primaryPosition"] = player.PrimaryPosition.ToString(),
            // Pipe-separated, the way the source document spells it; null when there are none, so
            // an empty string never becomes a position named "".
            ["secondaryPositions"] = player.SecondaryPositions.Count == 0
                ? null
                : Csv.JoinArray(player.SecondaryPositions.Select(position => position.ToString())),
            ["preferredFoot"] = player.PreferredFoot.ToString(),
            ["weakFootRating"] = player.WeakFootRating,
            ["skillMovesRating"] = player.SkillMovesRating,
            ["height"] = player.Height,
            ["buildType"] = player.BuildType.ToString(),
            ["attrs"] = attrs,
            ["potentialGap"] = player.PotentialGap,
            ["provenance"] = player.Provenance.ToString(),
            ["overall"] = player.Overall,
            ["potentialOverall"] = player.PotentialOverall,
            ["marketValueEUR"] = player.MarketValueEur,
            ["salaryMonthlyBRL"] = player.SalaryMonthlyBrl,
            ["audit"] = new JsonObject
            {
                ["anchorPlayerName"] = player.Audit.AnchorPlayerName,
                ["anchorNationality"] = player.Audit.AnchorNationality,
                ["deviationFromSurname"] = player.Audit.DeviationFromSurname,
                ["generatedSurname"] = player.Audit.GeneratedSurname,
                ["phoneticSimilarity"] = player.Audit.PhoneticSimilarity,
                ["deviationMethod"] = player.Audit.DeviationMethod,
                ["anchorFactsVerified"] = player.Audit.AnchorFactsVerified ? 1 : 0,
            },
        };
    }

    private static JsonNode Calibration(WorldCalibration calibration)
    {
        var constants = new JsonObject();
        foreach ((string key, CalibrationConstant constant) in calibration.Constants)
        {
            constants[key] = new JsonObject
            {
                ["value"] = constant.Value,
                ["unit"] = constant.Unit,
                ["note"] = constant.Note,
            };
        }

        var ageMult = new JsonArray();
        foreach (AgeMultStep step in calibration.AgeMult)
            ageMult.Add(new JsonArray(step.Age, step.Multiplier));

        var bands = new JsonObject();
        foreach ((PrestigeBand band, PrestigeBandCalibration entry) in calibration.Bands)
        {
            bands[band.ToString()] = new JsonObject
            {
                ["valueMult"] = entry.ValueMult,
                ["capMean"] = entry.CapMean,
                ["capSd"] = entry.CapSd,
                ["n"] = entry.N,
            };
        }

        var homeAdv = new JsonObject();
        foreach ((AtmosphereArchetype atmosphere, double advantage) in calibration.HomeAdv)
            homeAdv[atmosphere.ToString()] = advantage;

        var stadiumProfile = new JsonObject();
        foreach ((string countryId, StadiumProfileEntry entry) in calibration.StadiumProfile)
        {
            stadiumProfile[countryId] = new JsonObject
            {
                ["mean"] = entry.Mean,
                ["sd"] = entry.Sd,
                ["min"] = entry.Min,
                ["max"] = entry.Max,
            };
        }

        return new JsonObject
        {
            ["constants"] = constants,
            ["ageMult"] = ageMult,
            ["bands"] = bands,
            ["homeAdv"] = homeAdv,
            ["stadiumProfile"] = stadiumProfile,
        };
    }

    /// <summary>A sibling of <c>calibration</c> in the document, not a child of it — the reader
    /// takes it from the root, and the two have to agree.</summary>
    private static JsonNode PositionWeights(WorldCalibration calibration)
    {
        var weights = new JsonObject();
        foreach ((Position position, IReadOnlyDictionary<Attr, double> row) in calibration.PositionWeights)
        {
            var attrs = new JsonObject();
            foreach (Attr attr in Enum.GetValues<Attr>())
                attrs[attr.ToString()] = row[attr];

            weights[position.ToString()] = attrs;
        }

        return weights;
    }
}
