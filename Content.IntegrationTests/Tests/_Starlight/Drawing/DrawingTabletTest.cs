using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Content.Client._Starlight.Drawing.UI;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Starlight.Drawing;
using Content.Shared.Interaction;
using Content.Shared.Examine;
using Content.Shared.Damage.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Client.UserInterface;
using Robust.Client.Input;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Server.Player;
using Robust.Shared.Graphics;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Starlight.Drawing;

public sealed class DrawingTabletTest : GameTest
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task ConnectedEditorReceivesDocumentAndSendsLeftClick(bool dropOpeningRequest, bool dropOpeningResponse)
    {
        EntityUid map = default, tablet = default;
        var players = Server.ResolveDependency<IPlayerManager>();
        var session = Pair.Player!;
        var previous = session.AttachedEntity;
        try
        {
            await Client.WaitAssertion(() => Client.System<DrawingRequestDropTestSystem>().DropDocument = dropOpeningResponse);
            await Server.WaitAssertion(() =>
            {
                map = Server.System<SharedMapSystem>().CreateMap(out var id);
                var coords = new MapCoordinates(0, 0, id);
                var actor = SEntMan.SpawnEntity("MobHuman", coords);
                players.SetAttachedEntity(session, actor);
                tablet = SEntMan.SpawnEntity("DrawingTablet", coords);
                Server.System<DrawingRequestDropTestSystem>().DropNext = dropOpeningRequest;
                Server.System<SharedUserInterfaceSystem>().OpenUi(tablet, DrawingTabletUiKey.Key, actor);
            });
            await Pair.RunTicksSync(100);
            DrawingCanvasControl canvas = null!;
            await Client.WaitAssertion(() =>
            {
                var root = Client.ResolveDependency<IUserInterfaceManager>().RootControl;
                canvas = FindControls<DrawingTabletWindow>(root).Single().FindControl<DrawingCanvasControl>("Canvas");
                Assert.That(canvas.CanDraw, Is.True, "document must reach the editor.");
                Assert.That(canvas.PixelSize.X, Is.GreaterThan(0));
                var screenPoint = new ScreenCoordinates(canvas.GlobalPixelPosition + (Vector2) canvas.PixelSize / 2, WindowId.Main);
                Assert.That(Client.ResolveDependency<IUserInterfaceManager>().MouseGetControl(screenPoint), Is.SameAs(canvas),
                    "The canvas must receive actual mouse hit-testing, not just direct test events.");
            });
            var point = (Vector2) canvas.PixelSize / 2;
            await Client.DoGuiEvent(canvas, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down,
                default, true, point, point));
            await Client.DoGuiEvent(canvas, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up,
                default, true, point, point));
            await Pair.RunTicksSync(20);
            await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<DrawingTabletComponent>(tablet).Strokes, Has.Count.EqualTo(1)));
            var snapshots = 0;
            var deltas = 0;
            await Client.WaitAssertion(() =>
            {
                snapshots = Client.System<DrawingRequestDropTestSystem>().Snapshots;
                deltas = Client.System<DrawingRequestDropTestSystem>().HistoryChanges;
            });
            await ClickHistory("UndoButton");
            await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<DrawingTabletComponent>(tablet).TotalPoints, Is.Zero));
            await Client.WaitAssertion(() =>
            {
                var window = FindControls<DrawingTabletWindow>(Client.ResolveDependency<IUserInterfaceManager>().RootControl).Single();
                Assert.That(window.FindControl<Label>("PointCount").Text, Is.EqualTo("Complexity: 0 / 32,000"));
                Assert.That(canvas.TrySampleColor(point, out var color), Is.True);
                Assert.That(color, Is.EqualTo(Color.White));
            });
            await ClickHistory("RedoButton");
            await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<DrawingTabletComponent>(tablet).Strokes, Has.Count.EqualTo(1)));
            await Client.WaitAssertion(() =>
            {
                Assert.That(canvas.TrySampleColor(point, out var color), Is.True);
                Assert.That(color, Is.EqualTo(Color.Black));
                Assert.That(Client.System<DrawingRequestDropTestSystem>().Snapshots, Is.EqualTo(snapshots));
                Assert.That(Client.System<DrawingRequestDropTestSystem>().HistoryChanges, Is.EqualTo(deltas + 2));
            });
            await ClickHistory("UndoButton");
            await Server.WaitAssertion(() => Server.System<SharedUserInterfaceSystem>().CloseUi(tablet, DrawingTabletUiKey.Key, session.AttachedEntity!.Value));
            await Pair.RunTicksSync(10);
            await Server.WaitAssertion(() => Server.System<SharedUserInterfaceSystem>().OpenUi(tablet, DrawingTabletUiKey.Key, session.AttachedEntity!.Value));
            await Pair.RunTicksSync(100);
            await ClickHistory("RedoButton");
            await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<DrawingTabletComponent>(tablet).Strokes, Has.Count.EqualTo(1)));
            await Client.WaitAssertion(() =>
            {
                var window = FindControls<DrawingTabletWindow>(Client.ResolveDependency<IUserInterfaceManager>().RootControl).Single();
                Assert.That(window.FindControl<Label>("PointCount").Text, Is.EqualTo("Complexity: 32 / 32,000"));
                snapshots = Client.System<DrawingRequestDropTestSystem>().Snapshots;
            });
            await Server.WaitAssertion(() => Server.System<SharedUserInterfaceSystem>().ServerSendUiMessage(
                tablet, DrawingTabletUiKey.Key,
                new DrawingTabletHistoryMessage(-1, 0, DrawingTabletAction.Undo, null, true), session.AttachedEntity!.Value));
            await Pair.RunTicksSync(100);
            await Client.WaitAssertion(() =>
            {
                var window = FindControls<DrawingTabletWindow>(Client.ResolveDependency<IUserInterfaceManager>().RootControl).Single();
                Assert.That(window.FindControl<DrawingCanvasControl>("Canvas").CanDraw, Is.True);
                Assert.That(window.FindControl<Label>("PointCount").Text, Is.EqualTo("Complexity: 32 / 32,000"));
                Assert.That(Client.System<DrawingRequestDropTestSystem>().Snapshots, Is.EqualTo(snapshots + 1));
            });

            async Task ClickHistory(string name)
            {
                Button button = null!;
                await Client.WaitAssertion(() =>
                {
                    button = FindControls<DrawingTabletWindow>(Client.ResolveDependency<IUserInterfaceManager>().RootControl).Single().FindControl<Button>(name);
                    Assert.That(button.Disabled, Is.False);
                });
                var local = (Vector2) button.PixelSize / 2;
                var screen = new ScreenCoordinates(button.GlobalPixelPosition + local, WindowId.Main);
                await Client.DoGuiEvent(button, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, screen, true, local, local));
                await Client.DoGuiEvent(button, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, screen, true, local, local));
                await Pair.RunTicksSync(20);
            }

        }
        catch (Exception exception)
        {
            await TestContext.Progress.WriteLineAsync(exception.ToString());
            throw;
        }
        finally
        {
            await Server.WaitPost(() => { players.SetAttachedEntity(session, previous); if (SEntMan.EntityExists(map)) SEntMan.DeleteEntity(map); });
        }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task QueuedStrokesRecoverOrAreDiscardedOnImmediateClose(bool loseRequest, bool closeImmediately)
    {
        EntityUid map = default, tablet = default, actor = default;
        var players = Server.ResolveDependency<IPlayerManager>();
        var session = Pair.Player!;
        var previous = session.AttachedEntity;
        try
        {
            await Server.WaitAssertion(() =>
            {
                map = Server.System<SharedMapSystem>().CreateMap(out var id);
                var coords = new MapCoordinates(0, 0, id);
                actor = SEntMan.SpawnEntity("MobHuman", coords);
                SEntMan.EnsureComponent<GodmodeComponent>(actor);
                players.SetAttachedEntity(session, actor);
                tablet = SEntMan.SpawnEntity("DrawingTablet", coords);
                Server.System<SharedUserInterfaceSystem>().OpenUi(tablet, DrawingTabletUiKey.Key, actor);
            });
            await Pair.RunTicksSync(100);
            DrawingTabletWindow window = null!;
            DrawingCanvasControl canvas = null!;
            await Client.WaitAssertion(() =>
            {
                window = FindControls<DrawingTabletWindow>(Client.ResolveDependency<IUserInterfaceManager>().RootControl).Single();
                canvas = window.FindControl<DrawingCanvasControl>("Canvas");
                Client.System<DrawingRequestDropTestSystem>().DropAck = !loseRequest;
            });
            await Server.WaitAssertion(() => Server.System<DrawingRequestDropTestSystem>().DropStroke = loseRequest);
            var point = (Vector2) canvas.PixelSize / 2;
            for (var i = 0; i < 2; i++)
            {
                var local = point + new Vector2(i * 10, 0);
                await Client.DoGuiEvent(canvas, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, default, true, local, local));
                await Client.DoGuiEvent(canvas, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, default, true, local, local));
            }
            await Client.DoGuiEvent(canvas, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, default, true, point + new Vector2(20, 0), point + new Vector2(20, 0)));
            if (closeImmediately)
            {
                await Pair.RunTicksSync(20);
                await Client.WaitAssertion(() => { window.Close(); Assert.That(window.Parent, Is.Null); });
                await Pair.RunTicksSync(240);
                await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<DrawingTabletComponent>(tablet).Strokes,
                    Has.Count.EqualTo(loseRequest ? 0 : 1)));
                return;
            }
            await Client.WaitAssertion(() => window.FinishStroke());
            await Pair.RunTicksSync(240);
            await Server.WaitAssertion(() =>
            {
                Assert.That(SEntMan.GetComponent<DrawingTabletComponent>(tablet).Strokes, Has.Count.EqualTo(3));
                Assert.That(SEntMan.GetComponent<DrawingTabletComponent>(tablet).TotalPoints, Is.EqualTo(3));
            });
            await Client.WaitAssertion(() => { window.Close(); Assert.That(window.Parent, Is.Null); });
            await Pair.RunTicksSync(10);
            await Server.WaitAssertion(() => Server.System<SharedUserInterfaceSystem>().OpenUi(tablet, DrawingTabletUiKey.Key, actor));
            await Pair.RunTicksSync(100);
            DrawingTabletAckMessage stale = null!;
            DrawingTabletDocumentMessage staleDocument = null!;
            await Client.WaitAssertion(() =>
            {
                stale = Client.System<DrawingRequestDropTestSystem>().LastAck!;
                staleDocument = Client.System<DrawingRequestDropTestSystem>().LastDocument!;
                canvas = FindControls<DrawingTabletWindow>(Client.ResolveDependency<IUserInterfaceManager>().RootControl).Single().FindControl<DrawingCanvasControl>("Canvas");
            });
            await Server.WaitAssertion(() => Server.System<DrawingRequestDropTestSystem>().DropStroke = true);
            await Client.DoGuiEvent(canvas, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, default, true, point, point));
            await Client.DoGuiEvent(canvas, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, default, true, point, point));
            await Server.WaitAssertion(() =>
            {
                var ui = Server.System<SharedUserInterfaceSystem>();
                ui.ServerSendUiMessage(tablet, DrawingTabletUiKey.Key, stale, actor);
                ui.ServerSendUiMessage(tablet, DrawingTabletUiKey.Key, staleDocument, actor);
            });
            await Pair.RunTicksSync(240);
            await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<DrawingTabletComponent>(tablet).Strokes, Has.Count.EqualTo(4)));
            await Client.WaitAssertion(() =>
            {
                var reopened = FindControls<DrawingTabletWindow>(Client.ResolveDependency<IUserInterfaceManager>().RootControl).Single();
                Assert.That(reopened.FindControl<Label>("PointCount").Text, Is.EqualTo("Complexity: 128 / 32,000"));
                Assert.That(reopened.FindControl<Button>("UndoButton").Disabled, Is.False);
            });
        }
        catch (Exception exception)
        {
            await TestContext.Progress.WriteLineAsync(exception.ToString());
            throw;
        }
        finally
        {
            await Server.WaitPost(() => { players.SetAttachedEntity(session, previous); if (SEntMan.EntityExists(map)) SEntMan.DeleteEntity(map); });
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PrintedArtworkLoadsStaticTexture(bool dropOpeningRequest)
    {
        EntityUid map = default;
        var players = Server.ResolveDependency<IPlayerManager>();
        var session = Pair.Player!;
        var previous = session.AttachedEntity;
        try
        {
            await Server.WaitAssertion(() =>
            {
                map = Server.System<SharedMapSystem>().CreateMap(out var id);
                var coords = new MapCoordinates(0, 0, id);
                var actor = SEntMan.SpawnEntity("MobHuman", coords);
                players.SetAttachedEntity(session, actor);
                var paper = SEntMan.SpawnEntity("DrawingTabletPaper", coords);
                var print = SEntMan.GetComponent<DrawingPrintComponent>(paper);
                print.ArtistName = "Tablet Artist";
                print.Image = DrawingRaster.Encode(DrawingRaster.Render(new List<DrawingStroke>
                {
                    new() { Color = Color.Red, Size = 64, Points = new List<Vector2> { new(127, 165) } },
                }));
                Server.System<DrawingRequestDropTestSystem>().DropNext = dropOpeningRequest;
                Server.System<SharedUserInterfaceSystem>().OpenUi(paper, DrawingPrintUiKey.Key, actor);
            });
            await Pair.RunTicksSync(100);
            await Client.WaitAssertion(() =>
            {
                var root = Client.ResolveDependency<IUserInterfaceManager>().RootControl;
                var image = FindControls<TextureRect>(root).Single(c => c.Name == "PrintedArtworkImage");
                Assert.That(image.Texture, Is.Not.Null, "raster must arrive even if the opening request is lost.");
                Assert.That(image.Texture!.Size, Is.EqualTo(new Vector2i(DrawingRaster.CanvasWidthPixels, DrawingRaster.CanvasHeightPixels)));
                Assert.That(image.PixelSize.X, Is.GreaterThan(0));
                Assert.That(FindControls<Content.Client.UserInterface.Controls.FancyWindow>(root)
                    .Any(window => window.Title == "Tablet Artist's printed artwork"), Is.True);
            });
        }
        catch (Exception exception)
        {
            await TestContext.Progress.WriteLineAsync(exception.ToString());
            throw;
        }
        finally
        {
            await Server.WaitPost(() => { players.SetAttachedEntity(session, previous); if (SEntMan.EntityExists(map)) SEntMan.DeleteEntity(map); });
        }
    }

    [Test]
    public async Task OverheatBudgetIsSharedAndRecoversAfterTwoMinutes()
    {
        EntityUid map = default, actor = default, first = default, second = default;
        var players = Server.ResolveDependency<IPlayerManager>();
        var session = Pair.Player!;
        var previous = session.AttachedEntity;
        try
        {
            await Server.WaitAssertion(() =>
            {
                map = Server.System<SharedMapSystem>().CreateMap(out var id);
                var coords = new MapCoordinates(0, 0, id);
                actor = SEntMan.SpawnEntity("MobHuman", coords);
                SEntMan.EnsureComponent<GodmodeComponent>(actor);
                players.SetAttachedEntity(session, actor);
                first = SEntMan.SpawnEntity("DrawingTablet", coords);
                second = SEntMan.SpawnEntity("DrawingTablet", coords);
                var ui = Server.System<SharedUserInterfaceSystem>();
                ui.OpenUi(first, DrawingTabletUiKey.Key, actor);
                ui.OpenUi(second, DrawingTabletUiKey.Key, actor);
                for (var i = 0; i < 30; i++) Stroke(i % 2 == 0 ? first : second);
                Assert.That(SEntMan.GetComponent<DrawingTabletComponent>(first).Strokes.Count +
                    SEntMan.GetComponent<DrawingTabletComponent>(second).Strokes.Count, Is.EqualTo(30));
            });
            await Pair.RunSeconds(1f);
            TimeSpan until = default;
            await Server.WaitAssertion(() =>
            {
                Stroke(first);
                Stroke(second);
                Assert.That(SEntMan.GetComponent<DrawingTabletComponent>(first).Strokes, Has.Count.EqualTo(16));
                Assert.That(SEntMan.GetComponent<DrawingTabletComponent>(second).Strokes, Has.Count.EqualTo(16));
                Stroke(first);
                var comp = SEntMan.GetComponent<DrawingTabletComponent>(first);
                until = comp.OverheatedUntil;
                Assert.That((until - Server.ResolveDependency<IGameTiming>().CurTime).TotalSeconds, Is.EqualTo(120).Within(0.1));
                Assert.That(SEntMan.GetComponent<DrawingTabletComponent>(second).OverheatedUntil, Is.EqualTo(until));
                var paper = comp.Paper;
                for (var i = 0; i < 100; i++) Stroke(first);
                Server.System<SharedUserInterfaceSystem>().RaiseUiMessage(first, DrawingTabletUiKey.Key,
                    new DrawingTabletActionMessage(DrawingTabletAction.Print, comp.Revision) { Actor = actor });
                Assert.That(comp.Strokes, Has.Count.EqualTo(16));
                Assert.That(comp.Paper, Is.EqualTo(paper));
                Assert.That(comp.OverheatedUntil, Is.EqualTo(until), "Repeated attempts must not extend cooldown or trigger a new incident.");
            });
            await Pair.RunSeconds(119f);
            await Server.WaitAssertion(() =>
            {
                Stroke(second);
                Assert.That(SEntMan.GetComponent<DrawingTabletComponent>(second).Strokes, Has.Count.EqualTo(16));
            });
            await Pair.RunSeconds(2f);
            await Server.WaitAssertion(() =>
            {
                Assert.That(Server.ResolveDependency<IGameTiming>().CurTime, Is.GreaterThanOrEqualTo(until));
                Assert.That(Server.System<SharedUserInterfaceSystem>().IsUiOpen(second, DrawingTabletUiKey.Key, actor), Is.True);
                Stroke(second);
                Assert.That(SEntMan.GetComponent<DrawingTabletComponent>(second).Strokes, Has.Count.EqualTo(17));
                Assert.That(SEntMan.GetComponent<DrawingTabletComponent>(first).OverheatedUntil, Is.EqualTo(TimeSpan.Zero));
            });
        }
        catch (Exception exception)
        {
            await TestContext.Progress.WriteLineAsync(exception.ToString());
            throw;
        }
        finally
        {
            await Server.WaitPost(() => { players.SetAttachedEntity(session, previous); if (SEntMan.EntityExists(map)) SEntMan.DeleteEntity(map); });
        }

        void Stroke(EntityUid tablet)
        {
            var comp = SEntMan.GetComponent<DrawingTabletComponent>(tablet);
            Server.System<SharedUserInterfaceSystem>().RaiseUiMessage(tablet, DrawingTabletUiKey.Key,
                new DrawingTabletStrokeMessage(new DrawingStroke { Points = new List<Vector2> { new(20, 20) } }, comp.Revision) { Actor = actor });
        }
    }

    private static IEnumerable<T> FindControls<T>(Control root) where T : Control
    {
        if (root is T result) yield return result;
        foreach (var child in root.Children)
        foreach (var match in FindControls<T>(child))
            yield return match;
    }

    private sealed class DrawingRequestDropTestSystem : EntitySystem
    {
        public bool DropNext;
        public bool DropDocument;
        public bool DropAck;
        public bool DropStroke;
        public DrawingTabletAckMessage? LastAck;
        public DrawingTabletDocumentMessage? LastDocument;
        public int Snapshots;
        public int HistoryChanges;
        public override void Initialize() => SubscribeLocalEvent<BoundUserInterfaceMessageAttempt>(OnAttempt);
        private void OnAttempt(BoundUserInterfaceMessageAttempt args)
        {
            if (args.Message is DrawingTabletDocumentMessage document)
            {
                LastDocument = document;
                Snapshots++;
                if (DropDocument) { DropDocument = false; args.Cancel(); }
            }
            if (args.Message is DrawingTabletAckMessage ack)
            {
                LastAck = ack;
                if (DropAck) { DropAck = false; args.Cancel(); }
            }
            if (DropStroke && args.Message is DrawingTabletStrokeMessage)
            {
                DropStroke = false;
                args.Cancel();
            }
            if (args.Message is DrawingTabletHistoryMessage) HistoryChanges++;
            if (!DropNext || args.Message is not (DrawingTabletRequestMessage or DrawingPrintRequestMessage))
                return;
            DropNext = false;
            args.Cancel();
        }
    }

    [Test]
    public async Task PaperEditingAndPermanentPrint()
    {
        try
        {
            await CheckPaperEditingAndPermanentPrint();
        }
        catch (Exception exception)
        {
            await TestContext.Progress.WriteLineAsync(exception.ToString());
            throw;
        }
    }

    private async Task CheckPaperEditingAndPermanentPrint()
    {
        EntityUid map = default, tablet = default, user = default;
        await Server.WaitAssertion(() =>
        {
            map = Server.System<SharedMapSystem>().CreateMap(out var mapId);
            var coordinates = new MapCoordinates(0, 0, mapId);
            tablet = SEntMan.SpawnEntity("DrawingTablet", coordinates);
            user = SEntMan.SpawnEntity("MobHuman", coordinates);
            var comp = SEntMan.GetComponent<DrawingTabletComponent>(tablet);
            var ui = Server.System<SharedUserInterfaceSystem>();
            ui.OpenUi(tablet, DrawingTabletUiKey.Key, user);
            Assert.That(comp.Editor, Is.EqualTo(user));
            var paper = SEntMan.SpawnEntity("Paper", coordinates);
            SEntMan.EventBus.RaiseLocalEvent(tablet, new InteractUsingEvent(user, paper, tablet, new EntityCoordinates(map, 0, 0)));
            Assert.That(comp.Paper, Is.EqualTo(DrawingTabletSettings.StartingPaperSheets + 1));

            var stroke = new DrawingStroke { Points = [new Vector2(20, 20), new Vector2(80, 80)], Color = Color.Red, Size = 8 };
            ui.RaiseUiMessage(tablet, DrawingTabletUiKey.Key, new DrawingTabletStrokeMessage(stroke, comp.Revision) { Actor = user });
            Assert.That(comp.Strokes, Has.Count.EqualTo(1));
            ui.RaiseUiMessage(tablet, DrawingTabletUiKey.Key, new DrawingTabletStrokeMessage(stroke, -1) { Actor = user });
            Assert.That(comp.Strokes, Has.Count.EqualTo(1), " Stale revisions cannot change the drawing.");
            Action(DrawingTabletAction.Undo);
            Assert.That(comp.Strokes, Is.Empty);
            Action(DrawingTabletAction.Redo);
            Assert.That(comp.Strokes, Has.Count.EqualTo(1));
            Server.System<MetaDataSystem>().SetEntityName(user, "Tablet Artist");
            Action(DrawingTabletAction.Print);
            Server.System<MetaDataSystem>().SetEntityName(user, "Renamed Artist");
            Assert.That(comp.Paper, Is.EqualTo(DrawingTabletSettings.StartingPaperSheets));
            var revision = comp.Revision;
            Action(DrawingTabletAction.Clear);
            Assert.That(comp.Strokes, Has.Count.EqualTo(1), "Clearing without an ID must be rejected.");
            Assert.That(comp.Revision, Is.EqualTo(revision));
            var unrestricted = SEntMan.SpawnEntity("DrawingTabletUnlimited", coordinates);
            var unrestrictedComp = SEntMan.GetComponent<DrawingTabletComponent>(unrestricted);
            Assert.That(unrestrictedComp.RequiresClearAccess, Is.False);
            ui.OpenUi(unrestricted, DrawingTabletUiKey.Key, user);
            ui.RaiseUiMessage(unrestricted, DrawingTabletUiKey.Key, new DrawingTabletStrokeMessage(stroke, unrestrictedComp.Revision) { Actor = user });
            Assert.That(unrestrictedComp.Strokes, Has.Count.EqualTo(1));
            ui.RaiseUiMessage(unrestricted, DrawingTabletUiKey.Key, new DrawingTabletActionMessage(DrawingTabletAction.Clear, unrestrictedComp.Revision) { Actor = user });
            Assert.That(unrestrictedComp.Strokes, Is.Empty, "The nullspace tablet must clear without an ID.");
            var unauthorized = SEntMan.SpawnEntity("AssistantIDCard", coordinates);
            Assert.That(Server.System<SharedHandsSystem>().TryPickupAnyHand(user, unauthorized), Is.True);
            Action(DrawingTabletAction.Clear);
            Assert.That(comp.Strokes, Has.Count.EqualTo(1), "A non-Captain ID must not authorize clearing.");
            SEntMan.DeleteEntity(unauthorized);
            var captain = SEntMan.SpawnEntity("CaptainIDCard", coordinates);
            Assert.That(Server.System<SharedHandsSystem>().TryPickupAnyHand(user, captain), Is.True);
            Action(DrawingTabletAction.Clear);
            Assert.That(comp.Strokes, Is.Empty);

            void Action(DrawingTabletAction action) => ui.RaiseUiMessage(tablet, DrawingTabletUiKey.Key,
                new DrawingTabletActionMessage(action, comp.Revision) { Actor = user });
        });

        var printed = false;
        for (var i = 0; i < 100 && !printed; i++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitAssertion(() => printed = SEntMan.EntityQuery<DrawingPrintComponent>().Any());
        }
        await Server.WaitAssertion(() =>
        {
            Assert.That(printed, Is.True);
            var print = SEntMan.EntityQuery<DrawingPrintComponent>().Single();
            Assert.That(print.ArtistName, Is.EqualTo("Tablet Artist"));
            Assert.That(print.ReportId, Does.Match("^AW-[0-9]{2,}$"));
            var originalId = print.ReportId;
            var description = SEntMan.GetComponent<MetaDataComponent>(print.Owner).EntityDescription;
            var examine = Server.System<ExamineSystemShared>().GetExamineText(print.Owner, user, out _).ToString();
            Assert.That(examine, Does.Contain(originalId));
            Assert.That(examine.IndexOf(originalId, StringComparison.Ordinal), Is.GreaterThan(examine.IndexOf(description, StringComparison.Ordinal)));
            Server.System<MetaDataSystem>().SetEntityName(print.Owner, " Renamed artwork for test");
            Assert.That(print.ReportId, Is.EqualTo(originalId));
            var another = SEntMan.SpawnEntity("DrawingTabletPaper", SEntMan.GetComponent<TransformComponent>(tablet).Coordinates);
            var anotherId = SEntMan.GetComponent<DrawingPrintComponent>(another).ReportId;
            Assert.That(anotherId, Is.Not.EqualTo(originalId));
            SEntMan.DeleteEntity(another);
            another = SEntMan.SpawnEntity("DrawingTabletPaper", SEntMan.GetComponent<TransformComponent>(tablet).Coordinates);
            Assert.That(SEntMan.GetComponent<DrawingPrintComponent>(another).ReportId, Is.Not.EqualTo(anotherId));

            Assert.That(SEntMan.GetComponent<MetaDataComponent>(print.Owner).EntityDescription,
                Is.EqualTo("A printed copy of Tablet Artist's artwork"));
            Assert.That(DrawingRaster.TryDecode(print.Image, out var pixels), Is.True);
            var offset = (20 * DrawingRaster.CanvasWidthPixels + 20) * 3;
            Assert.That(pixels[offset], Is.EqualTo(255));
            Assert.That(pixels[offset + 1], Is.Zero, " Clearing the tablet must not change its queued print.");
            Assert.That(print.Thumbnail.Length, Is.EqualTo(DrawingRaster.ThumbnailWidthPixels * DrawingRaster.ThumbnailHeightPixels * 3));
            Assert.That(SEntMan.GetComponent<DrawingTabletComponent>(tablet).Paper, Is.EqualTo(DrawingTabletSettings.StartingPaperSheets));
            SEntMan.DeleteEntity(map);
        });
    }

    [Test]
    public async Task SamplerPicksCanvasAndCancels()
    {
        try { await CheckSampler(); }
        catch (Exception exception)
        {
            await TestContext.Progress.WriteLineAsync(exception.ToString());
            throw;
        }
    }

    private async Task CheckSampler()
    {
        DrawingTabletWindow window = null!;
        DrawingCanvasControl canvas = null!;
        DrawingColorSampler sampler = null!;
        await Client.WaitPost(() =>
        {
            window = new DrawingTabletWindow();
            window.SetDocument(new DrawingTabletState(new List<DrawingStroke>
            {
                new() { Points = new List<Vector2> { new(127, 165) }, Color = Color.Red, Size = 1 },
            }, 0, false, 1000));
            window.OpenCentered();
            canvas = window.FindControl<DrawingCanvasControl>("Canvas");
        });
        await Pair.RunTicksSync(2);
        await Client.WaitAssertion(() =>
        {
            // Start with the color entry focused to check that sampling clears it.
            var entry = FindControls<LineEdit>(window).First();
            entry.GrabKeyboardFocus();
        });
        async Task OpenSampler()
        {
            var button = window.FindControl<Button>("SampleTool");
            var local = (Vector2) button.PixelSize / 2;
            var screen = new ScreenCoordinates(button.GlobalPixelPosition + local, WindowId.Main);
            await Client.DoGuiEvent(button, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, screen, true, local, local));
            await Client.DoGuiEvent(button, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, screen, true, local, local));
            await Pair.RunTicksSync(2);
            await Client.WaitAssertion(() => sampler = FindControls<DrawingColorSampler>(Client.ResolveDependency<IUserInterfaceManager>().RootControl).Single());
        }
        await OpenSampler();
        await Client.WaitAssertion(() =>
        {
            Assert.That(Client.ResolveDependency<IUserInterfaceManager>().KeyboardFocused, Is.Null);
        });
        var point = new ScreenCoordinates(canvas.GlobalPixelPosition + (Vector2) canvas.PixelSize / 2, WindowId.Main);
        await Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            Assert.That(ui.MouseGetControl(point), Is.SameAs(canvas));
            var sampleButton = window.FindControl<Button>("SampleTool");
            var outside = new ScreenCoordinates(sampleButton.GlobalPixelPosition + (Vector2) sampleButton.PixelSize / 2, WindowId.Main);
            Assert.That(ui.MouseGetControl(outside), Is.Not.SameAs(sampler));
            var local = (Vector2) canvas.PixelSize / 2;
            Assert.That(canvas.TrySampleColor(local, out var before), Is.True);
            canvas.ZoomAt(local, 4);
            Assert.That(canvas.Zoom, Is.GreaterThan(1));
            Assert.That(canvas.TrySampleColor(local, out var after), Is.True);
            Assert.That(after, Is.EqualTo(before), " Zoom must preserve the paper point under the pointer.");
            canvas.ZoomAt(local, 100);
            Assert.That(canvas.Zoom, Is.EqualTo(16));
            canvas.ResetView();
            Assert.That(canvas.Zoom, Is.EqualTo(1));
        });
        var samplePosition = (Vector2) canvas.PixelSize / 2;
        await Client.DoGuiEvent(canvas, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down,
            point, true, samplePosition / canvas.UIScale, samplePosition));
        await Client.WaitAssertion(() =>
        {
            Assert.That(canvas.Ink, Is.EqualTo(Color.Red));
            Assert.That(canvas.Tool, Is.EqualTo(DrawingTool.Draw));
            Assert.That(sampler.Visible, Is.False);
        });
        await OpenSampler();
        await Client.DoGuiEvent(canvas, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIRightClick, BoundKeyState.Down, point, true, default, default));
        await Client.WaitAssertion(() =>
        {
            Assert.That(sampler.Visible, Is.False);
            Assert.That(canvas.Ink, Is.EqualTo(Color.Red));
        });
        await OpenSampler();
        await Client.WaitAssertion(() =>
        {
            var input = Client.ResolveDependency<IInputManager>();
            var escape = new KeyEventArgs(Keyboard.Key.Escape, false, false, false, false, false, 0);
            input.KeyDown(escape);
            input.KeyUp(escape);
            Assert.That(sampler.Visible, Is.False, "Escape must dismiss the sampling modal.");
            Assert.That(canvas.Ink, Is.EqualTo(Color.Red));
            Assert.That(window.IsOpen, Is.True, "Cancel sampling without closing the tablet.");
            window.Close();
        });
    }

    [Test]
    public async Task PixelModeSwitchesLabelAndEnablesSquareSizes()
    {
        DrawingTabletWindow window = null!;
        await Client.WaitPost(() => { window = new DrawingTabletWindow(); window.OpenCentered(); });
        await Pair.RunTicksSync(2);
        var toggle = window.FindControl<Button>("PixelMode");
        await Client.WaitAssertion(() => window.FindControl<Slider>("SizeSlider").Value = 16);
        async Task Click()
        {
            var local = (Vector2) toggle.PixelSize / 2;
            var screen = new ScreenCoordinates(toggle.GlobalPixelPosition + local, WindowId.Main);
            await Client.DoGuiEvent(toggle, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, screen, true, local, local));
            await Client.DoGuiEvent(toggle, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, screen, true, local, local));
        }
        await Click();
        await Client.WaitAssertion(() =>
        {
            Assert.That(toggle.Text, Is.EqualTo("Vector art"));
            Assert.That(window.FindControl<DrawingCanvasControl>("Canvas").PixelArt, Is.True);
            Assert.That(window.FindControl<Button>("CircleBrush").Disabled, Is.True);
            Assert.That(window.FindControl<Slider>("SizeSlider").Disabled, Is.False);
            Assert.That(window.FindControl<Slider>("SizeSlider").MaxValue, Is.EqualTo(4));
            var sizeInput = window.FindControl<BoxContainer>("SizeInput");
            Assert.That(FindControls<OptionButton>(sizeInput), Is.Empty);
            var sizeLabel = sizeInput.Children.OfType<Label>().Single();
            Assert.That(sizeLabel.Text, Is.EqualTo("1x1"));
            window.FindControl<Slider>("SizeSlider").Value = 3;
            Assert.That(sizeLabel.Text, Is.EqualTo("5x5"));
            Assert.That(window.FindControl<DrawingCanvasControl>("Canvas").BrushSize, Is.EqualTo(5));
            window.FindControl<Slider>("SizeSlider").Value = 4;
            Assert.That(sizeLabel.Text, Is.EqualTo("15x15"));
        });
        DrawingStroke? pixelStroke = null;
        var canvas = window.FindControl<DrawingCanvasControl>("Canvas");
        await Client.WaitAssertion(() =>
        {
            canvas.CanDraw = true;
            canvas.StrokeCompleted += stroke => pixelStroke = stroke;
        });
        var paperPoint = (Vector2) canvas.PixelSize / 2;
        await Client.DoGuiEvent(canvas, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down,
            default, true, paperPoint / canvas.UIScale, paperPoint));
        await Client.DoGuiEvent(canvas, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up,
            default, true, paperPoint / canvas.UIScale, paperPoint));
        await Client.WaitAssertion(() =>
        {
            Assert.That(pixelStroke, Is.Not.Null);
            Assert.That(pixelStroke!.Size, Is.EqualTo(15));
            Assert.That(pixelStroke.Points[0].X % 15, Is.Zero);
            Assert.That(pixelStroke.Points[0].Y % 15, Is.Zero);
        });
        await Click();
        await Client.WaitAssertion(() =>
        {
            Assert.That(toggle.Text, Is.EqualTo("Pixel art"));
            Assert.That(window.FindControl<Button>("CircleBrush").Disabled, Is.False);
            Assert.That(window.FindControl<Slider>("SizeSlider").MaxValue, Is.EqualTo(64));
            Assert.That(window.FindControl<DrawingCanvasControl>("Canvas").BrushSize, Is.EqualTo(16));
            window.Close();
        });
    }

    [Test]
    public async Task NativeWindowLoadsAndAcceptsDocument()
    {
        await Client.WaitAssertion(() =>
        {
            var window = new DrawingTabletWindow();
            // Simulate an older stroke with nearly transparent byte alpha 1.
            window.SetDocument(new DrawingTabletState(new List<DrawingStroke>
            {
                new() { Color = Color.Red.WithAlpha((byte) 1), Points = new List<Vector2> { new(20, 20) } },
            }, 0, false, 1000));
            var picker = window.FindControl<ColorSelectorSliders>("ColorPicker");
            picker.OnColorChanged?.Invoke(Color.Blue);
            Assert.That(window.FindControl<DrawingCanvasControl>("Canvas").Ink, Is.EqualTo(Color.Blue));
            var current = window.FindControl<PanelContainer>("CurrentColor");
            Assert.That(((StyleBoxFlat) current.PanelOverride!).BackgroundColor, Is.EqualTo(Color.Blue));
            var recent = window.FindControl<BoxContainer>("RecentColors");
            var swatch = FindControls<PanelContainer>(recent).First();
            Assert.That(((StyleBoxFlat) swatch.PanelOverride!).BackgroundColor, Is.EqualTo(Color.Red),
                "Existing drawings must recover fully opaque recent-color previews.");
            window.SetPending(1, false);
            window.SetPending(0, false);
            window.Dispose();
        });
    }
}
