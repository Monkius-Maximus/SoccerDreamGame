using SoccerSim.Core.Events;
using SoccerSim.Core.LifeSim;
using SoccerSim.Core.Localization;
using SoccerSim.Core.World;
using Xunit;

namespace SoccerSim.Core.Tests;

/// <summary>
/// Guards the code-in-English / game-in-Portuguese contract.
///
/// <para>
/// The coverage tests are the important ones: they enumerate every key the simulation can actually
/// produce and assert it resolves in <b>every</b> locale directly, not through the fallback. Without
/// them a missing translation only surfaces as a raw <c>activity.shower.name</c> on someone's screen
/// during playtest.
/// </para>
/// </summary>
public sealed class LocalizationTests
{
    /// <summary>Every key the Core-owned surface can emit, built the same way production builds it.</summary>
    public static IEnumerable<string> ProducibleKeys()
    {
        foreach (NeedKind need in Needs.All)
        {
            yield return LocKeys.NeedName(need);
            yield return LocKeys.NeedShort(need);
            yield return LocKeys.NeedDescription(need);
        }

        foreach (NeedBand band in Enum.GetValues<NeedBand>())
            yield return LocKeys.Band(band);

        foreach (CareerRole role in Enum.GetValues<CareerRole>())
            yield return LocKeys.Role(role);

        foreach (LifeActivity activity in LifeActivityCatalogue.All)
        {
            yield return activity.NameKey;
            yield return activity.DescriptionKey;
        }

        foreach (WorldLocation location in new TestWorldGazetteer().All)
        {
            yield return location.NameKey;
            yield return location.DescriptionKey;
        }

        foreach (LocationKind kind in Enum.GetValues<LocationKind>())
            yield return LocKeys.LocationKind(kind);

        foreach (EventTier tier in Enum.GetValues<EventTier>())
            yield return LocKeys.EventTier(tier);

        yield return LocKeys.HudWellbeing;
        yield return LocKeys.HudForm;
        yield return LocKeys.HudDate;
        yield return LocKeys.HudCareer;
        yield return LocKeys.HudCritical;
        yield return LocKeys.PanelWellbeing;
        yield return LocKeys.PanelActions;
        yield return LocKeys.PanelNoChange;
        yield return LocKeys.ActivityCost;
        yield return LocKeys.ActivityDuration;
        yield return LocKeys.EventAcknowledge;
        yield return LocKeys.EventNoConsequence;
        yield return LocKeys.PhoneTitle;
        yield return LocKeys.PhoneAppHealth;
        yield return LocKeys.PhoneAppAgenda;
        yield return LocKeys.PhoneAppMap;
        yield return LocKeys.PhoneTravelTime;
        yield return LocKeys.PhoneCurrentLocation;
        yield return LocKeys.PromptSelect;
        yield return LocKeys.PromptBack;
        yield return LocKeys.PromptClose;
        yield return LocKeys.PromptMenu;
        yield return LocKeys.PromptPhone;
        yield return LocKeys.PromptTravel;
        yield return LocKeys.PhoneAppBank;
        yield return LocKeys.BankBalance;
        yield return LocKeys.BankWeeklyWage;
        yield return LocKeys.MenuTitle;
        yield return LocKeys.MenuCareerRole;
        yield return LocKeys.MenuRoleHint;
        yield return LocKeys.MenuResume;
        yield return LocKeys.ActivityUnaffordable;
        yield return LocKeys.DerivedInjuryRisk;
        yield return LocKeys.DerivedStress;
        yield return LocKeys.DerivedDecisionQuality;
        yield return LocKeys.DerivedIndex;
    }

    public static TheoryData<string> Locales()
    {
        var data = new TheoryData<string>();
        foreach (string locale in StringCatalogue.Locales)
            data.Add(locale);
        return data;
    }

    // ── Coverage ────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Locales))]
    public void EveryProducibleKey_ResolvesDirectlyInEveryLocale(string locale)
    {
        IReadOnlyDictionary<string, string> catalogue = StringCatalogue.All[locale];

        string[] missing = ProducibleKeys().Distinct().Where(key => !catalogue.ContainsKey(key)).ToArray();

        Assert.True(
            missing.Length == 0,
            $"Locale '{locale}' is missing {missing.Length} key(s): {string.Join(", ", missing.Take(15))}");
    }

    [Fact]
    public void AllLocales_CarryTheSameKeySet()
    {
        // Drift between locales is the failure that a per-locale coverage test alone would miss:
        // adding a string to pt-BR and forgetting en leaves English silently falling through.
        var keySets = StringCatalogue.All.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Keys.ToHashSet(StringComparer.Ordinal));

        HashSet<string> reference = keySets[StringCatalogue.DefaultLocale];
        foreach ((string locale, HashSet<string> keys) in keySets)
        {
            if (locale == StringCatalogue.DefaultLocale)
                continue;

            Assert.True(
                reference.SetEquals(keys),
                $"Locale '{locale}' differs from '{StringCatalogue.DefaultLocale}'. "
                + $"Only in default: {string.Join(", ", reference.Except(keys).Take(10))}. "
                + $"Only in '{locale}': {string.Join(", ", keys.Except(reference).Take(10))}.");
        }
    }

    [Theory]
    [MemberData(nameof(Locales))]
    public void NoKeyResolvesToBlank(string locale)
    {
        var localizer = new Localizer(locale);

        foreach (string key in ProducibleKeys().Distinct())
            Assert.False(string.IsNullOrWhiteSpace(localizer.Get(key)), $"'{key}' is blank in '{locale}'.");
    }

    // ── Resolution behaviour ────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultLocale_IsPortuguese()
    {
        // The game ships in pt-BR; the code merely happens to be written in English.
        Assert.Equal("pt-BR", StringCatalogue.DefaultLocale);
        Assert.Equal("pt-BR", new Localizer().Locale);
    }

    [Fact]
    public void Get_ReturnsTheLocaleText()
    {
        Assert.Equal("Higiene", new Localizer("pt-BR").Get(LocKeys.NeedName(NeedKind.Hygiene)));
        Assert.Equal("Hygiene", new Localizer("en").Get(LocKeys.NeedName(NeedKind.Hygiene)));
    }

    [Fact]
    public void Get_UnknownKey_ReturnsTheKeyRatherThanBlank()
    {
        // A missing string must be loud in playtest. "menu.nonexistent" on a button is unmistakable;
        // an empty button is not.
        Assert.Equal("menu.nonexistent", new Localizer().Get("menu.nonexistent"));
        Assert.False(new Localizer().Has("menu.nonexistent"));
    }

    [Fact]
    public void UnknownLocale_Throws() =>
        // Silently falling back would ship a Portuguese game in English.
        Assert.Throws<ArgumentOutOfRangeException>(() => new Localizer("fr-FR"));

    // ── The contract itself ─────────────────────────────────────────────────────────────

    [Fact]
    public void SimulationTypes_ExposeKeys_NotProse()
    {
        LifeActivity sleep = LifeActivityCatalogue.ByKey("sleep");

        // The catalogue holds a key. Whether it renders as "Dormir" or "Sleep" is decided at draw
        // time, by the localizer, and never by the simulation.
        Assert.Equal("activity.sleep.name", sleep.NameKey);
        Assert.Equal("Dormir", new Localizer("pt-BR").Get(sleep.NameKey));
        Assert.Equal("Sleep", new Localizer("en").Get(sleep.NameKey));
    }

    [Fact]
    public void NeedKeys_UseSnakeCaseSlugs() =>
        // MuscleCondition -> muscle_condition, so keys stay readable in a .po export.
        Assert.Equal("need.muscle_condition.name", LocKeys.NeedName(NeedKind.MuscleCondition));

    [Fact]
    public void EventChoiceKeys_NestUnderTheirDefinition() =>
        Assert.Equal(
            "event.press_conference.choice.hit_back.label",
            new EventChoice("hit_back").LabelKey("press_conference"));
}
