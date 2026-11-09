// ReSharper disable CheckNamespace

using Content.Shared._Starlight.Shuttles.BUIStates;

namespace Content.Client.Shuttles.BUI;

public sealed partial class RadarConsoleBoundUserInterface
{
    private void RequestRadarState()
    {
        if (!EntMan.IsClientSide(Owner))
            SendMessage(new RadarConsoleRequestStateMessage());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is RadarConsoleUpdateMessage update)
            UpdateState(update.State);
    }
}
