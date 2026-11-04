using Content.Shared._Starlight.Thaven;

namespace Content.Server._Starlight.Thaven;

public sealed partial class ThavenMoodsSystem
{
    public void PrepareForWizardCurse(Entity<ThavenMoodsComponent> ent)
    {
        ent.Comp.FollowsSharedMoods = false;
        ent.Comp.MoodDatasets.Clear();
        if (ent.Comp.Action is null)
            ent.Comp.Action = _actions.AddAction(ent.Owner, _actionViewMoods);
        Dirty(ent);
        UpdateBUIState(ent);
    }
}
