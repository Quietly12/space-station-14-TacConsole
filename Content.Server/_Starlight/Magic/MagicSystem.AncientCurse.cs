using System.Numerics;
using System.Collections.Generic;
using Content.Shared._Funkystation.Fluids;
using Content.Shared._Starlight.Magic.Components;
using Content.Shared._Starlight.Magic.Events;
using Content.Shared.ActionBlocker;
using Content.Shared.Buckle.Components;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Physics;
using Content.Shared.Stunnable;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;

namespace Content.Server.Magic;

public sealed partial class MagicSystem
{
    private const string AncientCurseIndicator = "ActionWizardAncientCurseIndicator";
    private static readonly TimeSpan AncientStepDuration = TimeSpan.FromSeconds(0.3);
    private static readonly TimeSpan AncientStepRest = TimeSpan.FromSeconds(0.04);
    private readonly HashSet<EntityUid> _ancientPendingUnblock = new();

    [Dependency] private ActionBlockerSystem _ancientActionBlocker = default!;

    private void InitializeAncientCurse()
    {
        SubscribeLocalEvent<AncientCurseSpellEvent>(OnAncientCurseSpell);
        SubscribeLocalEvent<AncientCurseComponent, UpdateCanMoveEvent>(OnAncientCanMove);
        SubscribeLocalEvent<AncientCurseComponent, SpilledOnEvent>(OnAncientSplashed);
        SubscribeLocalEvent<AncientCurseComponent, ComponentShutdown>(OnAncientCurseShutdown);
    }

    private void OnAncientCurseSpell(AncientCurseSpellEvent args)
    {
        if (args.Handled || !PassesSpellPrerequisites(args.Action, args.Performer))
            return;

        args.Handled = TryApplyAncientCurse(args.Target);
    }

    public bool TryApplyAncientCurse(EntityUid target)
    {
        if (!_wizardMobState.IsAlive(target) || !HasComp<ActorComponent>(target) ||
            !HasComp<InputMoverComponent>(target) || HasComp<AncientCurseComponent>(target))
            return false;

        var xform = Transform(target);
        if (xform.GridUid is not { } grid || xform.ParentUid != grid)
            return false;

        var curse = EnsureComp<AncientCurseComponent>(target);
        curse.NextStepAt = _wizardTiming.CurTime;
        _wizardActions.AddAction(target, ref curse.IndicatorAction, AncientCurseIndicator);
        _ancientActionBlocker.UpdateCanMove(target);
        return true;
    }

    private void OnAncientCanMove(Entity<AncientCurseComponent> ent, ref UpdateCanMoveEvent args)
    {
        var xform = Transform(ent);
        if (xform.GridUid is { } grid && xform.ParentUid == grid)
            args.Cancel();
    }

    private void OnAncientSplashed(Entity<AncientCurseComponent> ent, ref SpilledOnEvent args)
    {
        if (args.Solution.GetTotalPrototypeQuantity("Holywater") > 0)
        {
            RemComp<AncientCurseComponent>(ent);
            _ancientActionBlocker.UpdateCanMove(ent);
        }
    }

    private void UpdateAncientCurse()
    {
        foreach (var uid in _ancientPendingUnblock)
        {
            if (!TerminatingOrDeleted(uid))
                _ancientActionBlocker.UpdateCanMove(uid);
        }
        _ancientPendingUnblock.Clear();

        var query = EntityQueryEnumerator<AncientCurseComponent, InputMoverComponent>();
        while (query.MoveNext(out var uid, out var curse, out var mover))
        {
            _ancientActionBlocker.UpdateCanMove(uid, mover);
            if (curse.Stepping)
            {
                AdvanceAncientStep(uid, curse);
                continue;
            }

            if (_wizardTiming.CurTime < curse.NextStepAt ||
                !_wizardMobState.IsAlive(uid) ||
                HasComp<StunnedComponent>(uid) ||
                CompOrNull<BuckleComponent>(uid)?.BuckledTo is not null)
                continue;

            var direction = AncientStepDirection(mover.HeldMoveButtons);
            if (direction == Vector2i.Zero)
                continue;

            TryAncientStep(uid, direction);
        }
    }

    private void AdvanceAncientStep(EntityUid uid, AncientCurseComponent curse)
    {
        if (Transform(uid).ParentUid != curse.StepGrid)
        {
            curse.Stepping = false;
            return;
        }

        var elapsed = _wizardTiming.CurTime - curse.StepStartedAt;
        var progress = Math.Clamp((float)(elapsed / AncientStepDuration), 0f, 1f);
        _wizardTransform.SetLocalPosition(uid, Vector2.Lerp(curse.StepFrom, curse.StepTo, progress));
        curse.Stepping = progress < 1f;
    }

    private static Vector2i AncientStepDirection(MoveButtons buttons)
    {
        if (buttons.HasFlag(MoveButtons.Up) && !buttons.HasFlag(MoveButtons.Down))
            return new Vector2i(0, 1);
        if (buttons.HasFlag(MoveButtons.Down) && !buttons.HasFlag(MoveButtons.Up))
            return new Vector2i(0, -1);
        if (buttons.HasFlag(MoveButtons.Right) && !buttons.HasFlag(MoveButtons.Left))
            return new Vector2i(1, 0);
        if (buttons.HasFlag(MoveButtons.Left) && !buttons.HasFlag(MoveButtons.Right))
            return new Vector2i(-1, 0);
        return Vector2i.Zero;
    }

    public bool TryAncientStep(EntityUid target, Vector2i direction)
    {
        if (!TryComp<AncientCurseComponent>(target, out var curse) || curse.Stepping ||
            Math.Abs(direction.X) + Math.Abs(direction.Y) != 1)
            return false;

        var xform = Transform(target);
        if (xform.GridUid is not { } gridUid || xform.ParentUid != gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        var tile = _redecoratingMap.LocalToTile(gridUid, grid, xform.Coordinates) + direction;
        if (!_redecoratingMap.TryGetTileRef(gridUid, grid, tile, out var destinationTile) ||
            destinationTile.Tile.IsEmpty || _redecoratingTurf.IsSpace(destinationTile))
            return false;

        var destination = _redecoratingMap.GridTileToLocal(gridUid, grid, tile);
        if (!_wizardInteraction.InRangeUnobstructed(target,
                _wizardTransform.ToMapCoordinates(destination), 2f, CollisionGroup.Impassable))
            return false;

        if (TryComp<PhysicsComponent>(target, out var body))
            _wizardPhysics.SetLinearVelocity(target, Vector2.Zero, body: body);
        if (!HasComp<NoRotateOnMoveComponent>(target))
            _wizardTransform.SetLocalRotation(target, new Angle(new Vector2(direction.X, direction.Y)));
        curse.StepGrid = gridUid;
        curse.StepFrom = xform.Coordinates.Position;
        curse.StepTo = destination.Position;
        curse.StepStartedAt = _wizardTiming.CurTime;
        curse.NextStepAt = curse.StepStartedAt + AncientStepDuration + AncientStepRest;
        curse.Stepping = true;
        return true;
    }

    private void OnAncientCurseShutdown(Entity<AncientCurseComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        if (ent.Comp.IndicatorAction is { } action)
            _wizardActions.RemoveAction(action);
        _ancientPendingUnblock.Add(ent);
    }
}
