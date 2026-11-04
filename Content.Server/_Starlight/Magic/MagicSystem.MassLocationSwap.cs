using System.Collections.Generic;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Buckle.Components;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    // Keep the swap local to one grid so every destination was already occupied safely.
    private const float MassLocationSwapRadius = 5f;

    private void InitializeMassLocationSwap()
    {
        SubscribeLocalEvent<MassLocationSwapSpellEvent>(OnMassLocationSwapSpell);
    }

    private void OnMassLocationSwapSpell(MassLocationSwapSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        args.Handled = TryMassLocationSwap(args.Performer);
    }

    public bool TryMassLocationSwap(EntityUid caster)
    {
        if (!_wizardMobState.IsAlive(caster))
            return false;

        var casterXform = Transform(caster);
        var participants = new List<EntityUid>();
        foreach (var target in _wizardLookup.GetEntitiesInRange<MobStateComponent>(
                     casterXform.Coordinates, MassLocationSwapRadius, LookupFlags.Uncontained))
        {
            var uid = target.Owner;
            var xform = Transform(uid);
            if (!_wizardMobState.IsAlive(uid) ||
                xform.GridUid != casterXform.GridUid ||
                CompOrNull<BuckleComponent>(uid)?.BuckledTo is not null)
                continue;

            participants.Add(uid);
        }

        if (participants.Count < 2)
            return false;

        _random.Shuffle(participants);
        var destinations = new List<EntityCoordinates>(participants.Count);
        foreach (var participant in participants)
        {
            destinations.Add(Transform(participant).Coordinates);
            Spawn("EffectVoidBlink", Transform(participant).Coordinates);
        }
        // A cyclic permutation guarantees that every participant changes places.
        for (var i = 0; i < participants.Count; i++)
        {
            _wizardTransform.SetCoordinates(participants[i], destinations[(i + 1) % participants.Count]);
            Spawn("EffectVoidBlink", Transform(participants[i]).Coordinates);
        }

        return true;
    }
}
