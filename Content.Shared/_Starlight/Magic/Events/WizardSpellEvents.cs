using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.RatKing;
using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.Magic.Events;

public sealed partial class FrogCurseSpellEvent : EntityTargetActionEvent;

public sealed partial class PerspectiveSpellEvent : EntityTargetActionEvent;

public sealed partial class MagicMissileSpellEvent : InstantActionEvent;

public sealed partial class ArcaneBarrageSpellEvent : InstantActionEvent;

public sealed partial class LightningBarrageSpellEvent : InstantActionEvent;

public sealed partial class ChainLightningSpellEvent : EntityTargetActionEvent;

public sealed partial class DimensionDoorSpellEvent : WorldTargetActionEvent;

public sealed partial class CloseDimensionDoorSpellEvent : InstantActionEvent;

public sealed partial class SnailCurseSpellEvent : EntityTargetActionEvent;

public sealed partial class NoirCurseSpellEvent : EntityTargetActionEvent;

public sealed partial class AncientCurseSpellEvent : EntityTargetActionEvent;

public sealed partial class MoodsCurseSpellEvent : EntityTargetActionEvent;

public sealed partial class MassLocationSwapSpellEvent : InstantActionEvent;

public sealed partial class StaticElectricitySpellEvent : EntityTargetActionEvent;

public sealed partial class LocalizedZeroGravitySpellEvent : InstantActionEvent;

public sealed partial class SummonBeeSwarmSpellEvent : InstantActionEvent;

public sealed partial class DoomCurseSpellEvent : EntityTargetActionEvent;

public sealed partial class InteriorRedecoratingSpellEvent : InstantActionEvent;

public sealed partial class MinorIllusionSpellEvent : WorldTargetActionEvent;

public sealed partial class SpeakWithDeadSpellEvent : InstantActionEvent;

public sealed partial class ArcaneChalkSpellEvent : InstantActionEvent;

public sealed partial class ToggleMageHandSpellEvent : InstantActionEvent;

public sealed partial class TornadoSpellEvent : WorldTargetActionEvent;

public sealed partial class GreaseSpellEvent : WorldTargetActionEvent;

public sealed partial class SleepingCloudSpellEvent : InstantActionEvent;

public sealed partial class MagicalSaunaSpellEvent : InstantActionEvent;

public sealed partial class StinkCloudSpellEvent : InstantActionEvent;

public sealed partial class TrashGolemSpellEvent : InstantActionEvent;

public sealed partial class TrashGolemOrderActionEvent : InstantActionEvent
{
    [DataField] public RatKingOrderType Type;
}

public sealed partial class CombustSpellEvent : InstantActionEvent;

public sealed partial class AsphyxiateSpellEvent : WorldTargetActionEvent;

public sealed partial class ConeOfHellfireSpellEvent : WorldTargetActionEvent;

public sealed partial class ConeOfColdSpellEvent : WorldTargetActionEvent;

public sealed partial class WizardInvisibilityCurseSpellEvent : EntityTargetActionEvent;

public sealed partial class WizardDominateMindSpellEvent : EntityTargetActionEvent;

public sealed partial class WizardMeteorSwarmSpellEvent : WorldTargetActionEvent;

public sealed partial class WizardStopTimeSpellEvent : InstantActionEvent;

public sealed partial class ReturnMageHandSpellEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class PerspectiveChairDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class StaticElectricityGloveDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class StaticElectricityForceFeedDoAfterEvent : SimpleDoAfterEvent;
