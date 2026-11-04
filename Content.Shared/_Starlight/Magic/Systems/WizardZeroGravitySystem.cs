using Content.Shared._Starlight.Magic.Components;
using Content.Shared.Gravity;

namespace Content.Shared._Starlight.Magic.Systems;

public sealed partial class WizardZeroGravitySystem : EntitySystem
{
    [Dependency] private SharedGravitySystem _gravity = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WizardZeroGravityComponent, IsWeightlessEvent>(OnIsWeightless);
        SubscribeLocalEvent<WizardZeroGravityComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WizardZeroGravityComponent, ComponentRemove>(OnRemove);
    }

    private void OnIsWeightless(Entity<WizardZeroGravityComponent> ent, ref IsWeightlessEvent args)
    {
        if (args.Handled || ent.Comp.Removing)
            return;

        args.Handled = true;
        args.IsWeightless = true;
    }

    private void OnStartup(Entity<WizardZeroGravityComponent> ent, ref ComponentStartup args)
    {
        _gravity.RefreshWeightless(ent.Owner);
    }

    private void OnRemove(Entity<WizardZeroGravityComponent> ent, ref ComponentRemove args)
    {
        if (TerminatingOrDeleted(ent.Owner) || !HasComp<GravityAffectedComponent>(ent.Owner))
            return;

        ent.Comp.Removing = true;
        _gravity.RefreshWeightless(ent.Owner);
    }
}
