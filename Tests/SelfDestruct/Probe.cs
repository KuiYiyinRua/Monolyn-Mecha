using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Monolyn.Mecha;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace SelfDestructTests
{
    [StaticConstructorOnStartup]
    static class Entry
    {
        static bool requested;
        static Entry()
        {
            if (!GenCommandLine.CommandLineArgPassed("mm-self-destruct-test")) return;
            var harmony = new Harmony("local.mecha.selfdestruct.test");
            harmony.Patch(AccessTools.Method(typeof(WorldGenerator), "GenerateWorld"),
                prefix: new HarmonyMethod(typeof(Entry), nameof(Push)), finalizer: new HarmonyMethod(typeof(Entry), nameof(Pop)));
            if (GenCommandLine.CommandLineArgPassed("mm-self-destruct-cold"))
                harmony.Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(Entry), nameof(Load)));
        }
        static void Push() => Rand.PushState(261018);
        static void Pop() => Rand.PopState();
        static void Load()
        {
            if (requested || LongEventHandler.AnyEventNowOrWaiting || Current.ProgramState != ProgramState.Entry) return;
            requested = true; GameDataSaveLoader.LoadGame("SelfDestructState");
        }
    }
    public sealed class Probe : GameComponent
    {
        readonly bool active = GenCommandLine.CommandLineArgPassed("mm-self-destruct-test");
        readonly bool cold = GenCommandLine.CommandLineArgPassed("mm-self-destruct-cold");
        string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        List<Pawn> pilots = new List<Pawn>();
        List<IntVec3> landings = new List<IntVec3>();
        List<Thing> targets = new List<Thing>();
        int stage, tick;
        bool done, coldChecked;
        public Probe(Game game) { }
        void Assert(bool ok, string note)
        {
            File.AppendAllText(Path.Combine(Root, "evidence.txt"), (ok ? "PASS " : "FAILED ") + note + "\n");
            if (!ok) throw new Exception(note);
        }
        Pawn Make(string kind, IntVec3 cell)
        { var p = PawnGenerator.GeneratePawn(PawnKindDef.Named(kind), Faction.OfPlayer); GenSpawn.Spawn(p, cell, Find.CurrentMap); return p; }
        Dialog_MessageBox Open(Pawn_Immortal unit)
        {
            unit.GetGizmos().OfType<Command_Action>().Single(g => g.defaultLabel == "MM_SelfDestruct".Translate()).action();
            var dialog = Find.WindowStack.Windows.OfType<Dialog_MessageBox>().Last();
            Assert(dialog.buttonADestructive && dialog.text.ToString().Contains("80") && dialog.text.ToString().Contains("8"), "destructive dialog explains damage");
            return dialog;
        }
        void Setup()
        {
            var map = Find.CurrentMap; map.fogGrid.ClearAllFog();
            foreach (var p in map.mapPawns.AllPawnsSpawned.ToArray())
            { p.DeSpawn(); Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.KeepForever); }
            foreach (var cell in new CellRect(20, 20, 150, 120))
            {
                foreach (var t in cell.GetThingList(map).ToArray()) { if (t.def.destroyable) t.Destroy(); else t.DeSpawn(); }
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil); map.roofGrid.SetRoof(cell, null);
            }
            using (var sha = System.Security.Cryptography.SHA256.Create())
                Assert(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(typeof(Pawn_Immortal).Assembly.Location))).Replace("-", "") == File.ReadAllText(Path.Combine(Root, "core.sha256")).Trim(), "loaded candidate hash");
            string[] names = { "MM_Immortal", "MM_Colossus", "MM_Reaver", "MM_Disruptor" };
            for (int i = 0; i < names.Length; i++)
            {
                var pos = new IntVec3(40 + i * 30, 0, 40);
                var unit = (Pawn_Immortal)Make(names[i], pos); unit.autoFire = false;
                var pilot = Make("Colonist", pos + IntVec3.North); pilot.drafter.Drafted = true;
                pilot.inventory.innerContainer.TryAdd(ThingMaker.MakeThing(ThingDefOf.Steel));
                var cargo = ThingMaker.MakeThing(ThingDefOf.Steel); unit.inventory.innerContainer.TryAdd(cargo);
                Assert(unit.Board(pilot), names[i] + " boarded");
                var dest = pos + IntVec3.North * 50;
                Assert(MechaSelfDestruct.ValidLanding(unit, dest) && !MechaSelfDestruct.ValidLanding(unit, dest + IntVec3.North), "50 inclusive, 51 rejected");
                var wall = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel), pos + IntVec3.East * 5, map);
                Assert(!MechaSelfDestruct.ValidLanding(unit, wall.Position) && !MechaSelfDestruct.ValidLanding(unit, pos + IntVec3.East * 10), "blocked landing and line of sight rejected"); wall.Destroy();
                Assert(!MechaSelfDestruct.ValidLanding(unit, IntVec3.Invalid) && !MechaSelfDestruct.ValidLanding(unit, new IntVec3(-1, 0, -1)), "invalid/off-map rejected");
                MechaSelfDestruct.Execute(map, unit, pilot.thingIDNumber + 1, dest);
                MechaSelfDestruct.Execute(map, unit, pilot.thingIDNumber, dest + IntVec3.North);
                Assert(!unit.Dead && unit.Pilot == pilot && !cargo.Destroyed, "stale pilot and invalid target cause no mutation");
                Open(unit).Close(); Assert(!unit.Dead && unit.Pilot == pilot, "dialog cancel safe");
                var dialog = Open(unit); dialog.buttonAAction(); dialog.Close();
                Assert(Find.Targeter.IsTargeting && !unit.Dead, "confirmation begins targeting without explosion");
                Find.Targeter.StopTargeting(); Assert(!unit.Dead, "target cancel safe");
                for (int d = 7; d <= 9; d++)
                {
                    var target = ThingMaker.MakeThing(ThingDef.Named("MM_TestTarget")); target.SetFaction(Faction.OfPlayer);
                    GenSpawn.Spawn(target, pos + IntVec3.West * d, map); targets.Add(target);
                }
                dialog = Open(unit); dialog.buttonAAction(); dialog.Close();
                var action = (Action<LocalTargetInfo>)AccessTools.Field(typeof(Targeter), "action").GetValue(Find.Targeter);
                action(dest); Find.Targeter.StopTargeting();
                Assert(unit.Dead && unit.Destroyed && unit.Corpse == null && !map.listerThings.AllThings.OfType<Corpse>().Any(c => c.InnerPawn == unit), names[i] + " immediately dead, no corpse");
                Assert(cargo.Destroyed && pilot.inventory.innerContainer.Count == 1 && !pilot.Dead, "cargo destroyed, pilot possessions preserved");
                var flyer = map.listerThings.AllThings.OfType<PawnFlyer>().Single(f => f.FlyingPawn == pilot);
                Assert(pilot.ParentHolder == flyer && !pilot.Spawned && flyer.DestinationPos.ToIntVec3() == dest, "real pilot flying to selected destination");
                var explosion = map.listerThings.AllThings.OfType<Explosion>().Single(e => e.Position == pos);
                Assert(explosion.radius == 8 && explosion.damAmount == 80 && Mathf.Approximately(explosion.armorPenetration, .8f) && !explosion.damageFalloff, "blast radius/damage/AP and no falloff");
                int count = map.listerThings.AllThings.OfType<Explosion>().Count();
                MechaSelfDestruct.Execute(map, unit, pilot.thingIDNumber, dest);
                Assert(map.listerThings.AllThings.OfType<Explosion>().Count() == count, "duplicate command cannot explode twice");
                pilots.Add(pilot); landings.Add(dest);
            }
            var owner = Make("Colonist", new IntVec3(150, 0, 120));
            owner.health.AddHediff(HediffDefOf.MechlinkImplant, owner.health.hediffSet.GetBrain());
            owner.health.AddHediff(DefDatabase<HediffDef>.GetNamed("MM_TestBandwidth")); owner.drafter.Drafted = true;
            var empty = (Pawn_Immortal)Make("MM_Immortal", new IntVec3(150, 0, 105)); MechaAutonomy.Bind(map, empty, owner);
            Assert(owner.mechanitor.UsedBandwidth == 4, "autonomous chassis bound");
            var emptyDialog = Open(empty); emptyDialog.buttonAAction(); emptyDialog.Close();
            Assert(empty.Dead && empty.Destroyed && !Find.Targeter.IsTargeting && owner.mechanitor.UsedBandwidth == 0, "empty confirmation detonates and releases bandwidth");
            stage = 1; tick = Find.TickManager.TicksGame;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            GameDataSaveLoader.SaveGame("SelfDestructState");
            File.WriteAllText(Path.Combine(Root, "expected-state.txt"), Snapshot());
            Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
        }
        string Snapshot() => string.Join("\n", pilots.Select((p, i) => p.ThingID + ":" + p.ParentHolder?.GetType().Name + ":" + landings[i] + ":" + p.Dead + ":" + p.inventory.innerContainer.TotalStackCount));
        public override void FinalizeInit() { if (active && cold) Find.TickManager.CurTimeSpeed = TimeSpeed.Paused; }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref pilots, "sdPilots", LookMode.Reference);
            Scribe_Collections.Look(ref landings, "sdLandings", LookMode.Value);
            Scribe_Collections.Look(ref targets, "sdTargets", LookMode.Reference);
            Scribe_Values.Look(ref stage, "sdStage"); Scribe_Values.Look(ref tick, "sdTick");
        }
        public override void GameComponentUpdate()
        {
            if (!active || done || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            try
            {
                if (File.Exists(Path.Combine(Root, "quit"))) { Application.Quit(); return; }
                if (cold && !coldChecked)
                {
                    coldChecked = true; string actual = Snapshot(); File.WriteAllText(Path.Combine(Root, "actual-state.txt"), actual);
                    Assert(actual == File.ReadAllText(Path.Combine(Root, "expected-state.txt")), "cold flight holder/pilot/inventory exact persistence");
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
                }
                if (cold && coldChecked) Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
                if (stage == 0) Setup();
                else if (Find.TickManager.TicksGame - tick >= 400)
                {
                    for (int i = 0; i < pilots.Count; i++)
                    {
                        Assert(!pilots[i].Dead && pilots[i].Spawned && pilots[i].Position == landings[i] && pilots[i].Drafted, "pilot " + i + " landed alive and drafted: " + pilots[i].Position + " alive=" + !pilots[i].Dead + " spawned=" + pilots[i].Spawned + " drafted=" + pilots[i].Drafted);
                        Assert(pilots[i].inventory.innerContainer.TotalStackCount == 1, "pilot inventory survived flight");
                        Assert(targets[i * 3].HitPoints < targets[i * 3].MaxHitPoints && targets[i * 3 + 1].HitPoints < targets[i * 3 + 1].MaxHitPoints, "friendly targets at 7 and 8 hit");
                        Assert(targets[i * 3 + 2].HitPoints == targets[i * 3 + 2].MaxHitPoints, "target at 9 untouched");
                    }
                    Assert(!Find.CurrentMap.listerThings.AllThings.OfType<PawnFlyer>().Any(), "no orphan flight holders");
                    File.WriteAllText(Path.Combine(Root, "complete"), "PASS"); done = true; Application.Quit();
                }
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(Root, "failed"), e.ToString()); done = true; Log.Error(e.ToString()); Application.Quit(); }
        }
    }
}
