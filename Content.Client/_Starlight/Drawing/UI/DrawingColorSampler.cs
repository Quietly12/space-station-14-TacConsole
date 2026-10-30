using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Starlight.Drawing.UI;

/// <summary>Keeps sampling active until the canvas selects a color or the player cancels it.</summary>
public sealed class DrawingColorSampler : Popup
{
    private readonly DrawingCanvasControl _canvas;

    public DrawingColorSampler(DrawingCanvasControl canvas)
    {
        _canvas = canvas;
        MouseFilter = MouseFilterMode.Ignore;
        CloseOnClick = true;
        CloseOnEscape = true;
    }

    public void OpenOverCanvas()
    {
        var root = UserInterfaceManager.ModalRoot;
        root.AddChild(this);
        Open(UIBox2.FromDimensions(_canvas.GlobalPosition - root.GlobalPosition, _canvas.Size));
        // Clears colorentry keyboard focus so sampler shortcuts can be handled.
        UserInterfaceManager.ReleaseKeyboardFocus();
    }
}
