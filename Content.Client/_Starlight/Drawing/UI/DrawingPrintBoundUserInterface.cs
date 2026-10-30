using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Starlight.Drawing;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using SixLabors.ImageSharp.PixelFormats;
using CancellationTokenSource = System.Threading.CancellationTokenSource;

namespace Content.Client._Starlight.Drawing.UI;

/// <summary>-Displays the finished print as a single texture; no stroke rendering. Very good for data efficiency! </summary>
public sealed class DrawingPrintBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private FancyWindow? _window;
    private TextureRect? _image;
    private Label? _status;
    private OwnedTexture? _texture;
    private CancellationTokenSource? _retry;
    private int _attempts;

    protected override void Open()
    {
        base.Open();
        var window = _window = this.CreateWindow<FancyWindow>();
        window.Title = Loc.GetString("drawing-print-title");
        window.Resizable = true;
        window.SetSize = new Vector2(534, 710);
        window.MinSize = new Vector2(279, 380);
        _image = new TextureRect
        {
            Name = "PrintedArtworkImage", Margin = new Thickness(10), CanShrink = true,
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
        };
        _status = new Label
        {
            Text = Loc.GetString("drawing-tablet-loading"), HorizontalAlignment = Control.HAlignment.Center,
            VerticalAlignment = Control.VAlignment.Center,
        };
        window.ContentsContainer.AddChild(_image);
        window.ContentsContainer.AddChild(_status);
        _attempts = 0;
        _retry = new CancellationTokenSource();
        RequestImage();
    }

    private void RequestImage()
    {
        _attempts++;
        SendMessage(new DrawingPrintRequestMessage());
        // Delay, because the client UI prediction system kept losing requests.
        Timer.Spawn(TimeSpan.FromSeconds(1), () =>
        {
            if (!IsOpened || _texture != null)
                return;
            if (_attempts < 4)
                RequestImage();
            else if (_status != null)
                _status.Text = Loc.GetString("drawing-print-load-failed");
        }, _retry!.Token);
    }

    /// <summary> Decodes a received print image and displays it in the artwork window.</summary>
    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is not DrawingPrintImageMessage print || _texture != null ||
            !DrawingRaster.TryDecode(print.Image, out var pixels))
            return;
        if (_window != null && !string.IsNullOrEmpty(print.ArtistName))
            _window.Title = Loc.GetString("drawing-print-title-attributed", ("artist", print.ArtistName));
        var rgba = new Rgba32[DrawingRaster.CanvasWidthPixels * DrawingRaster.CanvasHeightPixels];
        for (var i = 0; i < rgba.Length; i++)
            rgba[i] = new Rgba32(pixels[i * 3], pixels[i * 3 + 1], pixels[i * 3 + 2], 255);
        _texture = IoCManager.Resolve<IClyde>().CreateBlankTexture<Rgba32>((DrawingRaster.CanvasWidthPixels, DrawingRaster.CanvasHeightPixels));
        _texture.SetSubImage<Rgba32>((0, 0), (DrawingRaster.CanvasWidthPixels, DrawingRaster.CanvasHeightPixels), rgba);
        if (_image != null)
            _image.Texture = _texture;
        if (_status != null)
            _status.Visible = false;
        _retry?.Cancel();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _retry?.Cancel();
            _retry?.Dispose();
            _retry = null;
            if (_image != null)
                _image.Texture = null;
            _texture?.Dispose();
            _texture = null;
        }
        base.Dispose(disposing);
    }
}
