using Content.Shared._Starlight.Magic.Components;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._Starlight.Roles;

public sealed class WizardAllySystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    private static readonly ProtoId<FactionIconPrototype> WizardAllyIcon = "WizardAllyIcon";
    private static readonly ProtoId<FactionIconPrototype> WizardApprenticeIcon = "WizardApprenticeIcon";
    private static readonly ProtoId<FactionIconPrototype> WizardDominatedIcon = "WizardDominatedIcon";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WizardAllyComponent, GetStatusIconsEvent>(OnGetStatusIcons);
        SubscribeLocalEvent<WizardDominationIconComponent, GetStatusIconsEvent>(OnGetDominatedStatusIcons);
    }

    private void OnGetStatusIcons(Entity<WizardAllyComponent> ent, ref GetStatusIconsEvent args)
    {
        var iconId = HasComp<WizardApprenticeIconComponent>(ent.Owner) ? WizardApprenticeIcon : WizardAllyIcon;
        if (_prototypes.TryIndex(iconId, out var icon))
            args.StatusIcons.Add(icon);
    }

    private void OnGetDominatedStatusIcons(Entity<WizardDominationIconComponent> ent, ref GetStatusIconsEvent args)
    {
        if (_prototypes.TryIndex(WizardDominatedIcon, out var icon))
            args.StatusIcons.Add(icon);
    }
}
