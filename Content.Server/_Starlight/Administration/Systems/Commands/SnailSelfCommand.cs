using Content.Server.Administration;
using Content.Server.Magic;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Starlight.Administration.Systems.Commands;

[AdminCommand(AdminFlags.Fun)]
public sealed partial class SnailSelfCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entManager = default!;

    public string Command => "snailself";
    public string Description => "-NR- Applies the Snail curse to your current character for testing.";
    public string Help => "-NR- Usage: snailself";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError("-NR- Usage: snailself");
            return;
        }

        if (shell.Player?.AttachedEntity is not { } body)
        {
            shell.WriteError("-NR- You need to be controlling a character.");
            return;
        }

        if (!_entManager.System<MagicSystem>().TryApplySnailCurse(body))
        {
            shell.WriteError("-NR- Snail could not be applied to this character, or it is already active.");
            return;
        }

        shell.WriteLine("-NR- Snail applied to your character.");
    }
}
