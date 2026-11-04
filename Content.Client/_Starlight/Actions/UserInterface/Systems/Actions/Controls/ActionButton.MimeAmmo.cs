using System.Numerics;
using Content.Client.IoC;
using Content.Client.Resources;
using Content.Shared._Starlight.Magic.Mime;
using Content.Shared._Starlight.Magic.Components;
using Content.Shared.Magic.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.UserInterface.Systems.Actions.Controls;

public sealed partial class ActionButton
{
    private BoxContainer? _mimeAmmo;
    private readonly List<TextureRect> _mimeBullets = new();
    private float _mimeAmmoPulse;

    private partial void InitializeMimeAmmo()
    {
        _mimeAmmo = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalAlignment = HAlignment.Left,
            VerticalAlignment = VAlignment.Bottom,
            Margin = new Thickness(4, 0, 0, 3),
            SeparationOverride = 1,
            Visible = false,
        };
        var texture = StaticIoC.ResC.GetTexture("/Textures/Interface/ItemStatus/Bullets/normal.png");
        for (var i = 0; i < 5; i++)
        {
            var bullet = new TextureRect
            {
                Texture = texture,
                TextureScale = new Vector2(1, 1),
            };
            _mimeBullets.Add(bullet);
            _mimeAmmo.AddChild(bullet);
        }

        AddChild(_mimeAmmo);
    }

    private partial void UpdateMimeAmmo(float deltaSeconds)
    {
        if (_mimeAmmo == null)
            return;

        // The gun stays inside the action's container while holstered, preserving its recharging ammo.
        if (Action is not { } action ||
            !_entities.TryGetComponent<MimeFingerGunActionComponent>(action, out var fingerGun) ||
            fingerGun.Gun is not { } gun ||
            !_entities.TryGetComponent<BasicEntityAmmoProviderComponent>(gun, out var ammo))
        {
            _mimeAmmo.Visible = false;
            return;
        }

        _mimeAmmo.Visible = true;
        _mimeAmmoPulse += deltaSeconds;
        var count = Math.Clamp(ammo.Count ?? 0, 0, 5);
        for (var i = 0; i < _mimeBullets.Count; i++)
        {
            if (i < count)
            {
                _mimeBullets[i].ModulateSelfOverride = i == count - 1
                    ? Color.InterpolateBetween(Color.FromHex("#777777"), Color.White,
                        (MathF.Sin(_mimeAmmoPulse * 5f) + 1f) / 2f)
                    : Color.White;
                continue;
            }

            _mimeBullets[i].ModulateSelfOverride = Color.FromHex("#333333");
        }
    }

    private partial void UpdateMimeGunActiveIcon()
    {
        _bigActionIcon.DisplayRect.TextureScale = Action is { } selectedAction
            && (_entities.HasComponent<MimeMagicActionComponent>(selectedAction.Owner)
                || _entities.HasComponent<MagicComponent>(selectedAction.Owner)
                || _entities.HasComponent<WizardFullSizeIconComponent>(selectedAction.Owner))
            ? new Vector2(2, 2)
            : Vector2.One;

        if (Action is { } action && _entities.HasComponent<MimeFingerGunActionComponent>(action.Owner)
            && action.Comp.Toggled)
            _bigActionIcon.Modulate = Color.FromHex("#7fff7f");
    }
}
