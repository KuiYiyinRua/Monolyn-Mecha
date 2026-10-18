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
        public List<Pawn> constructs = new List<Pawn>(), foes = new List<Pawn>();
        public List<int> hits = new List<int>();
        public List<int> combatTicks = new List<int>();
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
            Scribe_Collections.Look(ref constructs, "aaConstructs", LookMode.Reference);
            Scribe_Collections.Look(ref foes, "aaFoes", LookMode.Reference);
            Scribe_Collections.Look(ref hits, "aaHits", LookMode.Value);
            Scribe_Collections.Look(ref combatTicks, "aaCombatTicks", LookMode.Value);
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
            if (!Driver.Enabled || stage < 0) return;
            int index = 0;
            foreach (var unit in units.Concat(emptyUnits))
            {
                if (!unit.Dead) unit.Light.currentLight = unit.LightMax;
                if (stage == 1 && unit.CurJob?.def == MechaCombatDefOf.MM_MechaCombat) combatTicks[index]++;
                index++;
            }
            foreach (var unit in constructs) if (unit.needs.energy != null) unit.needs.energy.CurLevel = unit.needs.energy.MaxLevel;
            foreach (var foe in foes) if (!foe.Dead && foe.IsHashIntervalTick(120)) { foe.jobs.StopAll(); foe.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 240)); }
            if (Find.TickManager.TicksGame % 300 != 0) return;
            foreach (var pawn in survivors.Concat(foes).Concat(new[] { owner }))
                if (pawn != null && !pawn.Dead)
                    foreach (var need in pawn.needs.AllNeeds)
                        if (need is Need_Food || need is Need_Rest || need is Need_Mood) need.CurLevel = need.MaxLevel;
        }
    }
    [StaticConstructorOnStartup]
    public static class Driver
    {
        public static readonly bool Enabled = GenCommandLine.CommandLineArgPassed("mm-aa-mp");
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
            new Harmony("local.mecha.autoattack.mp").Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(Driver), nameof(Update)));
            new Harmony("local.mecha.autoattack.damage").Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.PreApplyDamage)), prefix: new HarmonyMethod(typeof(Driver), nameof(Protect)));
            new Harmony("local.mecha.autoattack.warning").Patch(AccessTools.Method(typeof(Log), nameof(Log.Warning), new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Driver), nameof(Warning)));
            Log.Message("AA_MP BOOT peer=" + Peer + " run=" + Path.GetFileName(Root) + " core=" + typeof(Pawn_Immortal).Assembly.GetName().Version);
            foreach (string line in File.ReadAllLines(At("candidate.sha256"))) Log.Message("AA_MP HASH " + line);
            if (File.Exists(At("seed.sha256"))) Log.Message("AA_MP SEED " + File.ReadAllText(At("seed.sha256")).Trim());
        }
        static void Need(bool ok, string message) { if (!ok) throw new Exception(message); }
        static void Warning(string text)
        {
            if (warningCaptured || !text.StartsWith("Deep-saving destroyed thing")) return;
            warningCaptured = true; Log.Message("AA_MP WARNING_ORIGIN " + new System.Diagnostics.StackTrace());
        }
        static void Write(string name, string message) { File.WriteAllText(At(name), message); Log.Message("AA_MP " + name + " " + message); }
        static string N(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        static string Inventory(Pawn p)
            => string.Join(",", p.inventory.innerContainer.OrderBy(t => t.thingIDNumber).Select(t => t.ThingID + ":" + t.stackCount))
                + "|equipment=" + string.Join(",", p.equipment.AllEquipmentListForReading.OrderBy(t => t.thingIDNumber).Select(t => t.ThingID))
                + "|apparel=" + string.Join(",", p.apparel.WornApparel.OrderBy(t => t.thingIDNumber).Select(t => t.ThingID));
        static IEnumerable<Pawn> All => S.units.Cast<Pawn>().Concat(S.emptyUnits).Concat(S.constructs);
        static string Snapshot()
        {
            return "world=" + Tick + ":" + Mp.AsyncWorldTime.randState + " maps="
                + string.Join(";", Find.Maps.OrderBy(m => m.uniqueID).Select(m => m.uniqueID + ":" + m.AsyncTime().mapTicks + ":" + m.AsyncTime().randState))
                + " stage=" + S.round + ":" + S.stage + " hits=" + string.Join(",", S.hits ?? new List<int>())
                + string.Join("", All.Select(p => "\n" + p.ThingID + ":" + p.Position + ":" + p.CurJob?.def.defName));
        }
        static bool Protect(Pawn __instance, DamageInfo dinfo, ref bool absorbed)
        {
            if (Current.ProgramState != ProgramState.Playing || S.foes == null || !S.foes.Contains(__instance)) return true;
            var all = All.ToList(); int i = all.IndexOf(dinfo.Instigator as Pawn);
            // Give already-launched missiles/orbs ten seconds to finish after OFF.
            if (i >= 0 && (S.stage != 2 || Tick - S.stageTick >= 600)) S.hits[i]++;
            absorbed = true; return false;
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
            foreach (var pawn in All.Concat(S.foes).ToArray())
            {
                var overseer = pawn.GetOverseer();
                if (overseer != null)
                {
                    pawn.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, overseer);
                    overseer.mechanitor.Notify_BandwidthChanged();
                }
                pawn.DeSpawn(); Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            }
            S.units.Clear(); S.emptyUnits.Clear(); S.constructs.Clear(); S.foes.Clear(); S.hits.Clear(); S.pilots.Clear(); S.combatTicks.Clear();
            string[] names = { "MM_Immortal", "MM_Colossus", "MM_Reaver", "MM_Disruptor" };
            for (int i = 0; i < 8; i++)
            {
                var pos = new IntVec3(40 + (i % 4) * 30, 0, i < 4 ? 40 : 90);
                var unit = (Pawn_Immortal)Make(names[i % 4], Faction.OfPlayer, pos); unit.autoFire = false;
                if (i < 4)
                {
                    var pilot = Make("Colonist", Faction.OfPlayer, pos + IntVec3.South); pilot.drafter.Drafted = true;
                    Need(unit.Board(pilot), "fixture boarding"); S.units.Add(unit); S.survivors.Add(pilot); S.pilots.Add(pilot);
                }
                else { MechaAutonomy.Bind(Map, unit, S.owner); unit.drafter.Drafted = true; S.emptyUnits.Add(unit); }
            }
            for (int i = 0; i < 2; i++)
            {
                var unit = Make(i == 0 ? "MM_Stalker" : "MM_Guardian", Faction.OfPlayer, new IntVec3(40 + i * 60, 0, 120));
                unit.relations.AddDirectRelation(PawnRelationDefOf.Overseer, S.owner); S.owner.mechanitor.Notify_BandwidthChanged();
                unit.drafter.Drafted = true; S.constructs.Add(unit);
                unit.GetComp<CompGuardian>()?.SetAutoRestore(false);
                unit.GetComp<CompStalker>()?.SetAutoBlink(false);
            }
            var enemyFaction = Find.FactionManager.AllFactionsListForReading
                .Where(f => f.def.defName.StartsWith("Pirate") && f.HostileTo(Faction.OfPlayer))
                .OrderBy(f => f.loadID).First();
            foreach (var unit in All)
            {
                var foe = Make("Pirate", enemyFaction, unit.Position + IntVec3.North * 16);
                Need(foe.Faction == enemyFaction && foe.HostileTo(S.owner), "fixture enemy must be hostile to colony");
                foe.jobs.StopAll(); foe.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 6000)); S.foes.Add(foe); S.hits.Add(0); S.combatTicks.Add(0);
            }
            S.stage = 0; S.stageTick = Tick;
            Log.Message("AA_MP EXECUTED round=" + S.round + " prepare tick=" + Tick);
        }
        static void Toggle(Pawn pawn, string key)
        { pawn.GetGizmos().OfType<Command_Toggle>().Single(c => c.defaultLabel == key.Translate()).toggleAction(); }
        static void ToggleFire(Pawn_Immortal pawn)
        { Toggle(pawn, pawn is Pawn_Disruptor ? "MM_DisruptorAutoFire" : "MM_AutoFire"); }
        static void Action(Pawn pawn, string key)
        { pawn.GetGizmos().OfType<Command_Action>().Single(c => c.defaultLabel == key.Translate()).action(); }
        static void Dispatch()
        {
            Log.Message("AA_MP DISPATCH round=" + S.round + " stage=" + S.stage);
            if (S.stage == 0)
            {
                foreach (var p in S.units.Concat(S.emptyUnits)) ToggleFire(p);
                Toggle(S.constructs[0], "MM_StalkerAutoCombat"); Toggle(S.constructs[1], "MM_GuardianAutoCombat");
            }
            else if (S.stage == 1)
                foreach (var p in S.units.Concat(S.emptyUnits)) { ToggleFire(p); Toggle(p, "MM_MechaAutoCombat"); }
            else if (S.stage == 2)
            {
                foreach (var p in S.units.Concat(S.emptyUnits)) Toggle(p, "MM_MechaAutoCombat");
                Toggle(S.constructs[0], "MM_StalkerAutoCombat"); Toggle(S.constructs[1], "MM_GuardianAutoCombat");
                foreach (var p in S.units) Action(p, "MM_Eject");
            }
            else
            {
                for (int i = 0; i < S.units.Count; i++)
                {
                    Action(S.units[i], "MM_Board");
                    var menu = Find.WindowStack.Windows.OfType<FloatMenu>().Last();
                    var options = (List<FloatMenuOption>)AccessTools.Field(typeof(FloatMenu), "options").GetValue(menu);
                    options.Single(o => o.Label == S.pilots[i].LabelShortCap).action();
                    menu.Close();
                }
                foreach (var p in S.units.Concat(S.emptyUnits)) { ToggleFire(p); Toggle(p, "MM_MechaAutoCombat"); }
                Toggle(S.constructs[0], "MM_StalkerAutoCombat"); Toggle(S.constructs[1], "MM_GuardianAutoCombat");
            }
        }
        public static void Draw() { }
        public static void Check(int stage)
        {
            Need(S.stage == stage, "phase mismatch");
            var all = All.ToList();
            var failures = new List<string>();
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i]; var w = p as Pawn_Immortal; var s = p.GetComp<CompStalker>(); var g = p.GetComp<CompGuardian>();
                var cache = p.Map.attackTargetsCache.GetPotentialTargetsFor(p).ToArray();
                string data = p.def.defName + " id=" + p.ThingID + " hits=" + S.hits[i] + " job=" + p.CurJob?.def.defName
                    + " drafted=" + p.Drafted + " faction=" + p.Faction.loadID + " context=" + Faction.OfPlayer.loadID
                    + " potential=" + cache.Length + " hostile=" + p.HostileTo(S.foes[i]) + " reverse=" + S.foes[i].HostileTo(p)
                    + " controlled=" + p.IsColonyMechPlayerControlled + " subject=" + MechanitorUtility.IsPlayerOverseerSubject(p)
                    + " overseerState=" + p.OverseerSubject?.State + " enemyFaction=" + S.foes[i].Faction?.def.defName
                    + " autofire=" + w?.autoFire + " driven=" + w?.Driven + " ai=" + w?.Combat.Enabled + " hunt=" + w?.Combat.MayHunt
                    + " s=" + s?.MayHunt + " g=" + g?.MayAct + " shots=" + w?.shotsFired
                    + " shoot=" + w?.CanShoot(S.foes[i]) + " combatTicks=" + S.combatTicks[i];
                Log.Message("AA_MP DIAG stage=" + stage + " " + data);
                if (stage != 2 && S.hits[i] == 0) failures.Add(data);
                Need(!p.HostileTo(S.owner), "friendly hostility " + p.ThingID);
                Need(p.relations != null && p.workSettings != null, "native control trackers " + p.ThingID);
                if (stage != 2)
                {
                    Need(p.HostileTo(S.foes[i]) && S.foes[i].HostileTo(p), "symmetric hostility " + p.ThingID);
                    Need(cache.Contains(S.foes[i]) && p.Map.attackTargetsCache.GetPotentialTargetsFor(S.foes[i]).Contains(p), "native target discovery " + p.ThingID);
                    if (w != null && stage == 1)
                        Need(!w.autoFire && w.Combat.Enabled && S.combatTicks[i] > 0, "natural combat job " + p.ThingID);
                    if (w != null && stage == 3) Need(w.autoFire && w.Combat.Enabled, "reenabled combat " + p.ThingID);
                    if (i < 4) Need(w.Pilot == S.pilots[i] && w.GetOverseer() == null, "pilot control " + p.ThingID);
                }
                else if (w != null)
                {
                    Need(!w.autoFire && !w.Combat.Enabled && S.hits[i] == 0, "auto fire did not stop " + data);
                    if (i < 4) Need(w.Pilot == null && !w.Driven, "ejected chassis must stop " + p.ThingID);
                }
                else Need(s != null ? !s.autoCombat : !g.autoCombat, "construct auto combat did not stop " + p.ThingID);
            }
            Need(S.owner.mechanitor.UsedBandwidth == 25 && S.emptyUnits.All(p => p.GetOverseer() == S.owner && p.Driven), "native autonomous bandwidth/control");
            Need(failures.Count == 0, "AUTO_ATTACK_NO_HIT " + string.Join("\n", failures));
            Log.Message("AA_MP ASSERT round=" + S.round + " stage=" + stage + " tick=" + Tick + " PASS hits=" + string.Join(",", S.hits));
            if (stage == 3) { S.round++; S.stage = S.round >= 3 ? 4 : -1; }
            else S.stage++;
            for (int i = 0; i < S.hits.Count; i++) { S.hits[i] = 0; S.combatTicks[i] = 0; }
            S.stageTick = Tick;
        }
        static void Fail(Exception e) { failed = true; Log.Error("AA_MP FAILED " + e); Write(Peer + ".failed", e.ToString()); }
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
                        S.constructs = new List<Pawn>(); S.foes = new List<Pawn>(); S.hits = new List<int>(); S.combatTicks = new List<int>();
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
                if (Tick >= checkpoint) { checkpoint = Tick + 10000; Log.Message("AA_MP PROGRESS tick=" + Tick + " stage=" + S.round + ":" + S.stage + " players=2 desynced=False"); }
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
                int wait = 1500;
                if (Tick - S.stageTick >= wait && requested != key)
                { requested = key; advance.DoSync(null, S.stage); }
            }
            catch (Exception e) { Fail(e); }
        }
    }
}
