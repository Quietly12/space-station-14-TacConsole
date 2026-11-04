using Content.Shared._Starlight.Magic.Components;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private static readonly TimeSpan MinorIllusionDuration = TimeSpan.FromSeconds(15);
    private static readonly Color[] MinorIllusionTints =
    [
        Color.FromHex("#fff7ed"),
        Color.FromHex("#f2fff0"),
        Color.FromHex("#fff0f0"),
    ];

    [Dependency] private IPrototypeManager _minorIllusionPrototypes = default!;
    [Dependency] private IComponentFactory _minorIllusionComponents = default!;
    private readonly List<string> _minorIllusionCreatures = [];

    private void InitializeMinorIllusion()
    {
        SubscribeLocalEvent<MinorIllusionSpellEvent>(OnMinorIllusionSpell);
    }

    private void EnsureMinorIllusionCreatures()
    {
        if (_minorIllusionCreatures.Count > 0)
            return;
        foreach (var prototype in _minorIllusionPrototypes.EnumeratePrototypes<EntityPrototype>())
        {
            if (!prototype.Abstract && prototype.ID.StartsWith("Mob") &&
                prototype.HasComponent<MobStateComponent>(_minorIllusionComponents))
                _minorIllusionCreatures.Add(prototype.ID);
        }
    }

    private void OnMinorIllusionSpell(MinorIllusionSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer) ||
            !_wizardMobState.IsAlive(args.Performer))
            return;

        EnsureMinorIllusionCreatures();
        if (_minorIllusionCreatures.Count == 0)
            return;

        var origin = _wizardTransform.GetMapCoordinates(args.Performer);
        var destination = _wizardTransform.ToMapCoordinates(args.Target);
        if (origin.MapId != destination.MapId ||
            (origin.Position - destination.Position).LengthSquared() > 16f)
            return;

        var illusion = Spawn("WizardMinorIllusion", args.Target);
        var comp = Comp<WizardMinorIllusionComponent>(illusion);
        comp.CreaturePrototype = _minorIllusionCreatures[_random.Next(_minorIllusionCreatures.Count)];
        comp.ShimmerColor = MinorIllusionTints[_random.Next(MinorIllusionTints.Length)];
        comp.ExpiresAt = _wizardTiming.CurTime + MinorIllusionDuration;
        Dirty(illusion, comp);
        args.Handled = true;
    }

    private void UpdateMinorIllusions()
    {
        var query = EntityQueryEnumerator<WizardMinorIllusionComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.ExpiresAt > _wizardTiming.CurTime || TerminatingOrDeleted(uid))
                continue;

            Spawn("WizardMinorIllusionPuff", Transform(uid).Coordinates);
            QueueDel(uid);
        }
    }
}
