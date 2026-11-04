using Content.Shared._Starlight.Magic.Events;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const int SummonedAngryBees = 5;

    private void InitializeSummonBeeSwarm()
    {
        SubscribeLocalEvent<SummonBeeSwarmSpellEvent>(OnSummonBeeSwarmSpell);
    }

    private void OnSummonBeeSwarmSpell(SummonBeeSwarmSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        args.Handled = TrySummonBeeSwarm(args.Performer);
    }

    public bool TrySummonBeeSwarm(EntityUid caster)
    {
        if (!_wizardMobState.IsAlive(caster))
            return false;

        for (var i = 0; i < SummonedAngryBees; i++)
            Spawn("MobAngryBee", Transform(caster).Coordinates);
        return true;
    }
}
