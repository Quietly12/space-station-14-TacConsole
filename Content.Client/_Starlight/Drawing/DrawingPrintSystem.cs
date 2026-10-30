using Content.Shared._Starlight.Drawing;
using Content.Shared.GameTicking;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameStates;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._Starlight.Drawing;

/// <summary> Small dynamic sprite layers, shared by identical copies and disposed when no longer in view.</summary>
public sealed partial class DrawingPrintSystem : EntitySystem
{
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private SpriteSystem _sprites = default!;
    private sealed class CachedTexture(OwnedTexture texture)
    {
        public readonly OwnedTexture Texture = texture;
        public int Users;
    }
    private readonly Dictionary<string, CachedTexture> _cache = [];
    private readonly Dictionary<EntityUid, string> _users = [];

    public override void Initialize()
    {
        SubscribeLocalEvent<DrawingPrintComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<DrawingPrintComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<DrawingPrintComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRestart);
    }

    private void OnState(EntityUid uid, DrawingPrintComponent comp, ref AfterAutoHandleStateEvent args) => UpdateImage(uid, comp);
    private void OnStartup(EntityUid uid, DrawingPrintComponent comp, ComponentStartup args) => UpdateImage(uid, comp);
    private void OnShutdown(EntityUid uid, DrawingPrintComponent comp, ComponentShutdown args) => Release(uid);

    private void UpdateImage(EntityUid uid, DrawingPrintComponent comp)
    {
        if (comp.Thumbnail.Length != DrawingRaster.ThumbnailWidthPixels * DrawingRaster.ThumbnailHeightPixels * 3
            || !TryComp<SpriteComponent>(uid, out var sprite))
            return;
        var key = Convert.ToBase64String(comp.Thumbnail);
        if (_users.TryGetValue(uid, out var old) && old == key)
            return;
        Release(uid);
        if (!_cache.TryGetValue(key, out var cached))
        {
            // Fill the full 10 x 12 rectangle at x=11..20, y=10..21.
            var pixels = new Rgba32[32 * 32];
            for (var y = 0; y < DrawingRaster.ThumbnailHeightPixels; y++)
            for (var x = 0; x < DrawingRaster.ThumbnailWidthPixels; x++)
            {
                var source = (y * DrawingRaster.ThumbnailWidthPixels + x) * 3;
                pixels[(y + 10) * 32 + x + 11] = new Rgba32(comp.Thumbnail[source], comp.Thumbnail[source + 1], comp.Thumbnail[source + 2], 255);
            }
            var texture = _clyde.CreateBlankTexture<Rgba32>((32, 32));
            texture.SetSubImage<Rgba32>((0, 0), (32, 32), pixels);
            cached = new CachedTexture(texture);
            _cache.Add(key, cached);
        }
        cached.Users++;
        _users[uid] = key;
        _sprites.LayerSetTexture((uid, sprite), DrawingPrintVisualLayers.Image, cached.Texture);
        _sprites.LayerSetVisible((uid, sprite), DrawingPrintVisualLayers.Image, true);
    }

    private void Release(EntityUid uid)
    {
        if (!_users.Remove(uid, out var key) || !_cache.TryGetValue(key, out var cached))
            return;
        if (--cached.Users == 0)
        {
            cached.Texture.Dispose();
            _cache.Remove(key);
        }
    }

    private void OnRestart(RoundRestartCleanupEvent args) => Clear();
    private void Clear()
    {
        foreach (var cached in _cache.Values)
            cached.Texture.Dispose();
        _cache.Clear();
        _users.Clear();
    }

    public override void Shutdown()
    {
        Clear();
        base.Shutdown();
    }
}
