using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ASEL;
using HarmonyLib;
using Monolyn.Mecha;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace EnemyRaidTests
{
    [StaticConstructorOnStartup]
    public static class Entry
    {
        public static readonly bool Cold = GenCommandLine.CommandLineArgPassed("mm-enemy-raids-cold");
        public static readonly bool Diagnostics = GenCommandLine.CommandLineArgPassed("mm-enemy-raids-diagnostics");
        public static readonly bool Active = Cold || GenCommandLine.CommandLineArgPassed("mm-enemy-raids-test");
        public static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        public static Pawn Target, Unit;
        public static int Hits, Shots, Blinks, Failures;
        public static string Case;
        static Entry()
        {
            if (!Active) return;
            var mod = LoadedModManager.RunningMods.Single(m => m.assemblies.loadedAssemblies.Contains(typeof(Pawn_Immortal).Assembly));
            string hash;
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(Path.Combine(mod.RootDir, "1.6/Assemblies/Monolyn.Mecha.dll")))
                hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            if (hash != File.ReadAllText(Path.Combine(Root, "core.sha256")).Trim()) throw new Exception("Stale candidate");
            Log.Message("MM_RAIDS BOOT core=" + hash + " mvid=" + typeof(Pawn_Immortal).Module.ModuleVersionId);
            var harmony = new Harmony("local.monolyn.mecha.enemyraid.tests"); harmony.PatchAll();
            harmony.Patch(AccessTools.Method(typeof(WorldGenerator), "GenerateWorld"),
                prefix: new HarmonyMethod(typeof(Entry), nameof(Push)), finalizer: new HarmonyMethod(typeof(Entry), nameof(Pop)));
            if (Cold) LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("EnemyMechaRaidState"));
        }
        static void Push(ref string seedString) { seedString = "MonolynEnemyRaids261017"; Rand.PushState(261017); }
        static void Pop() => Rand.PopState();
        public static void Trace(string message) => File.AppendAllText(Path.Combine(Root, "trace.txt"), Find.TickManager.TicksGame + " " + Case + " " + message + "\n");
        public static void Assert(bool ok, string note)
        {
            if (!ok) Failures++;
            string message = (ok ? "PASS " : "FAILED ") + Case + " " + note;
            File.AppendAllText(Path.Combine(Root, "evidence.txt"), message + "\n"); Log.Message("MM_RAIDS " + message);
        }
    }

    public sealed class Probe : GameComponent
    {
        static readonly string[] Kinds = { "MM_Immortal", "MM_Colossus", "MM_Reaver", "MM_Disruptor", "MM_Stalker", "MM_Guardian" };
        Map map;
        Faction faction;
        Pawn unit;
        Lord lord;
        int stage, ticks, index = -1, arrival = -1, meleeTicks, orbTicks;
        bool prepared, done;
        List<Pawn> saved = new List<Pawn>();
        List<Pawn> priorRaidPawns;
        CompGuardian guardian;
        Pawn_Immortal support;
        IncidentParms raidParms;
        public Probe(Game game) { }
        void Run(Action action)
        {
            try { action(); }
            catch (Exception e)
            {
                done = true; File.WriteAllText(Path.Combine(Entry.Root, "failed"), e.ToString());
                Log.Error("MM_RAIDS FAILED " + e); Application.Quit();
            }
        }
        public override void FinalizeInit()
        {
            if (!Entry.Active || !Entry.Cold) return;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
        }
        public override void GameComponentUpdate()
        {
            if (!Entry.Active || done || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return;
            Run(() => {
                if (File.Exists(Path.Combine(Entry.Root, "quit"))) { Application.Quit(); return; }
                foreach (var w in Find.WindowStack.Windows.Where(w => w.forcePause).ToArray()) w.Close();
                if (!prepared)
                {
                    prepared = true; map = Find.CurrentMap;
                    if (Entry.Cold) { Cold(); Finish(); return; }
                    Setup(); Groups();
                    if (Entry.Diagnostics) { saved = Kinds.Select(k => PawnGenerator.GeneratePawn(PawnKindDef.Named(k), faction)).ToList(); SetupSupport(); }
                    else NextUnit();
                }
                Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            });
        }
        void Setup()
        {
            Entry.Trace("world=" + Find.World.info.seedString + " outdoor=" + map.mapTemperature.OutdoorTemp);
            map.fogGrid.ClearAllFog();
            foreach (var p in map.mapPawns.AllPawnsSpawned.ToArray()) p.DeSpawn();
            foreach (var c in new CellRect(22, 25, 115, 115))
            {
                map.roofGrid.SetRoof(c, null);
                foreach (var t in c.GetThingList(map).ToArray()) { if (t.def.destroyable) t.Destroy(); else t.DeSpawn(); }
                map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
            }
            faction = Find.FactionManager.AllFactions.First(f => f.def.defName == "UrielCult");
            Entry.Target = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(Entry.Target, new IntVec3(112, 0, 60), map);
            Entry.Target.drafter.Drafted = true;
        }
        PawnGroupMakerParms Parms(Faction f, float points) => new PawnGroupMakerParms {
            faction = f, groupKind = PawnGroupKindDefOf.Combat, points = points, tile = map.Tile,
            raidStrategy = DefDatabase<RaidStrategyDef>.GetNamed("ImmediateAttackMonolynEnemy")
        };
        void Groups()
        {
            Entry.Case = "raid-budget";
            var trees = DefDatabase<ThinkTreeDef>.AllDefsListForReading.Where(t => t.defName.StartsWith("MM_"));
            foreach (var tree in trees)
                foreach (var subtree in tree.thinkRoot.ThisAndChildrenRecursive.OfType<ThinkNode_Subtree>().Where(t => t.subtreeNode == null))
                    Entry.Trace("UNRESOLVED owner=" + tree.defName + " ref=" + Traverse.Create(subtree).Field<ThinkTreeDef>("treeDef").Value
                        + " count=" + subtree.subNodes.Count + " children=" + string.Join(",", subtree.subNodes.Select(t => t.GetType().Name + ":" + t.UniqueSaveKey)));
            Entry.Assert(trees.SelectMany(t => t.thinkRoot.ThisAndChildrenRecursive).OfType<ThinkNode_Subtree>()
                .All(t => t.subtreeNode != null), "all nested mecha fallback subtrees retain resolved references");
            var seen = new HashSet<string>(); int samples = 0;
            bool budgets = true, caps = true, gates = true;
            foreach (var group in faction.def.pawnGroupMakers.Where(g => g.kindDef == PawnGroupKindDefOf.Combat))
            foreach (int points in new[] { 250, 600, 1200, 2000, 2700, 3300, 3600, 4800, 8000, 10000 })
            for (int seed = 0; seed < 24; seed++)
            {
                var parms = Parms(faction, points); parms.seed = 1000 + seed;
                var choices = PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints(points, group.options, parms).ToList();
                var custom = choices.Where(x => x.Option.kind.GetModExtension<MechaRaidExtension>() != null).ToList();
                budgets &= choices.Sum(x => x.Cost) <= points + .01f && custom.Sum(x => x.Cost) <= points * .5f + .01f;
                foreach (var c in custom) { seen.Add(c.Option.kind.defName); gates &= points >= c.Option.kind.GetModExtension<MechaRaidExtension>().minRaidPoints; }
                foreach (var g in custom.GroupBy(x => x.Option.kind)) caps &= g.Count() <= g.Key.maxPerGroup;
                samples++;
            }
            Entry.Assert(budgets && caps && gates, "native selection respects total budget, 50% mecha share, thresholds and caps in " + samples + " samples");
            Entry.Assert(Kinds.All(seen.Contains), "all six kinds can occur: " + string.Join(",", seen.OrderBy(x => x)));
            var raw = faction.def.pawnGroupMakers.First(g => g.kindDef == PawnGroupKindDefOf.Combat).options;
            var options = raw.Concat(Kinds.Select(k => new PawnGenOption { kind = PawnKindDef.Named(k), selectionWeight = 10000f })).ToList();
            bool denied = true;
            foreach (var f in Find.FactionManager.AllFactions.Where(f => !EnemyMechaRaids.IsHostileMonolyn(f)))
            {
                var p = Parms(f, 10000);
                denied &= Kinds.All(k => !PawnGroupMakerUtility.PawnGenOptionValid(new PawnGenOption { kind = PawnKindDef.Named(k), selectionWeight = 1 }, p));
            }
            Entry.Assert(denied, "every other loaded faction rejects all six even when explicitly injected as options");
            var friendly = Find.FactionManager.AllFactions.First(f => f.def.defName == "RaphaelCult");
            Entry.Assert(!EnemyMechaRaids.IsHostileMonolyn(friendly), "friendly Raphael excluded");
            friendly.RelationWith(Faction.OfPlayer).kind = FactionRelationKind.Hostile;
            Faction.OfPlayer.RelationWith(friendly).kind = FactionRelationKind.Hostile;
            var friendlyParms = Parms(friendly, 10000);
            Entry.Assert(Kinds.All(k => PawnGroupMakerUtility.PawnGenOptionValid(new PawnGenOption { kind = PawnKindDef.Named(k) }, friendlyParms)), "hostile Raphael permits exactly the same mecha profiles");
            friendly.RelationWith(Faction.OfPlayer).kind = FactionRelationKind.Neutral;
            Faction.OfPlayer.RelationWith(friendly).kind = FactionRelationKind.Neutral;
            var noRaid = Parms(faction, 10000); noRaid.raidStrategy = null;
            Entry.Assert(!PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints(10000, options, noRaid).Any(x => Kinds.Contains(x.Option.kind.defName)), "non-raid combat excludes mecha");
            var trader = Parms(faction, 10000); trader.groupKind = PawnGroupKindDefOf.Trader;
            Entry.Assert(!PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints(10000, options, trader).Any(x => Kinds.Contains(x.Option.kind.defName)), "trader guards exclude mecha");
            var player = (Pawn_Immortal)PawnGenerator.GeneratePawn(PawnKindDef.Named("MM_Reaver"), Faction.OfPlayer);
            Entry.Assert(player.Pilot == null && !player.raidPrepared && !player.Combat.Enabled && ((Pawn_Reaver)player).missiles == 0, "player production remains empty cockpit, empty Reaver magazine and manual combat default");
            foreach (var name in Kinds)
            {
                var kind = PawnKindDef.Named(name); var ext = kind.GetModExtension<MechaRaidExtension>();
                Entry.Assert(!kind.allowInMechClusters && !kind.canArriveManhunter && (ext.pilotKind == null || (ext.pilotKind.race.defName == "MNRace" && ext.pilotKind.combatPower == 120)), name + " cluster/manhunter exclusion and Monolyn crew cost verified");
            }
        }
        void NextUnit()
        {
            if (unit != null)
            {
                Detach(unit); saved.Add(unit);
            }
            if (++index == Kinds.Length) { SetupSupport(); return; }
            Entry.Case = Kinds[index]; Entry.Hits = Entry.Shots = Entry.Blinks = 0;
            meleeTicks = orbTicks = 0;
            unit = PawnGenerator.GeneratePawn(PawnKindDef.Named(Kinds[index]), faction); Entry.Unit = unit;
            if (unit is Pawn_Immortal m)
            {
                Entry.Assert(m.Pilot != null && m.Pilot.def.defName == "MNRace" && m.Pilot.Faction == faction
                    && !m.Pilot.Downed && !m.Pilot.Dead && m.pilots.Count == 1 && !m.Pilot.Spawned, "one healthy real Monolyn pilot generated and seated");
                Entry.Assert(m.raidPrepared && m.Light.currentLight == m.LightMax && (!(m is Pawn_Reaver r) || r.missiles == 8), "full onboard light and initial ammunition");
                var pilot = m.Pilot; m.Light.currentLight -= 77; EnemyMechaRaids.Prepare(m);
                Entry.Assert(m.Pilot == pilot && m.Light.currentLight == m.LightMax - 77 && m.pilots.Count == 1, "repeat preparation neither duplicates pilot nor refills resources");
            }
            GenSpawn.Spawn(unit, new IntVec3(38, 0, 60), map);
            lord = LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction, canKidnap: false, canTimeoutOrFlee: false, canSteal: false), map, new[] { unit });
            Entry.Assert(EnemyMechaRaids.MayFight(unit), "real assault lord enables enemy tactics");
            stage = 1; ticks = 0;
        }
        void Detach(Pawn p)
        {
            p.GetLord()?.Notify_PawnLost(p, PawnLostCondition.ExitedMap);
            p.jobs.StopAll();
            if (p.Spawned) p.DeSpawn();
        }
        void SetupSupport()
        {
            Entry.Case = "guardian-support"; Entry.Target.DeSpawn();
            unit = saved.First(p => p.def.defName == "MM_Guardian"); saved.Remove(unit);
            GenSpawn.Spawn(unit, new IntVec3(58, 0, 60), map); guardian = unit.GetComp<CompGuardian>();
            support = (Pawn_Immortal)saved.First(p => p.def.defName == "MM_Immortal"); saved.Remove(support);
            GenSpawn.Spawn(support, new IntVec3(63, 0, 60), map); support.Light.currentLight = 0; support.Shield.currentHitPoints = 20;
            lord = LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction, canKidnap: false, canTimeoutOrFlee: false, canSteal: false), map, new[] { unit, support });
            stage = 2; ticks = 0;
        }
        void LightAndCrew()
        {
            Entry.Case = "light-and-crew";
            Entry.Assert(guardian.totalRestored >= 100 && guardian.raidSupportLight < 600 && support.Shield.currentHitPoints > 100, "enemy Guardian restores allied shields from finite saved onboard reserve");
            var tower = (ThingWithComps)ThingMaker.MakeThing(ThingDef.Named("TowerOfLight")); tower.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(tower, new IntVec3(67, 0, 66), map);
            var distributor = tower.GetComp<CompLightDistributor>(); distributor.UpdateConnection();
            ASELComponent.currentLight = 10000;
            float before = ASELComponent.currentLight, reserve = support.Light.currentLight;
            distributor.RegisterReceiver(support.Receiver); distributor.RegisterReceiverNoSound(support.Receiver);
            support.Receiver.AddDistributor(distributor); support.ReconnectLight();
            AccessTools.Method(typeof(CompLightDistributor), "TransferLight").Invoke(distributor, new object[] { 500f, support.Receiver });
            Entry.Assert(!distributor.registeredReceivers.Contains(support) && support.Receiver.cachedLightDistributors.Count == 0
                && ASELComponent.currentLight == before && support.Light.currentLight == reserve, "enemy chassis cannot register with or drain player's light grid");
            tower.Destroy();
            Pawn pilot = support.Pilot;
            Entry.Assert(support.Eject() && pilot.Spawned && pilot.GetLord() == lord && lord.ownedPawns.Contains(pilot), "ejected crew joins the original assault lord");
            Entry.Assert(!support.Driven && !support.MayWalk, "empty or powerless chassis cannot move");
            Entry.Assert(support.Board(pilot) && support.Pilot == pilot && pilot.GetLord() == null && support.pilots.Count == 1, "same enemy pilot reboards without duplication or a second lord membership");
            Detach(unit); Detach(support); saved.Add(unit); saved.Add(support); unit = null;
            NextArrival();
        }
        void NextArrival()
        {
            foreach (var p in map.mapPawns.AllPawnsSpawned.Where(p => p.Faction != Faction.OfPlayer).ToArray()) Detach(p);
            if (++arrival == 4) { Save(); return; }
            string mode = new[] { "EdgeDropMonolyn", "EdgeDropGroupsMonolyn", "CenterDropMonolyn", "EdgeDropMonolyn" }[arrival];
            Entry.Case = "incident-" + mode + (arrival == 3 ? "-breaching" : "");
            if (!Entry.Target.Spawned) GenSpawn.Spawn(Entry.Target, new IntVec3(112, 0, 60), map);
            priorRaidPawns = map.mapPawns.AllPawnsSpawned.ToList();
            raidParms = StorytellerUtility.DefaultParmsNow(IncidentDefOf.RaidEnemy.category, map);
            raidParms.forced = true; raidParms.faction = faction; raidParms.points = 10000;
            raidParms.raidStrategy = DefDatabase<RaidStrategyDef>.GetNamed(arrival == 3 ? "ImmediateAttackBreachingMonolyn" : "ImmediateAttackMonolynEnemy");
            raidParms.raidArrivalMode = DefDatabase<PawnsArrivalModeDef>.GetNamed(mode);
            raidParms.canKidnap = false; raidParms.canSteal = false; raidParms.canTimeoutOrFlee = false;
            Entry.Assert(IncidentDefOf.RaidEnemy.Worker.TryExecute(raidParms), "real RaidEnemy worker succeeds");
            stage = 3; ticks = 0;
        }
        public override void GameComponentTick()
        {
            if (!Entry.Active || Entry.Cold || !prepared || done) return;
            Run(() => {
                ticks++;
                if (stage == 1 && unit is Pawn_Immortal active)
                {
                    if (unit.CurJobDef == JobDefOf.AttackMelee && unit.Position.DistanceTo(Entry.Target.Position) > active.MinimumWeaponRange) meleeTicks++;
                    if (active.MovementLocked) orbTicks++;
                }
                if (ticks % 300 == 0) Entry.Trace("stage=" + stage + " age=" + ticks + " unit=" + unit?.ThingID + " pos=" + unit?.Position
                    + " job=" + unit?.CurJobDef + " lord=" + unit?.GetLord()?.loadID + " hits=" + Entry.Hits + " blinks=" + Entry.Blinks
                    + " spawned=" + unit?.Spawned + " down=" + unit?.Downed + " holder=" + unit?.ParentHolder + " enemy=" + (unit as Pawn_Immortal)?.Combat.Enemy
                    + " target=" + Entry.Target.Position + "/" + Entry.Target.Downed + "/" + Entry.Target.Spawned);
                if (stage == 1 && ticks >= 2400)
                {
                    if (unit is Pawn_Disruptor d && d.MovementLocked) return;
                    Entry.Assert(Entry.Hits > 0 && unit.Position.x > 50, "natural assault pursuit and actual weapon damage; hits=" + Entry.Hits + " pos=" + unit.Position);
                    Entry.Assert(unit.GetLord() == lord && lord.ownedPawns.Contains(unit), "assault membership survives movement and abilities");
                    if (unit is Pawn_Immortal m) Entry.Assert(m.shotsFired > 0 && m.Driven && m.Light.currentLight < m.LightMax, "real warframe weapon fires with seated pilot and consumes light");
                    if (unit is Pawn_Immortal) Entry.Assert(meleeTicks == 0, "ranged assault does not fall back to native melee chase; ticks=" + meleeTicks);
                    if (unit.def.defName == "MM_Stalker") Entry.Assert(Entry.Blinks > 0, "enemy Stalker uses movement blink");
                    if (unit is Pawn_Disruptor) Entry.Assert(orbTicks > 0, "real Disruptor orb movement lock exercised");
                    NextUnit();
                }
                else if (stage == 2 && ticks >= 600) LightAndCrew();
                else if (stage == 3 && ticks >= 600)
                {
                    var arrived = map.mapPawns.AllPawnsSpawned.Where(p => !priorRaidPawns.Contains(p) && p.Faction == faction).ToList();
                    var machines = arrived.Where(p => Kinds.Contains(p.def.defName)).ToList();
                    Entry.Assert(arrived.Count > 0 && machines.Count > 0, "native arrival spawns mixed raiders including mecha; count=" + machines.Count);
                    Entry.Assert(machines.All(p => p.GetLord() != null && (!(p is Pawn_Immortal m) || (m.Driven && m.Pilot.Faction == faction && m.pilots.Count == 1))), "arrived mecha have raid lords and valid crews");
                    NextArrival();
                }
            });
        }
        string State(Pawn p)
        {
            var m = p as Pawn_Immortal; var g = p.GetComp<CompGuardian>();
            return p.ThingID + "|" + p.Faction.loadID + "|" + m?.Pilot?.ThingID + "|" + m?.raidPrepared
                + "|" + m?.Light.currentLight.ToString("R") + "|" + (m as Pawn_Reaver)?.missiles
                + "|" + g?.raidSupportLight.ToString("R");
        }
        void Save()
        {
            Entry.Case = "save"; Entry.Target.DeSpawn();
            for (int i = 0; i < saved.Count; i++)
            {
                var p = saved[i]; GenSpawn.Spawn(p, new IntVec3(35 + i * 15, 0, 115), map);
                if (p is Pawn_Immortal m) { m.Light.currentLight = 4321 + i; m.autoFire = false; }
                LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction, canKidnap: false, canTimeoutOrFlee: false, canSteal: false), map, new[] { p });
            }
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            File.WriteAllLines(Path.Combine(Entry.Root, "expected-state.txt"), saved.OrderBy(p => p.thingIDNumber).Select(State));
            GameDataSaveLoader.SaveGame("EnemyMechaRaidState");
            Entry.Assert(File.Exists(Path.Combine(GenFilePaths.SaveDataFolderPath, "Saves/EnemyMechaRaidState.rws")), "raid state saved for independent cold process");
            Finish();
        }
        void Cold()
        {
            Entry.Case = "cold";
            var units = map.mapPawns.AllPawnsSpawned.Where(p => Kinds.Contains(p.def.defName)).OrderBy(p => p.thingIDNumber).ToList();
            Entry.Assert(units.Count == 6 && units.Select(State).SequenceEqual(File.ReadAllLines(Path.Combine(Entry.Root, "expected-state.txt"))), "six chassis preserve IDs, faction, pilot IDs, fuel, ammo and support reserve across restart");
            foreach (var m in units.OfType<Pawn_Immortal>())
            {
                Entry.Assert(m.Driven && m.pilots.Count == 1 && !m.Pilot.Spawned && m.Pilot.ParentHolder == m.pilotHolder, m.def.defName + " cockpit survives cold load");
                var oldPilot = m.Pilot; var oldLord = m.GetLord(); var light = m.Light.currentLight;
                EnemyMechaRaids.Prepare(m);
                Entry.Assert(m.Pilot == oldPilot && m.Light.currentLight == light, m.def.defName + " no respawn refill or duplicate crew");
                m.Kill(null);
                Entry.Assert(oldPilot.Spawned && !oldPilot.Dead && oldPilot.GetLord() == oldLord && oldLord.ownedPawns.Contains(oldPilot), m.def.defName + " destroyed chassis releases the same pilot to the original raid");
            }
        }
        void Finish()
        {
            done = true; File.WriteAllText(Path.Combine(Entry.Root, Entry.Failures == 0 ? "complete" : "failed"), "ASSERTION_FAILURES=" + Entry.Failures);
            Application.Quit();
        }
    }
    [HarmonyPatch(typeof(ThinkNode_Subtree), nameof(ThinkNode_Subtree.TryIssueJobPackage))]
    static class DiagnoseSubtree
    {
        static int faults, offmap;
        static void Prefix(ThinkNode_Subtree __instance, ThinkTreeDef ___treeDef, Pawn pawn)
        {
            if (!Entry.Diagnostics) return;
            if (__instance.subtreeNode == null && faults++ < 5)
                Entry.Trace("NULL_SUBTREE pawn=" + pawn.ThingID + " spawned=" + pawn.Spawned + " tree=" + ___treeDef + " parent=" + __instance.parent?.GetType().Name + " key=" + __instance.UniqueSaveKey + " stack=" + Environment.StackTrace);
            if (pawn is Pawn_Immortal && !pawn.Spawned && ___treeDef?.defName == "LordDuty" && offmap++ < 3)
                Entry.Trace("OFFMAP_LORD pawn=" + pawn.ThingID + " holder=" + pawn.ParentHolder + " stack=" + Environment.StackTrace);
        }
    }
    [HarmonyPatch(typeof(Storyteller), nameof(Storyteller.StorytellerTick))]
    static class HoldStoryteller { static bool Prefix() => !Entry.Active; }
    [HarmonyPatch(typeof(GenTemperature), nameof(GenTemperature.GetTemperatureForCell))]
    static class ControlledTemperature
    {
        static bool Prefix(ref float __result) { __result = 21f; return !Entry.Active; }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    static class TargetDamage
    {
        static bool Prefix(Thing __instance, DamageInfo dinfo, ref DamageWorker.DamageResult __result)
        { if (__instance != Entry.Target) return true; if (dinfo.Instigator == Entry.Unit) Entry.Hits++; __result = new DamageWorker.DamageResult(); return false; }
    }
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.JobTrackerTickInterval))]
    static class HoldTarget { static bool Prefix(Pawn ___pawn) => ___pawn != Entry.Target; }
    [HarmonyPatch(typeof(Verb_Shoot), "TryCastShot")]
    static class Shots { static void Postfix(Verb_Shoot __instance, bool __result) { if (__result && __instance.CasterPawn == Entry.Unit) Entry.Shots++; } }
    [HarmonyPatch(typeof(CompStalker), nameof(CompStalker.TryMovementBlink))]
    static class Blinks { static void Postfix(CompStalker __instance, bool __result) { if (__result && __instance.Pawn == Entry.Unit) Entry.Blinks++; } }
}
