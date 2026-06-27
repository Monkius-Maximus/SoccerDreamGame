using Godot;
using SoccerDreamGame.Autoload;
using SoccerSim.Core.Time;

namespace SoccerDreamGame.Scenes;

/// <summary>
/// Rendered calendar. Advances the background calendar simulation and handles the EventTrigger
/// interruption when an event fires: it surfaces the event, then resumes advancing toward the
/// original target date. A minimal UI drives the loop and hands control back to the hub via the
/// <see cref="GameModeManager"/>.
/// </summary>
public partial class CalendarScene : Control
{
    private Label _dateLabel = null!;

    public override void _Ready()
    {
        GD.Print("[CalendarScene] Entered Calendar mode.");
        BuildUi();
        UpdateDateLabel();
    }

    /// <summary>Advance the simulated calendar to <paramref name="target"/>, handling interrupts.</summary>
    public void AdvanceTo(DateTime target)
    {
        ITimeManager time = GameBootstrap.Instance.Time;
        TimeAdvanceResult result = time.AdvanceCalendar(target);

        while (result is { Interrupted: true, PendingEvent: not null })
        {
            GD.Print($"[Calendar] Interrupted by {result.PendingEvent.Tier} event on {result.ReachedDate:d}.");

            // A full implementation awaits EventBus to resolve High/Medium events here;
            // the scaffold simply resumes to keep the loop demonstrable.
            result = time.ResumeCalendar(result.ResumeToken, target);
        }

        GD.Print($"[Calendar] Simulation reached {result.ReachedDate:d}.");
    }

    private void OnAdvanceFortnightPressed()
    {
        AdvanceTo(GameBootstrap.Instance.Time.CurrentDate.AddDays(14));
        UpdateDateLabel();
    }

    private void OnBackPressed() => GameModeManager.Instance.EnterLoading();

    private void UpdateDateLabel() =>
        _dateLabel.Text = $"Current date: {GameBootstrap.Instance.Time.CurrentDate:yyyy-MM-dd}";

    private void BuildUi()
    {
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 16);
        center.AddChild(box);

        var title = new Label { Text = "Calendar", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 28);
        box.AddChild(title);

        _dateLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        box.AddChild(_dateLabel);

        var advance = new Button { Text = "Advance 14 Days", CustomMinimumSize = new Vector2(260, 48) };
        advance.Pressed += OnAdvanceFortnightPressed;
        box.AddChild(advance);

        var back = new Button { Text = "Back to Menu", CustomMinimumSize = new Vector2(260, 48) };
        back.Pressed += OnBackPressed;
        box.AddChild(back);
    }
}
