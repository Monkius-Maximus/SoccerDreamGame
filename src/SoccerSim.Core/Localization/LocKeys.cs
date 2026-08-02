using SoccerSim.Core.LifeSim;

namespace SoccerSim.Core.Localization;

/// <summary>
/// Builders for every string key the simulation produces.
///
/// <para>
/// Keys are built here rather than written as literals at each call site so a rename is a compiler
/// error instead of a silently missing translation. The scheme is
/// <c>domain.subject.field</c>, lowercase and dot-separated, which also groups sensibly when the
/// catalogue is eventually exported to <c>.po</c> files for a real translator.
/// </para>
/// </summary>
public static class LocKeys
{
    // ── Needs ───────────────────────────────────────────────────────────────────────────

    /// <summary>Full display name of a need, e.g. <c>need.energy.name</c>.</summary>
    public static string NeedName(NeedKind need) => $"need.{Slug(need)}.name";

    /// <summary>Abbreviated name for tight HUD rows, e.g. <c>need.muscle_condition.short</c>.</summary>
    public static string NeedShort(NeedKind need) => $"need.{Slug(need)}.short";

    /// <summary>One-line explanation of what the need governs.</summary>
    public static string NeedDescription(NeedKind need) => $"need.{Slug(need)}.desc";

    /// <summary>Label for a qualitative band, e.g. <c>band.critical</c>.</summary>
    public static string Band(NeedBand band) => $"band.{band.ToString().ToLowerInvariant()}";

    // ── Career roles ────────────────────────────────────────────────────────────────────

    /// <summary>Label for a career role, e.g. <c>role.manager</c>.</summary>
    public static string Role(CareerRole role) => $"role.{role.ToString().ToLowerInvariant()}";

    // ── Activities ──────────────────────────────────────────────────────────────────────

    public static string ActivityName(string activityKey) => $"activity.{activityKey}.name";

    public static string ActivityDescription(string activityKey) => $"activity.{activityKey}.desc";

    // ── Events ──────────────────────────────────────────────────────────────────────────

    public static string EventTitle(string definitionKey) => $"event.{definitionKey}.title";

    public static string EventPrompt(string definitionKey) => $"event.{definitionKey}.prompt";

    public static string EventChoiceLabel(string definitionKey, string choiceKey) =>
        $"event.{definitionKey}.choice.{choiceKey}.label";

    public static string EventChoiceDescription(string definitionKey, string choiceKey) =>
        $"event.{definitionKey}.choice.{choiceKey}.desc";

    /// <summary>Caption naming an event's stakes tier.</summary>
    public static string EventTier(Events.EventTier tier) => $"event.tier.{tier.ToString().ToLowerInvariant()}";

    // ── World ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Display name of a world location. The id is the dotted path used by the gazetteer
    /// (e.g. <c>br.sudeste.rj.rio-de-janeiro#lapa</c>), so keys stay stable across a world import.
    /// </summary>
    public static string LocationName(string locationId) => $"location.{locationId}.name";

    public static string LocationDescription(string locationId) => $"location.{locationId}.desc";

    /// <summary>Label for a location category, e.g. <c>location.kind.stadium</c>.</summary>
    public static string LocationKind(World.LocationKind kind) =>
        $"location.kind.{kind.ToString().ToLowerInvariant()}";

    // ── Interface chrome ────────────────────────────────────────────────────────────────

    public const string HudWellbeing = "hud.tile.wellbeing";
    public const string HudForm = "hud.tile.form";
    public const string HudDate = "hud.tile.date";
    public const string HudCareer = "hud.tile.career";
    public const string HudCritical = "hud.critical";

    public const string PanelWellbeing = "panel.wellbeing";
    public const string PanelActions = "panel.actions";
    public const string PanelNoChange = "panel.activity.no_change";

    public const string ActivityCost = "activity.cost";
    public const string ActivityDuration = "activity.duration";

    public const string EventAcknowledge = "event.acknowledge";
    public const string EventNoConsequence = "event.no_consequence";

    // ── In-game phone ───────────────────────────────────────────────────────────────────

    public const string PhoneTitle = "phone.title";
    public const string PhoneAppHealth = "phone.app.health";
    public const string PhoneAppAgenda = "phone.app.agenda";
    public const string PhoneAppMap = "phone.app.map";
    public const string PhoneTravelTime = "phone.travel_time";
    public const string PhoneCurrentLocation = "phone.current_location";

    public const string PhoneAppBank = "phone.app.bank";
    public const string BankBalance = "bank.balance";
    public const string BankWeeklyWage = "bank.weekly_wage";

    // ── Quick menu ──────────────────────────────────────────────────────────────────────

    public const string MenuTitle = "menu.title";
    public const string MenuCareerRole = "menu.career_role";
    public const string MenuRoleHint = "menu.role_hint";
    public const string MenuResume = "menu.resume";

    // ── Main menu (the hub scene) ───────────────────────────────────────────────────────

    public const string MainTitle = "menu.main.title";
    public const string MainLifeSim = "menu.main.life_sim";
    public const string MainPlayFixture = "menu.main.play_fixture";
    public const string MainAdvanceCalendar = "menu.main.advance_calendar";
    public const string MainNoFixture = "menu.main.no_fixture";

    // ── Contextual input prompts ────────────────────────────────────────────────────────

    public const string PromptSelect = "prompt.select";
    public const string PromptBack = "prompt.back";
    public const string PromptClose = "prompt.close";
    public const string PromptMenu = "prompt.menu";
    public const string PromptPhone = "prompt.phone";
    public const string PromptTravel = "prompt.travel";
    public const string ActivityUnaffordable = "activity.unaffordable";

    // ── Derived wellbeing readouts ──────────────────────────────────────────────────────

    public const string DerivedInjuryRisk = "derived.injury_risk";
    public const string DerivedStress = "derived.stress";
    public const string DerivedDecisionQuality = "derived.decision_quality";
    public const string DerivedIndex = "derived.index";

    /// <summary><c>MuscleCondition</c> → <c>muscle_condition</c>.</summary>
    private static string Slug(NeedKind need)
    {
        string name = need.ToString();
        var slug = new System.Text.StringBuilder(name.Length + 2);
        for (int i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
                slug.Append('_');
            slug.Append(char.ToLowerInvariant(name[i]));
        }

        return slug.ToString();
    }
}
