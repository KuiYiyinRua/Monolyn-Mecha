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
using Verse.AI;
using Verse.AI.Group;
using Mp = Multiplayer.Client.Multiplayer;

namespace SelfDestructMpTests
{
    public sealed class State : GameComponent
    {
        public List<Pawn_Immortal> units = new List<Pawn_Immortal>();
        public List<Pawn> pilots = new List<Pawn>();
        public List<Pawn> survivors = new List<Pawn>();
        public List<string> pilotInventory = new List<string>();
        public List<Thing> targets = new List<Thing>();
        public List<Pawn_Immortal> emptyUnits = new List<Pawn_Immortal>();
        public Pawn owner;
        public int stage = -1, round, stageTick, startTick, projectiles;
        public State(Game game) { }
        public override void GameComponentUpdate() { if (Driver.Enabled) Driver.Update(); }
        public override void GameComponentOnGUI() { if (Driver.Enabled) Driver.Draw(); }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref units, "mmTestUnits", LookMode.Reference);
            Scribe_Collections.Look(ref pilots, "mmTestPilots", LookMode.Reference);
            Scribe_Collections.Look(ref survivors, "mmTestSurvivors", LookMode.Reference);
            Scribe_Collections.Look(ref pilotInventory, "mmTestPilotInventory", LookMode.Value);
            Scribe_Collections.Look(ref targets, "mmTestTargets", LookMode.Reference);
            Scribe_Collections.Look(ref emptyUnits, "mmTestEmpty", LookMode.Reference);
            Scribe_References.Look(ref owner, "mmTestOwner");
            Scribe_Values.Look(ref stage, "mmTestStage", -1); Scribe_Values.Look(ref round, "mmTestRound");
            Scribe_Values.Look(ref stageTick, "mmTestStageTick"); Scribe_Values.Look(ref startTick, "mmTestStartTick");
            Scribe_Values.Look(ref projectiles, "mmTestProjectiles");
        }
        public override void GameComponentTick()
        {
            if (!Driver.Enabled || stage < 0 || Find.TickManager.TicksGame % 300 != 0) return;
            foreach (var pawn in survivors.Concat(new[] { owner }))
                if (pawn != null && !pawn.Dead)
                    foreach (var need in pawn.needs.AllNeeds)
                        if (need is Need_Food || need is Need_Rest || need is Need_Mood) need.CurLevel = need.MaxLevel;
        }
    }
    [StaticConstructorOnStartup]
    public static class Driver
    {
        public static readonly bool Enabled = GenCommandLine.CommandLineArgPassed("mm-sd-mp");
        static readonly bool Client = GenCommandLine.CommandLineArgPassed("mm-client");
        static readonly string Root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        static readonly int Port = GenCommandLine.TryGetCommandLineArg("mm-port", out string port) ? int.Parse(port) : 31419;
        static readonly int Duration = GenCommandLine.TryGetCommandLineArg("mm-ticks", out string ticks) ? int.Parse(ticks) : 12000;
        static bool hosted, ready, finished, failed, freezeSent, seedRequested;
        static bool warningCaptured;
        static int requested = -999, dispatched = -999, checkpoint;
        static ISyncMethod prepare, advance;
        static State S => Current.Game.GetComponent<State>();
        static Map Map => Find.Maps[0];
        static int Tick => Mp.AsyncWorldTime.worldTicks;
        static string Peer => Client ? "client" : "host";
        static string At(string name) => Path.Combine(Root, name);
        static Driver()
        {
            if (!Enabled) return;
            prepare = MP.RegisterSyncMethod(typeof(Driver), nameof(PrepareRound)).SetContext(SyncContext.CurrentMap);
            advance = MP.RegisterSyncMethod(typeof(Driver), nameof(Check)).SetContext(SyncContext.CurrentMap);
            new Harmony("local.mecha.selfdestruct.mp").Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(Driver), nameof(Update)));
            new Harmony("local.mecha.selfdestruct.warning").Patch(AccessTools.Method(typeof(Log), nameof(Log.Warning), new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Driver), nameof(Warning)));
            Log.Message("SD_MP BOOT peer=" + Peer + " run=" + Path.GetFileName(Root) + " core=" + typeof(Pawn_Immortal).Assembly.GetName().Version);
            foreach (string line in File.ReadAllLines(At("candidate.sha256"))) Log.Message("SD_MP HASH " + line);
            if (File.Exists(At("seed.sha256"))) Log.Message("SD_MP SEED " + File.ReadAllText(At("seed.sha256")).Trim());
        }
        static void Need(bool ok, string message) { if (!ok) throw new Exception(message); }
        static void Warning(string text)
        {
            if (warningCaptured || !text.StartsWith("Deep-saving destroyed thing")) return;
            warningCaptured = true; Log.Message("SD_MP WARNING_ORIGIN " + new System.Diagnostics.StackTrace());
        }
        static void Write(string name, string message) { File.WriteAllText(At(name), message); Log.Message("SD_MP " + name + " " + message); }
        static string N(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        static string Inventory(Pawn p)
            => string.Join(",", p.inventory.innerContainer.OrderBy(t => t.thingIDNumber).Select(t => t.ThingID + ":" + t.stackCount))
                + "|equipment=" + string.Join(",", p.equipment.AllEquipmentListForReading.OrderBy(t => t.thingIDNumber).Select(t => t.ThingID))
                + "|apparel=" + string.Join(",", p.apparel.WornApparel.OrderBy(t => t.thingIDNumber).Select(t => t.ThingID));
        static string Snapshot()
        {
            string result = "world=" + Tick + ":" + Mp.AsyncWorldTime.randState + " maps="
                + string.Join(";", Find.Maps.OrderBy(m => m.uniqueID).Select(m => m.uniqueID + ":" + m.AsyncTime().mapTicks + ":" + m.AsyncTime().randState))
                + " stage=" + S.round + ":" + S.stage + " bandwidth=" + S.owner.mechanitor.UsedBandwidth;
            foreach (var p in S.units.Concat(S.emptyUnits)) result += "\n" + p.ThingID + ":" + p.Dead + ":" + p.Destroyed + ":" + (p.Corpse == null);
            foreach (var p in S.survivors) result += "\n" + p.ThingID + ":" + p.Position + ":" + p.Spawned + ":" + p.Dead + ":" + p.Drafted + ":" + p.inventory.innerContainer.TotalStackCount;
            foreach (var t in S.targets) result += "\n" + t.ThingID + ":" + t.HitPoints;
            return result;
        }
        static Pawn Make(string kind, Faction faction, IntVec3 cell)
        {
            var pawn = PawnGenerator.GeneratePawn(PawnKindDef.Named(kind), faction);
            GenSpawn.Spawn(pawn, cell, Map); return pawn;
        }
        static void Setup()
        {
            Map.fogGrid.ClearAllFog();
            foreach (var p in Map.mapPawns.AllPawnsSpawned.ToArray())
            { p.GetLord()?.Notify_PawnLost(p, PawnLostCondition.ExitedMap); p.DeSpawn(); Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.KeepForever); }
            int relocated = 0;
            foreach (var cell in new CellRect(20, 20, 150, 120))
            {
                foreach (var thing in cell.GetThingList(Map).ToArray())
                {
                    if (thing.def.destroyable) thing.Destroy();
                    else
                    {
                        // Unique non-destroyable scenario objects (notably the Anomaly
                        // monolith) have saved global references: keep them on the map.
                        thing.DeSpawn(); GenSpawn.Spawn(thing, new IntVec3(180 + relocated++, 0, 180), Map);
                    }
                }
                Map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil); Map.roofGrid.SetRoof(cell, null);
            }
            S.owner = Make("Colonist", Faction.OfPlayer, new IntVec3(150, 0, 125));
            S.owner.health.AddHediff(HediffDefOf.MechlinkImplant, S.owner.health.hediffSet.GetBrain());
            S.owner.health.AddHediff(DefDatabase<HediffDef>.GetNamed("MM_TestBandwidth")); S.owner.drafter.Drafted = true;
            GameDataSaveLoader.SaveGame("SelfDestructMpSeed");
        }
        public static void PrepareRound()
        {
            if (S.startTick == 0) S.startTick = Tick;
            for (int i = 0; i < S.survivors.Count; i++)
            {
                var survivor = S.survivors[i]; survivor.jobs.StopAll(); survivor.drafter.Drafted = true;
                survivor.Position = new IntVec3(30 + 3 * i, 0, 130); survivor.Notify_Teleported();
            }
            S.units.Clear(); S.pilots.Clear(); S.targets.Clear(); S.emptyUnits.Clear();
            S.pilotInventory = new List<string>();
            string[] names = { "MM_Immortal", "MM_Colossus", "MM_Reaver", "MM_Disruptor" };
            for (int i = 0; i < 4; i++)
            {
                var pos = new IntVec3(40 + i * 30, 0, 40);
                var unit = (Pawn_Immortal)Make(names[i], Faction.OfPlayer, pos); unit.autoFire = false;
                var pilot = Make("Colonist", Faction.OfPlayer, pos + IntVec3.North); pilot.drafter.Drafted = true;
                pilot.inventory.innerContainer.TryAdd(ThingMaker.MakeThing(ThingDefOf.Steel));
                unit.inventory.innerContainer.TryAdd(ThingMaker.MakeThing(ThingDefOf.Steel));
                Need(unit.Board(pilot), "fixture boarding");
                S.pilotInventory.Add(Inventory(pilot));
                S.units.Add(unit); S.pilots.Add(pilot); S.survivors.Add(pilot);
                var empty = (Pawn_Immortal)Make(names[i], Faction.OfPlayer, new IntVec3(pos.x, 0, 110));
                MechaAutonomy.Bind(Map, empty, S.owner); S.emptyUnits.Add(empty);
                for (int d = 7; d <= 9; d++)
                {
                    var target = ThingMaker.MakeThing(ThingDef.Named("MM_TestTarget")); target.SetFaction(Faction.OfPlayer);
                    GenSpawn.Spawn(target, pos + IntVec3.West * d, Map); S.targets.Add(target);
                }
            }
            Need(S.owner.mechanitor.UsedBandwidth == 21, "fixture autonomous bandwidth");
            S.stage = 0; S.stageTick = Tick;
            Log.Message("SD_MP EXECUTED round=" + S.round + " prepare tick=" + Tick);
        }
        static Command_Action Command(Pawn pawn, string key) => pawn.GetGizmos().OfType<Command_Action>().Single(g => g.defaultLabel == key.Translate());
        static void Dispatch()
        {
            Log.Message("SD_MP DISPATCH round=" + S.round + " stage=" + S.stage);
            var p = S.units[S.stage];
            Find.Selector.ClearSelection(); Find.Selector.Select(p);
            Command(p, "MM_SelfDestruct").action();
            var dialog = Find.WindowStack.Windows.OfType<Dialog_MessageBox>().Last();
            Need(dialog.buttonADestructive && dialog.text.ToString().Contains("80"), "destructive warning");
            dialog.Close();
            Need(!p.Dead && p.Pilot == S.pilots[S.stage], "cancel dialog no mutation");
            Command(p, "MM_SelfDestruct").action(); dialog = Find.WindowStack.Windows.OfType<Dialog_MessageBox>().Last();
            dialog.buttonAAction(); dialog.Close();
            Need(Find.Targeter.IsTargeting && !p.Dead, "confirmation only targets");
            Find.Targeter.StopTargeting(); Need(!p.Dead, "cancel targeting safe");
            Command(p, "MM_SelfDestruct").action(); dialog = Find.WindowStack.Windows.OfType<Dialog_MessageBox>().Last();
            dialog.buttonAAction(); dialog.Close();
            var validator = (Func<LocalTargetInfo, bool>)AccessTools.Field(typeof(Targeter), "targetValidator").GetValue(Find.Targeter);
            Need(!validator(p.Position + IntVec3.North * 51) && validator(p.Position + IntVec3.North * 50), "50-cell UI boundary");
            var action = (Action<LocalTargetInfo>)AccessTools.Field(typeof(Targeter), "action").GetValue(Find.Targeter);
            action(p.Position + IntVec3.North * 50); Find.Targeter.StopTargeting();
            Command(S.emptyUnits[S.stage], "MM_SelfDestruct").action();
            dialog = Find.WindowStack.Windows.OfType<Dialog_MessageBox>().Last(); dialog.buttonAAction(); dialog.Close();
        }
        public static void Draw() { }
        public static void Check(int stage)
        {
            Need(S.stage == stage, "phase mismatch");
            var p = S.units[stage]; var pilot = S.pilots[stage]; var empty = S.emptyUnits[stage];
            Need(p.Dead && p.Destroyed && p.Corpse == null && p.inventory.innerContainer.Count == 0, "piloted chassis/cargo destroyed without corpse");
            Need(pilot.Spawned && !pilot.Dead && pilot.Drafted && pilot.Position == new IntVec3(40 + stage * 30, 0, 90)
                && Inventory(pilot) == S.pilotInventory[stage], "same pilot lands with inventory: pos=" + pilot.Position + " spawned=" + pilot.Spawned + " dead=" + pilot.Dead + " drafted=" + pilot.Drafted + " expected=" + S.pilotInventory[stage] + " actual=" + Inventory(pilot));
            Need(empty.Dead && empty.Destroyed && empty.Corpse == null, "unmanned chassis destroyed");
            Need(!S.owner.mechanitor.ControlledPawns.Contains(empty), "bandwidth released");
            Need(S.targets[stage * 3].HitPoints < S.targets[stage * 3].MaxHitPoints && S.targets[stage * 3 + 1].HitPoints < S.targets[stage * 3 + 1].MaxHitPoints
                && S.targets[stage * 3 + 2].HitPoints == S.targets[stage * 3 + 2].MaxHitPoints, "blast affects friendly targets within 8 but not 9");
            Log.Message("SD_MP ASSERT round=" + S.round + " stage=" + stage + " tick=" + Tick + " PASS pilot=" + pilot.ThingID + " hp7=" + S.targets[stage * 3].HitPoints + " hp8=" + S.targets[stage * 3 + 1].HitPoints);
            if (stage == 3) { S.round++; S.stage = S.round >= 3 ? 4 : -1; }
            else S.stage++;
            S.stageTick = Tick;
        }
        static void Fail(Exception e) { failed = true; Log.Error("SD_MP FAILED " + e); Write(Peer + ".failed", e.ToString()); }
        public static void Update()
        {
            if (!Enabled) return;
            if (File.Exists(At("quit"))) { Application.Quit(); return; }
            if (finished || failed || LongEventHandler.AnyEventNowOrWaiting) return;
            try
            {
                if (!Client && !seedRequested && Current.ProgramState == ProgramState.Entry && GenCommandLine.CommandLineArgPassed("mm-seed-load"))
                { seedRequested = true; GameDataSaveLoader.LoadGame("SelfDestructMpSeed"); return; }
                if (!Client && !hosted && Current.ProgramState == ProgramState.Playing)
                {
                    hosted = true;
                    if (S.owner == null) Setup();
                    else
                    {
                        Need(S.stage == -1 && S.units.Count == 0, "seed must precede action fixtures");
                        foreach (var lord in Map.lordManager.lords.ToArray())
                            foreach (var pawn in lord.ownedPawns.Where(p => !p.Spawned).ToArray()) lord.Notify_PawnLost(pawn, PawnLostCondition.ExitedMap);
                    }
                    HostWindow.HostProgrammatically(new ServerSettings { gameName = "SelfDestruct", direct = true, directAddress = "127.0.0.1:" + Port,
                        lan = false, steam = false, multifaction = false, asyncTime = false, syncConfigs = false, pauseOnJoin = false,
                        pauseOnLetter = PauseOnLetter.Never, autoJoinPoint = 0, autosaveInterval = 0, desyncTraces = GenCommandLine.CommandLineArgPassed("mm-trace") }); return;
                }
                if (Current.ProgramState != ProgramState.Playing || !MP.IsInMultiplayer || Mp.Client.State != ConnectionStateEnum.ClientPlaying || TickPatch.Simulating) return;
                Need(!Mp.session.desynced, "desynced=True");
                if (!Client && !ready && !TickPatch.serverFrozen) { Mp.Client.Send(new ClientFreezePacket(true)); return; }
                if (Client && !ready)
                { if (!TickPatch.Frozen) return; ready = true; Write("client.ready", Snapshot()); }
                if (Mp.session.players.Count != 2 || !File.Exists(At("client.ready"))) return;
                if (!Client && !ready)
                {
                    Need(Snapshot() == File.ReadAllText(At("client.ready")), "BASELINE_LOAD_DRIFT"); ready = true; Write("host.ready", Snapshot());
                    Find.Selector.ClearSelection(); Find.Selector.Select(S.owner);
                    Mp.Client.Send(new ClientFreezePacket(false)); Mp.Client.SendCommand(CommandType.GlobalTimeSpeed, ScheduledCommand.Global, (byte)TimeSpeed.Ultrafast);
                }
                if (TickPatch.Frozen)
                { if (freezeSent || File.Exists(At("host.final"))) { Write(Peer + ".final", Snapshot()); Write(Peer + ".complete", "PASS desynced=False ticks=" + (Tick - S.startTick)); finished = true; } return; }
                if (Tick >= checkpoint) { checkpoint = Tick + 10000; Log.Message("SD_MP PROGRESS tick=" + Tick + " stage=" + S.round + ":" + S.stage + " players=2 desynced=False"); }
                if (S.round >= 3)
                {
                    if (!Client && Tick - S.startTick >= Duration && !freezeSent) { freezeSent = true; Mp.Client.Send(new ClientFreezePacket(true)); }
                    return;
                }
                Need(S.stage < 0 || Tick - S.stageTick < 6000, "action timeout");
                if (!Client) return;
                int key = S.round * 10 + S.stage;
                if (S.stage < 0) { if (requested != key) { requested = key; prepare.DoSync(null); } return; }
                if (dispatched != key) { dispatched = key; Dispatch(); }
                int wait = 400;
                if (Tick - S.stageTick >= wait && requested != key)
                { requested = key; advance.DoSync(null, S.stage); }
            }
            catch (Exception e) { Fail(e); }
        }
    }
}
