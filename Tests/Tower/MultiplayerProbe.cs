using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ASEL;
using HarmonyLib;
using Monolyn.Mecha;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Client.Persistent;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Mp = Multiplayer.Client.Multiplayer;

namespace TowerTests
{
    public sealed class State : GameComponent
    {
        public List<Pawn> units = new List<Pawn>(), pilots = new List<Pawn>();
        public Pawn owner;
        public Building_LightConduit origin, destination;
        public int phase = -1, start, prepared, cycle, started, attempts;
        public State(Game game) { }
        public override void GameComponentUpdate() { if (Driver.Enabled) Driver.Update(); }
        public override void GameComponentTick()
        {
            if (!Driver.Enabled || owner == null || Find.TickManager.TicksGame % 250 != 0) return;
            foreach (var p in pilots.Concat(new[] { owner }))
                foreach (var n in p.needs.AllNeeds)
                    if (n is Need_Food || n is Need_Rest || n is Need_Mood) n.CurLevel = n.MaxLevel;
        }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref units, "towerUnits", LookMode.Reference);
            Scribe_Collections.Look(ref pilots, "towerPilots", LookMode.Reference);
            Scribe_References.Look(ref owner, "towerOwner"); Scribe_References.Look(ref origin, "towerA"); Scribe_References.Look(ref destination, "towerB");
            Scribe_Values.Look(ref phase, "towerPhase", -1); Scribe_Values.Look(ref start, "towerStart");
            Scribe_Values.Look(ref prepared, "towerPrepared"); Scribe_Values.Look(ref cycle, "towerCycle"); Scribe_Values.Look(ref started, "towerStarted"); Scribe_Values.Look(ref attempts, "towerAttempts");
        }
    }
    [StaticConstructorOnStartup]
    public static class Driver
    {
        public static readonly bool Enabled = GenCommandLine.CommandLineArgPassed("mm-tower-test");
        static readonly bool Client = GenCommandLine.CommandLineArgPassed("mm-client"), Baseline = GenCommandLine.CommandLineArgPassed("mm-baseline"), Async = GenCommandLine.CommandLineArgPassed("mm-async");
        static readonly string Root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        static readonly int Port = GenCommandLine.TryGetCommandLineArg("mm-port", out string port) ? int.Parse(port) : 31429;
        static readonly int Duration = GenCommandLine.TryGetCommandLineArg("mm-ticks", out string ticks) ? int.Parse(ticks) : 12000;
        static int cycle = GenCommandLine.TryGetCommandLineArg("mm-cycle", out string c) ? int.Parse(c) : 0;
        static State S => Current.Game.GetComponent<State>();
        static Map A => S.origin?.Map ?? Find.Maps[0];
        static Map B => S.destination?.Map ?? Find.Maps[1];
        static int Tick => Mp.AsyncWorldTime.worldTicks;
        static string Peer => Client ? "client" : "host";
        static string At(string name) => Path.Combine(Root, name);
        static Building_LightConduit Portal => S.phase == 4 || S.phase == 5 || S.phase == 9 || S.phase == 10 || S.phase == 11 ? S.destination : S.origin;
        static MapPortalSession Session
        {
            get
            {
                foreach (var session in Portal.Map.MpComp().sessionManager.AllSessions)
                    if (session is MapPortalSession portalSession && portalSession.portal == Portal) return portalSession;
                return null;
            }
        }
        static bool hosted, ready, finished, freezeSent;
        static int sent = -999, checkedPhase = -999, checkpoint, screenshotFrame;
        static ISyncMethod prepare, check, restart;
        static Driver()
        {
            if (!Enabled) return;
            var harmony = new Harmony("local.mecha.tower.test");
            harmony.Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(Driver), nameof(Update)));
            if (!Baseline) harmony.Patch(AccessTools.Method("Monolyn.Mecha.MechaTowerTransit:Transfer"), prefix: new HarmonyMethod(typeof(Driver), nameof(BeforeTransfer)), postfix: new HarmonyMethod(typeof(Driver), nameof(AfterTransfer)));
            if (!Baseline) harmony.Patch(AccessTools.Method(typeof(MapPortal), nameof(MapPortal.CancelLoad)), prefix: new HarmonyMethod(typeof(Driver), nameof(CancelTrace)));
            if (!Baseline) harmony.Patch(AccessTools.Method(typeof(EnterPortalUtility), nameof(EnterPortalUtility.MakeLordsAsAppropriate)), postfix: new HarmonyMethod(typeof(Driver), nameof(LordTrace)) { priority = Priority.Last });
            prepare = MP.RegisterSyncMethod(typeof(Driver), nameof(Prepare)); check = MP.RegisterSyncMethod(typeof(Driver), nameof(Check)); restart = MP.RegisterSyncMethod(typeof(Driver), nameof(Restart));
            Log.Message("TOWER BOOT peer=" + Peer + " run=" + Path.GetFileName(Root) + " mvid=" + typeof(Pawn_Immortal).Assembly.ManifestModule.ModuleVersionId);
            foreach (var line in File.ReadAllLines(At("candidate.sha256"))) Log.Message("TOWER HASH " + line);
        }
        static void Need(bool ok, string why) { if (!ok) throw new Exception(why); }
        static void Write(string path, string value) { File.WriteAllText(At(path), value); Log.Message("TOWER " + path + " " + value); }
        static string N(float n) => n.ToString("R", CultureInfo.InvariantCulture);
        static string Snapshot() => "world=" + Tick + ":" + Mp.AsyncWorldTime.randState
            + " maps=" + string.Join(";", Find.Maps.OrderBy(m => m.uniqueID).Select(m => m.uniqueID + ":" + m.AsyncTime().mapTicks + ":" + m.AsyncTime().randState))
            + " cycle=" + S.cycle + " phase=" + S.phase + " bandwidth=" + S.owner.mechanitor.UsedBandwidth
            + string.Join("", S.units.Select(p => "\n" + p.ThingID + ":" + p.Map?.uniqueID + ":" + p.Position + ":" + p.GetOverseer()?.ThingID
                + ":" + N(Energy(p)) + ":" + (p as Pawn_Immortal)?.Pilot?.ThingID + ":" + Cargo(p)));
        static int Cargo(Pawn p) => p.inventory.innerContainer.Where(t => t.def == ThingDefOf.Steel).Sum(t => t.stackCount);
        static float Energy(Pawn p) => p is Pawn_Immortal w ? w.Light.currentLight / w.Light.maxLightP : p.needs.energy.CurLevelPercentage;
        sealed class TransferState { internal Map map; internal int pawnClock, pilotClock; internal Pawn pilot; internal bool combat, restore; }
        static int RelativeClock(Pawn p, Map map) => (int)AccessTools.Field(typeof(Pawn_MindState), "canLovinTick").GetValue(p.mindState) - map.AsyncTime().mapTicks;
        static void BeforeTransfer(Pawn pawn, out TransferState __state)
        {
            S.attempts++;
            Diagnose();
            var pilot = (pawn as Pawn_Immortal)?.Pilot;
            __state = new TransferState { map = pawn.Map, pawnClock = RelativeClock(pawn, pawn.Map), pilot = pilot, pilotClock = pilot == null ? 0 : RelativeClock(pilot, pawn.Map) };
            // Seed a stale source-map deadline: the native MP timestamp fixer
            // cannot translate these custom fields. Exercise ON and OFF modes.
            __state.combat = __state.restore = S.phase < 6;
            if (pawn is Pawn_Immortal w) { w.Combat.SetEnabled(__state.combat); SeedScan(w.Combat, "nextScan", pawn); }
            var stalker = pawn.GetComp<CompStalker>();
            if (stalker != null) { stalker.SetAutoCombat(__state.combat); SeedScan(stalker, "nextScan", pawn); }
            var guardian = pawn.GetComp<CompGuardian>();
            if (guardian != null)
            { guardian.SetAutoCombat(__state.combat); guardian.SetAutoRestore(__state.restore); SeedScan(guardian, "nextScan", pawn); SeedScan(guardian, "nextSupportScan", pawn); }
        }
        static void SeedScan(object controller, string field, Pawn pawn)
            => AccessTools.Field(controller.GetType(), field).SetValue(controller, pawn.Map.AsyncTime().mapTicks + 200000);
        static void CheckScan(object controller, string field)
            => Need((int)AccessTools.Field(controller.GetType(), field).GetValue(controller) == 0, "stale source-map AI timer " + field);
        static void AfterTransfer(Pawn pawn, TransferState __state)
        {
            if (pawn.Map == __state.map) { Log.Message("TOWER BLOCKED " + pawn.ThingID); return; }
            Need(MechaRecall.Clear(pawn, pawn.Map, pawn.Position, pawn.Rotation), "unsafe immediate landing " + Footprint(pawn));
            Need(RelativeClock(pawn, pawn.Map) == __state.pawnClock, "chassis timestamp drift");
            if (__state.pilot != null) Need(RelativeClock(__state.pilot, pawn.Map) == __state.pilotClock, "pilot timestamp drift");
            if (pawn is Pawn_Immortal w) { CheckScan(w.Combat, "nextScan"); Need(w.Combat.Enabled == __state.combat, "warframe combat toggle changed"); }
            var stalker = pawn.GetComp<CompStalker>();
            if (stalker != null) { CheckScan(stalker, "nextScan"); Need(stalker.autoCombat == __state.combat, "stalker combat toggle changed"); }
            var guardian = pawn.GetComp<CompGuardian>();
            if (guardian != null)
            { CheckScan(guardian, "nextScan"); CheckScan(guardian, "nextSupportScan"); Need(guardian.autoCombat == __state.combat && guardian.autoRestore == __state.restore, "guardian toggles changed"); }
            Log.Message("TOWER TRANSFER " + pawn.ThingID + " from=" + __state.map.uniqueID + " to=" + pawn.Map.uniqueID + " pilot=" + __state.pilot?.ThingID + " cargo=" + Cargo(pawn) + " clockOffset=" + (pawn.Map.AsyncTime().mapTicks - __state.map.AsyncTime().mapTicks));
            Diagnose();
        }
        static void CancelTrace(MapPortal __instance)
        { if (__instance is Building_LightConduit) Log.Message("TOWER CANCEL " + __instance.ThingID + " traffic=" + Current.Game.GetComponent<ASELComponent>().conduitTraffic + "\n" + Environment.StackTrace); }
        static void LordTrace(List<Pawn> pawns, MapPortal portal)
        { if (portal is Building_LightConduit) { Log.Message("TOWER LORD pawns=" + string.Join(",", pawns.Select(p => p.ThingID))); Diagnose(); } }
        static string Footprint(Pawn p) => p.ThingID + " at=" + p.Position + " rot=" + p.Rotation + " cells="
            + string.Join(";", p.OccupiedRect().Select(c => c + ":" + (c.InBounds(p.Map) ? string.Join(",", c.GetThingList(p.Map).Select(t => t.ThingID)) : "OUTSIDE")));
        static void Fill(Pawn p, float value) { if (p is Pawn_Immortal w) w.Light.currentLight = w.Light.maxLightP * value; else p.needs.energy.CurLevelPercentage = value; }
        static Pawn Make(string kind, IntVec3 cell)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDef.Named(kind), Faction.OfPlayer, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            GenSpawn.Spawn(p, cell, A); return p;
        }
        static void Clear(Map map)
        {
            map.fogGrid.ClearAllFog();
            foreach (var p in map.mapPawns.AllPawnsSpawned.ToArray()) { p.GetLord()?.Notify_PawnLost(p, PawnLostCondition.ExitedMap); p.DeSpawn(); Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.KeepForever); }
            foreach (var cell in map.AllCells)
            {
                foreach (var t in cell.GetThingList(map).ToArray()) if (t.def.destroyable) t.Destroy();
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil); map.roofGrid.SetRoof(cell, null);
            }
        }
        static Building_LightConduit Tower(Map map, IntVec3 cell)
        {
            var tower = (Building_LightConduit)ThingMaker.MakeThing(ThingDef.Named("TowerOfLight")); tower.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(tower, cell, map); return tower;
        }
        static void Setup()
        {
            Clear(A);
            PlanetTile tile = TileFinder.RandomStartingTile();
            for (int i = 0; i < 100 && Find.WorldObjects.AllWorldObjects.Any(o => o.Tile == tile); i++) tile = TileFinder.RandomStartingTile();
            var parent = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement); parent.Tile = tile; parent.SetFaction(Faction.OfPlayer); Find.WorldObjects.Add(parent);
            MapGenerator.GenerateMap(new IntVec3(80, 1, 80), parent, A.generatorDef); Clear(B); Current.Game.CurrentMap = A;
            foreach (var p in Find.WorldPawns.AllPawnsAliveOrDead.ToArray()) p.relations?.ClearAllRelations();
            S.origin = Tower(A, new IntVec3(35, 0, 30)); S.destination = Tower(B, new IntVec3(5, 0, 4));
            S.origin.GetComp<CompLightConduit>().connectedConduit = S.destination.GetComp<CompLightConduit>();
            S.destination.GetComp<CompLightConduit>().connectedConduit = S.origin.GetComp<CompLightConduit>();
            // Native ASEL charges conduit traffic for transferred mass, including
            // the chassis. A default 1200-capacity network cannot send this fleet.
            Current.Game.GetComponent<ASELComponent>().conduitTrafficMax = 100000;
            S.owner = Make("Colonist", new IntVec3(30, 0, 40)); S.owner.health.AddHediff(HediffDefOf.MechlinkImplant, S.owner.health.hediffSet.GetBrain());
            S.owner.health.AddHediff(HediffDef.Named("MM_TestBandwidth")); S.owner.drafter.Drafted = true;
            string[] names = { "MM_Immortal", "MM_Colossus", "MM_Reaver", "MM_Disruptor", "MM_Stalker", "MM_Guardian" };
            for (int i = 0; i < names.Length; i++)
            {
                var p = Make(names[i], new IntVec3(30 + i * 6, 0, 50)); S.units.Add(p); Fill(p, 1);
                if (p is Pawn_Immortal w) w.autoFire = false;
                p.GetComp<CompGuardian>()?.SetAutoRestore(false); p.GetComp<CompStalker>()?.SetAutoBlink(false);
                var native = p.GetComp<Comp_MNC>(); if (native != null) native.autoCharge = false;
                var cargo = ThingMaker.MakeThing(ThingDefOf.Steel); cargo.stackCount = 17; p.inventory.innerContainer.TryAdd(cargo);
                if (i < 4) { var pilot = Make("Colonist", new IntVec3(30 + i * 6, 0, 46)); pilot.drafter.Drafted = true; S.pilots.Add(pilot); }
            }
            if (Baseline)
            {
                Configure(true);
                EnterPortalUtility.MakeLordsAsAppropriate(S.units, S.origin);
                int excluded = S.units.Take(4).Count(p => !(p.GetLord()?.LordJob is LordJob_LoadAndEnterPortal));
                Need(excluded == 4, "baseline did not reproduce missing pilot portal lord");
                Write("baseline.complete", "REPRODUCED: all four piloted warframes excluded from native LoadAndEnterPortal lord, even after caravan list repair.");
                finished = true; Application.Quit();
            }
        }
        static void Configure(bool piloted)
        {
            foreach (var p in S.units)
            {
                p.GetLord()?.Notify_PawnLost(p, PawnLostCondition.ForcedToJoinOtherLord); p.jobs.StopAll(); p.pather.StopDead();
                if (p.Map != A) { var old = p.Map; p.DeSpawn(); MechaRecall.SpawnTransferred(p, old, A, new IntVec3(30 + S.units.IndexOf(p) * 6, 0, 50)); }
                if (p is Pawn_Immortal w)
                {
                    if (w.Pilot != null) w.Eject(); MechaAutonomy.Unbind(A, w);
                    if (piloted) { var pilot = S.pilots[S.units.IndexOf(p)]; pilot.jobs.StopAll(); pilot.Position = w.Position + IntVec3.South; w.Board(pilot); Need(w.Pilot == pilot, "fixture board"); }
                }
                if (!(p is Pawn_Immortal) || !piloted)
                { if (p.GetOverseer() == null) p.relations.AddDirectRelation(PawnRelationDefOf.Overseer, S.owner); }
                Fill(p, 1); if (p.drafter != null) p.drafter.Drafted = false;
            }
            S.owner.mechanitor.Notify_BandwidthChanged();
        }
        static AcceptanceReport Eligibility(Pawn p) => (AcceptanceReport)AccessTools.Method("Monolyn.Mecha.MechaTowerTransit:CanEnter").Invoke(null, new object[] { p, S.origin });
        static List<Pawn> Rows(Dialog_EnterPortal dialog)
        {
            var widget = AccessTools.Field(typeof(Dialog_EnterPortal), "pawnsTransfer").GetValue(dialog);
            var rows = new List<Pawn>();
            foreach (var section in (IEnumerable)AccessTools.Field(typeof(TransferableOneWayWidget), "sections").GetValue(widget))
                foreach (var tr in (IEnumerable<TransferableOneWay>)AccessTools.Field(section.GetType(), "transferables").GetValue(section))
                    if (tr.AnyThing is Pawn p) rows.Add(p);
            return rows;
        }
        static void NewSession()
        {
            var session = Portal.Map.MpComp().sessionManager.GetOrAddSession(MapPortalSession.Of(Portal));
            foreach (var tr in session.transferables) tr.AdjustTo(S.units.Contains(tr.AnyThing as Pawn) ? 1 : 0);
            foreach (var pawn in S.units)
                Need(session.transferables.Single(t => t.things.Contains(pawn)).CountToTransfer == 1, "missing eligible candidate");
        }
        static void BlockExit(bool blocked)
        {
            foreach (var cell in GenRadial.RadialCellsAround(S.destination.Position, 13, true))
                if (cell.InBounds(B)) B.terrainGrid.SetTerrain(cell, blocked ? TerrainDefOf.WaterDeep : TerrainDefOf.Soil);
        }
        public static void Prepare()
        {
            S.phase++; S.prepared = TickPatch.Timer; S.started = Tick; if (S.start == 0) S.start = Tick;
            if (S.phase == 0) Current.Game.GetComponent<ASELComponent>().conduitTraffic = 0;
            if (S.phase == 0)
            {
                Configure(false);
                foreach (var p in S.units) p.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, S.owner);
                S.owner.mechanitor.Notify_BandwidthChanged();
            }
            if (S.phase == 1) Configure(true);
            if (S.phase == 6 || S.phase == 15) Configure(false);
            if (new[] { 1, 4, 6, 9, 12, 15 }.Contains(S.phase)) NewSession();
            if (S.phase == 13)
            {
                foreach (var p in S.units) p.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, S.owner);
                S.owner.mechanitor.Notify_BandwidthChanged();
            }
            if (S.phase == 14)
            {
                Session?.Remove(); Configure(false); S.attempts = 0;
                foreach (var p in S.units) { Fill(p, 0); Need(!Eligibility(p).Accepted, "empty battery accepted"); Fill(p, 1); }
                var link = S.origin.GetComp<CompLightConduit>(); var old = link.connectedConduit; link.connectedConduit = null;
                Need(S.units.All(p => !Eligibility(p).Accepted), "disconnected accepted"); link.connectedConduit = old;
                var component = Current.Game.GetComponent<ASELComponent>(); component.conduitTraffic = component.conduitTrafficMax + 1;
                Need(S.units.All(p => !Eligibility(p).Accepted), "overloaded accepted"); component.conduitTraffic = 0;
                BlockExit(true);
            }
            if (S.phase == 15) BlockExit(false);
        }
        static void Open()
        {
            Current.Game.CurrentMap = Portal.Map;
            foreach (var window in Find.WindowStack.Windows.OfType<Dialog_EnterPortal>().ToArray()) window.Close(false);
            var command = Portal.GetGizmos().OfType<Command_Action>().Single(g => g.defaultLabel == Portal.EnterString + "...");
            Need(!command.Disabled, "native enter button disabled"); command.action();
            Dialog_EnterPortal dialog = Find.WindowStack.Windows.OfType<Dialog_EnterPortal>().Last();
            Need(S.units.All(p => Rows(dialog).Count(x => x == p) == 1), "missing or duplicate visible row");
            dialog.Close(false); command.action();
            screenshotFrame = Time.frameCount + 10;
        }
        static void Dispatch()
        {
            if (new[] { 1, 4, 6, 9, 12, 15 }.Contains(S.phase)) Open();
            if (new[] { 2, 5, 7, 10, 13, 16 }.Contains(S.phase))
            {
                Dialog_EnterPortal dialog = Find.WindowStack.Windows.OfType<Dialog_EnterPortal>().Last();
                AccessTools.Method(typeof(Dialog_EnterPortal), "TryAccept").Invoke(dialog, null); dialog.Close(false);
            }
            if (S.phase == 14) S.units[1].jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.EnterPortal, S.origin), JobTag.Misc);
            Log.Message("TOWER DISPATCH phase=" + S.phase + " peer=" + Peer);
        }
        static bool Settled()
        {
            if (S.phase == 2 || S.phase == 7 || S.phase == 16) return !S.units.Any(p => p.GetLord() == null && p.Map == A);
            if (S.phase == 3 || S.phase == 8 || S.phase == 17) return S.units.All(p => p.Spawned && p.Map == B);
            if (S.phase == 5 || S.phase == 11) return S.units.All(p => p.Spawned && p.Map == A);
            if (S.phase == 10) return Session == null;
            if (S.phase == 14) return Tick - S.started >= 2400;
            return true;
        }
        public static void Check()
        {
            Need(Settled(), "stage not settled");
            if (S.phase == 0) Need(S.units.All(p => !Eligibility(p).Accepted), "uncontrolled accepted");
            if (S.phase == 13) Need(Session != null && S.units.All(p => p.Spawned && p.Map == A && p.GetLord() == null) && !S.origin.LoadInProgress, "stale selection executed");
            if (S.phase == 14) Need(S.attempts > 0 && S.units.All(p => p.Spawned && p.Map == A), "blocked exit was not attempted or despawned a unit");
            if (new[] { 3, 5, 8, 11, 17 }.Contains(S.phase))
            {
                foreach (var p in S.units)
                {
                    // Other ordinary pawns may walk through this cell after the
                    // transfer; collision-free arrival is checked at the boundary.
                    Need(p.OccupiedRect().InBounds(p.Map) && p.OccupiedRect().All(c => c.Standable(p.Map) && c.GetEdifice(p.Map) == null), "unsafe footprint " + Footprint(p));
                    Need(!p.IsWorldPawn() && p.GetLord() == null && Cargo(p) == 17, "lost identity/cargo/lord " + p);
                    bool piloted = S.phase <= 5 && p is Pawn_Immortal;
                    Need(piloted ? ((Pawn_Immortal)p).Pilot == S.pilots[S.units.IndexOf(p)] && p.GetOverseer() == null : p.GetOverseer() == S.owner, "controller changed " + p);
                    if (piloted) Need(((Pawn_Immortal)p).Pilot.ParentHolder is PilotHolder && !((Pawn_Immortal)p).Pilot.Spawned, "pilot holder lost");
                }
                Need(S.owner.Map == A, "owner moved unexpectedly");
                Need(!S.origin.LoadInProgress && !S.destination.LoadInProgress, "load list retained arrived units");
            }
            Log.Message("TOWER ASSERT phase=" + S.phase + " tick=" + Tick + " PASS"); S.prepared = -1;
        }
        public static void Restart(int value) { Need(value == S.cycle + 1, "cycle mismatch"); S.cycle = value; S.phase = -1; S.start = 0; S.prepared = 0; }
        static void Diagnose()
        {
            if (S == null) return;
            foreach (var p in S.units) Log.Message("TOWER DIAG " + p.ThingID + " map=" + p.Map?.uniqueID + " pos=" + p.Position + " job=" + p.CurJob + " duty=" + p.mindState.duty + " lord=" + p.GetLord()?.CurLordToil + " spawned=" + p.Spawned + " holder=" + p.ParentHolder + " driver=" + (p as Pawn_Immortal)?.Driven + " power=" + Energy(p));
            Log.Message("TOWER LOAD " + string.Join(";", (S.origin.leftToLoad ?? new List<TransferableOneWay>()).Select(t => t.AnyThing?.ThingID + ":" + t.CountToTransfer)));
        }
        static void Fail(Exception e)
        {
            finished = true;
            Diagnose();
            Log.Error("TOWER FAILED " + e); Write(Peer + ".failed", e.ToString());
        }
        public static void Update()
        {
            if (!Enabled) return;
            if (screenshotFrame > 0 && Time.frameCount >= screenshotFrame)
            { screenshotFrame = 0; ScreenCapture.CaptureScreenshot(At("tower-ui-" + S.phase + ".png")); }
            if (File.Exists(At("quit")) || (Client && File.Exists(At("quit-client-" + cycle)))) { Application.Quit(); return; }
            if (finished && !Client && File.Exists(At("resume-cycle-" + (cycle + 1))))
            { cycle++; finished = false; ready = false; freezeSent = false; sent = checkedPhase = -999; }
            if (finished || LongEventHandler.AnyEventNowOrWaiting) return;
            try
            {
                if (!Client && !hosted && Current.ProgramState == ProgramState.Playing)
                {
                    hosted = true; Setup(); if (Baseline) return;
                    HostWindow.HostProgrammatically(new ServerSettings { gameName = "Tower", direct = true, directAddress = "127.0.0.1:" + Port,
                        lan = false, steam = false, multifaction = false, asyncTime = Async, syncConfigs = false, pauseOnJoin = false,
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
                    if (Async) { Mp.Client.SendCommand(CommandType.MapTimeSpeed, A.uniqueID, (byte)TimeSpeed.Ultrafast); Mp.Client.SendCommand(CommandType.MapTimeSpeed, B.uniqueID, (byte)TimeSpeed.Superfast); } if (cycle > S.cycle) restart.DoSync(null, cycle);
                }
                if (TickPatch.Frozen)
                {
                    if (freezeSent || File.Exists(At("host.final")))
                    { Write(Peer + ".final", Snapshot()); Write(Peer + ".complete", "PASS desynced=False ticks=" + (Tick - S.start)); finished = true; }
                    return;
                }
                if (Tick >= checkpoint) { checkpoint = Tick + 10000; Log.Message("TOWER PROGRESS tick=" + Tick + " phase=" + S.phase + " players=2 desynced=False"); Diagnose(); }
                if (S.cycle < cycle) return;
                if (S.phase >= 17 && S.prepared == -1)
                { if (!Client && Tick - S.start >= (cycle == 0 ? Duration : 12000) && !freezeSent) { freezeSent = true; Mp.Client.Send(new ClientFreezePacket(true)); } return; }
                Need(S.phase < 0 || S.prepared < 0 || (TickPatch.Timer - S.prepared < 12000 && Tick - S.started < 16000), "action timeout phase=" + S.phase);
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
