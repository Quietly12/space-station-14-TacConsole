using Content.Server.Administration;
using Content.Server.Magic;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Starlight.Administration.Systems.Commands;

[AdminCommand(AdminFlags.Fun)]
public sealed partial class MoodsSelfCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entManager = default!;

    public string Command => "moodsself";
    public string Description => "-NR- Applies Moods to your current character for testing.";
    public string Help => "-NR- Usage: moodsself";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError("-NR- Usage: moodsself");
            return;
        }

        if (shell.Player?.AttachedEntity is not { } body)
        {
            shell.WriteError("-NR- You need to be controlling a character.");
            return;
        }

        if (!_entManager.System<MagicSystem>().TryApplyMoodsCurse(body))
        {
            shell.WriteError("-NR- Moods could not be applied to this character, or it is already active.");
            return;
        }

        shell.WriteLine("-NR- Moods applied to your character.");
    }
}
