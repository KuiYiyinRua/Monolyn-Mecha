using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using Monolyn.Mecha;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MonolynMechaVersionTests;

[StaticConstructorOnStartup]
static class FunctionalEntry
{
    static bool requested;
    static FunctionalEntry()
    {
        if (!GenCommandLine.CommandLineArgPassed("mm17-functional")) return;
        var harmony = new Harmony("local.monolyn.mecha.functional17");
        harmony.Patch(AccessTools.Method(typeof(WorldGenerator), nameof(WorldGenerator.GenerateWorld)),
            prefix: new HarmonyMethod(typeof(FunctionalEntry), nameof(Push)),
            finalizer: new HarmonyMethod(typeof(FunctionalEntry), nameof(Pop)));
        if (GenCommandLine.CommandLineArgPassed("mm17-cold"))
            harmony.Patch(AccessTools.Method(typeof(Root_Entry), "Update"),
                postfix: new HarmonyMethod(typeof(FunctionalEntry), nameof(Load)));
    }
    static void Push() => Rand.PushState(261021);
    static void Pop() => Rand.PopState();
    static void Load()
    {
        if (requested || LongEventHandler.AnyEventNowOrWaiting || Current.ProgramState != ProgramState.Entry) return;
        requested = true;
        GameDataSaveLoader.LoadGame("Mecha17State");
    }
}

public sealed class FunctionalProbe : GameComponent
{
    readonly bool active = GenCommandLine.CommandLineArgPassed("mm17-functional");
    readonly bool cold = GenCommandLine.CommandLineArgPassed("mm17-cold");
    string Root => GenFilePaths.SaveDataFolderPath;
    List<Pawn> units = new();
    List<Thing> targets = new();
    List<IntVec3> destinations = new();
    Pawn owner, pilot;
    int stage, tick, frames;
    bool done, checkedCold;
    public FunctionalProbe(Game game) { }
    void Assert(bool ok, string note)
    {
        File.AppendAllText(Path.Combine(Root, "assertions.txt"), (ok ? "PASS " : "FAIL ") + note + "\n");
        if (!ok) throw new Exception(note);
    }
    void Phase(int next)
    {
        stage = next; tick = Find.TickManager.TicksGame; frames = 0;
        Log.Message("[MM17] STAGE=" + stage + " tick=" + tick);
    }
    Pawn Make(string name, IntVec3 position)
    {
        var p = PawnGenerator.GeneratePawn(PawnKindDef.Named(name), Faction.OfPlayer);
        GenSpawn.Spawn(p, position, Find.CurrentMap);
        return p;
    }
    void BindMenu(Pawn_Immortal pawn)
    {
        Find.Selector.ClearSelection(); Find.Selector.Select(pawn);
        for (int i = 0; i < 3; i++)
        {
            pawn.GetGizmos().OfType<Command_Action>().Single(g => g.defaultLabel == "MM_Autonomy".Translate()).action();
            var menu = Find.WindowStack.Windows.OfType<FloatMenu>().Last();
            var options = (List<FloatMenuOption>)AccessTools.Field(typeof(FloatMenu), "options").GetValue(menu);
            var option = options.Single(o => o.Label.StartsWith(owner.LabelShortCap));
            Assert(option.action != null, pawn.def.defName + " binding menu available round=" + i);
            if (i == 2) option.action();
            menu.Close();
        }
    }
    void Setup()
    {
        var map = Find.CurrentMap;
        map.fogGrid.ClearAllFog();
        var area = new CellRect(30, 40, 100, 90);
        foreach (var p in map.mapPawns.AllPawnsSpawned.ToArray())
        {
            p.GetLord()?.Notify_PawnLost(p, PawnLostCondition.ExitedMap);
            p.DeSpawn();
            Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.KeepForever);
        }
        foreach (var cell in area)
        {
            foreach (var thing in cell.GetThingList(map).ToArray())
            {
                if (thing.def.destroyable) thing.Destroy();
                else { thing.DeSpawn(); GenSpawn.Spawn(thing, new IntVec3(15, 0, 15), map); }
            }
            map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
            map.roofGrid.SetRoof(cell, null);
        }
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(Pawn_Immortal).Assembly.Location)));
        File.WriteAllText(Path.Combine(Root, "loaded-core.sha256"), hash);
        Assert(VersionControl.CurrentVersionStringWithRev.StartsWith("1.7."), "game is 1.7: " + VersionControl.CurrentVersionStringWithRev);
        Assert(typeof(Pawn_Immortal).Assembly.GetName().Version.ToString() == "0.9.4.0", "loaded 1.7 candidate 0.9.4.0");
        owner = Make("Colonist", new IntVec3(76, 0, 85));
        owner.health.AddHediff(HediffDefOf.MechlinkImplant, owner.health.hediffSet.GetBrain());
        owner.health.AddHediff(DefDatabase<HediffDef>.GetNamed("MM_TestBandwidth"));
        owner.drafter.Drafted = true;
        var names = new[] { "MM_Immortal", "MM_Colossus", "MM_Reaver", "MM_Disruptor", "MM_Stalker", "MM_Guardian" };
        for (int i = 0; i < names.Length; i++)
        {
            var pawn = Make(names[i], new IntVec3(56 + 8 * i, 0, 80));
            units.Add(pawn);
            if (pawn is Pawn_Immortal w)
            {
                w.Light.currentLight = w.LightMax; w.autoFire = false;
                if (w is Pawn_Reaver r) { r.missiles = 10; r.autoFabricate = false; }
                BindMenu(w);
                Assert(w.Autonomous && w.GetOverseer() == owner && w.GetMechControlGroup() != null, names[i] + " bound and grouped");
            }
            else
            {
                pawn.relations.AddDirectRelation(PawnRelationDefOf.Overseer, owner);
                pawn.needs.energy.CurLevel = pawn.needs.energy.MaxLevel;
                pawn.GetComp<CompStalker>()?.SetAutoCombat(false);
                pawn.GetComp<CompStalker>()?.SetAutoBlink(false);
                pawn.GetComp<CompGuardian>()?.SetAutoCombat(false);
                pawn.GetComp<CompGuardian>()?.SetAutoRestore(false);
            }
            pawn.drafter.Drafted = true;
            Assert(pawn.Drafted && !pawn.Dead && pawn.GetGizmos().Any(), names[i] + " draft/gizmos available");
            Assert(!string.IsNullOrEmpty(pawn.GetInspectString()), names[i] + " inspection text available");
            var body = pawn.ageTracker.CurKindLifeStage.bodyGraphicData.Graphic;
            Assert(body.MatAt(Rot4.South, pawn).mainTexture != null && body.MatAt(Rot4.East, pawn).mainTexture != null
                && body.MatAt(Rot4.North, pawn).mainTexture != null && pawn.def.uiIcon != null, names[i] + " three body views and icon resolve");
            var steel = ThingMaker.MakeThing(ThingDefOf.Steel); steel.stackCount = 7;
            pawn.inventory.innerContainer.TryAdd(steel);
            var dest = pawn.Position + IntVec3.South * 5;
            destinations.Add(dest);
            if (pawn is Pawn_Immortal mech) MechaActions.Move(map, mech, dest);
            else pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, dest));
            Assert(pawn.CurJobDef == JobDefOf.Goto, names[i] + " accepts real move job");
        }
        Assert(owner.mechanitor.UsedBandwidth == 25, "six units consume 25 bandwidth");
        var field = units[5].GetComp<CompGuardianField>();
        field.Seeds.Refuel(30); field.Seeds.allowAutoRefuel = false;
        field.Activate();
        Assert(field.Active && field.remainingTicks == 1800 && field.Seeds.Fuel == 25, "Guardian shield starts for 5 seeds / 1800 ticks");
        foreach (var name in names)
            Assert(DefDatabase<ASEL.MonolynRecipeDef>.AllDefs.Any(r => r.pawnKindDef?.defName == name), name + " production recipe resolves");
        Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
        Phase(1);
    }
    void StartCombat()
    {
        var faction = Find.FactionManager.AllFactions.First(f => !f.IsPlayer && f.HostileTo(Faction.OfPlayer) && f.def.humanlikeFaction);
        for (int i = 0; i < units.Count; i++)
        {
            var pawn = units[i];
            Assert(pawn.Position == destinations[i], pawn.def.defName + " reached movement destination");
            var target = ThingMaker.MakeThing(ThingDef.Named("MM_TestTarget"));
            target.SetFaction(faction);
            GenSpawn.Spawn(target, pawn.Position + IntVec3.North * 16, pawn.Map);
            targets.Add(target);
            if (pawn is Pawn_Immortal w)
            {
                MechaActions.Attack(w.Map, w, target);
                Assert(w.attackTarget == target, pawn.def.defName + " real attack command accepted");
            }
            else pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.AttackStatic, target));
        }
        Find.Selector.ClearSelection(); Find.Selector.Select(units[3]); CameraJumper.TryJump(units[3]);
        Phase(2);
    }
    void RecallAndBoard()
    {
        for (int i = 0; i < units.Count; i++)
        {
            var pawn = units[i];
            Assert(targets[i].HitPoints < targets[i].MaxHitPoints, pawn.def.defName + " weapon hit target hp=" + targets[i].HitPoints);
            if (pawn is Pawn_Immortal w) { MechaActions.Stop(w.Map, w); w.Light.currentLight = w.LightMax; }
            else { pawn.jobs.StopAll(); pawn.needs.energy.CurLevel = pawn.needs.energy.MaxLevel; }
        }
        // Wait for already launched Disruptor orbs to end before recalling.
        Phase(3);
    }
    void TestRecall()
    {
        for (int i = 0; i < units.Count; i++)
        {
            var pawn = units[i];
            if (pawn is Pawn_Immortal w) w.Light.currentLight = w.LightMax;
            else pawn.needs.energy.CurLevel = pawn.needs.energy.MaxLevel;
            Assert(MechaRecall.CanRecall(pawn, owner), pawn.def.defName + " recall allowed");
            MechaRecall.Command(pawn).action();
            Assert(pawn.Position.InHorDistOf(owner.Position, 8f) && MechaRecall.Clear(pawn, pawn.Map, pawn.Position, pawn.Rotation), pawn.def.defName + " recalled to safe footprint");
            Assert(Math.Abs(MechaRecall.Energy(pawn) - .7f) < .0001f, pawn.def.defName + " recall consumed exactly 30 percent");
            Assert(pawn.inventory.innerContainer.TotalStackCount == 7, pawn.def.defName + " recall retained cargo");
        }
        var immortal = (Pawn_Immortal)units[0];
        pilot = Make("Colonist", immortal.Position + IntVec3.South);
        MechaActions.OrderBoard(immortal.Map, immortal, pilot);
        Assert(pilot.CurJobDef.defName == "MM_Board", "real pilot boarding job accepted");
        Phase(4);
    }
    string Snapshot()
    {
        return string.Join("\n", units.OrderBy(p => p.thingIDNumber).Select(p =>
        {
            var w = p as Pawn_Immortal; var f = p.GetComp<CompGuardianField>();
            return p.ThingID + "|" + p.Position + "|" + p.GetOverseer()?.ThingID + "|" + w?.Pilot?.ThingID
                + "|" + p.inventory.innerContainer.TotalStackCount + "|" + p.Drafted
                + "|" + (w?.Light.currentLight ?? p.needs.energy.CurLevel).ToString("R", CultureInfo.InvariantCulture)
                + "|" + w?.Shield.currentHitPoints + "|" + (p as Pawn_Reaver)?.missiles
                + "|" + f?.Seeds.Fuel + "|" + f?.remainingTicks;
        }));
    }
    void Save()
    {
        var immortal = (Pawn_Immortal)units[0];
        Assert(immortal.Pilot == pilot && !pilot.Spawned && immortal.Driven, "pilot seated through native boarding job");
        Assert(immortal.GetOverseer() == null && owner.mechanitor.UsedBandwidth == 21, "boarding releases 4 bandwidth");
        Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
        var field = units[5].GetComp<CompGuardianField>();
        field.remainingTicks = 0; field.Activate();
        Assert(field.Active, "active Guardian field will be saved");
        Find.Selector.ClearSelection(); Find.Selector.Select(units[5]); CameraJumper.TryJump(units[5]);
        Phase(5);
    }
    void Complete(string result)
    {
        File.WriteAllText(Path.Combine(Root, "complete"), result);
        Log.Message("[MM17] " + result);
        done = true; Application.Quit();
    }
    public override void FinalizeInit()
    {
        if (active && cold) Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
    }
    public override void ExposeData()
    {
        Scribe_Collections.Look(ref units, "mm17Units", LookMode.Reference);
        Scribe_References.Look(ref owner, "mm17Owner");
        Scribe_References.Look(ref pilot, "mm17Pilot");
    }
    public override void GameComponentUpdate()
    {
        if (!active || done || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
        try
        {
            int now = Find.TickManager.TicksGame;
            if (cold)
            {
                if (!checkedCold)
                {
                    checkedCold = true;
                    string actual = Snapshot(); File.WriteAllText(Path.Combine(Root, "actual-state.txt"), actual);
                    Assert(actual == File.ReadAllText(Path.Combine(Root, "expected-state.txt")), "cold load exact light/shield/ammo/cargo/pilot/overseer/field snapshot");
                    var immortal = (Pawn_Immortal)units[0];
                    Assert(immortal.Pilot == pilot && immortal.Driven, "saved pilot remains operational");
                    MechaActions.Eject(immortal.Map, immortal);
                    Assert(pilot.Spawned && !pilot.Dead && immortal.Pilot == null, "pilot can disembark after cold load");
                    tick = now; Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                }
                if (now - tick >= 1000) Complete("PASS_COLD_1000_TICKS");
                return;
            }
            if (stage == 0) Setup();
            else if (stage == 1 && now - tick >= 600) StartCombat();
            else if (stage == 2 && now - tick >= 1200) RecallAndBoard();
            else if (stage == 3 && now - tick >= 360) TestRecall();
            else if (stage == 4 && now - tick >= 300) Save();
            else if (stage == 5 && ++frames == 30)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(Root, "GuardianField.png"));
                File.WriteAllText(Path.Combine(Root, "expected-state.txt"), Snapshot());
                GameDataSaveLoader.SaveGame("Mecha17State");
            }
            else if (stage == 5 && frames >= 70) Complete("PASS_FUNCTIONAL");
            if (stage != 5 && now - tick > 4000) throw new Exception("Stage timeout " + stage);
        }
        catch (Exception e)
        {
            done = true; File.WriteAllText(Path.Combine(Root, "failed"), e.ToString());
            Log.Error("[MM17] FAILED " + e); Application.Quit();
        }
    }
}
