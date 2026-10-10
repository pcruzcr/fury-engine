// Fighter.Core — fight observation (R1 refactor, Observer pattern).
// WHY (§61 observability): metrics, tracers, replay recorders and debug tools must observe
// fights WITHOUT touching simulation state. The simulation calls into this interface at
// deterministic points; a null observer costs one branch per step. Observation output is
// never part of snapshots, hashes or replays.
namespace Fighter.Core;

/// <summary>Deterministic observation hooks for a fight. All calls happen in-fight, in order.</summary>
public interface IFightObserver
{
    /// <summary>Called once per completed step, after timers/energy, before Frame++.</summary>
    void OnFrame(FightSimulation sim, int frame);

    /// <summary>Called once per simulation event, in emission order, same step as OnFrame.</summary>
    void OnSimEvent(FightSimulation sim, SimEvent e);
}
