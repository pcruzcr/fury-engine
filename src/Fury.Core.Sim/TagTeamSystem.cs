using Fury.Core.Data;
using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Tag team system for 2v2 and 3v3 battles (Marvel vs Capcom, KOF, Capcom vs SNK).
/// Manages active fighter, bench fighters, tag-in/tag-out, and assist attacks.
/// Deterministic: all state transitions use integer math only.
/// </summary>
public sealed class TagTeamSystem
{
    public const int MaxTeamSize = 3;
    public const int MaxFighters = MaxTeamSize * 2; // 2 teams

    private readonly FightConfig _cfg;
    private readonly CharacterConfig[][] _rosters; // [team][slot]
    private readonly int[] _activeIndex; // per team: which slot is active
    private readonly int[] _health; // per team: shared health pool per slot
    private readonly int[] _meter; // per team: shared meter pool
    private readonly bool[] _isAlive; // per team per slot
    private readonly int[] _tagCooldown; // per team: frames until next tag allowed

    public TagTeamSystem(FightConfig cfg, CharacterConfig[] teamA, CharacterConfig[] teamB)
    {
        _cfg = cfg;
        _rosters = new CharacterConfig[2][];
        _rosters[0] = teamA;
        _rosters[1] = teamB;
        _activeIndex = new int[2];
        _health = new int[MaxFighters];
        _meter = new int[MaxFighters];
        _isAlive = new bool[MaxFighters];
        _tagCooldown = new int[2];

        for (int team = 0; team < 2; team++)
        {
            for (int slot = 0; slot < MaxTeamSize; slot++)
            {
                int idx = team * MaxTeamSize + slot;
                _health[idx] = cfg.MaxHealth;
                _meter[idx] = 0;
                _isAlive[idx] = true;
            }
        }
    }

    /// <summary>Snapshot words: active slots, health, meter, alive flags, tag cooldowns.</summary>
    public const int StateWords = 2 + MaxFighters + MaxFighters + MaxFighters + 2;

    /// <summary>Writes the whole team state (rollback savestate).</summary>
    public void ExportState(Span<int> dest)
    {
        int o = 0;
        for (int t = 0; t < 2; t++) dest[o++] = _activeIndex[t];
        for (int i = 0; i < MaxFighters; i++) dest[o++] = _health[i];
        for (int i = 0; i < MaxFighters; i++) dest[o++] = _meter[i];
        for (int i = 0; i < MaxFighters; i++) dest[o++] = _isAlive[i] ? 1 : 0;
        for (int t = 0; t < 2; t++) dest[o++] = _tagCooldown[t];
    }

    /// <summary>Restores state written by <see cref="ExportState"/>.</summary>
    public void ImportState(ReadOnlySpan<int> source)
    {
        int o = 0;
        for (int t = 0; t < 2; t++) _activeIndex[t] = source[o++];
        for (int i = 0; i < MaxFighters; i++) _health[i] = source[o++];
        for (int i = 0; i < MaxFighters; i++) _meter[i] = source[o++];
        for (int i = 0; i < MaxFighters; i++) _isAlive[i] = source[o++] != 0;
        for (int t = 0; t < 2; t++) _tagCooldown[t] = source[o++];
    }

    /// <summary>Brings the next living partner in, ignoring the cooldown (a fallen fighter is replaced at once). False when nobody is left.</summary>
    public bool ForceTag(int team)
    {
        int currentSlot = _activeIndex[team];
        for (int i = 1; i < MaxTeamSize; i++)
        {
            int candidate = (currentSlot + i) % MaxTeamSize;
            if (!_isAlive[(team * MaxTeamSize) + candidate]) continue;
            _activeIndex[team] = candidate;
            _tagCooldown[team] = 120;
            return true;
        }
        return false;
    }

    public int ActiveIndex(int team) => _activeIndex[team];
    public CharacterConfig ActiveFighter(int team) => _rosters[team][_activeIndex[team]];
    public int Health(int team, int slot) => _health[team * MaxTeamSize + slot];
    public int Meter(int team) => _meter[team * MaxTeamSize + _activeIndex[team]];
    public bool IsAlive(int team, int slot) => _isAlive[team * MaxTeamSize + slot];
    public int TagCooldown(int team) => _tagCooldown[team];

    public void SetHealth(int team, int slot, int value)
    {
        _health[team * MaxTeamSize + slot] = value;
        if (value <= 0)
        {
            _isAlive[team * MaxTeamSize + slot] = false;
        }
    }

    public void AddMeter(int team, int amount)
    {
        int idx = team * MaxTeamSize + _activeIndex[team];
        _meter[idx] = System.Math.Clamp(_meter[idx] + amount, 0, 1000);
    }

    public bool TryTag(int team)
    {
        if (_tagCooldown[team] > 0) return false;

        int currentSlot = _activeIndex[team];

        // Find next alive fighter (never self-tag)
        for (int i = 1; i < MaxTeamSize; i++)
        {
            int candidate = (currentSlot + i) % MaxTeamSize;
            if (_isAlive[team * MaxTeamSize + candidate])
            {
                _activeIndex[team] = candidate;
                _tagCooldown[team] = 120; // 2 seconds at 60fps
                return true;
            }
        }

        return false; // No alive partner to tag to
    }

    public void Update()
    {
        for (int team = 0; team < 2; team++)
        {
            if (_tagCooldown[team] > 0)
            {
                _tagCooldown[team]--;
            }
        }
    }

    public bool IsTeamWiped(int team)
    {
        for (int slot = 0; slot < MaxTeamSize; slot++)
        {
            if (_isAlive[team * MaxTeamSize + slot])
            {
                return false;
            }
        }
        return true;
    }

    public int CountAlive(int team)
    {
        int count = 0;
        for (int slot = 0; slot < MaxTeamSize; slot++)
        {
            if (_isAlive[team * MaxTeamSize + slot])
            {
                count++;
            }
        }
        return count;
    }
}
