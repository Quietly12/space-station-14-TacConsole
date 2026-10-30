using Content.Server.Administration.Logs;
using Content.Server.Popups;
using Content.Shared._Starlight.Drawing;
using Content.Shared.Access.Systems;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Drawing;

/// <summary>Handles tablet editing, overheating, paper, and printing on the server.</summary>
public sealed partial class DrawingTabletSystem : EntitySystem
{
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedStackSystem _stacks = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IAdminLogManager _admin = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private SharedIdCardSystem _idCards = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => { _budgets.Clear(); _hotTablets.Clear(); _nextArtworkReportNumber = 0; });
        Subs.BuiEvents<DrawingTabletComponent>(DrawingTabletUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOpened);
            subs.Event<BoundUIClosedEvent>(OnClosed);
            subs.Event<DrawingTabletStrokeMessage>(OnStroke);
            subs.Event<DrawingTabletActionMessage>(OnTabletAction);
            subs.Event<DrawingTabletRequestMessage>(OnDocumentRequested);
        });
        Subs.BuiEvents<DrawingPrintComponent>(DrawingPrintUiKey.Key, subs => subs.Event<DrawingPrintRequestMessage>(OnPrintRequested));
        SubscribeLocalEvent<DrawingPrintComponent, ComponentStartup>(OnPrintStartup);
        SubscribeLocalEvent<DrawingPrintComponent, ExaminedEvent>(OnPrintExamine);
        SubscribeLocalEvent<DrawingTabletComponent, InteractUsingEvent>(OnInsertPaper);
        SubscribeLocalEvent<DrawingTabletComponent, ExaminedEvent>(OnExamine);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateOverheatedTablets();
        FinishPrintJobs();
    }
}
