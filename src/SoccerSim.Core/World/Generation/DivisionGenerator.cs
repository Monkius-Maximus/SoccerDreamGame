using SoccerSim.Core.Random;
using SoccerSim.Core.World.Competitions;

namespace SoccerSim.Core.World.Generation;

/// <summary>
/// What the owner decides about a division's new clubs: how many, how prestigious (one band for
/// the batch, authored per D-38) and how strong, as a range the clubs are spread across.
/// <see cref="Seed"/> is the determinism contract for the whole batch.
/// </summary>
public sealed record DivisionGenerationRequest(
    string CountryId,
    string DivisionId,
    int ClubCount,
    PrestigeBand Band,
    double StrengthMin,
    double StrengthMax,
    long Seed);

/// <summary>
/// A generated batch: the clubs, their squads, and the pyramid with every club enrolled in the
/// requested division. Nothing has been written; persisting it is one transaction (Sprint 11b).
/// </summary>
public sealed record DivisionGenerationResult(
    IReadOnlyList<ClubIdentity> Clubs,
    IReadOnlyList<CharacterRecord> Characters,
    LeaguePyramid Pyramid);

/// <summary>
/// Fills a division with generated clubs (ADR-0011 §6, Sprint 11). Pure: no database, no clock,
/// no ambient state — the same arguments always produce the same batch.
///
/// <para>Each club comes from <see cref="ClubGenerator"/> and its squad from
/// <see cref="SquadGenerator"/>, the same two functions the single-club path uses, so a division
/// is nothing but clubs made the usual way. Clubs of one batch never collide with each other or
/// with the world: each is folded into the <see cref="ClubGenerationContext"/> before the next is
/// drawn.</para>
///
/// <para><b>Strength.</b> Spread evenly from <see cref="DivisionGenerationRequest.StrengthMax"/>
/// down to <see cref="DivisionGenerationRequest.StrengthMin"/>, rounded to two decimals as the
/// pilot league writes it. A division is a ladder, and a seeded spread would sometimes bunch the
/// whole field together.</para>
/// </summary>
public static class DivisionGenerator
{
    public static DivisionGenerationResult Generate(
        DivisionGenerationRequest request,
        LeaguePyramid pyramid,
        ClubProfiles clubProfiles,
        GenerationProfiles playerProfiles,
        CountryProfile country,
        IReadOnlyList<GeoNode> geoNodes,
        WorldCalibration calibration,
        IReadOnlyList<ClubIdentity> existingClubs,
        long masterSeed)
    {
        Division division = Validate(request, pyramid, country);

        IDeterministicRandom rng = DeterministicRng.CreateStream(
            (ulong)masterSeed,
            StableHash.Of("division"),
            StableHash.Of(request.CountryId),
            StableHash.Of(division.DivisionId),
            unchecked((ulong)request.Seed));

        ClubGenerationContext taken = ClubGenerationContext.From(existingClubs);
        var clubs = new List<ClubIdentity>(request.ClubCount);
        var characters = new List<CharacterRecord>();

        foreach (double strength in Strengths(request))
        {
            long seed = unchecked((long)rng.NextULong());

            ClubIdentity club = ClubGenerator.Generate(
                new ClubGenerationRequest(request.CountryId, request.Band, strength, seed),
                clubProfiles,
                geoNodes,
                calibration,
                taken,
                masterSeed);

            characters.AddRange(SquadGenerator.Generate(
                club,
                SquadGenerationOptions.For(club, seed),
                playerProfiles,
                calibration,
                masterSeed,
                country));

            pyramid = PyramidEditor.Enrol(pyramid, division.DivisionId, club.ClubId);
            taken = taken.With(club);
            clubs.Add(club);
        }

        return new DivisionGenerationResult(clubs, characters, pyramid);
    }

    /// <summary>The strengths the batch is drawn at, strongest first.</summary>
    public static IReadOnlyList<double> Strengths(DivisionGenerationRequest request)
    {
        if (request.ClubCount == 1)
            return [Math.Round((request.StrengthMin + request.StrengthMax) / 2, 2, MidpointRounding.AwayFromZero)];

        double step = (request.StrengthMax - request.StrengthMin) / (request.ClubCount - 1);
        return Enumerable.Range(0, request.ClubCount)
            .Select(i => Math.Round(request.StrengthMax - (i * step), 2, MidpointRounding.AwayFromZero))
            .ToList();
    }

    private static Division Validate(DivisionGenerationRequest request, LeaguePyramid pyramid, CountryProfile country)
    {
        if (pyramid.CountryId != request.CountryId)
        {
            throw new ArgumentException(
                $"The pyramid is {pyramid.CountryId}'s, but the division is requested in {request.CountryId}.",
                nameof(pyramid));
        }

        if (country.CountryId != request.CountryId)
        {
            throw new ArgumentException(
                $"The country profile describes {country.CountryId}, but the division is requested in {request.CountryId}.",
                nameof(country));
        }

        Division division = pyramid.Divisions.SingleOrDefault(d => d.DivisionId == request.DivisionId)
            ?? throw new ArgumentException(
                $"{request.CountryId} has no division '{request.DivisionId}' "
                + $"(there are: {string.Join(", ", pyramid.Divisions.Select(d => d.DivisionId))}).",
                nameof(request));

        int seats = division.ClubCount - division.ClubIds.Count;
        if (request.ClubCount < 1 || request.ClubCount > seats)
        {
            throw new ArgumentOutOfRangeException(nameof(request),
                $"{division.Name} has {seats} free seat(s) of {division.ClubCount}; "
                + $"cannot generate {request.ClubCount} club(s).");
        }

        if (request.StrengthMin <= 0 || request.StrengthMax > 1 || request.StrengthMin > request.StrengthMax)
        {
            throw new ArgumentOutOfRangeException(nameof(request),
                $"the strength range {request.StrengthMin}–{request.StrengthMax} must satisfy 0 < min ≤ max ≤ 1.");
        }

        return division;
    }
}
