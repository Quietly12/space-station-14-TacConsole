using Content.Server._Starlight.Magic;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Starlight.Administration.Systems.Commands;

[AdminCommand(AdminFlags.Fun)]
public sealed partial class TelepathySelfCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entManager = default!;

    public string Command => "telepathyself";
    public string Description => "-NR-Opens the Telepathy dialog targeting your current character.";
    public string Help => "-NR-Usage: telepathyself";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError("-NR-Usage: telepathyself");
            return;
        }

        if (shell.Player?.AttachedEntity is not { } body)
        {
            shell.WriteError("-NR-You need to be controlling a living character.");
            return;
        }

        if (!_entManager.System<PsychicWhisperSystem>().TryOpenWhisperDialog(body, body))
            shell.WriteError("-NR-Telepathy could not open for this character.");
    }
}
