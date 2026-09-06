namespace SoccerSim.Core.World.Fields;

/// <summary>
/// The player modal's editable surface: identity, role, the twelve attributes, the economy the
/// tool computes, and the deviation audit.
///
/// <para>
/// The economy group is shown but never patched — overall, potential, market value and salary all
/// follow from the attributes, the age and the club's band. Editing an attribute moves them; the
/// modal shows the new numbers, which is the point of surfacing them at all.
/// </para>
/// </summary>
public static class CharacterFields
{
    /// <summary>The attribute group is built from the <see cref="Attr"/> enum rather than listed,
    /// so a thirteenth attribute appears in the form the moment the schema has it.</summary>
    public const string AttributeGroupId = "attrs";

    public static IReadOnlyList<WorldFieldGroup> Groups { get; } = BuildGroups();

    public static IReadOnlyDictionary<string, WorldField> ByPath { get; } =
        Groups.SelectMany(group => group.Fields).ToDictionary(field => field.Path);

    public static CharacterRecord Apply(CharacterRecord character, string path, string? value)
    {
        // Attributes are addressed as attrs.<Attr>, e.g. attrs.Finishing.
        if (path.StartsWith("attrs.", StringComparison.Ordinal))
            return ApplyAttribute(character, path, value);

        if (!Setters.TryGetValue(path, out Func<CharacterRecord, string?, CharacterRecord>? setter))
        {
            throw ByPath.TryGetValue(path, out WorldField? known)
                ? new FieldPatchException(path, $"'{known.Label}' is {known.Provenance.ToString().ToLowerInvariant()} and is recalculated on write")
                : new FieldPatchException(path, "unknown field");
        }

        return setter(character, value);
    }

    public static string? Read(CharacterRecord character, string path)
    {
        if (path.StartsWith("attrs.", StringComparison.Ordinal))
        {
            Attr attr = FieldValue.Enum<Attr>(path, path["attrs.".Length..]);
            return FieldValue.Format(character.Attrs[attr]);
        }

        return Readers.TryGetValue(path, out Func<CharacterRecord, string?>? reader)
            ? reader(character)
            : throw new FieldPatchException(path, "unknown field");
    }

    private static CharacterRecord ApplyAttribute(CharacterRecord character, string path, string? value)
    {
        Attr attr = FieldValue.Enum<Attr>(path, path["attrs.".Length..]);
        var attrs = new Dictionary<Attr, int>(character.Attrs)
        {
            [attr] = FieldValue.IntInRange(path, value, 1, 99),
        };

        return character with { Attrs = attrs };
    }

    private static IReadOnlyList<WorldFieldGroup> BuildGroups() =>
    [
        new WorldFieldGroup("Identidade", "identity",
        [
            new("firstName", "Nome", FieldKind.Text, FieldProvenance.Authored),
            new("lastName", "Sobrenome", FieldKind.Text, FieldProvenance.Authored),
            new("shirtName", "Nome na camisa", FieldKind.Text, FieldProvenance.Derived, Editable: false),
            new("shirtNumber", "Camisa", FieldKind.Int, FieldProvenance.Sampled),
            new("nationality", "Nacionalidade", FieldKind.Text, FieldProvenance.Authored),
            new("secondNationality", "Segunda nacionalidade", FieldKind.Text, FieldProvenance.Authored),
            new("dateOfBirth", "Nascimento", FieldKind.Text, FieldProvenance.Sampled),
            new("age", "Idade", FieldKind.Int, FieldProvenance.Derived),
            new("height", "Altura (cm)", FieldKind.Int, FieldProvenance.Sampled),
            new("buildType", "Compleição", FieldKind.Enum, FieldProvenance.Sampled, EnumName: nameof(BuildType)),
            new("preferredFoot", "Pé preferido", FieldKind.Enum, FieldProvenance.Sampled, EnumName: nameof(PreferredFoot)),
            new("weakFootRating", "Pé ruim (1–5)", FieldKind.Int, FieldProvenance.Sampled),
            new("skillMovesRating", "Dribles (1–5)", FieldKind.Int, FieldProvenance.Sampled),
        ]),

        new WorldFieldGroup("Papel", "role",
        [
            new("primaryPosition", "Posição principal", FieldKind.Enum, FieldProvenance.Sampled, EnumName: nameof(Position)),
            new("secondaryPositions", "Posições secundárias", FieldKind.Text, FieldProvenance.Sampled),
            new("squadRole", "Papel no elenco", FieldKind.Enum, FieldProvenance.Derived, EnumName: nameof(SquadRole)),
            new("phase", "Fase de carreira", FieldKind.Enum, FieldProvenance.Derived, EnumName: nameof(Phase)),
            new("provenance", "Proveniência", FieldKind.Enum, FieldProvenance.Authored, EnumName: nameof(Provenance)),
            new("potentialGap", "Gap de potencial", FieldKind.Int, FieldProvenance.Sampled),
        ]),

        new WorldFieldGroup("Atributos", AttributeGroupId,
        [
            .. Enum.GetValues<Attr>().Select(attr =>
                new WorldField($"attrs.{attr}", attr.ToString(), FieldKind.Int, FieldProvenance.Sampled)),
        ]),

        new WorldFieldGroup("Economia", "economy",
        [
            new("overall", "OVR", FieldKind.Int, FieldProvenance.Calculated, Editable: false),
            new("potentialOverall", "Potencial", FieldKind.Int, FieldProvenance.Calculated, Editable: false),
            new("marketValueEur", "Valor (EUR)", FieldKind.Int, FieldProvenance.Calculated, Editable: false),
            new("salaryMonthlyBrl", "Salário mensal (BRL)", FieldKind.Int, FieldProvenance.Calculated, Editable: false),
        ]),

        new WorldFieldGroup("Auditoria", "audit",
        [
            new("audit.anchorPlayerName", "Jogador âncora", FieldKind.Text, FieldProvenance.Authored),
            new("audit.anchorNationality", "Nacionalidade da âncora", FieldKind.Text, FieldProvenance.Authored),
            new("audit.deviationFromSurname", "Sobrenome da âncora", FieldKind.Text, FieldProvenance.Authored),
            new("audit.generatedSurname", "Sobrenome gerado", FieldKind.Text, FieldProvenance.Derived),
            new("audit.phoneticSimilarity", "Similaridade fonética", FieldKind.Float, FieldProvenance.Calculated),
            new("audit.deviationMethod", "Método de desvio", FieldKind.Text, FieldProvenance.Authored),
            new("audit.anchorFactsVerified", "Fatos verificados (1/0)", FieldKind.Int, FieldProvenance.Authored),
        ]),
    ];

    private static readonly Dictionary<string, Func<CharacterRecord, string?, CharacterRecord>> Setters = new()
    {
        ["firstName"] = (character, value) => character with { FirstName = FieldValue.RequireText("firstName", value) },
        ["lastName"] = (character, value) => character with { LastName = FieldValue.RequireText("lastName", value) },
        ["shirtNumber"] = (character, value) => character with { ShirtNumber = FieldValue.IntInRange("shirtNumber", value, 1, 99) },
        ["nationality"] = (character, value) => character with { Nationality = FieldValue.RequireText("nationality", value) },
        ["secondNationality"] = (character, value) => character with { SecondNationality = FieldValue.OptionalText(value) },
        ["dateOfBirth"] = (character, value) => character with { DateOfBirth = FieldValue.Date("dateOfBirth", value) },
        ["age"] = (character, value) => character with { Age = FieldValue.IntInRange("age", value, 15, 50) },
        ["height"] = (character, value) => character with { Height = FieldValue.IntInRange("height", value, 140, 230) },
        ["buildType"] = (character, value) => character with { BuildType = FieldValue.Enum<BuildType>("buildType", value) },
        ["preferredFoot"] = (character, value) => character with { PreferredFoot = FieldValue.Enum<PreferredFoot>("preferredFoot", value) },
        ["weakFootRating"] = (character, value) => character with { WeakFootRating = FieldValue.IntInRange("weakFootRating", value, 1, 5) },
        ["skillMovesRating"] = (character, value) => character with { SkillMovesRating = FieldValue.IntInRange("skillMovesRating", value, 1, 5) },

        ["primaryPosition"] = (character, value) => character with { PrimaryPosition = FieldValue.Enum<Position>("primaryPosition", value) },
        ["secondaryPositions"] = (character, value) => character with { SecondaryPositions = ParsePositions(value) },
        ["squadRole"] = (character, value) => character with { SquadRole = FieldValue.Enum<SquadRole>("squadRole", value) },
        ["phase"] = (character, value) => character with { Phase = FieldValue.Enum<Phase>("phase", value) },
        ["provenance"] = (character, value) => character with { Provenance = FieldValue.Enum<Provenance>("provenance", value) },
        ["potentialGap"] = (character, value) => character with { PotentialGap = FieldValue.IntInRange("potentialGap", value, -20, 40) },

        ["audit.anchorPlayerName"] = (character, value) => character with { Audit = character.Audit with { AnchorPlayerName = FieldValue.OptionalText(value) } },
        ["audit.anchorNationality"] = (character, value) => character with { Audit = character.Audit with { AnchorNationality = FieldValue.OptionalText(value) } },
        ["audit.deviationFromSurname"] = (character, value) => character with { Audit = character.Audit with { DeviationFromSurname = FieldValue.OptionalText(value) } },
        ["audit.generatedSurname"] = (character, value) => character with { Audit = character.Audit with { GeneratedSurname = FieldValue.OptionalText(value) } },
        ["audit.phoneticSimilarity"] = (character, value) => character with { Audit = character.Audit with { PhoneticSimilarity = FieldValue.OptionalFloat("audit.phoneticSimilarity", value) } },
        ["audit.deviationMethod"] = (character, value) => character with { Audit = character.Audit with { DeviationMethod = FieldValue.OptionalText(value) } },
        ["audit.anchorFactsVerified"] = (character, value) => character with { Audit = character.Audit with { AnchorFactsVerified = FieldValue.Flag("audit.anchorFactsVerified", value) } },
    };

    private static readonly Dictionary<string, Func<CharacterRecord, string?>> Readers = new()
    {
        ["firstName"] = character => character.FirstName,
        ["lastName"] = character => character.LastName,
        ["shirtName"] = character => character.ShirtName,
        ["shirtNumber"] = character => FieldValue.Format(character.ShirtNumber),
        ["nationality"] = character => character.Nationality,
        ["secondNationality"] = character => character.SecondNationality,
        ["dateOfBirth"] = character => character.DateOfBirth.ToString("yyyy-MM-dd"),
        ["age"] = character => FieldValue.Format(character.Age),
        ["height"] = character => FieldValue.Format(character.Height),
        ["buildType"] = character => character.BuildType.ToString(),
        ["preferredFoot"] = character => character.PreferredFoot.ToString(),
        ["weakFootRating"] = character => FieldValue.Format(character.WeakFootRating),
        ["skillMovesRating"] = character => FieldValue.Format(character.SkillMovesRating),

        ["primaryPosition"] = character => character.PrimaryPosition.ToString(),
        ["secondaryPositions"] = character => character.SecondaryPositions.Count == 0
            ? null
            : string.Join('|', character.SecondaryPositions),
        ["squadRole"] = character => character.SquadRole.ToString(),
        ["phase"] = character => character.Phase.ToString(),
        ["provenance"] = character => character.Provenance.ToString(),
        ["potentialGap"] = character => FieldValue.Format(character.PotentialGap),

        ["overall"] = character => FieldValue.Format(character.Overall),
        ["potentialOverall"] = character => FieldValue.Format(character.PotentialOverall),
        ["marketValueEur"] = character => FieldValue.Format(character.MarketValueEur),
        ["salaryMonthlyBrl"] = character => FieldValue.Format(character.SalaryMonthlyBrl),

        ["audit.anchorPlayerName"] = character => character.Audit.AnchorPlayerName,
        ["audit.anchorNationality"] = character => character.Audit.AnchorNationality,
        ["audit.deviationFromSurname"] = character => character.Audit.DeviationFromSurname,
        ["audit.generatedSurname"] = character => character.Audit.GeneratedSurname,
        ["audit.phoneticSimilarity"] = character => character.Audit.PhoneticSimilarity is { } value ? FieldValue.Format(value) : null,
        ["audit.deviationMethod"] = character => character.Audit.DeviationMethod,
        ["audit.anchorFactsVerified"] = character => FieldValue.Format(character.Audit.AnchorFactsVerified),
    };

    /// <summary>Pipe-separated, the shape the source data uses ("AM|ST"). Empty clears them.</summary>
    private static IReadOnlyList<Position> ParsePositions(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => FieldValue.Enum<Position>("secondaryPositions", part))
            .ToList();
    }
}
