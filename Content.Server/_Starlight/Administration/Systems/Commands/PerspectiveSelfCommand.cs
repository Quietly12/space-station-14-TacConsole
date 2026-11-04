using Content.Server.Administration;
using Content.Server.Magic;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Starlight.Administration.Systems.Commands;

[AdminCommand(AdminFlags.Fun)]
public sealed partial class PerspectiveSelfCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entManager = default!;

    public string Command => "perspectiveself";
    public string Description => "-NR- Applies New Perspective to your current character for testing.";
    public string Help => "-NR- Usage: perspectiveself";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError("-NR- Usage: perspectiveself");
            return;
        }

        if (shell.Player?.AttachedEntity is not { } body)
        {
            shell.WriteError("-NR- You need to be controlling a character.");
            return;
        }

        if (!_entManager.System<MagicSystem>().TryApplyPerspectiveCurse(body))
        {
            shell.WriteError("-NR- New Perspective could not be applied to this character, or it is already active.");
            return;
        }

        shell.WriteLine("-NR- New Perspective applied to your character.");
    }
}
