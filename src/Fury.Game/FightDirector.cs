using Fury.Core.Data;
using Fury.Core.Net;
using Fury.Core.Replay;
using Fury.Core.Sim;
using Fury.Game.Net;
using Fury.Game.Presentation;
using Godot;

namespace Fury.Game;

/// <summary>
/// Orchestrates one fight session: owns the simulation, feeds it (keyboard/pad, CPU, netplay or replay),
/// drives the match flow (intro → fight → round over, best of three offline) and turns simulation events
/// into presentation (VFX, SFX, camera shake, HUD). Draws nothing itself: a swappable
/// <see cref="FightPresentation"/> shows the fight in 2D, 2.5D or 3D.
/// The simulation advances ONLY on the fixed 60 Hz physics tick, never on render time.
/// </summary>
public sealed partial class FightDirector : Node
{
    private enum Phase
    {
        Connecting,
        Intro,
        Fighting,
        RoundOver,
        MatchOver,
        Failed,
    }

    private const int RoundsToWin = 2;
    private const int IntroFrames = 100;
    private const int RoundOverFrames = 150;
    private const int OnlineResultFrames = 150;

    private readonly FightEventQueue _events = new();
    private FightSimulation _sim = null!;
    private CharacterConfig _p1 = null!;
    private CharacterConfig _p2 = null!;
    private FightPresentation _view = null!;
    private Hud _hud = null!;
    private AudioManager _audio = null!;
    private IInputProvider? _cpu;
    private IInputProvider? _cpu1;
    private IInputProvider? _onlineBrain;
    private string? _capturePath;
    private int _captureTick = -1;
    private int _quitTick = -1;
    private ulong _netCheckHash;
    private int _ticks;
    private OnlineMatch? _online;
    private ReplayRecorder? _recorder;
    private ReplayFile? _replay;
    private bool _replayPaused;
    private bool _stepOnce;

    private Phase _phase = Phase.Intro;
    private int _phaseFrame;
    private int _round = 1;
    private readonly int[] _wins = new int[2];
    private int _trainingIdleFrames;
    private string _statusText = "";
    private UI.PauseMenu? _pauseMenu;
    private bool _paused;
    private Stages.PlayableStage? _stage;

    public override void _Ready()
    {
        GameSettings.ApplyAudio();
        Tools.CharacterMods.LoadAll();
        InputBindings.Load();
        ParseUserArgs();
        LoadStage();

        _hud = new Hud();
        _audio = new AudioManager();
        AddChild(_audio);
        AddChild(_hud);
        if (GameSession.Mode != GameMode.Replay) AddChild(new UI.TouchControls());
        if (GameSession.Mode is GameMode.Arcade or GameMode.Versus or GameMode.Training) BuildPauseMenu();

        SetView(GameSettings.ViewMode);

        switch (GameSession.Mode)
        {
            case GameMode.Online:
                StartOnline();
                break;
            case GameMode.Replay:
                StartReplay();
                break;
            default:
                _p1 = GameSession.CreateP1();
                _p2 = GameSession.CreateP2();
                _cpu = _cpu1 != null ? new CpuBrain(2, aggression: 8)
                    : GameSession.Mode == GameMode.Arcade ? new CpuBrain((ulong)Time.GetTicksUsec(), aggression: 6)
                    : GameSession.Mode == GameMode.Training ? new DummyBrain()
                    : null;
                StartRound();
                break;
        }
    }

    public override void _ExitTree() => _online?.Dispose();

    // ------------------------------------------------------------------ dev / CI smoke

    /// <summary>
    /// Command-line hooks (after <c>--</c>), used by CI smoke tests and screenshots:
    /// <c>--fury-demo</c> AI vs AI; <c>--fury-view=0|1|2</c>; <c>--fury-frames=N</c> with <c>--fury-capture=file.png</c>
    /// saves a screenshot after N ticks and quits; <c>--fury-quit=N</c> quits after N ticks;
    /// <c>--fury-online=host|guest</c> (+ <c>--fury-remote=ip</c>, <c>--fury-port=n</c>, <c>--fury-character=id</c>,
    /// <c>--fury-seed=n</c>) plays a CPU-driven online match and prints a confirmed-state hash for cross-checking.
    /// </summary>
    private void ParseUserArgs()
    {
        int frames = 240;
        ulong seed = 1;
        bool onlineBrain = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--fury-demo")
            {
                _cpu1 = new CpuBrain(1, aggression: 8);
                GameSession.Mode = GameMode.Versus;
            }
            else if (arg == "--fury-touch") GameSettings.ForceTouchControls = true;
            else if (arg.StartsWith("--fury-view=", StringComparison.Ordinal) && int.TryParse(arg.AsSpan(12), out int v) && v is >= 0 and <= 2) GameSettings.ViewMode = (ViewMode)v;
            else if (arg.StartsWith("--fury-frames=", StringComparison.Ordinal) && int.TryParse(arg.AsSpan(14), out int n) && n > 0) frames = n;
            else if (arg.StartsWith("--fury-capture=", StringComparison.Ordinal)) _capturePath = arg[15..];
            else if (arg.StartsWith("--fury-quit=", StringComparison.Ordinal) && int.TryParse(arg.AsSpan(12), out int q) && q > 0) _quitTick = q;
            else if (arg.StartsWith("--fury-online=", StringComparison.Ordinal))
            {
                GameSession.Mode = GameMode.Online;
                GameSession.NetIsHost = arg[14..] == "host";
                onlineBrain = true;
            }
            else if (arg.StartsWith("--fury-remote=", StringComparison.Ordinal)) GameSession.NetRemoteHost = arg[14..];
            else if (arg.StartsWith("--fury-port=", StringComparison.Ordinal) && int.TryParse(arg.AsSpan(12), out int port)) GameSession.NetPort = port;
            else if (arg.StartsWith("--fury-character=", StringComparison.Ordinal)) GameSession.P1CharacterId = arg[17..];
            else if (arg.StartsWith("--fury-stage=", StringComparison.Ordinal)) GameSettings.StageName = arg[13..];
            else if (arg.StartsWith("--fury-round-seconds=", StringComparison.Ordinal) && int.TryParse(arg.AsSpan(21), out int seconds) && seconds > 0) GameSession.RoundSecondsOverride = seconds;
            else if (arg.StartsWith("--fury-seed=", StringComparison.Ordinal) && ulong.TryParse(arg.AsSpan(12), out ulong s)) seed = s;
        }
        if (onlineBrain) _onlineBrain = new CpuBrain(seed, aggression: 8);
        if (_capturePath != null) _captureTick = frames;
    }

    public override void _Process(double delta)
    {
        if (_quitTick > 0 && _ticks >= _quitTick)
        {
            _quitTick = -1;
            GD.Print($"[fury] online-result status={_online?.Session?.Status} round={_round} wins={_wins[0]}-{_wins[1]} check120={_netCheckHash:x16} phase={_phase}");
            GetTree().Quit();
            return;
        }

        if (_captureTick < 0 || _ticks < _captureTick) return;
        _captureTick = -1;
        var error = GetViewport().GetTexture().GetImage().SavePng(_capturePath);
        GD.Print(error == Error.Ok ? $"[fury] captured {_capturePath}" : $"[fury] capture failed: {error}");
        GetTree().Quit();
    }

    // ------------------------------------------------------------------ setup

    private void SetView(ViewMode mode)
    {
        if (_view != null)
        {
            RemoveChild(_view);
            _view.QueueFree();
        }

        _view = mode == ViewMode.Flat2D ? new FightPresentation2D() : new FightPresentation3D { Cinematic = mode == ViewMode.Cinematic3D };
        _view.ShowBoxes = GameSettings.ShowHitboxes || GameSession.Mode is GameMode.Training or GameMode.Replay;
        _view.StageBackground = _stage?.Background;
        AddChild(_view);
        MoveChild(_view, 0); // behind the HUD
        if (_sim != null) _view.Bind(_sim);
    }

    /// <summary>
    /// Custom stages only apply to the local modes: online peers, replays and the netcode fingerprints know the default arena,
    /// so those always use it.
    /// </summary>
    private void LoadStage()
    {
        if (GameSession.Mode is not (GameMode.Arcade or GameMode.Versus or GameMode.Training) || GameSettings.StageName.Length == 0) return;
        var data = Stages.StageLoader.TryLoad(GameSettings.StageName);
        if (data != null) _stage = Stages.StageLoader.Build(data, FightConfig.Default());
    }

    private FightConfig BuildConfig()
    {
        var cfg = _stage?.Config ?? FightConfig.Default();
        if (GameSession.RoundSecondsOverride > 0) cfg = cfg with { RoundSeconds = GameSession.RoundSecondsOverride };
        return GameSession.Mode == GameMode.Training ? cfg with { RoundSeconds = 9999 } : cfg;
    }

    private void StartRound()
    {
        var cfg = BuildConfig();
        _events.Clear();
        var stageModule = _stage?.CreateModule();
        _sim = new FightSimulation(_p1, _p2, cfg, _events, modules: stageModule != null ? [stageModule] : null);
        _view.Bind(_sim);
        _hud.Setup(_p1.Id, _p2.Id, cfg.MaxHealth);
        _hud.SetRoundWins(_wins[0], _wins[1], RoundsToWin);

        // Replays do not carry the stage geometry, so fights on a custom stage are not recorded.
        bool record = GameSession.Mode is GameMode.Arcade or GameMode.Versus && _stage == null;
        _recorder = record ? new ReplayRecorder(_sim, ReplayHeader.For(_p1, _p2, cfg)) : null;

        EnterPhase(Phase.Intro);
        _hud.ShowBanner(GameSession.Mode == GameMode.Training ? "TRAINING" : $"ROUND {_round}", 1.0f);
        _audio.Play(Sfx.RoundStart);
    }

    private void StartOnline()
    {
        _online = new OnlineMatch(
            GameSession.NetIsHost, GameSession.NetRemoteHost, GameSession.NetPort,
            GameSession.P1CharacterId, BuildConfig(), GameSession.NetInputDelay);
        _p1 = GameSession.CreateP1();
        _p2 = GameSession.CreateP2();
        _phase = Phase.Connecting;
        _hud.ShowBanner("CONNECTING...", 0f);
    }

    private void StartReplay()
    {
        try
        {
            _replay = ReplayFile.Parse(File.ReadAllBytes(GameSession.ReplayPath ?? throw new InvalidDataException("No replay selected.")));
            _p1 = CharacterRoster.Create(_replay.Header.P1Id);
            _p2 = CharacterRoster.Create(_replay.Header.P2Id);
            var cfg = FightConfig.Default();
            if (_replay.Header.ConfigHash != cfg.Fingerprint() || _replay.Header.P1Hash != _p1.Fingerprint() || _replay.Header.P2Hash != _p2.Fingerprint())
            {
                throw new InvalidDataException("This replay was recorded with different rules or character data.");
            }

            _sim = new FightSimulation(_p1, _p2, cfg, _events);
            _view.Bind(_sim);
            _hud.Setup(_p1.Id, _p2.Id, cfg.MaxHealth);
            EnterPhase(Phase.Fighting);
            _hud.ShowBanner("REPLAY  ·  SPACE pause  ·  → step", 2.5f);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            Fail($"Cannot play replay: {e.Message}");
        }
    }

    private void Fail(string message)
    {
        _phase = Phase.Failed;
        _hud.ShowBanner($"{message}\n\nPress ESC", 0f);
    }

    private void EnterPhase(Phase phase)
    {
        _phase = phase;
        _phaseFrame = 0;
    }

    // ------------------------------------------------------------------ simulation loop

    public override void _PhysicsProcess(double delta)
    {
        _ticks++;
        if (_paused) return; // the simulation only advances while the game is running: pausing costs nothing and cannot desync
        switch (_phase)
        {
            case Phase.Connecting:
                TickConnecting();
                return;
            case Phase.Intro:
                if (++_phaseFrame == IntroFrames - 40) _hud.ShowBanner("FIGHT!", 0.7f);
                if (_phaseFrame >= IntroFrames) EnterPhase(Phase.Fighting);
                _view.Sync();
                UpdateHud();
                return;
            case Phase.Fighting:
                TickFight();
                return;
            case Phase.RoundOver:
                TickRoundOver();
                return;
            case Phase.MatchOver:
                _online?.Session?.Pump(); // the peer may still need our last inputs
                return;
        }
    }

    private void TickConnecting()
    {
        // The local player always uses binding set 0, whichever fighter slot the netcode assigned.
        _online!.Poll(LocalOnlineInput, _events);
        if (_online.Status == OnlineMatch.Phase.Failed)
        {
            Fail(_online.Error ?? "Connection failed.");
        }
        else if (_online.Status == OnlineMatch.Phase.Ready)
        {
            _sim = _online.Sim!;
            _p1 = _sim.Data(0);
            _p2 = _sim.Data(1);
            _view.Bind(_sim);
            _hud.Setup(_p1.Id, _p2.Id, _sim.Config.MaxHealth);
            EnterPhase(Phase.Fighting);
            _hud.ShowBanner("FIGHT!", 0.8f);
            _audio.Play(Sfx.RoundStart);
        }
    }

    private InputState LocalOnlineInput(int frame) =>
        _onlineBrain != null && _sim != null && _online != null
            ? _onlineBrain.Poll(frame, _sim.ViewFor(_online.LocalSlot))
            : InputAdapter.Poll(0);

    private void TickFight()
    {
        bool advanced = true;
        if (_online?.Session is { } net)
        {
            advanced = net.Tick();
            if (net.Status == NetplayStatus.Desynced)
            {
                Fail($"Desync detected at frame {net.DesyncFrame}. The match was stopped.");
                return;
            }
            _statusText = $"online {net.Status} - rtt {net.RttTicks}t - depth {net.Runner.PredictionDepth} - rollbacks {_sim.Stats.Rollbacks}";
            if (_netCheckHash == 0 && net.Frame < 190) _netCheckHash = net.Runner.ConfirmedHash(120);
        }
        else if (_replay != null)
        {
            advanced = StepReplay();
        }
        else
        {
            var in1 = _cpu1 != null ? _cpu1.Poll(_sim.Frame, _sim.ViewFor(0)) : InputAdapter.Poll(0);
            var in2 = _cpu != null ? _cpu.Poll(_sim.Frame, _sim.ViewFor(1)) : InputAdapter.Poll(1);
            if (_recorder != null) _recorder.Step(in1, in2);
            else _sim.Step(in1, in2);
            if (GameSession.Mode == GameMode.Training) TrainingMaintenance();
        }

        if (advanced) AfterSimStep();
        if (_sim.State.RoundState != 0) OnRoundEnded();
    }

    private bool StepReplay()
    {
        if (_replayPaused && !_stepOnce) return false;
        _stepOnce = false;
        if (_sim.Frame >= _replay!.FrameCount)
        {
            _hud.ShowBanner("END OF REPLAY\nPress ESC", 0f);
            return false;
        }
        var (a, b) = _replay.Get(_sim.Frame);
        _sim.Step(a, b);
        return true;
    }

    private void AfterSimStep()
    {
        DrainEvents();
        _view.Sync();
        UpdateHud();
    }

    private void DrainEvents()
    {
        while (_events.TryDequeue(out var e))
        {
            switch (e.Id)
            {
                case SimEventId.AttackHit:
                    e.Unpack(out _, out int defender, out int damage);
                    _view.SpawnImpact(defender, ImpactKind.Hit);
                    _view.Shake(Mathf.Clamp(damage / 160f, 0.15f, 0.8f));
                    _audio.PlayHit(damage);
                    if (damage >= 100) _hud.Flash(new Color(1f, 0.9f, 0.6f), 0.25f);
                    break;
                case SimEventId.AttackBlocked:
                    e.Unpack(out _, out int blocker, out _);
                    _view.SpawnImpact(blocker, ImpactKind.Block);
                    _view.Shake(0.1f);
                    _audio.Play(Sfx.Block);
                    break;
                case SimEventId.RageUnlocked:
                    _view.SpawnImpact((int)e.Payload, ImpactKind.Rage);
                    _audio.Play(Sfx.Rage);
                    break;
                case SimEventId.Throw:
                    e.Unpack(out _, out int grabbed, out int throwDamage);
                    _view.SpawnImpact(grabbed, ImpactKind.Hit);
                    _view.Shake(0.5f);
                    _audio.PlayHit(Math.Max(throwDamage, 100));
                    break;
                case SimEventId.ThrowTeched:
                    e.Unpack(out int techA, out int techB, out _);
                    _view.SpawnImpact(techA, ImpactKind.Block);
                    _view.SpawnImpact(techB, ImpactKind.Block);
                    _audio.Play(Sfx.Block, pitch: 0.7f);
                    break;
                case SimEventId.PowerActivated:
                case SimEventId.Tagged:
                    e.Unpack(out int actor, out _, out _);
                    _view.SpawnImpact(actor, ImpactKind.Rage);
                    _audio.Play(Sfx.Rage, pitch: 1.3f);
                    break;
            }
        }
    }

    private void UpdateHud()
    {
        var s = _sim.State;
        _hud.Apply(new HudState(
            s.GetHealth(0), s.GetHealth(1), s.GetExMeter(0), s.GetExMeter(1), s.RoundTimerFrames,
            s.GetComboCount(0), s.GetComboCount(1), s.IsRaging(0), s.IsRaging(1)));

        if (GameSettings.ShowDebugOverlay)
        {
            var trace = _events.LastFrame;
            _hud.SetDebugText($"frame {_sim.Frame}  hash {trace.StateHash:x16}  {Engine.GetFramesPerSecond()} fps  {_statusText}");
        }
        else
        {
            _hud.SetDebugText(null);
        }
    }

    // ------------------------------------------------------------------ match flow

    private void OnRoundEnded()
    {
        int winner = _sim.State.Winner;
        _audio.Play(Sfx.Ko);
        _view.SpawnImpact(winner == 0 ? 1 : 0, ImpactKind.Ko);
        _view.Shake(0.9f);

        bool timeout = _sim.State.GetHealth(0) > 0 && _sim.State.GetHealth(1) > 0;
        _hud.ShowBanner(winner < 0 ? "DRAW" : timeout ? "TIME UP" : "K.O.", 0f);

        if (_online != null)
        {
            EnterPhase(Phase.RoundOver); // wins are counted once the result is confirmed by both peers
            return;
        }

        if (_replay != null || GameSession.Mode == GameMode.Training)
        {
            EnterPhase(Phase.MatchOver);
            if (_replay == null && GameSession.Mode != GameMode.Training) _hud.ShowBanner($"{WinnerName(winner)}\nPress ENTER", 0f);
            return;
        }

        if (winner < 0) { _wins[0]++; _wins[1]++; }
        else _wins[winner]++;
        _hud.SetRoundWins(_wins[0], _wins[1], RoundsToWin);
        SaveReplay();
        EnterPhase(Phase.RoundOver);
    }

    /// <summary>
    /// Online round end: keep playing the round out on the shared timeline, then, once every frame up to the next round start
    /// is confirmed (so both peers derive the same start from the same final state), count the win and swap the simulation.
    /// </summary>
    private void TickOnlineRoundOver()
    {
        var net = _online!.Session!;
        if (_sim.State.RoundState == 0)
        {
            // A rollback showed the round was not over after all: resume fighting.
            _hud.HideBanner();
            EnterPhase(Phase.Fighting);
            return;
        }

        int target = _sim.State.RoundEndFrame + 1 + OnlineResultFrames;
        if (net.Frame < target)
        {
            net.Tick();
            AfterSimStep();
            return;
        }

        net.Pump();
        if (net.Frame != target || net.Runner.Session.ConfirmedThrough < target - 1) return;

        int winner = _sim.State.Winner;
        if (winner < 0) { _wins[0]++; _wins[1]++; }
        else _wins[winner]++;
        _hud.SetRoundWins(_wins[0], _wins[1], RoundsToWin);

        if (_wins[0] >= RoundsToWin || _wins[1] >= RoundsToWin)
        {
            int champion = _wins[0] == _wins[1] ? -1 : _wins[0] > _wins[1] ? 0 : 1;
            EnterPhase(Phase.MatchOver);
            _hud.ShowBanner($"{WinnerName(champion)}\nPress ENTER", 0f);
            return;
        }

        _round++;
        _events.Clear();
        _sim = _online.BeginNextRound(target);
        _view.Bind(_sim);
        _hud.Setup(_p1.Id, _p2.Id, _sim.Config.MaxHealth);
        _hud.SetRoundWins(_wins[0], _wins[1], RoundsToWin);
        _hud.ShowBanner($"ROUND {_round}", 1.0f);
        _audio.Play(Sfx.RoundStart);
        EnterPhase(Phase.Fighting);
    }

    private void TickRoundOver()
    {
        if (_online?.Session != null)
        {
            TickOnlineRoundOver();
            return;
        }

        // Keep stepping with neutral input so knockdowns and falls finish playing out.
        if (_phaseFrame < RoundOverFrames)
        {
            _sim.Step(new InputState(0), new InputState(0));
            AfterSimStep();
        }

        if (++_phaseFrame < RoundOverFrames + 30) return;

        bool matchDecided = _wins[0] >= RoundsToWin || _wins[1] >= RoundsToWin;
        if (matchDecided)
        {
            int winner = _wins[0] == _wins[1] ? -1 : _wins[0] > _wins[1] ? 0 : 1;
            EnterPhase(Phase.MatchOver);
            _hud.ShowBanner($"{WinnerName(winner)}\nPress ENTER", 0f);
        }
        else
        {
            _round++;
            StartRound();
        }
    }

    private string WinnerName(int winner) => winner < 0 ? "DRAW GAME" : $"{(winner == 0 ? _p1.Id : _p2.Id).ToUpperInvariant()} WINS";

    private void SaveReplay()
    {
        if (_recorder == null) return;
        try
        {
            string dir = ProjectSettings.GlobalizePath("user://replays");
            Directory.CreateDirectory(dir);
            string name = $"{DateTime.Now:yyyyMMdd_HHmmss}_{_p1.Id}_vs_{_p2.Id}_r{_round}.fry";
            File.WriteAllBytes(Path.Combine(dir, name), _recorder.Finish().ToBytes());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Could not save replay: {e.Message}");
        }
    }

    private void TrainingMaintenance()
    {
        // Refill the dummy's health when it has been left alone for a second (training convenience, offline only).
        var s = _sim.State;
        bool idle = s.GetStateId(1) == FighterStateId.Idle && s.GetStateId(0) is FighterStateId.Idle or FighterStateId.Crouch;
        _trainingIdleFrames = idle ? _trainingIdleFrames + 1 : 0;
        if (_trainingIdleFrames > 60 && (s.GetHealth(1) < _sim.Config.MaxHealth || s.GetHealth(0) < _sim.Config.MaxHealth))
        {
            s.SetHealth(0, _sim.Config.MaxHealth);
            s.SetHealth(1, _sim.Config.MaxHealth);
            s.SetRaging(0, false);
            s.SetRaging(1, false);
        }
    }

    // ------------------------------------------------------------------ pause

    private void BuildPauseMenu()
    {
        _pauseMenu = new UI.PauseMenu();
        AddChild(_pauseMenu);
        _pauseMenu.ResumeRequested += TogglePause;
        _pauseMenu.RestartRequested += () =>
        {
            TogglePause();
            _wins[0] = 0;
            _wins[1] = 0;
            _round = 1;
            StartRound();
        };
        _pauseMenu.CharacterSelectRequested += () => GetTree().ChangeSceneToFile(GameSession.CharacterSelectScene);
        _pauseMenu.MainMenuRequested += () => GetTree().ChangeSceneToFile(GameSession.MainMenuScene);
    }

    private void TogglePause()
    {
        if (_pauseMenu == null) return;
        _paused = !_paused;
        if (_paused) _pauseMenu.Open();
        else _pauseMenu.Close();
    }

    // ------------------------------------------------------------------ input

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        switch (key.Keycode)
        {
            case Key.Escape:
                if (_pauseMenu != null && _phase is Phase.Intro or Phase.Fighting or Phase.RoundOver) TogglePause();
                else GetTree().ChangeSceneToFile(GameSession.MainMenuScene);
                break;
            case Key.Enter or Key.KpEnter when _phase == Phase.MatchOver:
                GetTree().ChangeSceneToFile(GameSession.MainMenuScene);
                break;
            case Key.F1:
                _view.ShowBoxes = !_view.ShowBoxes;
                break;
            case Key.F2:
                GameSettings.ViewMode = (ViewMode)(((int)GameSettings.ViewMode + 1) % 3);
                SetView(GameSettings.ViewMode);
                GameSettings.Save();
                break;
            case Key.F3:
                GameSettings.ShowDebugOverlay = !GameSettings.ShowDebugOverlay;
                break;
            case Key.Space when _replay != null:
                _replayPaused = !_replayPaused;
                break;
            case Key.Right when _replay != null && _replayPaused:
                _stepOnce = true;
                break;
        }
    }
}
