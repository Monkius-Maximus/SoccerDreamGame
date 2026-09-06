using SoccerSim.Core.World;
using SoccerSim.Core.World.Fields;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// The catalog is the single source for what the form shows, what the server accepts and which
/// seal a field carries. These pin the properties that make that safe: every field is readable,
/// every editable field round-trips, and nothing the derivations own can be written by hand.
/// </summary>
public sealed class FieldCatalogTests
{
    [Fact]
    public void EveryClubField_CanBeRead()
    {
        ClubIdentity club = WorldSamples.Club();

        // A field the form would show but nothing can read is a blank box with a label.
        foreach (WorldField field in ClubFields.ByPath.Values)
            ClubFields.Read(club, field.Path);
    }

    [Fact]
    public void EveryEditableClubField_RoundTripsItsOwnValue()
    {
        ClubIdentity club = WorldSamples.Club();

        foreach (WorldField field in ClubFields.ByPath.Values.Where(field => field.Editable))
        {
            string? current = ClubFields.Read(club, field.Path);
            if (current is null)
                continue;   // nullable and unset: nothing to round-trip

            ClubIdentity applied = ClubFields.Apply(club, field.Path, current);

            Assert.Equal(current, ClubFields.Read(applied, field.Path));
        }
    }

    [Fact]
    public void EveryCharacterField_CanBeRead()
    {
        CharacterRecord character = WorldSamples.Character();

        foreach (WorldField field in CharacterFields.ByPath.Values)
            CharacterFields.Read(character, field.Path);
    }

    [Fact]
    public void EveryEditableCharacterField_RoundTripsItsOwnValue()
    {
        CharacterRecord character = WorldSamples.Character();

        foreach (WorldField field in CharacterFields.ByPath.Values.Where(field => field.Editable))
        {
            string? current = CharacterFields.Read(character, field.Path);
            if (current is null)
                continue;

            CharacterRecord applied = CharacterFields.Apply(character, field.Path, current);

            Assert.Equal(current, CharacterFields.Read(applied, field.Path));
        }
    }

    [Theory]
    // Everything WorldDerivations owns. If one of these becomes editable, a user can write a value
    // the next save silently overwrites — the worst kind of edit, because it looks like it worked.
    [InlineData("kits.deltaE")]
    [InlineData("kits.home.luminance")]
    [InlineData("kits.polarityRule")]
    [InlineData("aiProfile.homeAdvantageModifier")]
    [InlineData("crest.colors")]
    public void DerivedClubFields_AreListedButNotEditable(string path)
    {
        Assert.False(ClubFields.ByPath[path].Editable);
        Assert.Throws<FieldPatchException>(() => ClubFields.Apply(WorldSamples.Club(), path, "1"));
    }

    [Theory]
    [InlineData("shirtName")]
    [InlineData("overall")]
    [InlineData("potentialOverall")]
    [InlineData("marketValueEur")]
    [InlineData("salaryMonthlyBrl")]
    public void DerivedCharacterFields_AreListedButNotEditable(string path)
    {
        Assert.False(CharacterFields.ByPath[path].Editable);
        Assert.Throws<FieldPatchException>(() => CharacterFields.Apply(WorldSamples.Character(), path, "1"));
    }

    [Fact]
    public void TheTwelveAttributes_AreDerivedFromTheEnum()
    {
        WorldFieldGroup attributes = CharacterFields.Groups.Single(group => group.Id == CharacterFields.AttributeGroupId);

        Assert.Equal(Enum.GetValues<Attr>().Length, attributes.Fields.Count);
        Assert.All(attributes.Fields, field => Assert.True(field.Editable));
        Assert.Contains(attributes.Fields, field => field.Path == "attrs.Reflexes");
    }

    [Fact]
    public void ClubGroups_MatchTheNineOfTheClubPage()
    {
        Assert.Equal(
            new[] { "identity", "geography", "world", "crest", "palette", "kits", "stadium", "aiProfile", "audit" },
            ClubFields.Groups.Select(group => group.Id));
    }

    [Fact]
    public void EnumFields_NameTheirEnum()
    {
        // The form builds its <select> options from this name, so a field that forgets it renders
        // as a free-text box that accepts anything until the server refuses it.
        foreach (WorldField field in ClubFields.ByPath.Values.Concat(CharacterFields.ByPath.Values))
        {
            if (field.Kind == FieldKind.Enum)
                Assert.False(string.IsNullOrEmpty(field.EnumName), $"{field.Path} is an enum field with no enum name");
        }
    }

    [Fact]
    public void AnInvalidEnumValue_NamesWhatWouldHaveBeenValid()
    {
        var exception = Assert.Throws<FieldPatchException>(
            () => ClubFields.Apply(WorldSamples.Club(), "stadium.pitchSurface", "Astroturf"));

        Assert.Contains("Astroturf", exception.Message);
        Assert.Contains("Pristine", exception.Message);
    }

    [Fact]
    public void ColoursAreNormalised_SoTheSchemasCheckHolds()
    {
        ClubIdentity club = ClubFields.Apply(WorldSamples.Club(), "palette.primary", "1a2b3c");

        // Accepts a missing '#' and lower case; stores the one shape the CHECK constraint and the
        // colour maths both expect.
        Assert.Equal("#1A2B3C", club.Palette.Primary);
    }

    [Fact]
    public void DecimalsAcceptTheCommaTheUserTypes()
    {
        ClubIdentity club = ClubFields.Apply(WorldSamples.Club(), "world.clubStrength", "0,95");

        Assert.Equal(0.95, club.World.ClubStrength, precision: 4);
    }
}
