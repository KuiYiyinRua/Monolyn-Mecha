using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASEL;
using HarmonyLib;
using Monolyn.Mecha;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace Monolyn.Mecha.LightBlinkTests;

[StaticConstructorOnStartup]
static class Entry
{
    internal static readonly List<float> soundVolumes = new();
    internal static int effects;
    static Entry()
    {
        if (GenCommandLine.CommandLineArgPassed("mm-blink-settings-cold"))
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                string root = GenFilePaths.SaveDataFolderPath;
                if (MechaMod.Settings?.muteBlinkSounds == true)
                {
                    File.WriteAllText(Path.Combine(root, "complete"), "PASS persisted blink setting on cold startup");
                    File.WriteAllText(Path.Combine(root, "assertions.txt"), "PASS persisted blink setting on cold startup\n");
                }
                else File.WriteAllText(Path.Combine(root, "failed"), "Persisted blink setting was not loaded");
                Application.Quit();
            });
        }
        if (!GenCommandLine.CommandLineArgPassed("mm-light-blink")) return;
        var harmony = new Harmony("local.monolyn.mecha.lightblinkprobe");
        harmony.Patch(AccessTools.Method(typeof(SoundStarter), nameof(SoundStarter.PlayOneShot)),
            prefix: new HarmonyMethod(typeof(Entry), nameof(Sound)) { priority = Priority.Last });
        harmony.Patch(AccessTools.Method(typeof(Effecter), nameof(Effecter.Trigger)),
            postfix: new HarmonyMethod(typeof(Entry), nameof(Effect)));
        foreach (string method in new[] { "UnregisterReceiver", "UnregisterReceiverNoSound" })
            harmony.Patch(AccessTools.Method(typeof(CompLightDistributor), method, new[] { typeof(CompLightReceiver) }),
                prefix: new HarmonyMethod(typeof(Entry), nameof(Disconnect)));
    }
    static void Sound(SoundDef soundDef, SoundInfo info)
    {
        if (soundDef?.defName == "MNTransfer" || soundDef?.defName == "MNGateTransfer")
            soundVolumes.Add(info.volumeFactor);
    }
    static void Effect(Effecter __instance)
    {
        if (__instance.def.defName == "BulletTransferA" || __instance.def.defName == "BulletTransferB") effects++;
    }
    static void Disconnect(CompLightDistributor __instance, CompLightReceiver receiver)
    {
        if (receiver?.parent?.def?.defName is "BaptismalFont" or "BloodRegulator" or "DeepExtractor")
            File.AppendAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "disconnects.txt"),
                receiver.parent.def.defName + " spawned=" + receiver.parent.Spawned + " destroyed=" + receiver.parent.Destroyed
                + " from=" + __instance.parent.def.defName + "\n" + new System.Diagnostics.StackTrace() + "\n");
    }
}

public sealed class Probe : GameComponent
{
    readonly bool active = GenCommandLine.CommandLineArgPassed("mm-light-blink");
    string Root => GenFilePaths.SaveDataFolderPath;
    readonly List<Pawn> units = new();
    readonly List<Pawn> energyUnits = new();
    readonly List<ThingWithComps> buildings = new();
    CompLightDistributor distributor;
    Pawn native;
    int stage, startTick, frames;
    bool done;
    public Probe(Game game) { }
    void Check(bool ok, string note)
    {
        File.AppendAllText(Path.Combine(Root, "assertions.txt"), (ok ? "PASS " : "FAIL ") + note + "\n");
        if (!ok) throw new Exception(note);
    }
    Pawn Make(string kind, IntVec3 pos)
    {
        var pawn = PawnGenerator.GeneratePawn(PawnKindDef.Named(kind), Faction.OfPlayer);
        GenSpawn.Spawn(pawn, pos, Find.CurrentMap);
        units.Add(pawn);
        return pawn;
    }
    void SupplyBuildings(string phase)
    {
        foreach (var building in buildings)
        {
            var storage = building.GetComp<CompLightStorage>();
            storage.currentLight = 0;
            float before = ASELComponent.currentLight;
            building.GetComp<CompLightConsumer>().RequestLight();
            Check(storage.currentLight > 0 && ASELComponent.currentLight < before,
                phase + ": native building receives light " + building.def.defName
                    + " bank=" + before + " storage=" + storage.currentLight
                    + " connected=" + distributor.ConnectedWithTower + " towerSpawned=" + distributor.parent.Spawned
                    + " links=" + building.GetComp<CompLightReceiver>().cachedLightDistributors.Count);
        }
    }
    void Setup()
    {
        Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
        var map = Find.CurrentMap;
        map.fogGrid.ClearAllFog();
        foreach (var pawn in map.mapPawns.AllPawnsSpawned.ToArray())
        {
            pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ExitedMap);
            pawn.DeSpawn();
            Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
        }
        foreach (var cell in new CellRect(40, 40, 70, 60))
        {
            foreach (var thing in cell.GetThingList(map).ToArray())
            {
                if (thing.def.destroyable) thing.Destroy();
                else thing.DeSpawn();
            }
            map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
        }
        var tower = (ThingWithComps)ThingMaker.MakeThing(ThingDef.Named("TowerOfLight"));
        tower.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(tower, new IntVec3(60, 0, 60), map);
        distributor = tower.GetComp<CompLightDistributor>();
        ASELComponent.UpdateConnections();
        Check(distributor.ConnectedWithTower, "native tower is connected");
        ASELComponent.maxLight = ASELComponent.currentLight = 200000;
        var defs = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.category == ThingCategory.Building
            && d.HasComp(typeof(CompLightStorage)) && d.HasComp(typeof(CompLightReceiver))
            && d.HasComp(typeof(CompLightConsumer)) && !d.HasComp(typeof(CompLightDistributor))
            && !d.HasComp(typeof(CompLightBuilder))).OrderBy(d => d.defName).Take(3).ToArray();
        Check(defs.Length == 3, "three native building receiver types available");
        int offset = 0;
        var sites = new[] { new IntVec3(48, 0, 55), new IntVec3(60, 0, 46), new IntVec3(73, 0, 55) };
        foreach (var def in defs)
        {
            var building = (ThingWithComps)ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.Steel : null);
            building.SetFaction(Faction.OfPlayer);
            var site = sites[offset++];
            Check(!GenAdj.OccupiedRect(site, Rot4.North, def.size).Cells.Any(c => c.GetEdifice(map) != null),
                "fixture building footprint is clear " + def.defName);
            GenSpawn.Spawn(building, site, map);
            Check(tower.Spawned, "fixture preserves light tower " + def.defName);
            distributor.RegisterReceiverNoSound(building.GetComp<CompLightReceiver>());
            buildings.Add(building);
        }
        SupplyBuildings("before mecha creation");
        int index = 0;
        foreach (var kind in new[] { "MM_Immortal", "MM_Colossus", "MM_Reaver", "MM_Disruptor", "MM_Stalker", "MM_Guardian" })
        {
            var pawn = Make(kind, new IntVec3(51 + index++ * 3, 0, 66));
            if (pawn is Pawn_Immortal warframe)
            {
                warframe.ReconnectLight();
                warframe.Light.currentLight = 0;
                float before = ASELComponent.currentLight;
                warframe.GetComp<CompLightConsumer>().RequestLight();
                Check(warframe.Light.currentLight > 0 && ASELComponent.currentLight < before,
                    kind + " receives native light transfer");
            }
            else
            {
                Check(pawn.needs?.energy != null, kind + " retains native energy need");
                pawn.needs.energy.CurLevel = 0;
                energyUnits.Add(pawn);
            }
            SupplyBuildings("after " + kind);
        }
        var nativeKind = DefDatabase<PawnKindDef>.AllDefsListForReading.First(k => !k.defName.StartsWith("MM_")
            && k.race.HasComp(typeof(Comp_MNC)));
        native = Make(nativeKind.defName, new IntVec3(72, 0, 66));
        native.needs.energy.CurLevel = 0;
        energyUnits.Add(native);
        Check(native.GetComp<Comp_MNC>().autoCharge, "native construct automatic charging enabled");
        File.WriteAllLines(Path.Combine(Root, "loaded-mods.txt"), LoadedModManager.RunningModsListForReading.Select(m => m.PackageId + "|" + m.RootDir));
        startTick = Find.TickManager.TicksGame;
        stage = 1;
        Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
    }
    void TestSounds()
    {
        Check(!MechaMod.Settings.muteBlinkSounds, "blink sound is enabled by default");
        var pawn = units.Single(p => p.def.defName == "MM_Stalker");
        var ability = pawn.abilities.GetAbility(StalkerDefOf.MM_StalkerBlink);
        var blink = ability.CompOfType<CompStalkerBlink>();
        // A real path gives the upstream 1.7 Blink a valid destination to resume.
        pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, new IntVec3(90, 0, 66)), JobCondition.InterruptForced);
        foreach (bool mute in new[] { false, true, false })
        {
            MechaMod.Settings.muteBlinkSounds = mute;
            Entry.soundVolumes.Clear(); Entry.effects = 0;
            var target = pawn.Position + new IntVec3(0, 0, 3);
            int identity = pawn.thingIDNumber;
            blink.Apply(target, LocalTargetInfo.Invalid);
            Check(pawn.Position == target && pawn.thingIDNumber == identity, "real blink retains pawn and reaches target mute=" + mute);
            Check(Entry.effects == 2, "both native visual effecters run mute=" + mute);
            Check(Entry.soundVolumes.Count >= 2 && Entry.soundVolumes.All(v => mute ? v == 0 : v > 0),
                "native blink sound volume follows setting mute=" + mute);
        }
        MechaMod.Settings.muteBlinkSounds = true;
        Entry.soundVolumes.Clear(); Entry.effects = 0;
        var direct = AccessTools.Method(typeof(CompAbilityEffect_SyncBlink), "Blink");
        direct.Invoke(blink, new object[] { new LocalTargetInfo(pawn.Position + new IntVec3(0, 0, 3)) });
        Check(Entry.effects == 2 && Entry.soundVolumes.Count >= 2 && Entry.soundVolumes.All(v => v == 0),
            "upstream 1.7 direct automatic Blink is also muted");
        Entry.soundVolumes.Clear();
        DefDatabase<SoundDef>.GetNamed("MNTransfer").PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
        Check(Entry.soundVolumes.Count == 1 && Entry.soundVolumes[0] > 0,
            "shared transfer sound outside blink is unaffected");
        var mod = LoadedModManager.GetMod<MechaMod>();
        mod.WriteSettings();
        // Re-entering the global Scribe loader inside a live game can rebuild
        // unrelated pending references. Verify the file here and cold-load it separately.
        var file = Directory.GetFiles(Path.Combine(Root, "Config"), "*MechaMod.xml").Single();
        var document = System.Xml.Linq.XDocument.Load(file);
        Check(bool.Parse(document.Descendants("muteBlinkSounds").Single().Value), "setting saved to native mod configuration");
        MechaMod.Settings.muteBlinkSounds = false;
    }
    public override void GameComponentUpdate()
    {
        if (!active || done || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
        try
        {
            if (stage < 2)
                foreach (var window in Find.WindowStack.Windows.Where(w => w.forcePause).ToArray()) window.Close();
            if (stage == 0) Setup();
            else if (stage == 1 && Find.TickManager.TicksGame - startTick >= 1500)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                Snapshot();
                foreach (var pawn in energyUnits)
                    Check(!pawn.Dead && pawn.needs?.energy != null && pawn.needs.energy.CurLevel > 0,
                        pawn.def.defName + " recharges during 1500 real ticks");
                SupplyBuildings("after 1500 real ticks");
                TestSounds();
                MechaMod.Settings.muteBlinkSounds = true;
                Find.WindowStack.Add(new Dialog_ModSettings(LoadedModManager.GetMod<MechaMod>()));
                stage = 2;
            }
            else if (stage == 2)
            {
                if (++frames == 5) ScreenCapture.CaptureScreenshot(Path.Combine(Root, "settings.png"));
                if (frames > 30 && File.Exists(Path.Combine(Root, "settings.png")))
                {
                    Check(true, "native mod settings window rendered");
                    done = true;
                    File.WriteAllText(Path.Combine(Root, "complete"), "PASS LightAndBlink");
                    Application.Quit();
                }
            }
        }
        catch (Exception ex)
        {
            done = true;
            File.WriteAllText(Path.Combine(Root, "failed"), ex.ToString());
            Log.Error("[MM LightBlink] " + ex);
            Application.Quit();
        }
    }

    void Snapshot()
    {
        File.AppendAllText(Path.Combine(Root, "state.txt"), "tick=" + Find.TickManager.TicksGame
            + " bank=" + ASELComponent.currentLight + " connected=" + distributor.ConnectedWithTower
            + " towerSpawned=" + distributor.parent.Spawned + " registered=" + distributor.registeredReceivers.Count + "\n"
            + string.Join("\n", units.Select(p => p.def.defName + " dead=" + p.Dead + " spawned=" + p.Spawned
                + " energy=" + p.needs?.energy?.CurLevel + " job=" + p.CurJobDef?.defName
                + " light=" + p.GetComp<CompLightStorage>()?.currentLight)) + "\n"
            + string.Join("\n", buildings.Select(b => b.def.defName + " spawned=" + b.Spawned + " destroyed=" + b.Destroyed
                + " light=" + b.GetComp<CompLightStorage>().currentLight + " links=" + b.GetComp<CompLightReceiver>().cachedLightDistributors.Count)) + "\n");
    }

    public override void GameComponentTick()
    {
        if (active && stage == 1 && Find.TickManager.TicksGame % 250 == 0) Snapshot();
    }
}
