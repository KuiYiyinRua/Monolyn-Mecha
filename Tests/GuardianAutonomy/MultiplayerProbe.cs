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
using UnityEngine;
using Verse;
using Verse.AI;
using Mp = Multiplayer.Client.Multiplayer;

namespace GuardianAutonomyMpTests
{
    public sealed class State : GameComponent
    {
        public List<Pawn_Immortal> units = new List<Pawn_Immortal>();
        public List<Pawn> guardians = new List<Pawn>();
        public Pawn owner, enemy;
        public int stage = -1, round, stageTick, startTick, projectiles;
        public State(Game game) { }
        public override void GameComponentUpdate() { if (Driver.Enabled) Driver.Update(); }
        public override void GameComponentOnGUI() { if (Driver.Enabled) Driver.Draw(); }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref units, "mmTestUnits", LookMode.Reference);
            Scribe_Collections.Look(ref guardians, "mmTestGuardians", LookMode.Reference);
            Scribe_References.Look(ref owner, "mmTestOwner"); Scribe_References.Look(ref enemy, "mmTestEnemy");
            Scribe_Values.Look(ref stage, "mmTestStage", -1); Scribe_Values.Look(ref round, "mmTestRound");
            Scribe_Values.Look(ref stageTick, "mmTestStageTick"); Scribe_Values.Look(ref startTick, "mmTestStartTick");
            Scribe_Values.Look(ref projectiles, "mmTestProjectiles");
        }
        public override void GameComponentTick()
        {
            if (!Driver.Enabled || stage < 0 || !enemy.Spawned || Find.TickManager.TicksGame % 30 != 0) return;
            if (Find.TickManager.TicksGame % 300 == 0)
                foreach (var pawn in new[] { owner, enemy })
                    foreach (var need in pawn.needs.AllNeeds)
                        if (need is Need_Food || need is Need_Rest || need is Need_Mood) need.CurLevel = need.MaxLevel;
            if (stage == 4 && Find.TickManager.TicksGame % 2100 == 0)
            {
                foreach (var p in guardians) { var f = p.GetComp<CompGuardianField>(); f.Seeds.Refuel(30); f.Activate(); }
                foreach (var p in units)
                { p.Light.currentLight = p.LightMax; MechaActions.Move(p.Map, p, new IntVec3(p.Position.x, 0, p.Position.z >= 60 ? 58 : 61)); }
            }
            // Real native ballistic motion crosses both overlapping fields during
            // the action test and soak; no direct call to the reflection helper.
            var projectile = (Projectile)ThingMaker.MakeThing(ThingDef.Named("Bullet_AssaultRifle"));
            GenSpawn.Spawn(projectile, enemy.Position, enemy.Map);
            projectile.Launch(enemy, new IntVec3(80, 0, 78), new IntVec3(80, 0, 78), ProjectileHitFlags.None);
            projectiles++;
        }
    }
    [StaticConstructorOnStartup]
    public static class Driver
    {
        public static readonly bool Enabled = GenCommandLine.CommandLineArgPassed("mm-ga-mp");
        static readonly bool Client = GenCommandLine.CommandLineArgPassed("mm-client");
        static readonly string Root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        static readonly int Port = GenCommandLine.TryGetCommandLineArg("mm-port", out string port) ? int.Parse(port) : 31418;
        static readonly int Duration = GenCommandLine.TryGetCommandLineArg("mm-ticks", out string ticks) ? int.Parse(ticks) : 12000;
        static bool hosted, ready, finished, failed, freezeSent;
        static int requested = -999, dispatched = -999, guiDone = -999, guiRound = -999, guiStep, checkpoint;
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
            new Harmony("local.mecha.guardianautonomy.mp").Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(Driver), nameof(Update)));
            new Harmony("local.mecha.guardianautonomy.target").Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.PreApplyDamage)), prefix: new HarmonyMethod(typeof(Driver), nameof(ProtectTarget)));
            Log.Message("GA_MP BOOT peer=" + Peer + " run=" + Path.GetFileName(Root) + " core=" + typeof(Pawn_Immortal).Assembly.GetName().Version);
            foreach (string line in File.ReadAllLines(At("candidate.sha256"))) Log.Message("GA_MP HASH " + line);
        }
        static void Need(bool ok, string message) { if (!ok) throw new Exception(message); }
        static bool ProtectTarget(Pawn __instance, ref bool absorbed)
        { if (Current.ProgramState != ProgramState.Playing || __instance != S.enemy) return true; absorbed = true; return false; }
        static void Write(string name, string message) { File.WriteAllText(At(name), message); Log.Message("GA_MP " + name + " " + message); }
        static string N(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        static string Snapshot()
        {
            string result = "world=" + Tick + ":" + Mp.AsyncWorldTime.randState + " maps="
                + string.Join(";", Find.Maps.OrderBy(m => m.uniqueID).Select(m => m.uniqueID + ":" + m.AsyncTime().mapTicks + ":" + m.AsyncTime().randState))
                + " stage=" + S.round + ":" + S.stage + " projectiles=" + S.projectiles + " bandwidth=" + S.owner.mechanitor.UsedBandwidth;
            foreach (var p in S.units) result += "\n" + p.ThingID + ":" + p.Position + ":" + p.GetOverseer()?.ThingID + ":" + p.Drafted + ":" + p.Driven + ":" + N(p.Light.currentLight);
            foreach (var p in S.guardians)
            {
                var f = p.GetComp<CompGuardianField>();
                var set = (HashSet<int>)AccessTools.Field(typeof(CompGuardianField), "attempted").GetValue(f);
                result += "\n" + p.ThingID + ":" + N(f.Seeds.Fuel) + ":" + N(f.Seeds.TargetFuelLevel) + ":" + f.Seeds.allowAutoRefuel + ":" + f.remainingTicks + ":" + string.Join(",", set.OrderBy(x => x));
            }
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
            foreach (var p in Map.mapPawns.AllPawnsSpawned.ToArray()) p.DeSpawn();
            foreach (var cell in new CellRect(25, 30, 75, 60))
            {
                foreach (var thing in cell.GetThingList(Map).ToArray()) { if (thing.def.destroyable) thing.Destroy(); else thing.DeSpawn(); }
                Map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil); Map.roofGrid.SetRoof(cell, null);
            }
            S.owner = Make("Colonist", Faction.OfPlayer, new IntVec3(60, 0, 50));
            S.owner.health.AddHediff(HediffDefOf.MechlinkImplant, S.owner.health.hediffSet.GetBrain());
            S.owner.health.AddHediff(DefDatabase<HediffDef>.GetNamed("MM_TestBandwidth")); S.owner.drafter.Drafted = true;
            string[] names = { "MM_Immortal", "MM_Colossus", "MM_Reaver", "MM_Disruptor" };
            for (int i = 0; i < 4; i++) S.units.Add((Pawn_Immortal)Make(names[i], Faction.OfPlayer, new IntVec3(44 + i * 10, 0, 58)));
            for (int i = 0; i < 2; i++)
            {
                var guardian = Make("MM_Guardian", Faction.OfPlayer, new IntVec3(50 + 2 * i, 0, 77));
                guardian.relations.AddDirectRelation(PawnRelationDefOf.Overseer, S.owner);
                guardian.GetComp<CompGuardian>().SetAutoCombat(false); guardian.GetComp<CompGuardian>().SetAutoRestore(false);
                guardian.needs.energy.CurLevel = guardian.needs.energy.MaxLevel; guardian.drafter.Drafted = true;
                S.guardians.Add(guardian);
            }
            S.enemy = Make("Colonist", Faction.OfAncientsHostile, new IntVec3(31, 0, 78));
            S.enemy.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 999999), JobCondition.InterruptForced);
            GameDataSaveLoader.SaveGame("GuardianAutonomyMpSeed");
        }
        public static void PrepareRound()
        {
            if (S.startTick == 0) S.startTick = Tick;
            for (int i = 0; i < 4; i++)
            {
                var unit = S.units[i]; MechaAutonomy.Unbind(Map, unit); unit.jobs.StopAll(); unit.pather.StopDead();
                unit.Position = new IntVec3(44 + i * 10, 0, 58); unit.Notify_Teleported(); unit.Light.currentLight = unit.LightMax; unit.autoFire = false;
            }
            foreach (var p in S.guardians)
            { var f = p.GetComp<CompGuardianField>(); f.remainingTicks = 0; f.Seeds.Refuel(30); f.Seeds.TargetFuelLevel = 30; f.Seeds.allowAutoRefuel = false; }
            S.stage = 0; S.stageTick = Tick;
            Log.Message("GA_MP EXECUTED round=" + S.round + " prepare tick=" + Tick);
        }
        static Command_Action Command(Pawn pawn, string key) => pawn.GetGizmos().OfType<Command_Action>().Single(g => g.defaultLabel == key.Translate());
        static void Dispatch()
        {
            Log.Message("GA_MP DISPATCH round=" + S.round + " stage=" + S.stage);
            if (S.stage == 0)
            {
                foreach (var p in S.units)
                {
                    Find.Selector.ClearSelection(); Find.Selector.Select(p);
                    for (int open = 0; open < 2; open++)
                    {
                        Command(p, "MM_Autonomy").action(); var menu = Find.WindowStack.Windows.OfType<FloatMenu>().Last();
                        var options = (List<FloatMenuOption>)AccessTools.Field(typeof(FloatMenu), "options").GetValue(menu);
                        if (open == 1) options.First(o => o.Label.StartsWith(S.owner.LabelShortCap)).action();
                        menu.Close();
                    }
                }
            }
            if (S.stage == 1)
            {
                foreach (var p in S.units) p.GetGizmos().OfType<Command_Toggle>().Single(g => g.hotKey == KeyBindingDefOf.Command_ColonistDraft).toggleAction();
                foreach (var p in S.guardians) Command(p, "MM_GuardianField").action();
            }
            if (S.stage == 2) foreach (var p in S.units) MechaActions.Move(Map, p, p.Position + IntVec3.North * 3);
            if (S.stage == 3) foreach (var p in S.units) Command(p, "MM_AutonomyDisconnect").action();
        }
        // Exercise the native slider and checkbox with real IMGUI events. Native
        // MP prefixes watch these fields; this harness registers no replacement.
        public static void Draw()
        {
            if (!Client || !ready || finished || failed || !MP.IsInMultiplayer || S.stage != 2 || guiDone == S.round) return;
            if (Event.current.type != EventType.Repaint) return;
            var saved = Event.current;
            try
            {
                Find.Selector.ClearSelection(); Find.Selector.Select(S.guardians[0]);
                var slider = S.guardians[0].GetGizmos().OfType<Gizmo_GuardianSeeds>().Single();
                if (guiRound != S.round) { guiRound = S.round; guiStep = 0; }
                var events = new[] {
                    new Event { type = EventType.MouseDown, button = 0, mousePosition = new Vector2(Mathf.Min(151.9f, 8 + 144f * (S.round + 1) / 3f), 55) },
                    new Event { type = EventType.MouseUp, button = 0, mousePosition = new Vector2(Mathf.Min(151.9f, 8 + 144f * (S.round + 1) / 3f), 55) },
                    new Event { type = EventType.MouseDown, button = 0, mousePosition = new Vector2(140, 18) },
                    new Event { type = EventType.MouseUp, button = 0, mousePosition = new Vector2(140, 18) } };
                {
                    var evt = events[guiStep];
                    Event.current = evt;
                    MP.WatchBegin();
                    try { slider.GizmoOnGUI(Vector2.zero, 160, default(GizmoRenderParms)); }
                    finally { MP.WatchEnd(); }
                }
                Log.Message("GA_MP UI event=" + guiStep + " hot=" + GUIUtility.hotControl + " fuelToggle=" + S.guardians[0].GetComp<CompGuardianField>().Seeds.allowAutoRefuel);
                if (++guiStep == events.Length) { guiDone = S.round; Log.Message("GA_MP UI slider+toggle round=" + S.round); }
            }
            catch (Exception e) { Fail(e); }
            finally { Event.current = saved; }
        }
        public static void Check(int stage)
        {
            Need(S.stage == stage, "phase mismatch");
            if (stage == 0) Need(S.units.All(p => p.Autonomous && p.GetMechControlGroup() != null) && S.owner.mechanitor.UsedBandwidth == 25, "native binding/bandwidth");
            if (stage == 1) Need(S.units.All(p => p.Drafted) && S.guardians.All(p => p.GetComp<CompGuardianField>().Active && p.GetComp<CompGuardianField>().Seeds.Fuel == 25), "draft and field activation");
            if (stage == 2)
            {
                Need(S.units.All(p => p.Position.z >= 60), "real autonomous movement");
                var f = S.guardians[0].GetComp<CompGuardianField>();
                Need(Mathf.Abs(f.Seeds.TargetFuelLevel - (S.round + 1) * 10) < .01f && f.Seeds.allowAutoRefuel, "native slider/toggle target=" + f.Seeds.TargetFuelLevel + " auto=" + f.Seeds.allowAutoRefuel);
            }
            if (stage == 3) Need(S.units.All(p => p.GetOverseer() == null && !p.Driven) && S.owner.mechanitor.UsedBandwidth == 4, "disconnect releases bandwidth");
            Log.Message("GA_MP ASSERT round=" + S.round + " stage=" + stage + " tick=" + Tick + " PASS");
            if (stage == 3)
            {
                S.round++; S.stage = S.round >= 3 ? 4 : -1;
                if (S.stage == 4) foreach (var p in S.units) { MechaAutonomy.Bind(Map, p, S.owner); p.drafter.Drafted = true; }
            }
            else S.stage++;
            S.stageTick = Tick;
        }
        static void Fail(Exception e) { failed = true; Log.Error("GA_MP FAILED " + e); Write(Peer + ".failed", e.ToString()); }
        public static void Update()
        {
            if (!Enabled) return;
            if (File.Exists(At("quit"))) { Application.Quit(); return; }
            if (finished || failed || LongEventHandler.AnyEventNowOrWaiting) return;
            try
            {
                if (!Client && !hosted && Current.ProgramState == ProgramState.Playing)
                {
                    hosted = true; Setup();
                    HostWindow.HostProgrammatically(new ServerSettings { gameName = "GuardianAutonomy", direct = true, directAddress = "127.0.0.1:" + Port,
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
                    Find.Selector.ClearSelection(); Find.Selector.Select(S.guardians[0]);
                    Mp.Client.Send(new ClientFreezePacket(false)); Mp.Client.SendCommand(CommandType.GlobalTimeSpeed, ScheduledCommand.Global, (byte)TimeSpeed.Ultrafast);
                }
                if (TickPatch.Frozen)
                { if (freezeSent || File.Exists(At("host.final"))) { Write(Peer + ".final", Snapshot()); Write(Peer + ".complete", "PASS desynced=False ticks=" + (Tick - S.startTick)); finished = true; } return; }
                if (Tick >= checkpoint) { checkpoint = Tick + 10000; Log.Message("GA_MP PROGRESS tick=" + Tick + " stage=" + S.round + ":" + S.stage + " players=2 desynced=False"); }
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
                int wait = S.stage == 2 ? 600 : 180;
                if (Tick - S.stageTick >= wait && requested != key && (S.stage != 2 || guiDone == S.round))
                { requested = key; advance.DoSync(null, S.stage); }
            }
            catch (Exception e) { Fail(e); }
        }
    }
}
