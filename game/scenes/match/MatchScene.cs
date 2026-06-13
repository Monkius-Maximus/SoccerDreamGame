using Godot;
using SoccerDreamGame.Autoload;
using SoccerSim.Core.Simulation;

namespace SoccerDreamGame.Scenes;

/// <summary>
/// The Match scene. For now a broadcast-style "ticker": it asks the core for the next Tier 1
/// fixture — which the <see cref="MatchEngine"/> simulates in full (minute timeline + box score) —
/// then plays that timeline out minute-by-minute: scoreboard, a live clock, a scrolling event
/// feed, and a full-time box score. The side-on sprite engine (GDD §1) will later live on this
/// Node2D plane beneath the same HUD CanvasLayer.
/// </summary>
public partial class MatchScene : Node2D
{
    /// <summary>Real seconds per simulated minute; ~0.4 plays a full match in well under a minute.</summary>
    [Export] public float SecondsPerSimMinute { get; set; } = 0.4f;

    private MatchPresentation? _presentation;
    private IReadOnlyList<MatchMinuteEvent> _events = Array.Empty<MatchMinuteEvent>();
    private int _nextEventIndex;
    private int _finalMinute;
    private double _elapsed;
    private bool _finished;

    private int _homeTeamId;
    private int _homeGoals;
    private int _awayGoals;

    private Label _scoreLabel = null!;
    private Label _clockLabel = null!;
    private ScrollContainer _feedScroll = null!;
    private VBoxContainer _feed = null!;
    private Label _statsLabel = null!;
    private Button _skipButton = null!;

    public override void _Ready()
    {
        GD.Print("[MatchScene] Loading next Tier 1 fixture.");
        _presentation = GameBootstrap.Instance.Match.PlayNextFixture(SimulationTier.ActiveHuman);

        BuildUi();

        if (_presentation is null)
        {
            _scoreLabel.Text = "No fixture available";
            _clockLabel.Text = string.Empty;
            _skipButton.Disabled = true;
            _finished = true;
            return;
        }

        _events = _presentation.Simulation.Timeline;
        _finalMinute = _events.Count > 0 ? _events[_events.Count - 1].Minute : 0;
        _homeTeamId = _presentation.Display.HomeTeamId;
        UpdateScoreboard();
        _clockLabel.Text = "0'";
    }

    public override void _Process(double delta)
    {
        if (_finished || _presentation is null)
            return;

        _elapsed += delta;
        int minute = (int)(_elapsed / SecondsPerSimMinute);
        if (minute > _finalMinute)
            minute = _finalMinute;

        RevealEventsThrough(minute);
        _clockLabel.Text = $"{minute}'";

        if (minute >= _finalMinute && _nextEventIndex >= _events.Count)
            Finish();
    }

    private void RevealEventsThrough(int minute)
    {
        while (_nextEventIndex < _events.Count && _events[_nextEventIndex].Minute <= minute)
        {
            ApplyEvent(_events[_nextEventIndex]);
            _nextEventIndex++;
        }
    }

    private void ApplyEvent(MatchMinuteEvent e)
    {
        if (e.Kind == MatchEventKind.Goal && e.TeamId is int scoringTeam)
        {
            if (scoringTeam == _homeTeamId)
                _homeGoals++;
            else
                _awayGoals++;
            UpdateScoreboard();
        }

        AppendFeedLine(FormatEvent(e));
    }

    private string FormatEvent(MatchMinuteEvent e) => e.Kind switch
    {
        MatchEventKind.KickOff => "Kick-off",
        MatchEventKind.HalfTime => $"{e.Minute}'  — Half-time —",
        MatchEventKind.FullTime => $"{e.Minute}'  — Full-time —",
        MatchEventKind.Goal => $"{e.Minute}'  ⚽ GOAL!  {PlayerName(e.PlayerId)}  ({TeamName(e.TeamId)})",
        _ => $"{e.Minute}'  {TeamName(e.TeamId)} — {e.Description}",
    };

    private void Finish()
    {
        _finished = true;
        _clockLabel.Text = "FT";
        _skipButton.Disabled = true;

        if (_presentation is null)
            return;

        MatchStats s = _presentation.Simulation.Stats;
        _statsLabel.Text =
            $"Possession {s.HomePossession}% – {s.AwayPossession}%      " +
            $"Shots {s.HomeShots} – {s.AwayShots}      " +
            $"On target {s.HomeShotsOnTarget} – {s.AwayShotsOnTarget}";
    }

    private void OnSkipPressed()
    {
        if (_finished)
            return;
        RevealEventsThrough(_finalMinute);
        Finish();
    }

    private void OnBackPressed() =>
        GetTree().ChangeSceneToFile("res://scenes/main_menu/MainMenu.tscn");

    private void UpdateScoreboard()
    {
        if (_presentation is null)
            return;
        MatchDisplayInfo d = _presentation.Display;
        _scoreLabel.Text = $"{d.HomeTeamName}   {_homeGoals} – {_awayGoals}   {d.AwayTeamName}";
    }

    private string PlayerName(int? playerId) =>
        playerId is int id && _presentation is not null
            && _presentation.Display.PlayerNames.TryGetValue(id, out string? name)
            ? name
            : $"#{playerId}";

    private string TeamName(int? teamId)
    {
        if (_presentation is null || teamId is not int id)
            return "?";
        MatchDisplayInfo d = _presentation.Display;
        return id == d.HomeTeamId ? d.HomeTeamName
            : id == d.AwayTeamId ? d.AwayTeamName
            : $"Team {id}";
    }

    private void AppendFeedLine(string text)
    {
        _feed.AddChild(new Label { Text = text });
        // Keep the newest line in view (layout updates next frame, so defer the scroll).
        _feedScroll.SetDeferred("scroll_vertical", 1_000_000);
    }

    private void BuildUi()
    {
        var canvas = new CanvasLayer();
        AddChild(canvas);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        canvas.AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(root);

        _scoreLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _scoreLabel.AddThemeFontSizeOverride("font_size", 30);
        root.AddChild(_scoreLabel);

        _clockLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _clockLabel.AddThemeFontSizeOverride("font_size", 22);
        root.AddChild(_clockLabel);

        _feedScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 360),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        root.AddChild(_feedScroll);

        _feed = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _feedScroll.AddChild(_feed);

        _statsLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        root.AddChild(_statsLabel);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 16);
        root.AddChild(buttons);

        _skipButton = new Button { Text = "Skip to Result", CustomMinimumSize = new Vector2(160, 40) };
        _skipButton.Pressed += OnSkipPressed;
        buttons.AddChild(_skipButton);

        var back = new Button { Text = "Back to Menu", CustomMinimumSize = new Vector2(160, 40) };
        back.Pressed += OnBackPressed;
        buttons.AddChild(back);
    }
}
