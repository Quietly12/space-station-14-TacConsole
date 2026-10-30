using Robust.Shared.Serialization;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Content.Shared.Access;

namespace Content.Shared._Starlight.Drawing;

// Balance tuning shared by the server and client.
public static class DrawingTabletSettings
{
    // Anti-abuse protection
    // Budgetcapacity is how many reserve actions a tablet has, they regenerate per second. Running out of these charges hits an overheat duration and prints an admin log.
    public const int RequestBudgetCapacity = 30;
    public const float RequestsRestoredPerSecond = 2f;
    public const int OverheatDurationSeconds = 120;

    // Standard tablet defaults.
    // Nullspace tablet overrides live in Resources/Prototypes/_Starlight/Entities/Objects/Devices/drawing_tablet.yml.
    public const int StartingPaperSheets = 4;
    public const int PaperBufferCapacity = 20;

    // Drawing Limits: all used to calculate complexity.
    // Stroke Limit
    public const int MaximumDrawingStrokes = 1000;
    // Maximum number of recorded drawing points in one artwork.
    public const int MaximumDrawingPoints = 32_000;
    // Safety limit on estimated brush work
    public const int MaximumDrawingRenderWork = 32_000_000;

    // Print-job limit is server-wide as it's a background process.
    public const int MaximumSimultaneousPrintJobs = 1;
    public const int PrintCooldownSeconds = 3;
    // How many asyncronous strokes can fill the buffer before the tablet locks up without a server response.
    public const int MaximumPendingClientStrokes = 16;
}

public static class DrawingTabletComplexity
{
    //  point, stroke, and render-work budgets result in a single complexity value that indicates to the artist how close they are to the drawing budget.
    public static int Calculate(int strokes, int points, int renderWork, int maxStrokes, int maxPoints, int maxRenderWork)
    {
        if (maxPoints <= 0)
            return 0;
        var pointUse = Math.Clamp(points, 0, maxPoints);
        var strokeUse = Scale(strokes, maxStrokes, maxPoints);
        var renderUse = Scale(renderWork, maxRenderWork, maxPoints);
        return Math.Max(pointUse, Math.Max(strokeUse, renderUse));
    }

    private static int Scale(int used, int maximum, int displayMaximum)
    {
        if (maximum <= 0)
            return displayMaximum;
        return (int) Math.Clamp(((long) Math.Max(0, used) * displayMaximum + maximum - 1) / maximum, 0, displayMaximum);
    }
}

/// <summary>Stores the tablet's drawing, paper supply, limits and editing state.</summary>
[RegisterComponent]
public sealed partial class DrawingTabletComponent : Component
{
    [DataField] public int Paper = DrawingTabletSettings.StartingPaperSheets;
    [DataField] public int PaperCapacity = DrawingTabletSettings.PaperBufferCapacity;
    // Cap Placeholder until the Artist access level exists.
    [DataField] public bool RequiresClearAccess = true;
    [DataField] public ProtoId<AccessLevelPrototype> ClearAccess = "Captain";
    [DataField] public SoundSpecifier PrintSound = new SoundPathSpecifier("/Audio/Machines/diagnoser_printing.ogg")
    {
        Params = AudioParams.Default.WithVolume(-2f).WithMaxDistance(3f),
    };
    [DataField] public int MaxStrokes = DrawingTabletSettings.MaximumDrawingStrokes;
    [DataField] public int MaxPoints = DrawingTabletSettings.MaximumDrawingPoints;
    [DataField] public int MaxRenderWork = DrawingTabletSettings.MaximumDrawingRenderWork;
    [DataField] public List<DrawingStroke> Strokes = [];
    public readonly List<DrawingStroke> Redo = [];
    public int Revision;
    public Guid LastAcceptedStroke;
    public EntityUid? Editor;
    public int TotalPoints;
    public int RenderWork;
    public byte[]? PrintedImage;
    public byte[]? PrintedThumbnail;
    public TimeSpan NextPrint;
    public TimeSpan OverheatedUntil;
    public bool InitialSnapshotSent;
    public TimeSpan NextSnapshot;
    public TimeSpan NextHeatNotice;
    public TimeSpan BudgetUpdated;
    public float EditTokens = DrawingTabletSettings.RequestBudgetCapacity;
}

[Serializable, NetSerializable]
public enum DrawingTabletUiKey : byte { Key }

[Serializable, NetSerializable]
public enum DrawingTabletAction : byte { Undo, Redo, Clear, Print }

[Serializable, NetSerializable]
public sealed class DrawingTabletState(List<DrawingStroke> strokes, int revision, bool canRedo, int maxStrokes, int maxPoints = DrawingTabletSettings.MaximumDrawingPoints, bool overheated = false, Guid lastAcceptedStroke = default, int maxRenderWork = DrawingTabletSettings.MaximumDrawingRenderWork) : BoundUserInterfaceState
{
    public readonly List<DrawingStroke> Strokes = strokes;
    public readonly int Revision = revision;
    public readonly bool CanRedo = canRedo;
    public readonly int MaxStrokes = maxStrokes;
    public readonly int MaxPoints = maxPoints;
    public readonly int MaxRenderWork = maxRenderWork;
    public readonly bool Overheated = overheated;
    public readonly Guid LastAcceptedStroke = lastAcceptedStroke;
}

[Serializable, NetSerializable]
public sealed class DrawingTabletStrokeMessage(DrawingStroke stroke, int revision, Guid requestId = default) : BoundUserInterfaceMessage
{
    public readonly Guid RequestId = requestId;
    public readonly DrawingStroke Stroke = stroke;
    public readonly int Revision = revision;
}

[Serializable, NetSerializable]
public sealed class DrawingTabletActionMessage(DrawingTabletAction action, int revision, Guid requestId = default) : BoundUserInterfaceMessage
{
    public readonly Guid RequestId = requestId;
    public readonly DrawingTabletAction Action = action;
    public readonly int Revision = revision;
}

[Serializable, NetSerializable]
public sealed class DrawingTabletAckMessage(int revision, Guid requestId = default) : BoundUserInterfaceMessage
{
    public readonly Guid RequestId = requestId;
    public readonly int Revision = revision;
}

[Serializable, NetSerializable]
public sealed class DrawingTabletNoticeMessage(string text) : BoundUserInterfaceMessage
{
    public readonly string Text = text;
}

[Serializable, NetSerializable]
public sealed class DrawingTabletRequestMessage(bool resync = false, Guid requestId = default) : BoundUserInterfaceMessage
{
    public readonly Guid RequestId = requestId;
    public readonly bool Resync = resync;
}

[Serializable, NetSerializable]
public sealed class DrawingTabletHistoryMessage(int previousRevision, int revision, DrawingTabletAction action, DrawingStroke? restoredStroke, bool canRedo, Guid requestId = default) : BoundUserInterfaceMessage
{
    public readonly Guid RequestId = requestId;
    public readonly int PreviousRevision = previousRevision;
    public readonly int Revision = revision;
    public readonly DrawingTabletAction Action = action;
    public readonly DrawingStroke? RestoredStroke = restoredStroke;
    public readonly bool CanRedo = canRedo;
}

[Serializable, NetSerializable]
public sealed class DrawingTabletDocumentMessage(DrawingTabletState document, Guid requestId = default, bool rejectPending = false) : BoundUserInterfaceMessage
{
    public readonly Guid RequestId = requestId;
    public readonly DrawingTabletState Document = document;
    public readonly bool RejectPending = rejectPending;
}
