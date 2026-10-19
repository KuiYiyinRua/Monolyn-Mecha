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

namespace RecallTests
{
    public sealed class State : GameComponent
    {
        public List<Pawn> units = new List<Pawn>();
        public Pawn owner;
        public int phase = -1, start, prepared, executed, cycle;
        public State(Game game) { }
        public override void GameComponentUpdate() { if (Driver.Enabled) Driver.Update(); }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref units, "recallUnits", LookMode.Reference);
            Scribe_References.Look(ref owner, "recallOwner");
            Scribe_Values.Look(ref phase, "recallPhase", -1);
            Scribe_Values.Look(ref start, "recallStart"); Scribe_Values.Look(ref prepared, "recallPrepared");
            Scribe_Values.Look(ref executed, "recallExecuted");
            Scribe_Values.Look(ref cycle, "recallCycle");
        }
        public override void GameComponentTick()
        {
            if (!Driver.Enabled || owner == null || Find.TickManager.TicksGame % 250 != 0) return;
            foreach (var n in owner.needs.AllNeeds)
                if (n is Need_Food || n is Need_Rest || n is Need_Mood) n.CurLevel = n.MaxLevel;
        }
    }
    [StaticConstructorOnStartup]
    public static class Driver
    {
        public static readonly bool Enabled = GenCommandLine.CommandLineArgPassed("mm-recall-test");
        static readonly bool Client = GenCommandLine.CommandLineArgPassed("mm-client");
        static readonly bool Baseline = GenCommandLine.CommandLineArgPassed("mm-baseline");
        static readonly bool Async = GenCommandLine.CommandLineArgPassed("mm-async");
        static int cycle = GenCommandLine.TryGetCommandLineArg("mm-cycle", out string cycleArg) ? int.Parse(cycleArg) : 0;
        static readonly string Root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        static readonly int Port = GenCommandLine.TryGetCommandLineArg("mm-port", out string port) ? int.Parse(port) : 31425;
        static readonly int Duration = GenCommandLine.TryGetCommandLineArg("mm-ticks", out string ticks) ? int.Parse(ticks) : 12000;
        static State S => Current.Game.GetComponent<State>();
        static Map A => Find.Maps[0];
        static Map B => Find.Maps.Count > 1 ? Find.Maps[1] : A;
        static int Tick => Mp.AsyncWorldTime.worldTicks;
        static string Peer => Client ? "client" : "host";
        static string At(string path) => Path.Combine(Root, path);
        static bool hosted, ready, finished, freezeSent;
        static bool negativeCheck;
        static readonly AccessTools.FieldRef<Ability, int> cooldownEnd = AccessTools.FieldRefAccess<Ability, int>("cooldownEndTick");
        static int Clock(Pawn p) => p.Map?.AsyncTime().mapTicks ?? Tick;
        static int sent = -999, checkedPhase = -999, checkpoint;
        static int screenshotFrame;
        static ISyncMethod prepare, check, restart;
        static Driver()
        {
            if (!Enabled) return;
            var h = new Harmony("local.mecha.recall.test");
            h.Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(Driver), nameof(Update)));
            if (!Baseline)
            {
                prepare = MP.RegisterSyncMethod(typeof(Driver), nameof(Prepare));
                check = MP.RegisterSyncMethod(typeof(Driver), nameof(Check));
                restart = MP.RegisterSyncMethod(typeof(Driver), nameof(Restart));
                h.Patch(AccessTools.Method(typeof(MechaRecall), nameof(MechaRecall.Execute)),
                    prefix: new HarmonyMethod(typeof(Driver), nameof(Before)), postfix: new HarmonyMethod(typeof(Driver), nameof(After)));
            }
            Log.Message("RECALL BOOT peer=" + Peer + " run=" + Path.GetFileName(Root) + " mvid=" + typeof(Pawn_Immortal).Assembly.ManifestModule.ModuleVersionId);
            foreach (var line in File.ReadAllLines(At("candidate.sha256"))) Log.Message("RECALL HASH " + line);
        }
        static void Need(bool b, string why) { if (!b) throw new Exception(why); }
        static void Write(string path, string value) { File.WriteAllText(At(path), value); Log.Message("RECALL " + path + " " + value); }
        static string N(float n) => n.ToString("R", CultureInfo.InvariantCulture);
        static float Energy(Pawn p) => p is Pawn_Immortal w ? w.Light.currentLight / w.Light.maxLightP : p.needs.energy.CurLevelPercentage;
        static void Fill(Pawn p, float value)
        { if (p is Pawn_Immortal w) w.Light.currentLight = w.Light.maxLightP * value; else p.needs.energy.CurLevelPercentage = value; }
        static string Snapshot() => "world=" + Tick + ":" + Mp.AsyncWorldTime.randState
            + " maps=" + string.Join(";", Find.Maps.OrderBy(m => m.uniqueID).Select(m => m.uniqueID + ":" + m.AsyncTime().mapTicks + ":" + m.AsyncTime().randState))
            + " cycle=" + S.cycle + " phase=" + S.phase + " executed=" + S.executed + " bandwidth=" + S.owner.mechanitor.UsedBandwidth
            + string.Join("", S.units.Select(p => "\n" + p.ThingID + ":" + p.Map?.uniqueID + ":" + p.Position + ":" + p.GetOverseer()?.ThingID + ":" + N(Energy(p))));
        static Pawn Make(string kind, IntVec3 cell)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDef.Named(kind), Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            GenSpawn.Spawn(p, cell, A); return p;
        }
        static void Clear(Map map)
        {
            map.fogGrid.ClearAllFog();
            foreach (var p in map.mapPawns.AllPawnsSpawned.ToArray())
            { p.GetLord()?.Notify_PawnLost(p, PawnLostCondition.ExitedMap); p.DeSpawn(); Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.KeepForever); }
            foreach (var c in map.AllCells)
            {
                foreach (var t in c.GetThingList(map).ToArray()) if (t.def.destroyable) t.Destroy();
                map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil); map.roofGrid.SetRoof(c, null);
            }
        }
        static void Setup()
        {
            Clear(A);
            if (!Baseline && !GenCommandLine.CommandLineArgPassed("mm-one-map"))
            {
                PlanetTile tile = TileFinder.RandomStartingTile();
                for (int i = 0; i < 100 && Find.WorldObjects.AllWorldObjects.Any(o => o.Tile == tile); i++) tile = TileFinder.RandomStartingTile();
                var parent = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                parent.Tile = tile; parent.SetFaction(Faction.OfPlayer); Find.WorldObjects.Add(parent);
                MapGenerator.GenerateMap(new IntVec3(80, 1, 80), parent, A.generatorDef);
                Clear(B); Current.Game.CurrentMap = A;
            }
            // Quicktest/map generation can leave relatives of discarded generated
            // pawns. The isolated fixture does not use those social relationships.
            foreach (var pawn in Find.WorldPawns.AllPawnsAliveOrDead.ToArray())
                pawn.relations?.ClearAllRelations();
            S.owner = Make("Colonist", new IntVec3(35, 0, 35));
            S.owner.health.AddHediff(HediffDefOf.MechlinkImplant, S.owner.health.hediffSet.GetBrain());
            S.owner.health.AddHediff(HediffDef.Named("MM_TestBandwidth")); S.owner.drafter.Drafted = true;
            string[] names = { "MM_Immortal", "MM_Colossus", "MM_Reaver", "MM_Disruptor", "MM_Stalker", "MM_Guardian" };
            for (int i = 0; i < names.Length; i++)
            {
                var p = Make(names[i], new IntVec3(40 + i * 6, 0, 50));
                p.relations.AddDirectRelation(PawnRelationDefOf.Overseer, S.owner);
                if (p is Pawn_Immortal w) w.autoFire = false;
                p.GetComp<CompGuardian>()?.SetAutoRestore(false);
                p.GetComp<CompStalker>()?.SetAutoBlink(false);
                var native = p.GetComp<ASEL.Comp_MNC>(); if (native != null) native.autoCharge = false;
                p.drafter.Drafted = true; Fill(p, 1); S.units.Add(p);
                var cargo = ThingMaker.MakeThing(ThingDefOf.Steel); cargo.stackCount = 17;
                p.inventory.innerContainer.TryAdd(cargo);
            }
            S.owner.mechanitor.Notify_BandwidthChanged();
            Need(S.owner.mechanitor.ControlledPawns.Count == 6, "fixture bandwidth");
            if (Baseline)
            {
                EnterAtEdge();
                var lost = S.units.Where(p => !p.Spawned).ToArray();
                Need(lost.Any(p => p is Pawn_Colossus) && lost.Any(p => p is Pawn_Reaver) && lost.Any(p => p is Pawn_Disruptor), "baseline did not reproduce edge loss");
                Write("baseline.complete", "REPRODUCED lost=" + string.Join(",", lost.Select(p => p.ThingID + ":world=" + p.IsWorldPawn() + ":owner=" + p.GetOverseer()?.ThingID + ":holder=" + p.ParentHolder)));
                finished = true; Application.Quit();
            }
        }
        static void ToWorld(Pawn p)
        {
            p.jobs?.StopAll(); p.pather?.StopDead();
            if (p.Spawned) p.DeSpawn();
            if (!p.IsWorldPawn())
            {
                // Register while held: passing a free mech to the world changes faction.
                var holder = CaravanMaker.MakeCaravan(new[] { p }, p.Faction, A.Tile, true);
                holder.RemovePawn(p); holder.Destroy();
            }
        }
        static void EnterAtEdge()
        {
            foreach (var p in S.units.Concat(new[] { S.owner })) ToWorld(p);
            var caravan = CaravanMaker.MakeCaravan(S.units.Concat(new[] { S.owner }), Faction.OfPlayer, B.Tile, true);
            Log.Message("RECALL ENTRY before=" + string.Join(",", caravan.PawnsListForReading.Select(p => p.ThingID)));
            CaravanEnterMapUtility.Enter(caravan, B, p => IntVec3.Zero);
            Log.Message("RECALL ENTRY after=" + string.Join(",", S.units.Select(p => p.ThingID + ":spawned=" + p.Spawned + ":map=" + p.Map?.uniqueID)));
        }
        public static void Prepare()
        {
            if (S.start == 0) S.start = Tick;
            S.phase++; S.executed = 0; S.prepared = Tick;
            int kind = S.phase % 6;
            if (kind == 5)
            {
                EnterAtEdge();
                foreach (var p in S.units)
                    Need(p.Spawned && p.Map == B && p.OccupiedRect().InBounds(B) && p.GetOverseer() == S.owner, "caravan loss " + p.ThingID);
                Need(S.owner.mechanitor.ControlledPawns.Count == 6, "entry control lost");
                S.executed = 6;
                return;
            }
            // Reset every fixture through native map/world transitions.
            if (S.owner.Map != A) { ToWorld(S.owner); GenSpawn.Spawn(S.owner, new IntVec3(35, 0, 35), A); }
            S.owner.Position = new IntVec3(35, 0, 35); S.owner.Notify_Teleported(); S.owner.drafter.Drafted = true;
            for (int i = 0; i < S.units.Count; i++)
            {
                var p = S.units[i];
                Log.Message("RECALL RESET before=" + p.ThingID + " spawned=" + p.Spawned + " faction=" + p.Faction?.loadID + " player=" + Faction.OfPlayer.loadID + " mech=" + p.IsColonyMech + " drafter=" + (p.drafter != null));
                ToWorld(p);
                if (kind <= 1 || kind == 4)
                    GenSpawn.Spawn(p, new IntVec3(40 + i * 5, 0, 50), kind == 1 ? B : A);
                Log.Message("RECALL RESET after=" + p.ThingID + " spawned=" + p.Spawned + " faction=" + p.Faction?.loadID + " player=" + Faction.OfPlayer.loadID + " mech=" + p.IsColonyMech + " drafter=" + (p.drafter != null));
                Fill(p, kind == 4 ? .29f : 1f);
                var blink = p.GetComp<CompStalker>()?.Blink;
                if (blink != null) { blink.StartCooldown(6000); cooldownEnd(blink) = Clock(p) + 6000; }
                if (p.Spawned) { Need(p.drafter != null, "spawned fixture missing drafter " + p.ThingID); p.drafter.Drafted = true; }
            }
            if (kind == 2) CaravanMaker.MakeCaravan(S.units, Faction.OfPlayer, B.Tile, true);
            Log.Message("RECALL PREPARED phase=" + S.phase + " tick=" + Tick);
        }
        public struct BeforeState { public bool active; public Pawn pawn; public float energy; public int kind, clock, cooldown; public Map map; public IntVec3 pos; }
        static void Before(int pawnId, ref BeforeState __state)
        {
            if (!Enabled || negativeCheck || !MP.IsInMultiplayer || MP.InInterface || S.phase < 0) return;
            var p = S.units.FirstOrDefault(x => x.thingIDNumber == pawnId);
            if (p != null) __state = new BeforeState { active = true, pawn = p, energy = Energy(p), kind = S.phase % 6, map = p.Map, pos = p.Position,
                clock = Clock(p), cooldown = p.GetComp<CompStalker>()?.Blink is Ability a ? cooldownEnd(a) : -1 };
        }
        static void After(BeforeState __state)
        {
            if (!__state.active) return;
            try
            {
                var p = __state.pawn;
                if (__state.kind == 4)
                    Need(p.Map == __state.map && p.Position == __state.pos && Energy(p) == __state.energy, "insufficient energy mutated state");
                else
                {
                    float cost = __state.map == S.owner.Map ? .3f : .5f;
                    Need(p.Spawned && p.Map == S.owner.Map && p.Position.InHorDistOf(S.owner.Position, 8.1f), "recall destination " + p.ThingID);
                    Need(Math.Abs(Energy(p) - (__state.energy - cost)) < .00001f, "wrong energy " + p.ThingID + " before=" + N(__state.energy) + " after=" + N(Energy(p)));
                    Need(p.GetOverseer() == S.owner && S.owner.mechanitor.ControlledPawns.Contains(p), "recall control " + p.ThingID);
                    Need(p.OccupiedRect().InBounds(p.Map) && !p.IsWorldPawn(), "recall registration");
                    Need(p.inventory.innerContainer.TotalStackCountOfDef(ThingDefOf.Steel) == 17, "cargo lost");
                    if (__state.cooldown != -1)
                        Need(cooldownEnd(p.GetComp<CompStalker>().Blink) == __state.cooldown + Clock(p) - __state.clock, "blink clock conversion");
                }
                S.executed++;
                Log.Message("RECALL EXECUTED phase=" + S.phase + " pawn=" + p.ThingID + " energy=" + N(Energy(p)) + " map=" + p.Map?.uniqueID);
            }
            catch (Exception e) { Fail(e); }
        }
        static void Dispatch()
        {
            int kind = S.phase % 6;
            Current.Game.CurrentMap = (Async && kind == 1) ? B : A;
            if (S.phase == 0 && cycle == 0)
            {
                Find.Selector.ClearSelection(); Find.Selector.Select(S.units[0]);
                CameraJumper.TryJump(S.units[0]); screenshotFrame = Time.frameCount + 15;
            }
            if (kind == 5) return;
            foreach (var p in S.units)
            {
                if (kind == 4)
                {
                    var command = p.GetGizmos().OfType<Command_Action>().Single(c => c.defaultLabel == "MM_Recall".Translate());
                    Need(command.Disabled, "low energy button enabled");
                    MechaRecall.Execute(p.thingIDNumber, S.owner.thingIDNumber); continue;
                }
                if (kind == 0 || kind == 1)
                {
                    var command = p.GetGizmos().OfType<Command_Action>().Single(c => c.defaultLabel == "MM_Recall".Translate());
                    Need(!command.Disabled, "recall button disabled: " + command.disabledReason); command.action();
                }
                else
                {
                    var command = S.owner.GetGizmos().OfType<Command_Action>().Single(c => c.defaultLabel == "MM_RecallMenu".Translate()); command.action();
                    var menu = Find.WindowStack.Windows.OfType<FloatMenu>().Last();
                    var options = (List<FloatMenuOption>)AccessTools.Field(typeof(FloatMenu), "options").GetValue(menu);
                    var option = options.Single(o => o.Label.StartsWith(p.LabelShortCap + " ("));
                    Need(option.action != null, "menu entry disabled " + option.Label); option.action(); menu.Close();
                }
            }
            Log.Message("RECALL DISPATCH phase=" + S.phase + " peer=" + Peer);
        }
        public static void Check()
        {
            Need(S.executed == 6, "command count " + S.executed);
            Need(S.units.All(p => p.GetOverseer() == S.owner), "relationship changed");
            if (Async && S.phase >= 1)
                Need(A.AsyncTime().mapTicks != B.AsyncTime().mapTicks, "async fixture clocks did not separate");
            if (S.phase % 6 == 4) NegativeChecks();
            Log.Message("RECALL ASSERT phase=" + S.phase + " tick=" + Tick + " PASS count=" + S.executed);
            S.prepared = -1;
        }
        static void NegativeChecks()
        {
            negativeCheck = true;
            try
            {
                var p = S.units[0]; var pos = p.Position; var map = p.Map;
                Fill(p, 1);
                MechaRecall.Execute(p.thingIDNumber, -1);
                Need(p.Position == pos && Energy(p) == 1, "stale owner command mutated");
                p.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, S.owner);
                Need(MechaRecall.Command(p).Disabled, "unbound recall enabled");
                MechaRecall.Execute(p.thingIDNumber, S.owner.thingIDNumber);
                Need(p.Position == pos && Energy(p) == 1, "unbound recall mutated");
                p.relations.AddDirectRelation(PawnRelationDefOf.Overseer, S.owner); S.owner.mechanitor.Notify_BandwidthChanged();
                var terrain = new Dictionary<IntVec3, TerrainDef>();
                foreach (var c in GenRadial.RadialCellsAround(S.owner.Position, 11, true))
                    if (c.InBounds(map)) { terrain[c] = c.GetTerrain(map); map.terrainGrid.SetTerrain(c, TerrainDefOf.WaterDeep); }
                try
                {
                    MechaRecall.Execute(p.thingIDNumber, S.owner.thingIDNumber);
                    Need(p.Position == pos && Energy(p) == 1, "blocked landing charged or moved");
                }
                finally { foreach (var pair in terrain) map.terrainGrid.SetTerrain(pair.Key, pair.Value); }
                ToWorld(p); GenSpawn.Spawn(p, new IntVec3(55, 0, 55), B);
                Fill(p, MechaRecall.Fraction(p, S.owner) - .01f);
                var before = Energy(p); pos = p.Position;
                MechaRecall.Execute(p.thingIDNumber, S.owner.thingIDNumber);
                Need(p.Map == B && p.Position == pos && Energy(p) == before, "cross-map insufficient energy mutated");
                if (B != A)
                {
                    var stalker = S.units[4]; ToWorld(stalker); GenSpawn.Spawn(stalker, new IntVec3(60, 0, 60), B);
                    Fill(stalker, 1); var blink = stalker.GetComp<CompStalker>().Blink;
                    blink.StartCooldown(6000); cooldownEnd(blink) = Clock(stalker) + 6000;
                    // Immediately after native world removal, the arrival marker is
                    // still this world tick. Direct recall must not apply it twice.
                    MechaRecall.Execute(stalker.thingIDNumber, S.owner.thingIDNumber);
                    Need(stalker.Map == A && cooldownEnd(blink) == Clock(stalker) + 6000, "same-tick arrival marker double conversion");
                }
            }
            finally { negativeCheck = false; }
        }
        public static void Restart(int value)
        {
            Need(value == S.cycle + 1, "cycle mismatch");
            S.cycle = value; S.phase = -1; S.start = 0; S.prepared = 0; S.executed = 0;
        }
        static void Fail(Exception e) { finished = true; Log.Error("RECALL FAILED " + e); Write(Peer + ".failed", e.ToString()); }
        public static void Update()
        {
            if (!Enabled) return;
            if (screenshotFrame > 0 && Time.frameCount >= screenshotFrame)
            { screenshotFrame = 0; ScreenCapture.CaptureScreenshot(At("recall-ui.png")); }
            if (File.Exists(At("quit")) || (Client && File.Exists(At("quit-client-" + cycle)))) { Application.Quit(); return; }
            if (finished && !Client && File.Exists(At("resume-cycle-" + (cycle + 1))))
            {
                cycle++; finished = false; ready = false; freezeSent = false; sent = checkedPhase = -999;
            }
            if (finished || LongEventHandler.AnyEventNowOrWaiting) return;
            try
            {
                if (!Client && !hosted && Current.ProgramState == ProgramState.Playing)
                {
                    hosted = true; Setup(); if (Baseline) return;
                    HostWindow.HostProgrammatically(new ServerSettings { gameName = "Recall", direct = true, directAddress = "127.0.0.1:" + Port,
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
                    if (Async)
                    {
                        Mp.Client.SendCommand(CommandType.MapTimeSpeed, A.uniqueID, (byte)TimeSpeed.Ultrafast);
                        Mp.Client.SendCommand(CommandType.MapTimeSpeed, B.uniqueID, (byte)TimeSpeed.Normal);
                    }
                    if (cycle > S.cycle) restart.DoSync(null, cycle);
                }
                if (TickPatch.Frozen)
                {
                    if (freezeSent || File.Exists(At("host.final")))
                    { Write(Peer + ".final", Snapshot()); Write(Peer + ".complete", "PASS desynced=False ticks=" + (Tick - S.start)); finished = true; }
                    return;
                }
                if (Tick >= checkpoint) { checkpoint = Tick + 10000; Log.Message("RECALL PROGRESS tick=" + Tick + " phase=" + S.phase + " players=2 maps=" + Find.Maps.Count + " desynced=False"); }
                if (S.cycle < cycle) return;
                if (S.phase >= 17 && S.prepared == -1)
                { if (!Client && Tick - S.start >= (cycle == 0 ? Duration : 12000) && !freezeSent) { freezeSent = true; Mp.Client.Send(new ClientFreezePacket(true)); } return; }
                Need(S.phase < 0 || S.prepared < 0 || Tick - S.prepared < 4000, "action timeout phase=" + S.phase + " count=" + S.executed);
                if (!Client) return;
                if (S.phase < 0 || S.prepared == -1)
                { if (sent != S.phase * 10 + 9) { sent = S.phase * 10 + 9; prepare.DoSync(null); } return; }
                if (sent != S.phase * 10) { sent = S.phase * 10; Dispatch(); }
                if (S.executed == 6 && Tick - S.prepared >= 60 && checkedPhase != S.phase)
                { checkedPhase = S.phase; check.DoSync(null); }
            }
            catch (Exception e) { Fail(e); }
        }
    }
}
