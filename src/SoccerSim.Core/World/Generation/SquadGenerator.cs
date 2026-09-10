using SoccerSim.Core.Random;

namespace SoccerSim.Core.World.Generation;

/// <summary>
/// Builds a full squad for a club from its strength and a seed (ALGORITHMS.md §6).
///
/// <para><b>On randomness.</b> The prototype used mulberry32 because it ran in a browser. This
/// uses <see cref="DeterministicRng.CreateStream"/> over SplitMix64, which the repository requires
/// of all simulation randomness. The squads therefore do NOT match the prototype's player for
/// player, and are not meant to: the contract is "the same seed produces the same squad within
/// this implementation", not parity with a browser PRNG. Reproducing mulberry32 in C# to make the
/// numbers line up would trade a reproducible, cross-platform generator for a cosmetic match.</para>
///
/// <para><b>On safety.</b> Every generated player is <see cref="Provenance.Regen"/> with no anchor
/// and <c>AnchorFactsVerified = false</c>. A generated player therefore asserts nothing about any
/// real person — which is the entire reason generation is allowed to invent freely. That property
/// is covered by a test, and the test is a safety test, not a style one (§6.10).</para>
/// </summary>
public static class SquadGenerator
{
    /// <summary>The reference date the contract fixes ages against (DATA_CONTRACT.md §4).</summary>
    private static readonly DateOnly ReferenceDate = new(2026, 1, 28);

    private const int Starters = 11;
    private const int Rotation = 7;
    private const int Prospects = 5;

    /// <summary>How far below the target overall each role is aimed (§6.3).</summary>
    private static readonly IReadOnlyDictionary<SquadRole, int> RoleDelta = new Dictionary<SquadRole, int>
    {
        [SquadRole.Titular] = 0,
        [SquadRole.Rotacao] = -3,
        [SquadRole.Reserva] = -6,
        [SquadRole.Promessa] = -11,
    };

    private static readonly IReadOnlyDictionary<Position, (int Min, int Max)> HeightRange =
        new Dictionary<Position, (int, int)>
        {
            [Position.GK] = (186, 199), [Position.CB] = (182, 196), [Position.FB] = (170, 183),
            [Position.DM] = (175, 189), [Position.CM] = (172, 186), [Position.AM] = (168, 182),
            [Position.WG] = (166, 181), [Position.ST] = (174, 192),
        };

    /// <summary>Build types in order of preference for each position, weighted 3:2:1.</summary>
    private static readonly IReadOnlyDictionary<Position, BuildType[]> BuildBias =
        new Dictionary<Position, BuildType[]>
        {
            [Position.GK] = [BuildType.Stocky, BuildType.Athletic, BuildType.Balanced],
            [Position.CB] = [BuildType.Stocky, BuildType.Athletic, BuildType.Balanced],
            [Position.FB] = [BuildType.Athletic, BuildType.Lean, BuildType.Balanced],
            [Position.DM] = [BuildType.Stocky, BuildType.Balanced, BuildType.Athletic],
            [Position.CM] = [BuildType.Balanced, BuildType.Lean, BuildType.Athletic],
            [Position.AM] = [BuildType.Lean, BuildType.Balanced, BuildType.Athletic],
            [Position.WG] = [BuildType.Lean, BuildType.Athletic, BuildType.Balanced],
            [Position.ST] = [BuildType.Athletic, BuildType.Stocky, BuildType.Balanced],
        };

    private static readonly IReadOnlyDictionary<Position, Position[]> AdjacentPositions =
        new Dictionary<Position, Position[]>
        {
            [Position.GK] = [],
            [Position.CB] = [Position.FB, Position.DM],
            [Position.FB] = [Position.WG, Position.CB],
            [Position.DM] = [Position.CM, Position.CB],
            [Position.CM] = [Position.DM, Position.AM],
            [Position.AM] = [Position.CM, Position.WG],
            [Position.WG] = [Position.AM, Position.ST],
            [Position.ST] = [Position.AM, Position.WG],
        };

    /// <summary>The numbers a position takes first; anything else falls back to 12–99.</summary>
    private static readonly IReadOnlyDictionary<Position, int[]> PreferredNumbers =
        new Dictionary<Position, int[]>
        {
            [Position.GK] = [1, 12, 22, 23],
            [Position.CB] = [3, 4, 13, 14, 26],
            [Position.FB] = [2, 6, 16, 21, 24],
            [Position.DM] = [5, 8, 15, 18, 25],
            [Position.CM] = [8, 10, 17, 20, 28],
            [Position.AM] = [10, 20, 27, 29],
            [Position.WG] = [7, 11, 19, 30, 37],
            [Position.ST] = [9, 99, 33, 39, 77],
        };

    /// <summary>
    /// The nationality mix observed in the current batch. Explicitly Brazil-only: another league
    /// needs its own distribution, with a source (§6.8). A country without one cannot be
    /// populated, and the tool should say so rather than quietly making everyone Brazilian.
    /// </summary>
    private static readonly IReadOnlyList<(string Code, double Weight)> NationalityPool =
    [
        ("BRA", 545), ("ARG", 47), ("COL", 22), ("URU", 21), ("PAR", 16), ("CHI", 10),
        ("VEN", 8), ("ECU", 7), ("PER", 4), ("ITA", 2), ("ESP", 1), ("NED", 1),
        ("DEN", 1), ("POR", 1), ("ANG", 1), ("FRA", 1),
    ];

    private static int AgeShift(AgeProfile profile) => profile switch
    {
        AgeProfile.Young => -3,
        AgeProfile.Experienced => 3,
        _ => 0,
    };

    /// <summary>
    /// Generates a complete squad. Pure: no database, no clock, no ambient state — the same
    /// arguments always produce the same players.
    /// </summary>
    public static IReadOnlyList<CharacterRecord> Generate(
        ClubIdentity club,
        SquadGenerationOptions options,
        GenerationProfiles profiles,
        WorldCalibration calibration,
        long masterSeed)
    {
        if (profiles.IsEmpty)
        {
            throw new InvalidOperationException(
                "No generation profiles are loaded. Run `worldbuilder import-profiles <file>` before "
                + "generating a squad — the attribute shapes and name pools are measured data, not defaults.");
        }

        // An isolated stream per club and seed: regenerating one club cannot disturb another's
        // numbers, and the same (club, seed) pair always lands on the same stream.
        IDeterministicRandom rng = DeterministicRng.CreateStream(
            (ulong)masterSeed,
            Hash(club.ClubId),
            Hash("squad"),
            unchecked((ulong)options.Seed));

        int size = Math.Clamp(options.SquadSize, SquadShape.MinSquadSize, SquadShape.MaxSquadSize);
        IReadOnlyDictionary<Position, int> counts = SquadShape.CompositionFor(size, options.Formation);
        List<(Position Position, SquadRole Role)> assignments = AssignRoles(counts, options.Formation);

        var context = new GenerationContext(club, options, profiles, calibration, rng);
        var squad = new List<CharacterRecord>(assignments.Count);

        for (int index = 0; index < assignments.Count; index++)
        {
            (Position position, SquadRole role) = assignments[index];
            squad.Add(context.CreatePlayer(position, role, index + 1));
        }

        return squad;
    }

    /// <summary>
    /// Who starts, who rotates, who is a prospect. The formation's eleven are the starters; the
    /// rest are dealt rotation, then reserves, then prospects, in that order.
    /// </summary>
    private static List<(Position, SquadRole)> AssignRoles(
        IReadOnlyDictionary<Position, int> counts,
        Formation formation)
    {
        var pool = new List<Position>();
        foreach ((Position position, int count) in counts.OrderBy(entry => entry.Key))
        {
            for (int i = 0; i < count; i++)
                pool.Add(position);
        }

        var assignments = new List<(Position, SquadRole)>(pool.Count);

        // The eleven come out of the pool first, so a starter is never also counted as cover.
        foreach ((Position position, int needed) in SquadShape.Starters(formation).OrderBy(entry => entry.Key))
        {
            for (int i = 0; i < needed; i++)
            {
                pool.Remove(position);
                assignments.Add((position, SquadRole.Titular));
            }
        }

        var roles = new List<SquadRole>();
        roles.AddRange(Enumerable.Repeat(SquadRole.Rotacao, Rotation));
        roles.AddRange(Enumerable.Repeat(SquadRole.Reserva, Math.Max(0, pool.Count - Rotation - Prospects)));
        roles.AddRange(Enumerable.Repeat(SquadRole.Promessa, Prospects));

        // A small squad runs out of places before it runs out of roles, and a large one the other
        // way round; reserves absorb the difference either way.
        while (roles.Count > pool.Count)
            roles.RemoveAt(roles.Count - 1);
        while (roles.Count < pool.Count)
            roles.Add(SquadRole.Reserva);

        for (int i = 0; i < pool.Count; i++)
            assignments.Add((pool[i], roles[i]));

        return assignments;
    }

    /// <summary>A stable 64-bit hash of a string, for deriving stream keys. FNV-1a: tiny, and
    /// fixed forever — unlike <see cref="string.GetHashCode()"/>, which is randomised per process
    /// and would make every run produce a different squad.</summary>
    private static ulong Hash(string value)
    {
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;

        ulong hash = offsetBasis;
        foreach (char c in value)
        {
            hash ^= c;
            hash *= prime;
        }

        return hash;
    }

    /// <summary>Holds the per-squad state that must not leak between clubs: the numbers and names
    /// already taken.</summary>
    private sealed class GenerationContext
    {
        private readonly ClubIdentity _club;
        private readonly SquadGenerationOptions _options;
        private readonly GenerationProfiles _profiles;
        private readonly WorldCalibration _calibration;
        private readonly IDeterministicRandom _rng;
        private readonly HashSet<int> _usedNumbers = [];
        private readonly HashSet<string> _usedNames = [];
        private readonly string _slug;

        public GenerationContext(
            ClubIdentity club,
            SquadGenerationOptions options,
            GenerationProfiles profiles,
            WorldCalibration calibration,
            IDeterministicRandom rng)
        {
            _club = club;
            _options = options;
            _profiles = profiles;
            _calibration = calibration;
            _rng = rng;
            _slug = Slug(club.ClubId);
        }

        public CharacterRecord CreatePlayer(Position position, SquadRole role, int index)
        {
            (string firstName, string lastName) = TakeName(index);
            int age = AgeFor(role);
            Phase phase = PhaseForAge(age);

            // The role target is what the player is AIMED at. The overall is what the attributes
            // actually produce once the position's weights are applied — the target is an input,
            // never the answer (§6.5).
            double roleTarget = _options.TargetOverall + RoleDelta[role] + (_rng.NextGaussian() * 3);
            IReadOnlyDictionary<Attr, int> attrs = SampleAttributes(position, roleTarget);

            int potentialGap = phase switch
            {
                Phase.Prospect => _rng.NextInt(3, 12),
                Phase.Breakthrough => _rng.NextInt(0, 7),
                _ => 0,
            };

            var character = new CharacterRecord(
                PlayerId: $"plr_gen_{_slug}_{index:D4}",
                ClubId: _club.ClubId,
                ShirtNumber: TakeNumber(position),
                FirstName: firstName,
                LastName: lastName,
                ShirtName: string.Empty,          // derived on write
                Nationality: _rng.Weighted(NationalityPool),
                SecondNationality: _rng.NextDouble() < 0.06 ? _rng.Weighted(NationalityPool) : null,
                DateOfBirth: BirthDateFor(age),
                Age: age,
                Phase: phase,
                SquadRole: role,
                PrimaryPosition: position,
                SecondaryPositions: SecondaryFor(position),
                PreferredFoot: _rng.NextDouble() < 0.75 ? PreferredFoot.Right : PreferredFoot.Left,
                WeakFootRating: position == Position.GK ? _rng.NextInt(1, 4) : _rng.NextInt(2, 5),
                SkillMovesRating: SkillMovesFor(position),
                Height: HeightFor(position),
                BuildType: _rng.Weighted(BuildBias[position].Select((build, order) => (build, (double)(3 - order))).ToList()),
                Attrs: attrs,
                PotentialGap: potentialGap,
                Provenance: Provenance.Regen,
                Overall: 0,                       // derived below
                PotentialOverall: 0,
                MarketValueEur: 0,
                SalaryMonthlyBrl: 0,
                Audit: new CharacterDeviationAudit(
                    // No anchor at all: a generated player makes no claim about a real person.
                    AnchorPlayerName: null,
                    AnchorNationality: null,
                    DeviationFromSurname: null,
                    GeneratedSurname: lastName,
                    PhoneticSimilarity: null,
                    DeviationMethod: "N/A — Regen (sem âncora)",
                    AnchorFactsVerified: false));

            // The same derivation the repository applies on write, so a previewed player and a
            // saved one carry identical numbers.
            return WorldDerivations.Recalculate(character, _club.World.PrestigeBand, _calibration);
        }

        private IReadOnlyDictionary<Attr, int> SampleAttributes(Position position, double roleTarget)
        {
            IReadOnlyDictionary<Attr, AttributeProfile> profile = _profiles.Attributes[position];
            var attrs = new Dictionary<Attr, int>();

            foreach (Attr attr in Enum.GetValues<Attr>())
            {
                AttributeProfile shape = profile[attr];

                // The 0.55 narrows the spread relative to the measured data on purpose: generated
                // squads come out more coherent than hand-sampled ones (§6.5).
                double value = roleTarget + shape.OffsetMean + (_rng.NextGaussian() * shape.StdDev * 0.55);
                attrs[attr] = Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 1, 99);
            }

            return attrs;
        }

        private int AgeFor(SquadRole role)
        {
            int shift = AgeShift(_options.AgeProfile);
            int age = role switch
            {
                SquadRole.Promessa => _rng.NextInt(17, 21),
                SquadRole.Titular => _rng.NextInt(24, 33) + shift,
                SquadRole.Rotacao => _rng.NextInt(22, 34) + shift,
                _ => _rng.NextInt(20, 35) + shift,
            };

            // The occasional veteran, everywhere except among the prospects.
            if (role != SquadRole.Promessa && _rng.NextDouble() < 0.07)
                age = _rng.NextInt(33, 38);

            return Math.Clamp(age, 17, 40);
        }

        private static Phase PhaseForAge(int age) => age switch
        {
            <= 20 => Phase.Prospect,
            <= 26 => Phase.Breakthrough,
            <= 32 => Phase.Prime,
            <= 38 => Phase.Veteran,
            _ => Phase.Twilight,
        };

        /// <summary>A birthday consistent with the age at the contract's reference date.</summary>
        private DateOnly BirthDateFor(int age)
        {
            int month = _rng.NextInt(1, 13);
            int day = _rng.NextInt(1, 29);   // 28 days keeps every month valid

            bool birthdayPassed = month < ReferenceDate.Month
                || (month == ReferenceDate.Month && day <= ReferenceDate.Day);

            return new DateOnly(ReferenceDate.Year - age - (birthdayPassed ? 0 : 1), month, day);
        }

        private int HeightFor(Position position)
        {
            (int min, int max) = HeightRange[position];
            double centre = (min + max) / 2.0;
            double height = centre + (_rng.NextGaussian() * ((max - min) / 4.0));
            return Math.Clamp((int)Math.Round(height, MidpointRounding.AwayFromZero), min, max);
        }

        private int SkillMovesFor(Position position) => position switch
        {
            Position.GK => 1,
            Position.AM or Position.WG or Position.ST => _rng.NextInt(3, 6),
            _ => _rng.NextInt(1, 4),
        };

        private IReadOnlyList<Position> SecondaryFor(Position position)
        {
            Position[] adjacent = AdjacentPositions[position];
            if (adjacent.Length == 0 || _rng.NextDouble() >= 0.35)
                return [];

            return [_rng.Pick(adjacent)];
        }

        /// <summary>The position's preferred numbers first, then any free number from 12 up.
        /// Shirt numbers are unique within a club, and the schema enforces it.</summary>
        private int TakeNumber(Position position)
        {
            foreach (int number in PreferredNumbers[position])
            {
                if (_usedNumbers.Add(number))
                    return number;
            }

            for (int number = 12; number <= 99; number++)
            {
                if (_usedNumbers.Add(number))
                    return number;
            }

            throw new InvalidOperationException(
                $"No shirt number is free for {position}: a squad cannot exceed 99 players.");
        }

        /// <summary>
        /// A name pair not already in this squad. After enough collisions the surname is suffixed
        /// rather than looping forever — with 108 first names and 338 surnames a 40-player squad
        /// effectively never gets here, but "effectively never" is not a termination condition.
        /// </summary>
        private (string First, string Last) TakeName(int index)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                string first = _rng.Pick(_profiles.FirstNames);
                string last = _rng.Pick(_profiles.LastNames);

                if (_usedNames.Add($"{first} {last}"))
                    return (first, last);
            }

            return (_rng.Pick(_profiles.FirstNames), $"{_rng.Pick(_profiles.LastNames)} {index}");
        }

        private static string Slug(string clubId)
        {
            string trimmed = clubId.StartsWith("clb_", StringComparison.Ordinal) ? clubId[4..] : clubId;
            string cleaned = new(trimmed.Where(char.IsLetterOrDigit).ToArray());
            return cleaned.Length > 12 ? cleaned[..12] : cleaned;
        }
    }
}
