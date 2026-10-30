using System.Numerics;
using Content.Shared._Starlight.Drawing;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._Starlight.Drawing.UI;

public enum DrawingTool : byte { Draw, Erase, Sample }

/// <summary> Displays the paper canvas and handles drawing, color sampling, zoom, and pan input.</summary>
public sealed class DrawingCanvasControl : Control
{
    // grid appears when its cells are large enough to distinguish on screen.
    private const float MinimumVisibleGridCellPixels = 5f;
    // Opacity of grid lines over the editor canvas.
    private const float GridLineOpacity = 0.3f;

    private byte[] _pixels = DrawingRaster.Blank();
    private readonly Rgba32[] _texturePixels = new Rgba32[DrawingRaster.CanvasWidthPixels * DrawingRaster.CanvasHeightPixels];
    private OwnedTexture? _texture;
    private DrawingStroke? _stroke;
    private bool _dirty = true;
    private bool _held;
    private Vector2? _hoverPoint;
    private int _completedPoints;
    private int _completedStrokes;
    private int _completedRenderWork;
    private int _currentStrokeWork;

    private float _zoom = 1f;
    private Vector2 _pan;
    private bool _panning;
    public bool PixelArt;
    public float Zoom => _zoom;
    public int ComplexityStrokeCount => _completedStrokes + (_stroke == null ? 0 : 1);
    public int ComplexityRenderWork => _completedRenderWork + _currentStrokeWork;
    public bool CanDraw;
    public DrawingTool Tool;
    public DrawingBrush Brush;
    public Color Ink = Color.Black;
    public byte BrushSize = 4;
    public byte Opacity = 100;
    public event Action<DrawingStroke>? StrokeCompleted;
    public event Action<Color>? ColorSampled;
    public event Action? SamplingCancelled;
    public event Action<int>? PointCountChanged;

    public DrawingCanvasControl()
    {
        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;
        HorizontalExpand = true;
        VerticalExpand = true;
        MinSize = new Vector2(255, 330);
    }

    public void SetDocument(IEnumerable<DrawingStroke> strokes)
    {
        _stroke = null;
        _held = false;
        _pixels = DrawingRaster.Render(strokes);
        _completedPoints = 0;
        _completedStrokes = 0;
        _completedRenderWork = 0;
        _currentStrokeWork = 0;
        foreach (var stroke in strokes)
        {
            _completedPoints += stroke.Points.Count;
            _completedStrokes++;
            if (DrawingRaster.Validate(stroke, out var work))
                _completedRenderWork += work;
        }
        PointCountChanged?.Invoke(_completedPoints);
        _dirty = true;
    }

    public void RestorePendingStroke(DrawingStroke stroke)
    {
        DrawingRaster.Apply(_pixels, stroke);
        _completedPoints += stroke.Points.Count;
        _completedStrokes++;
        if (DrawingRaster.Validate(stroke, out var work))
            _completedRenderWork += work;
        PointCountChanged?.Invoke(_completedPoints);
        _dirty = true;
    }

    public void SetImage(byte[] pixels)
    {
        _pixels = pixels;
        _dirty = true;
    }

    private UIBox2 PaperBounds()
    {
        var scale = MathF.Min(PixelSize.X / (float) DrawingRaster.CanvasWidthPixels, PixelSize.Y / (float) DrawingRaster.CanvasHeightPixels);
        var size = new Vector2(DrawingRaster.CanvasWidthPixels, DrawingRaster.CanvasHeightPixels) * scale * _zoom;
        return UIBox2.FromDimensions(((Vector2) PixelSize - size) / 2 + _pan, size);
    }

    public void ResetView()
    {
        FinishStroke();
        _held = false;
        _zoom = 1f;
        _pan = Vector2.Zero;
    }

    public void ZoomAt(Vector2 relativePixel, float wheelDelta)
    {
        FinishStroke();
        _held = false;
        var old = PaperBounds();
        if (old.Width <= 0 || old.Height <= 0)
            return;
        var paperPoint = (relativePixel - old.TopLeft) / old.Size;
        _zoom = Math.Clamp(_zoom * MathF.Pow(1.25f, wheelDelta), 1f, 16f);
        var bounds = PaperBounds();
        _pan += relativePixel - (bounds.TopLeft + paperPoint * bounds.Size);
        ClampPan();
        _hoverPoint = null;
    }

    private void ClampPan()
    {
        var limit = Vector2.Max(Vector2.Zero, (PaperBounds().Size - (Vector2) PixelSize) / 2);
        _pan = Vector2.Clamp(_pan, -limit, limit);
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        args.Handle();
        ZoomAt(args.RelativePixelPosition, args.Delta.Y);
    }

    private bool TryPoint(Vector2 relative, out Vector2 point)
    {
        var bounds = PaperBounds();
        point = default;
        if (bounds.Width <= 0 || !bounds.Contains(relative))
            return false;
        point = (relative - bounds.TopLeft) * new Vector2(DrawingRaster.CanvasWidthPixels / bounds.Width, DrawingRaster.CanvasHeightPixels / bounds.Height);
        if (PixelArt)
        {
            var size = BrushSize;
            var x = Math.Clamp((int) MathF.Floor(point.X / size), 0, DrawingRaster.CanvasWidthPixels / size - 1);
            var y = Math.Clamp((int) MathF.Floor(point.Y / size), 0, DrawingRaster.CanvasHeightPixels / size - 1);
            point = new Vector2(x * size, y * size);
        }
        else
        {
            point = Vector2.Clamp(new Vector2(MathF.Round(point.X), MathF.Round(point.Y)), Vector2.Zero,
                new Vector2(DrawingRaster.CanvasWidthPixels - 1, DrawingRaster.CanvasHeightPixels - 1));
        }
        return true;
    }

    public bool TrySampleColor(Vector2 relativePixel, out Color color)
    {
        color = default;
        var bounds = PaperBounds();
        if (bounds.Width <= 0 || !bounds.Contains(relativePixel) || !((UIBox2) PixelSizeBox).Contains(relativePixel))
            return false;
        // Texture pixels occupy cells, so sample by floor even in vector mode.
        var point = (relativePixel - bounds.TopLeft) * new Vector2(DrawingRaster.CanvasWidthPixels / bounds.Width, DrawingRaster.CanvasHeightPixels / bounds.Height);
        var x = Math.Clamp((int) MathF.Floor(point.X), 0, DrawingRaster.CanvasWidthPixels - 1);
        var y = Math.Clamp((int) MathF.Floor(point.Y), 0, DrawingRaster.CanvasHeightPixels - 1);
        var offset = (y * DrawingRaster.CanvasWidthPixels + x) * 3;
        color = new Color(_pixels[offset], _pixels[offset + 1], _pixels[offset + 2]);
        return true;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            if (Tool == DrawingTool.Sample)
            {
                SamplingCancelled?.Invoke();
                args.Handle();
                return;
            }
            FinishStroke();
            _held = false;
            _panning = true;
            args.Handle();
            return;
        }
        if (args.Function != EngineKeyFunctions.UIClick)
            return;
        if (Tool == DrawingTool.Sample)
        {
            args.Handle();
            if (TrySampleColor(args.RelativePixelPosition, out var color))
                ColorSampled?.Invoke(color);
            else
                SamplingCancelled?.Invoke();
            return;
        }
        if (!CanDraw || !TryPoint(args.RelativePixelPosition, out var point))
            return;
        args.Handle();
        _held = true;
        StartStroke(point);
    }

    private void StartStroke(Vector2 point)
    {
        _stroke = new DrawingStroke
        {
            Points = new List<Vector2> { point }, Brush = PixelArt ? DrawingBrush.PixelSquare : Brush, Color = Ink.WithAlpha(1f), Size = BrushSize,
            Opacity = Opacity, Erase = Tool == DrawingTool.Erase,
        };
        DrawingRaster.Validate(_stroke, out _currentStrokeWork);
        PointCountChanged?.Invoke(_completedPoints + 1);
        _dirty = true;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        if (_panning)
        {
            _pan += args.Relative * UIScale;
            ClampPan();
            _hoverPoint = null;
            return;
        }
        _hoverPoint = TryPoint(args.RelativePixelPosition, out var hover) ? hover : null;
        if (!_held || !CanDraw || _stroke == null || !TryPoint(args.RelativePixelPosition, out var point))
            return;
        var previous = _stroke.Points[^1];
        if (point == previous)
            return;
        _stroke.Points.Add(point);
        if (!DrawingRaster.Validate(_stroke, out _currentStrokeWork))
        {
            _stroke.Points.RemoveAt(_stroke.Points.Count - 1);
            DrawingRaster.Validate(_stroke, out _currentStrokeWork);
            FinishStroke();
            if (!CanDraw)
                return;
            StartStroke(previous);
            _stroke!.Points.Add(point);
            DrawingRaster.Validate(_stroke, out _currentStrokeWork);
        }
        PointCountChanged?.Invoke(_completedPoints + _stroke.Points.Count);
        _dirty = true;
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            _panning = false;
            args.Handle();
            return;
        }
        if (args.Function != EngineKeyFunctions.UIClick || !_held)
            return;
        args.Handle();
        _held = false;
        FinishStroke();
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hoverPoint = null;
        _panning = false;
        _held = false;
        FinishStroke();
    }

    public void FinishStroke()
    {
        if (_stroke == null)
            return;
        var completed = _stroke;
        _stroke = null;
        _completedStrokes++;
        _completedRenderWork += _currentStrokeWork;
        _currentStrokeWork = 0;
        DrawingRaster.Apply(_pixels, completed);
        _completedPoints += completed.Points.Count;
        PointCountChanged?.Invoke(_completedPoints);
        _dirty = true;
        StrokeCompleted?.Invoke(completed);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (_dirty)
        {
            var pixels = _pixels;
            if (_stroke != null)
            {
                pixels = (byte[]) pixels.Clone();
                DrawingRaster.Apply(pixels, _stroke);
            }
            for (var i = 0; i < _texturePixels.Length; i++)
                _texturePixels[i] = new Rgba32(pixels[i * 3], pixels[i * 3 + 1], pixels[i * 3 + 2], 255);
            _texture ??= IoCManager.Resolve<IClyde>().CreateBlankTexture<Rgba32>((DrawingRaster.CanvasWidthPixels, DrawingRaster.CanvasHeightPixels));
            _texture.SetSubImage<Rgba32>((0, 0), (DrawingRaster.CanvasWidthPixels, DrawingRaster.CanvasHeightPixels), _texturePixels);
            _dirty = false;
        }
        if (_texture != null)
            handle.DrawTextureRect(_texture, PaperBounds());
        if (PixelArt)
        {
            var bounds = PaperBounds();
            var scale = bounds.Width / DrawingRaster.CanvasWidthPixels;
            if (BrushSize * scale >= MinimumVisibleGridCellPixels)
            {
                // Grid lines are an editor overlay only
                var ink = Color.Black.WithAlpha(GridLineOpacity);
                var top = MathF.Max(0, bounds.Top);
                var bottom = MathF.Min(PixelSize.Y, bounds.Bottom);
                var left = MathF.Max(0, bounds.Left);
                var right = MathF.Min(PixelSize.X, bounds.Right);
                for (var x = 0; x <= DrawingRaster.CanvasWidthPixels; x += BrushSize)
                {
                    var screenX = bounds.Left + x * scale;
                    if (screenX >= left && screenX <= right)
                        handle.DrawLine(new Vector2(screenX, top), new Vector2(screenX, bottom), ink);
                }
                for (var y = 0; y <= DrawingRaster.CanvasHeightPixels; y += BrushSize)
                {
                    var screenY = bounds.Top + y * scale;
                    if (screenY >= top && screenY <= bottom)
                        handle.DrawLine(new Vector2(left, screenY), new Vector2(right, screenY), ink);
                }
            }
        }
        if (CanDraw && Tool != DrawingTool.Sample && _hoverPoint is { } point)
        {
            var bounds = PaperBounds();
            var scale = bounds.Width / DrawingRaster.CanvasWidthPixels;
            if (PixelArt)
            {
                var origin = bounds.TopLeft + point * scale;
                var size = new Vector2(BrushSize * scale);
                handle.DrawRect(UIBox2.FromDimensions(origin - Vector2.One, size + new Vector2(2)), Color.Black, false);
                handle.DrawRect(UIBox2.FromDimensions(origin, size), Color.White, false);
            }
            else if (Brush == DrawingBrush.Circle)
            {
                var center = bounds.TopLeft + (point + new Vector2(0.5f)) * scale;
                var radius = MathF.Max(1, BrushSize * scale / 2);
                handle.DrawCircle(center, radius + 1, Color.Black, false);
                handle.DrawCircle(center, radius, Color.White, false);
            }
            else
            {
                var center = bounds.TopLeft + (point + new Vector2(0.5f)) * scale;
                var radius = MathF.Max(1, BrushSize * scale / 2);
                handle.DrawRect(UIBox2.FromDimensions(center - new Vector2(radius + 1), new Vector2((radius + 1) * 2)), Color.Black, false);
                handle.DrawRect(UIBox2.FromDimensions(center - new Vector2(radius), new Vector2(radius * 2)), Color.White, false);
            }
        }
    }

    protected override void ExitedTree()
    {
        _texture?.Dispose();
        _texture = null;
        _dirty = true;
        base.ExitedTree();
    }
}
