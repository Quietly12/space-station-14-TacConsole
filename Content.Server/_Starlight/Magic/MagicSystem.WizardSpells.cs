using Content.Server.Actions;
using Content.Server.Polymorph.Systems;
using Content.Shared.Fluids;
using Content.Shared.Gravity;
using Content.Shared.Interaction;
using Content.Shared.DoAfter;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    [Dependency] private ActionsSystem _wizardActions = default!;
    [Dependency] private PolymorphSystem _wizardPolymorph = default!;
    [Dependency] private SharedDoAfterSystem _wizardDoAfter = default!;
    [Dependency] private MobStateSystem _wizardMobState = default!;
    [Dependency] private SharedStunSystem _wizardStun = default!;
    [Dependency] private SharedGunSystem _wizardGun = default!;
    [Dependency] private SharedPhysicsSystem _wizardPhysics = default!;
    [Dependency] private SharedTransformSystem _wizardTransform = default!;
    [Dependency] private EntityLookupSystem _wizardLookup = default!;
    [Dependency] private IGameTiming _wizardTiming = default!;
    [Dependency] private SharedPuddleSystem _wizardPuddles = default!;
    [Dependency] private SharedGravitySystem _wizardGravity = default!;
    [Dependency] private SharedInteractionSystem _wizardInteraction = default!;

    private void InitializeStarlightWizardSpells()
    {
        InitializeExistingCurses();
        InitializeFrogCurse();
        InitializePerspectiveCurse();
        InitializeMagicMissile();
        InitializeArcaneBarrage();
        InitializeSnailCurse();
        InitializeNoirCurse();
        InitializeAncientCurse();
        InitializeMoodsCurse();
        InitializeMassLocationSwap();
        InitializeStaticElectricityCurse();
        InitializeLocalizedZeroGravity();
        InitializeSummonBeeSwarm();
        InitializeDoomCurse();
        InitializeInteriorRedecorating();
        InitializeMinorIllusion();
        InitializeSpeakWithDead();
        InitializeArcaneChalk();
        InitializeMageHand();
        InitializeTornado();
        InitializeGrease();
        InitializeAtmosCloudSpells();
        InitializeTrashGolem();
        InitializeElementalCones();
        InitializeSummonGunStaff();
        InitializeRandomCreatureWand();
        InitializeChainLightning();
        InitializeDimensionDoor();
        InitializeInvisibilityCurse();
        InitializeDominateMind();
        InitializeApprenticeshipContract();
        InitializeRandomCurseWand();
        InitializeMeteorSwarm();
        InitializeStopTime();
    }

    private void UpdateStarlightWizardSpells(float frameTime)
    {
        UpdatePerspectiveCurse();
        UpdateMagicMissiles();
        UpdateArcaneBarrage();
        UpdateSnailCurse();
        UpdateAncientCurse();
        UpdateLocalizedZeroGravity();
        UpdateDoomCurse();
        UpdateMinorIllusions();
        UpdateSpeakWithDead();
        UpdateArcaneChalk();
        UpdateMageHands();
        UpdateTornadoes(frameTime);
        UpdateSummonGunVolleys();
        UpdateDimensionDoors();
        UpdateMeteorSwarm();
        UpdateStopTime();
        UpdateAsphyxiate();
        UpdateWizardGasSparks();
        UpdateWizardGasWind();
        UpdateElementalCones();
        UpdateDominateMind();
    }
}
