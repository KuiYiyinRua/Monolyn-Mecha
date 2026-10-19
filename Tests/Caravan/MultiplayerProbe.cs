using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using Monolyn.Mecha;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;
using Mp = Multiplayer.Client.Multiplayer;

namespace CaravanTests
{
    public sealed class State : GameComponent
    {
        public List<Pawn> units = new List<Pawn>(), pilots = new List<Pawn>();
        public Pawn owner, hauler, animal, vanilla;
        public int phase = -1, start, prepared, cycle;
        public bool formed;
        public PlanetTile destination;
        public State(Game game) { }
        public override void GameComponentUpdate() { if (Driver.Enabled) Driver.Update(); }
        public override void GameComponentTick()
        {
            if (!Driver.Enabled || owner == null) return;
            if (units.Any(p => p.GetLord()?.LordJob is LordJob_FormAndSendCaravan)) formed = true;
            if (Find.TickManager.TicksGame % 250 != 0) return;
            foreach (var p in pilots.Concat(new[] { owner, hauler }))
                foreach (var n in p.needs.AllNeeds)
                    if (n is Need_Food || n is Need_Rest || n is Need_Mood) n.CurLevel = n.MaxLevel;
        }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref units, "caravanUnits", LookMode.Reference);
            Scribe_Collections.Look(ref pilots, "caravanPilots", LookMode.Reference);
            Scribe_References.Look(ref owner, "caravanOwner"); Scribe_References.Look(ref hauler, "caravanHauler");
            Scribe_References.Look(ref animal, "caravanAnimal"); Scribe_References.Look(ref vanilla, "caravanVanilla");
            Scribe_Values.Look(ref phase, "caravanPhase", -1); Scribe_Values.Look(ref start, "caravanStart");
            Scribe_Values.Look(ref prepared, "caravanPrepared"); Scribe_Values.Look(ref cycle, "caravanCycle");
            Scribe_Values.Look(ref formed, "caravanFormed"); Scribe_Values.Look(ref destination, "caravanDestination");
        }
    }
    [StaticConstructorOnStartup]
    public static class Driver
    {
        public static readonly bool Enabled = GenCommandLine.CommandLineArgPassed("mm-caravan-test");
        static readonly bool Client = GenCommandLine.CommandLineArgPassed("mm-client");
        static readonly string Root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        static readonly int Port = GenCommandLine.TryGetCommandLineArg("mm-port", out string port) ? int.Parse(port) : 31427;
        static readonly int Duration = GenCommandLine.TryGetCommandLineArg("mm-ticks", out string ticks) ? int.Parse(ticks) : 12000;
        static int cycle = GenCommandLine.TryGetCommandLineArg("mm-cycle", out string c) ? int.Parse(c) : 0;
        static State S => Current.Game.GetComponent<State>();
        static Map A => Find.Maps[0];
        static int Tick => Mp.AsyncWorldTime.worldTicks;
        static string Peer => Client ? "client" : "host";
        static string At(string name) => Path.Combine(Root, name);
        static CaravanFormingSession Session => A.MpComp().sessionManager.GetFirstOfType<CaravanFormingSession>();
        static Caravan Convoy => S.units[0].GetCaravan();
        static bool hosted, ready, finished, freezeSent;
        static int sent = -999, checkedPhase = -999, checkpoint, screenshotFrame;
        static ISyncMethod prepare, check, restart;
        static Driver()
        {
            if (!Enabled) return;
            new Harmony("local.mecha.caravan.test").Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(Driver), nameof(Update)));
            prepare = MP.RegisterSyncMethod(typeof(Driver), nameof(Prepare));
            check = MP.RegisterSyncMethod(typeof(Driver), nameof(Check));
            restart = MP.RegisterSyncMethod(typeof(Driver), nameof(Restart));
            Log.Message("CARAVAN BOOT peer=" + Peer + " run=" + Path.GetFileName(Root) + " mvid=" + typeof(Pawn_Immortal).Assembly.ManifestModule.ModuleVersionId);
            foreach (var line in File.ReadAllLines(At("candidate.sha256"))) Log.Message("CARAVAN HASH " + line);
        }
        static void Need(bool ok, string why) { if (!ok) throw new Exception(why); }
        static void Write(string path, string value) { File.WriteAllText(At(path), value); Log.Message("CARAVAN " + path + " " + value); }
        static string N(float n) => n.ToString("R", CultureInfo.InvariantCulture);
        static string Snapshot() => "world=" + Tick + ":" + Mp.AsyncWorldTime.randState + " map=" + A.uniqueID + ":" + A.AsyncTime().mapTicks + ":" + A.AsyncTime().randState
            + " cycle=" + S.cycle + " phase=" + S.phase + " bandwidth=" + S.owner.mechanitor.UsedBandwidth
            + string.Join("", S.units.Select(p => "\n" + p.ThingID + ":" + p.Map?.uniqueID + ":" + p.Position + ":" + p.GetOverseer()?.ThingID + ":" + p.GetCaravan()?.ID
                + ":" + N(((Pawn_Immortal)p).Light.currentLight) + ":" + ((Pawn_Immortal)p).Pilot?.ThingID + ":" + Cargo(p)));
        static int Cargo(Pawn p) => p.inventory.innerContainer.Where(t => t.def == ThingDefOf.Steel).Sum(t => t.stackCount);
        static Pawn Make(string kind, IntVec3 cell)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDef.Named(kind), Faction.OfPlayer, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            GenSpawn.Spawn(p, cell, A); return p;
        }
        static void Setup()
        {
            A.fogGrid.ClearAllFog();
            foreach (var p in A.mapPawns.AllPawnsSpawned.ToArray())
            { p.GetLord()?.Notify_PawnLost(p, PawnLostCondition.ExitedMap); p.DeSpawn(); Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.KeepForever); }
            foreach (var cell in A.AllCells)
            {
                foreach (var t in cell.GetThingList(A).ToArray()) if (t.def.destroyable) t.Destroy();
                A.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil); A.roofGrid.SetRoof(cell, null);
            }
            foreach (var p in Find.WorldPawns.AllPawnsAliveOrDead.ToArray()) p.relations?.ClearAllRelations();
            S.owner = Make("Colonist", new IntVec3(30, 0, 30));
            S.owner.health.AddHediff(HediffDefOf.MechlinkImplant, S.owner.health.hediffSet.GetBrain());
            S.owner.health.AddHediff(HediffDef.Named("MM_TestBandwidth")); S.owner.drafter.Drafted = true;
            S.hauler = Make("Colonist", new IntVec3(38, 0, 38));
            S.animal = Make("Muffalo", new IntVec3(28, 0, 28));
            S.vanilla = Make("Mech_Lifter", new IntVec3(25, 0, 25));
            string[] names = { "MM_Immortal", "MM_Colossus", "MM_Reaver", "MM_Disruptor" };
            for (int i = 0; i < names.Length; i++)
            {
                var p = (Pawn_Immortal)Make(names[i], new IntVec3(40 + i * 7, 0, 50));
                p.autoFire = false; p.Light.currentLight = p.LightMax; S.units.Add(p);
                var pilot = Make("Colonist", new IntVec3(40 + i * 7, 0, 44)); pilot.drafter.Drafted = true; S.pilots.Add(pilot);
            }
            var neighbors = new List<PlanetTile>(); Find.WorldGrid.GetTileNeighbors(A.Tile, neighbors);
            S.destination = neighbors.First(t => !Find.World.Impassable(t) && !Find.WorldObjects.AnyWorldObjectAt(t));
        }
        static void NewSession(bool select)
        {
            var session = A.MpComp().CreateCaravanFormingSession(Faction.OfPlayer, false, null, false, new IntVec3(45, 0, 45));
            session.autoSelectTravelSupplies = false; session.startingTile = S.destination; session.destinationTile = S.destination;
            if (select)
                foreach (var tr in session.transferables)
                {
                    bool take = tr.AnyThing == S.hauler || S.units.Contains(tr.AnyThing as Pawn) || tr.ThingDef == ThingDefOf.Steel || tr.ThingDef == ThingDefOf.MealSurvivalPack;
                    tr.AdjustTo(take ? tr.MaxCount : 0);
                }
            CheckRows(session.transferables);
        }
        static void CheckRows(List<TransferableOneWay> trs)
        {
            var widget = Selection.Widget(trs); var all = Selection.Rows(widget); var animals = Selection.Rows(widget, "AnimalsSection".Translate());
            foreach (var p in S.units)
            {
                Need(all.Count(x => x == p) == 1 && animals.Count(x => x == p) == 1, "warframe row missing or duplicated " + p.ThingID);
                Need(!p.IsAnimal && p.IsColonyMech && MassUtility.Capacity(p) == ((Pawn_Immortal)p).CargoMax, "global classification/capacity changed");
            }
            Need(animals.Contains(S.animal) && !all.Contains(S.vanilla), "vanilla animal/uncontrolled mech regression");
        }
        public static void Prepare()
        {
            S.phase++; S.prepared = TickPatch.Timer; if (S.start == 0) S.start = Tick;
            int kind = S.phase % 6, mode = S.phase / 6;
            if (kind == 0)
            {
                S.formed = false;
                // Colonists outside the convoy may be unloading the previous
                // trip too. Clear every spawned holder before seeding new cargo.
                foreach (var p in A.mapPawns.AllPawnsSpawned.ToArray())
                {
                    p.jobs.StopAll(); p.pather.StopDead();
                    p.carryTracker?.innerContainer.ClearAndDestroyContents();
                    foreach (var t in p.inventory.innerContainer.Where(t => t.def == ThingDefOf.Steel).ToArray()) t.Destroy();
                }
                foreach (var p in S.units.Concat(new[] { S.hauler }))
                {
                    p.jobs.StopAll(); p.pather.StopDead(); p.inventory.innerContainer.ClearAndDestroyContents();
                    p.carryTracker?.innerContainer.ClearAndDestroyContents();
                    p.Position = p == S.hauler ? new IntVec3(38, 0, 38) : new IntVec3(40 + S.units.IndexOf(p) * 7, 0, 50);
                    if (p is Pawn_Immortal w)
                    {
                        if (w.Pilot != null) w.Eject();
                        if (w.GetOverseer() != null) MechaAutonomy.Unbind(A, w);
                        w.Light.currentLight = w.LightMax;
                        if (mode == 1)
                        {
                            var pilot = S.pilots[S.units.IndexOf(w)]; pilot.Position = w.Position + new IntVec3(0, 0, -1);
                            Need(w.Board(pilot), "pilot boarding failed");
                        }
                        if (mode == 2)
                        {
                            MechaAutonomy.Bind(A, w, S.owner);
                            Need(w.GetOverseer() == S.owner, "binding failed");
                            w.GetMechControlGroup().SetWorkMode(MechWorkModeDefOf.Escort);
                        }
                    }
                    if (p.drafter != null) p.drafter.Drafted = false;
                }
                // Stopping an unload job can drop its carried stack. Purge the
                // previous trip only after all jobs/holders have been cleared.
                foreach (var t in A.listerThings.ThingsOfDef(ThingDefOf.Steel).ToArray()) t.Destroy();
                for (int i = 0; i < 4; i++)
                {
                    var cell = new IntVec3(39 + i, 0, 38); A.areaManager.Home[cell] = true;
                    var steel = ThingMaker.MakeThing(ThingDefOf.Steel); steel.stackCount = 75; GenSpawn.Spawn(steel, cell, A);
                }
                var meals = ThingMaker.MakeThing(ThingDefOf.MealSurvivalPack); meals.stackCount = 20; S.hauler.inventory.innerContainer.TryAdd(meals);
                NewSession(false);
            }
            if (kind == 1) NewSession(true);
            Log.Message("CARAVAN PREPARE phase=" + S.phase + " mode=" + mode + " tick=" + Tick);
        }
        static void Dispatch()
        {
            int kind = S.phase % 6;
            if (kind == 0 || kind == 1)
            {
                Dialog_FormCaravan dialog = Session.OpenWindow(); CheckRows(dialog.transferables); dialog.Close();
                dialog = Session.OpenWindow(); CheckRows(dialog.transferables);
                if (kind == 0) { dialog.Close(); Session.Cancel(); }
                else
                {
                    Need(S.units.All(p => dialog.transferables.Single(t => t.things.Contains(p)).CountToTransfer == 1), "selection reset by Escort");
                    Log.Message("CARAVAN PREVIEW capacity=" + dialog.MassCapacity + " mass=" + dialog.MassUsage
                        + " selected=" + string.Join(";", dialog.transferables.Where(t => t.CountToTransfer > 0).Select(t => t.ThingDef.defName + ":" + t.CountToTransfer)));
                    Need(dialog.MassCapacity >= 3900 && dialog.MassUsage > 100, "capacity/cargo preview capacity=" + dialog.MassCapacity + " mass=" + dialog.MassUsage);
                    screenshotFrame = Time.frameCount + 10;
                }
            }
            if (kind == 2)
            {
                Dialog_FormCaravan dialog = Find.WindowStack.Windows.OfType<Dialog_FormCaravan>().LastOrDefault() ?? Session.OpenWindow();
                AccessTools.Method(typeof(Dialog_FormCaravan), "TryFormAndSendCaravan").Invoke(dialog, null);
                dialog.Close();
            }
            if (kind == 4)
            {
                var options = FloatMenuMakerWorld.ChoicesAtFor(A.Tile, Convoy).Where(o => !o.Disabled).ToList();
                Need(options.Count > 0, "no home arrival option");
                Log.Message("CARAVAN RETURN choices=" + string.Join(";", options.Select(o => o.Label)));
                options[0].action();
            }
            Log.Message("CARAVAN DISPATCH phase=" + S.phase + " peer=" + Peer);
        }
        static bool Settled()
        {
            switch (S.phase % 6)
            {
                case 0: return Session == null;
                case 2: return S.formed;
                case 3: return Convoy != null && Convoy.Tile == S.destination && !Convoy.pather.Moving;
                case 4: return S.units.All(p => p.Spawned && p.Map == A) && S.hauler.Spawned;
                default: return true;
            }
        }
        public static void Check()
        {
            Need(Settled(), "stage not settled");
            int kind = S.phase % 6, mode = S.phase / 6;
            if (kind == 1)
            {
                Need(S.units.All(p => Session.transferables.Single(t => t.things.Contains(p)).CountToTransfer == 1), "selection changed");
                Need(Session.transferables.Single(t => t.things.Contains(S.owner)).CountToTransfer == 0, "mechanitor unexpectedly selected");
            }
            if (kind == 3)
            {
                Need(S.formed && S.units.All(p => !p.Spawned && p.GetCaravan() == Convoy && p.IsWorldPawn()), "native exit failed");
                Need(Convoy.PawnsListForReading.Sum(Cargo) == 300 && S.units.Sum(Cargo) > 0, "native loading lost cargo");
                Need(S.units.All(p => ((Pawn_Immortal)p).Light.currentLight < ((Pawn_Immortal)p).LightMax), "travel consumed no light");
            }
            if (kind == 4 || kind == 5)
            {
                foreach (var p in S.units)
                {
                    Need(p.Spawned && p.Map == A && p.OccupiedRect().InBounds(A) && !p.IsWorldPawn() && p.GetCaravan() == null, "return lost chassis " + p.ThingID);
                    Need(mode == 2 ? p.GetOverseer() == S.owner : p.GetOverseer() == null, "overseer changed");
                    Need(mode == 1 ? ((Pawn_Immortal)p).Pilot == S.pilots[S.units.IndexOf(p)] : ((Pawn_Immortal)p).Pilot == null, "pilot lost or duplicated");
                }
                int ground = A.listerThings.ThingsOfDef(ThingDefOf.Steel).Sum(t => t.stackCount);
                int inventory = A.mapPawns.AllPawnsSpawned.Sum(Cargo);
                int carried = A.mapPawns.AllPawnsSpawned.Sum(p => p.carryTracker?.CarriedThing?.def == ThingDefOf.Steel ? p.carryTracker.CarriedThing.stackCount : 0);
                Log.Message("CARAVAN CARGO phase=" + S.phase + " ground=" + ground + " inventory=" + inventory + " carried=" + carried
                    + " holders=" + string.Join(";", A.mapPawns.AllPawnsSpawned.Where(p => Cargo(p) > 0 || p.carryTracker?.CarriedThing?.def == ThingDefOf.Steel)
                        .Select(p => p.ThingID + ":inventory=" + Cargo(p) + ":hands=" + p.carryTracker?.CarriedThing + ":job=" + p.CurJob)));
                Need(ground + inventory + carried == 300, "cargo conservation failed on return " + (ground + inventory + carried));
                if (kind == 5) CheckRows(Dialog_FormCaravan.AllSendablePawns(A, true).Select(p => { var t = new TransferableOneWay(); t.things.Add(p); return t; }).ToList());
            }
            Log.Message("CARAVAN ASSERT phase=" + S.phase + " tick=" + Tick + " PASS mode=" + mode);
            S.prepared = -1;
        }
        public static void Restart(int value)
        { Need(value == S.cycle + 1, "cycle mismatch"); S.cycle = value; S.phase = -1; S.start = 0; S.prepared = 0; }
        static void Fail(Exception e)
        {
            finished = true;
            if (S != null) foreach (var p in S.units) Log.Message("CARAVAN DIAG " + p.ThingID + " pos=" + p.Position + " job=" + p.CurJob + " duty=" + p.mindState.duty + " lord=" + p.GetLord()?.CurLordToil + " caravan=" + p.GetCaravan()?.ID + " light=" + ((Pawn_Immortal)p).Light.currentLight);
            Log.Error("CARAVAN FAILED " + e); Write(Peer + ".failed", e.ToString());
        }
        public static void Update()
        {
            if (!Enabled) return;
            if (screenshotFrame > 0 && Time.frameCount >= screenshotFrame)
            { screenshotFrame = 0; ScreenCapture.CaptureScreenshot(At("caravan-ui-" + S.phase + ".png")); }
            if (File.Exists(At("quit")) || (Client && File.Exists(At("quit-client-" + cycle)))) { Application.Quit(); return; }
            if (finished && !Client && File.Exists(At("resume-cycle-" + (cycle + 1))))
            { cycle++; finished = false; ready = false; freezeSent = false; sent = checkedPhase = -999; }
            if (finished || LongEventHandler.AnyEventNowOrWaiting) return;
            try
            {
                if (!Client && !hosted && Current.ProgramState == ProgramState.Playing)
                {
                    hosted = true; Setup();
                    HostWindow.HostProgrammatically(new ServerSettings { gameName = "Caravan", direct = true, directAddress = "127.0.0.1:" + Port,
                        lan = false, steam = false, multifaction = false, asyncTime = false, syncConfigs = false, pauseOnJoin = false,
                        pauseOnLetter = PauseOnLetter.Never, autoJoinPoint = 0, autosaveInterval = 0, desyncTraces = GenCommandLine.CommandLineArgPassed("mm-trace") }); return;
                }
                if (Current.ProgramState != ProgramState.Playing || !MP.IsInMultiplayer || Mp.Client.State != ConnectionStateEnum.ClientPlaying || TickPatch.Simulating) return;
                Need(!Mp.session.desynced, "desynced=True");
                if (!Client && !ready && !TickPatch.serverFrozen) { Mp.Client.Send(new ClientFreezePacket(true)); return; }
                if (Client && !ready) { if (!TickPatch.Frozen) return; ready = true; Write("client.ready", Snapshot()); }
                if (Mp.session.players.Count != 2 || !File.Exists(At("client.ready"))) return;
                if (!Client && !ready)
                {
                    Need(Snapshot() == File.ReadAllText(At("client.ready")), "BASELINE_LOAD_DRIFT"); ready = true; Write("host.ready", Snapshot());
                    Mp.Client.Send(new ClientFreezePacket(false)); Mp.Client.SendCommand(CommandType.GlobalTimeSpeed, ScheduledCommand.Global, (byte)TimeSpeed.Ultrafast);
                    if (cycle > S.cycle) restart.DoSync(null, cycle);
                }
                if (TickPatch.Frozen)
                {
                    if (freezeSent || File.Exists(At("host.final")))
                    { Write(Peer + ".final", Snapshot()); Write(Peer + ".complete", "PASS desynced=False ticks=" + (Tick - S.start)); finished = true; }
                    return;
                }
                if (Tick >= checkpoint) { checkpoint = Tick + 10000; Log.Message("CARAVAN PROGRESS tick=" + Tick + " phase=" + S.phase + " players=2 desynced=False"); }
                if (S.cycle < cycle) return;
                if (S.phase >= 17 && S.prepared == -1)
                { if (!Client && Tick - S.start >= (cycle == 0 ? Duration : 12000) && !freezeSent) { freezeSent = true; Mp.Client.Send(new ClientFreezePacket(true)); } return; }
                Need(S.phase < 0 || S.prepared < 0 || TickPatch.Timer - S.prepared < 45000, "action timeout phase=" + S.phase);
                if (!Client) return;
                if (S.phase < 0 || S.prepared == -1)
                { if (sent != S.phase * 10 + 9) { sent = S.phase * 10 + 9; prepare.DoSync(null); } return; }
                if (sent != S.phase * 10) { sent = S.phase * 10; Dispatch(); }
                if (Settled() && TickPatch.Timer - S.prepared >= 120 && checkedPhase != S.phase)
                { checkedPhase = S.phase; check.DoSync(null); }
            }
            catch (Exception e) { Fail(e); }
        }
    }
}
