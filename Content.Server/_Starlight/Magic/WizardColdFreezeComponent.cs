using System;
using Robust.Shared.Maths;

namespace Content.Server.Magic;

/// <summary>Tracks the temporary ice shell and the tiles crossed by a frozen target.</summary>
[RegisterComponent]
public sealed partial class WizardColdFreezeComponent : Component
{
    public TimeSpan ExpiresAt;
    public EntityUid? Overlay;
    public EntityUid? LastGrid;
    public Vector2i? LastTile;
}
