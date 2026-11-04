using Content.Server.Administration;
using Content.Server.Magic;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Starlight.Administration.Systems.Commands;

[AdminCommand(AdminFlags.Fun)]
public sealed partial class NoirSelfCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entManager = default!;

    public string Command => "noirself";
    public string Description => "-NR- Applies the Noir curse to your current character for testing.";
    public string Help => "-NR- Usage: noirself";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError("-NR- Usage: noirself");
            return;
        }

        if (shell.Player?.AttachedEntity is not { } body)
        {
            shell.WriteError("-NR- You need to be controlling a character.");
            return;
        }

        if (!_entManager.System<MagicSystem>().TryApplyNoirCurse(body))
        {
            shell.WriteError("-NR- Noir could not be applied to this character, or it is already active.");
            return;
        }

        shell.WriteLine("-NR- Noir applied to your character.");
    }
}
