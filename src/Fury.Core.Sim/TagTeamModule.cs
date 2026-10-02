using Fury.Core.Data;

namespace Fury.Core.Sim;

/// <summary>
/// Tag teams (Marvel vs Capcom / KOF style): each side has up to <see cref="TagTeamSystem.MaxTeamSize"/> characters but only
/// one is on screen. <see cref="InputButton.Macro4"/> in a neutral state tags the next living partner in (cooldown
/// applies); a fighter whose health reaches 0 is replaced at once by a partner and the round only ends when the whole
/// team is down. Each character keeps its own health; the EX meter is shared by the team. The partners' health, who is
/// alive and the cooldowns are part of the rollback snapshot, and the swapped-in character's whole move table takes effect.
/// </summary>
public sealed class TagTeamModule : SimModule
{
    private const int TagInvulnFrames = 30;

    public TagTeamModule(FightConfig cfg, CharacterConfig[] teamA, CharacterConfig[] teamB)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        ValidateTeam(teamA, nameof(teamA));
        ValidateTeam(teamB, nameof(teamB));
        Team = new TagTeamSystem(cfg, teamA, teamB);

        // Slots without a character are dead from the start, so they are never tagged in.
        MarkMissingSlots(0, teamA.Length);
        MarkMissingSlots(1, teamB.Length);
        for (int slot = 0; slot < teamA.Length; slot++) Team.SetHealth(0, slot, teamA[slot].MaxHealth);
        for (int slot = 0; slot < teamB.Length; slot++) Team.SetHealth(1, slot, teamB[slot].MaxHealth);
    }

    public TagTeamSystem Team { get; }

    public override int StateWords => TagTeamSystem.StateWords;

    private static void ValidateTeam(CharacterConfig[] team, string name)
    {
        ArgumentNullException.ThrowIfNull(team, name);
        if (team.Length is < 1 or > TagTeamSystem.MaxTeamSize) throw new ArgumentException($"A team has 1..{TagTeamSystem.MaxTeamSize} characters.", name);
    }

    private void MarkMissingSlots(int team, int length)
    {
        for (int slot = length; slot < TagTeamSystem.MaxTeamSize; slot++) Team.SetHealth(team, slot, 0);
    }

    public override CharacterConfig ActiveCharacter(int fighter) => Team.ActiveFighter(fighter);

    public override void Tick(FightState state)
    {
        Team.Update();
        if (state.RoundState != 0) return;

        for (int f = 0; f < FightState.FighterCount; f++)
        {
            if (!FighterStateId.IsNeutral(state.GetStateId(f))) continue;
            if (Sim.Input(f).Pressed(InputButton.Macro4) && Team.TagCooldown(f) == 0) SwapIn(state, f, forced: false);
        }
    }

    public override bool TrySubstituteKo(int fighter, FightState state)
    {
        if (state.GetHealth(fighter) != 0) return false;
        Team.SetHealth(fighter, Team.ActiveIndex(fighter), 0); // marks the fallen character dead
        return SwapIn(state, fighter, forced: true);
    }

    private bool SwapIn(FightState state, int f, bool forced)
    {
        int outgoing = Team.ActiveIndex(f);
        Team.SetHealth(f, outgoing, state.GetHealth(f));
        if (!(forced ? Team.ForceTag(f) : Team.TryTag(f))) return false;

        int incoming = Team.ActiveIndex(f);
        state.SetHealth(f, Team.Health(f, incoming));

        // The new character starts from a clean neutral state, briefly protected.
        state.SetStateId(f, FighterStateId.Idle);
        state.SetHitstun(f, 0);
        state.SetBlockstun(f, 0);
        state.SetHitstop(f, 0);
        state.SetMoveIndex(f, 0);
        state.SetHasHit(f, false);
        state.SetConnect(f, 0);
        state.SetCrouching(f, false);
        state.SetRaging(f, false);
        state.SetComboCount(1 - f, 0);
        state.SetComboDamage(1 - f, 0);
        state.SetInvuln(f, TagInvulnFrames);
        Sim.EmitEvent(SimEventId.Tagged, SimEventPayload.Pack(f, incoming, 0));
        return true;
    }

    public override void Serialize(Span<int> dest) => Team.ExportState(dest);

    public override void Deserialize(ReadOnlySpan<int> source) => Team.ImportState(source);
}
