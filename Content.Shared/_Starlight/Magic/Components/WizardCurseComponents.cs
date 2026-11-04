using System.Collections.Generic;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Content.Shared.FixedPoint;
using System.Numerics;

namespace Content.Shared._Starlight.Magic.Components;

[RegisterComponent]
public sealed partial class WizardCluwneCurseComponent : Component
{
    public EntityUid? IndicatorAction;
}

[RegisterComponent]
public sealed partial class WizardSlipperyCurseComponent : Component
{
    public EntityUid? IndicatorAction;
}

[RegisterComponent]
public sealed partial class FrogCurseComponent : Component
{
    public EntityUid? IndicatorAction;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PerspectiveCurseComponent : Component
{
    [AutoNetworkedField]
    public bool RotationLocked = true;

    public Angle OriginalCameraAngle;
    public Angle OriginalFacing;
    public MapId OriginalMap;
    public TimeSpan UnlockAt;
    public TimeSpan? SeatedSince;
    public EntityUid? Chair;
    public EntityUid? IndicatorAction;
    public bool DoAfterRunning;
    public bool Restoring;
    public TimeSpan SpinUntil;
    public TimeSpan NextSpin;
    public int SpinStep;
}

[RegisterComponent]
public sealed partial class WizardCurseChairComponent : Component;

[RegisterComponent]
public sealed partial class MagicMissileHomingComponent : Component
{
    public EntityUid Target;
    public TimeSpan ExpiresAt;
}

[RegisterComponent]
public sealed partial class SnailCurseComponent : Component
{
    public TimeSpan ExpiresAt;
    public TimeSpan NextTrailAt;
    public Vector2 LastTrailPosition;
    public MapId LastTrailMap;
    public EntityUid? IndicatorAction;
}

[RegisterComponent]
public sealed partial class NoirCurseComponent : Component
{
    public bool AddedOverlay;
    public EntityUid? IndicatorAction;
}

[RegisterComponent]
public sealed partial class NoirCureGlassComponent : Component;

[RegisterComponent]
public sealed partial class AncientCurseComponent : Component
{
    public TimeSpan NextStepAt;
    public TimeSpan StepStartedAt;
    public EntityUid StepGrid;
    public Vector2 StepFrom;
    public Vector2 StepTo;
    public bool Stepping;
    public EntityUid? IndicatorAction;
}

[RegisterComponent]
public sealed partial class WizardMoodsCurseComponent : Component
{
    public EntityUid? IndicatorAction;
    public FixedPoint2 PsicodineMetabolized;
}

[RegisterComponent]
public sealed partial class StaticElectricityCurseComponent : Component
{
    public EntityUid? IndicatorAction;
    public readonly Dictionary<EntityUid, TimeSpan> NextShockByToucher = new();
}

/// <summary>
/// Marks the specific glove prototypes that may cure the static electricity curse.
/// </summary>
[RegisterComponent]
public sealed partial class StaticElectricityCurativeGloveComponent : Component;

[RegisterComponent, NetworkedComponent]
public sealed partial class WizardZeroGravityComponent : Component
{
    public bool Removing;
    public EntityCoordinates? LastPosition;
    public MapCoordinates? LastMapPosition;
    public TimeSpan LastPositionAt;
}

public enum DoomObjective : byte
{
    Hug,
    GetHugged,
    Sit,
    LieDown,
    Eat,
    Drink,
    GetPunched,
    Throw,
    Punch,
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DoomCurseComponent : Component
{
    [AutoNetworkedField]
    public DoomObjective Objective;

    [AutoNetworkedField]
    public TimeSpan ExpiresAt;

    public EntityUid? IndicatorAction;
    public EntityUid? AudioStream;
    public EntityUid? Caster;
    public TimeSpan? SittingSince;
    public TimeSpan? LyingSince;
    public int LastShownSeconds = -1;
}
