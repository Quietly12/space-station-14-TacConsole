using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.ViewVariables;

namespace Content.Shared._Starlight.Drawing;

/// <summary> Only the tiny world thumbnail is replicated to nearby players.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class DrawingPrintComponent : Component
{
    // Round-specific report number shown on examine, in admin View Variables, and in the admin log when a paper is printed.
    [ViewVariables] public string ReportId = string.Empty;
    [DataField] public string ArtistName = string.Empty;
    [DataField] public byte[] Image = [];
    [DataField, AutoNetworkedField] public byte[] Thumbnail = [];
}

[Serializable, NetSerializable]
public enum DrawingPrintUiKey : byte { Key }

[Serializable, NetSerializable]
public enum DrawingPrintVisualLayers : byte { Image }

[Serializable, NetSerializable]
public sealed class DrawingPrintRequestMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class DrawingPrintImageMessage(byte[] image, string artistName) : BoundUserInterfaceMessage
{
    public readonly byte[] Image = image;
    public readonly string ArtistName = artistName;
}
