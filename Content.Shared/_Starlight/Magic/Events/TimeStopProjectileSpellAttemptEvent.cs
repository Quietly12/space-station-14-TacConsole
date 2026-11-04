using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._Starlight.Magic.Events;

[ByRefEvent]
public record struct TimeStopProjectileSpellAttemptEvent(
    EntityUid Caster,
    EntProtoId Prototype,
    EntityCoordinates From,
    EntityCoordinates To,
    float Speed)
{
    public bool Suspended;
}
