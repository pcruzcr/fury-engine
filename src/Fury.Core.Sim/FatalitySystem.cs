using Fury.Core.Data;
using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Fatality/finisher system for Mortal Kombat-style finishing moves.
/// Triggered when opponent is in a vulnerable state at low health.
/// All state is deterministic and serializable.
/// </summary>
public sealed class FatalitySystem
{
    public const int MaxFatalities = 4;

    public enum FatalityType : byte
    {
        None = 0,
        Fatality = 1,
        Brutality = 2,
        Friendship = 3,
        Animality = 4,
        Babality = 5,
        Mercy = 6,
        StageFatality = 7
    }

    public enum FatalityState : byte
    {
        Inactive = 0,
        Available = 1,
        Inputting = 2,
        Executing = 3,
        Completed = 4
    }

    private readonly FatalityData[] _fatalities;

    public FatalitySystem(FightConfig cfg)
    {
        _ = cfg;
        _fatalities = new FatalityData[MaxFatalities];
        for (int i = 0; i < MaxFatalities; i++)
        {
            _fatalities[i] = new FatalityData();
        }
    }

    public int Count { get; private set; }

    /// <summary>Dynamic words per configured finisher in a snapshot.</summary>
    public const int WordsPerSlot = 3;

    /// <summary>Number of snapshot words for the finishers configured so far.</summary>
    public int StateWords => Count * WordsPerSlot;

    /// <summary>Writes the mutable state of every configured finisher (rollback savestate).</summary>
    public void ExportState(Span<int> dest)
    {
        for (int i = 0; i < Count; i++)
        {
            int o = i * WordsPerSlot;
            dest[o] = (int)_fatalities[i].State;
            dest[o + 1] = _fatalities[i].InputTimer;
            dest[o + 2] = _fatalities[i].ExecutionTimer;
        }
    }

    /// <summary>Restores state written by <see cref="ExportState"/>.</summary>
    public void ImportState(ReadOnlySpan<int> source)
    {
        for (int i = 0; i < Count; i++)
        {
            int o = i * WordsPerSlot;
            ref var item = ref _fatalities[i];
            item.State = (FatalityState)source[o];
            item.InputTimer = source[o + 1];
            item.ExecutionTimer = source[o + 2];
        }
    }

    public void AddFatality(in FatalityData data)
    {
        if (Count >= MaxFatalities) return;
        _fatalities[Count] = data;
        Count++;
    }

    public void Clear()
    {
        Count = 0;
        for (int i = 0; i < MaxFatalities; i++)
        {
            _fatalities[i] = new FatalityData();
        }
    }

    public void Update()
    {
        for (int i = 0; i < Count; i++)
        {
            ref var f = ref _fatalities[i];
            switch (f.State)
            {
                case FatalityState.Inputting:
                    f.InputTimer--;
                    if (f.InputTimer <= 0)
                    {
                        f.State = FatalityState.Available;
                    }
                    break;
                case FatalityState.Executing:
                    f.ExecutionTimer--;
                    if (f.ExecutionTimer <= 0)
                    {
                        f.State = FatalityState.Completed;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Checks if a fatality is available for the given fighter.
    /// </summary>
    public bool IsAvailable(int fatalityIndex, int health, int opponentHealth)
    {
        if (fatalityIndex < 0 || fatalityIndex >= Count) return false;
        ref var f = ref _fatalities[fatalityIndex];
        if (f.State != FatalityState.Available) return false;
        return opponentHealth <= f.HealthThreshold;
    }

    /// <summary>
    /// Attempts to start fatality input sequence.
    /// </summary>
    public bool TryStartInput(int fatalityIndex)
    {
        if (fatalityIndex < 0 || fatalityIndex >= Count) return false;
        ref var f = ref _fatalities[fatalityIndex];
        if (f.State != FatalityState.Available) return false;

        f.State = FatalityState.Inputting;
        f.InputTimer = f.InputWindow;
        return true;
    }

    /// <summary>
    /// Attempts to execute fatality after successful input.
    /// </summary>
    public bool TryExecute(int fatalityIndex)
    {
        if (fatalityIndex < 0 || fatalityIndex >= Count) return false;
        ref var f = ref _fatalities[fatalityIndex];
        if (f.State != FatalityState.Inputting) return false;

        f.State = FatalityState.Executing;
        f.ExecutionTimer = f.ExecutionDuration;
        return true;
    }

    /// <summary>
    /// Gets the fatality type.
    /// </summary>
    public FatalityType GetFatalityType(int fatalityIndex)
    {
        if (fatalityIndex < 0 || fatalityIndex >= Count) return FatalityType.None;
        return _fatalities[fatalityIndex].Type;
    }

    /// <summary>
    /// Gets a copy of the input sequence for a fatality (debug/UI only — never in the hot path).
    /// </summary>
    public int[] GetInputSequence(int fatalityIndex)
    {
        if (fatalityIndex < 0 || fatalityIndex >= Count) return [];
        return _fatalities[fatalityIndex].GetInputsCopy();
    }

    /// <summary>
    /// Verifies a candidate input sequence against the stored one (exact match, deterministic).
    /// Input matching is the caller's responsibility: call this after <see cref="TryStartInput"/>
    /// and only call <see cref="TryExecute"/> on success.
    /// </summary>
    public bool VerifyInputs(int fatalityIndex, ReadOnlySpan<int> candidate)
    {
        if (fatalityIndex < 0 || fatalityIndex >= Count) return false;
        return _fatalities[fatalityIndex].Matches(candidate);
    }

    /// <summary>Sets the stored input sequence (setup-time only, before the fight starts).</summary>
    public void SetInputs(int fatalityIndex, ReadOnlySpan<int> inputs)
    {
        if (fatalityIndex < 0 || fatalityIndex >= Count) return;
        ref var f = ref _fatalities[fatalityIndex];
        f.SetInputs(inputs);
    }

    public FatalityData GetFatality(int index)
    {
        if (index < 0 || index >= Count) return new FatalityData();
        return _fatalities[index];
    }

    /// <summary>
    /// Serializable fatality data. Static setup (type, thresholds, durations, inputs) is
    /// fixed before the fight like move tables (ADR-003); dynamic state (State, timers)
    /// must be snapshotted for rollback — see F8 integration note on FightState.
    /// Inputs are stored as fixed value fields (no managed references) for AOT/determinism.
    /// </summary>
    public record struct FatalityData
    {
        public const int MaxInputs = 8;

        public FatalityType Type { get; init; }
        public FatalityState State { get; set; }
        public int HealthThreshold { get; init; }
        public int InputWindow { get; init; }
        public int ExecutionDuration { get; init; }
        public int InputTimer { get; set; }
        public int ExecutionTimer { get; set; }
        public int InputLength { get; private set; }
        public int Input0 { get; private set; }
        public int Input1 { get; private set; }
        public int Input2 { get; private set; }
        public int Input3 { get; private set; }
        public int Input4 { get; private set; }
        public int Input5 { get; private set; }
        public int Input6 { get; private set; }
        public int Input7 { get; private set; }
        public bool RequiresStage { get; init; }
        public Fp Damage { get; init; }

        public FatalityData()
        {
            Type = FatalityType.Fatality;
            State = FatalityState.Available;
            HealthThreshold = 200;
            InputWindow = 120;
            ExecutionDuration = 180;
            Damage = Fp.FromInt(1000);
        }

        public void SetInputs(ReadOnlySpan<int> inputs)
        {
            int n = System.Math.Min(inputs.Length, MaxInputs);
            InputLength = n;
            Input0 = n > 0 ? inputs[0] : 0;
            Input1 = n > 1 ? inputs[1] : 0;
            Input2 = n > 2 ? inputs[2] : 0;
            Input3 = n > 3 ? inputs[3] : 0;
            Input4 = n > 4 ? inputs[4] : 0;
            Input5 = n > 5 ? inputs[5] : 0;
            Input6 = n > 6 ? inputs[6] : 0;
            Input7 = n > 7 ? inputs[7] : 0;
        }

        public readonly int GetInputAt(int i) => i switch
        {
            0 => Input0,
            1 => Input1,
            2 => Input2,
            3 => Input3,
            4 => Input4,
            5 => Input5,
            6 => Input6,
            7 => Input7,
            _ => 0,
        };

        public readonly bool Matches(ReadOnlySpan<int> candidate)
        {
            if (candidate.Length != InputLength) return false;
            for (int i = 0; i < InputLength; i++)
            {
                if (candidate[i] != GetInputAt(i)) return false;
            }
            return true;
        }

        public readonly int[] GetInputsCopy()
        {
            var arr = new int[InputLength];
            for (int i = 0; i < InputLength; i++) arr[i] = GetInputAt(i);
            return arr;
        }
    }
}
