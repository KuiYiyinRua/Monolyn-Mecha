using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Monolyn.Mecha;
using RimWorld;
using UnityEngine;
using Verse;

namespace MechEnergyTests
{
    public sealed class Probe : GameComponent
    {
        static readonly Harmony observer = new Harmony("local.monolyn.energyprobe");
        static Pawn current;
        static readonly Dictionary<Pawn, float> fills = new Dictionary<Pawn, float>();
        static readonly Dictionary<Pawn, string> labels = new Dictionary<Pawn, string>();
        static readonly Dictionary<Pawn, int> paints = new Dictionary<Pawn, int>();
        static readonly List<Exception> errors = new List<Exception>();
        static bool observing;
        readonly bool active = GenCommandLine.CommandLineArgPassed("mm-energy-test");
        readonly bool baseline = GenCommandLine.CommandLineArgPassed("mm-energy-baseline");
        readonly List<Pawn> units = new List<Pawn>();
        string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        bool setup, done;
        int stage, frame, lastErrors;
        public Probe(Game game) { }
        void Assert(bool ok, string text)
        {
            File.AppendAllText(Path.Combine(Root, "evidence.txt"), (ok ? "PASS " : "FAIL ") + text + "\n");
            if (!ok) throw new Exception(text);
        }
        static void Begin(Pawn pawn)
        {
            if (!observing) return;
            current = pawn;
            if (Event.current.type == EventType.Repaint)
                paints[pawn] = paints.TryGetValue(pawn, out int n) ? n + 1 : 1;
        }
        static void End(Exception __exception)
        {
            if (observing && __exception != null) errors.Add(__exception);
            current = null;
        }
        static void Fill(float fillPercent) { if (current != null) fills[current] = fillPercent; }
        static void Label(string label) { if (current != null) labels[current] = label; }
        void Open()
        {
            Find.WindowStack.TryRemove(typeof(LudeonTK.EditWindow_Log), false);
            Find.MainTabsRoot.SetCurrentTab(DefDatabase<MainButtonDef>.GetNamed("Mechs"));
            frame = Time.frameCount;
            paints.Clear(); labels.Clear(); fills.Clear();
        }
        void Setup()
        {
            setup = true;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            var map = Find.CurrentMap;
            map.fogGrid.ClearAllFog();
            foreach (var pawn in map.mapPawns.AllPawnsSpawned.ToArray()) pawn.DeSpawn();
            var owner = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(owner, new IntVec3(90, 0, 90), map);
            owner.health.AddHediff(HediffDefOf.MechlinkImplant, owner.health.hediffSet.GetBrain());
            var kinds = new[] { "MM_Immortal", "MM_Colossus", "MM_Reaver", "MM_Disruptor", "MM_Stalker", "MM_Guardian", "Mech_Lifter" };
            for (int i = 0; i < kinds.Length; i++)
            {
                var p = PawnGenerator.GeneratePawn(PawnKindDef.Named(kinds[i]), Faction.OfPlayer);
                var cell = new IntVec3(70 + 6 * i, 0, 80);
                foreach (var c in CellRect.CenteredOn(cell, 2))
                {
                    foreach (var t in c.GetThingList(map).ToArray()) if (t.def.destroyable) t.Destroy();
                    map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
                }
                GenSpawn.Spawn(p, cell, map); units.Add(p);
                p.relations.AddDirectRelation(PawnRelationDefOf.Overseer, owner);
                Assert(p.OverseerSubject != null, kinds[i] + " visible in native Mechs table");
                if (p is Pawn_Immortal w)
                {
                    w.Light.currentLight = w.Light.maxLightP * (i / 3f);
                    Assert(p.needs.energy == null, kinds[i] + " retains light-only energy model");
                }
                else { p.needs.energy.CurLevel = p.needs.energy.MaxLevel * .6f; }
            }
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(typeof(Pawn_Immortal).Assembly.Location))
                Assert(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") == File.ReadAllText(Path.Combine(Root, "core.sha256")).Trim(), "loaded exact frozen DLL");
            var windowMethod = AccessTools.Method(typeof(MainTabWindow_Mechs), "DoWindowContents");
            Assert(Harmony.GetPatchInfo(windowMethod).Postfixes.Any(p => p.PatchMethod.DeclaringType.FullName.StartsWith("AncotLibrary.")), "real Ancot window postfix active");
            observer.Patch(AccessTools.Method(typeof(PawnColumnWorker_Energy), "DoCell"),
                prefix: new HarmonyMethod(typeof(Probe), nameof(Begin)) { priority = Priority.First },
                finalizer: new HarmonyMethod(typeof(Probe), nameof(End)));
            observer.Patch(AccessTools.Method(typeof(Widgets), "FillableBar", new[] { typeof(Rect), typeof(float), typeof(Texture2D), typeof(Texture2D), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(Probe), nameof(Fill)));
            observer.Patch(AccessTools.Method(typeof(Widgets), "Label", new[] { typeof(Rect), typeof(string) }),
                prefix: new HarmonyMethod(typeof(Probe), nameof(Label)));
            observing = true;
            Open();
        }
        void CheckDisplay()
        {
            Assert(errors.Count == 0, "no energy-cell exceptions");
            foreach (var p in units)
            {
                float value, max;
                if (p is Pawn_Immortal w) { value = w.Light.currentLight; max = w.Light.maxLightP; }
                else { value = p.needs.energy.CurLevel; max = p.needs.energy.MaxLevel; }
                Assert(paints.TryGetValue(p, out int n) && n >= 5, p.def.defName + " repeatedly repainted");
                Assert(fills.TryGetValue(p, out float fill) && Mathf.Abs(fill - Mathf.Clamp01(value / max)) < .0001f,
                    p.def.defName + " correct actual fill " + fill);
                Assert(labels.TryGetValue(p, out string label) && label == Mathf.RoundToInt(value) + " / " + Mathf.RoundToInt(max),
                    p.def.defName + " correct actual label " + label);
                if (p is Pawn_Immortal) Assert(p.needs.energy == null, p.def.defName + " UI did not add a battery");
            }
        }
        public override void GameComponentUpdate()
        {
            if (!active || done || Current.ProgramState != ProgramState.Playing) return;
            try
            {
                if (!setup) { Setup(); return; }
                if (Time.frameCount - frame < 40) return;
                if (baseline)
                {
                    Assert(errors.Count > lastErrors && errors.Skip(lastErrors).All(e => e is NullReferenceException),
                        "baseline reproduces vanilla energy null dereference; Ancot postfix " + (stage == 0 ? "enabled" : "removed"));
                    File.AppendAllText(Path.Combine(Root, "expected-exceptions.txt"), errors.Last() + "\n");
                    if (stage++ == 0)
                    {
                        var method = AccessTools.Method(typeof(MainTabWindow_Mechs), "DoWindowContents");
                        foreach (var patch in Harmony.GetPatchInfo(method).Postfixes.Where(p => p.PatchMethod.DeclaringType.FullName.StartsWith("AncotLibrary.")).ToArray())
                            observer.Unpatch(method, patch.PatchMethod);
                        lastErrors = errors.Count;
                        Find.MainTabsRoot.SetCurrentTab(null); Open(); return;
                    }
                    Finish("EXPECTED_BASELINE_FAILURE_WITH_AND_WITHOUT_ANCOT_POSTFIX"); return;
                }
                if (stage == 0)
                {
                    CheckDisplay();
                    ScreenCapture.CaptureScreenshot(Path.Combine(Root, "mechs-energy.png"));
                    stage = 1; frame = Time.frameCount; return;
                }
                if (stage == 1)
                {
                    // Values change while paused, so presentation must not wait for a game tick.
                    foreach (var w in units.OfType<Pawn_Immortal>()) w.Light.currentLight = w.Light.maxLightP * .25f;
                    Find.MainTabsRoot.SetCurrentTab(null); Open(); stage = 2; return;
                }
                if (stage == 2)
                {
                    CheckDisplay();
                    ScreenCapture.CaptureScreenshot(Path.Combine(Root, "mechs-energy-updated.png"));
                    GameDataSaveLoader.SaveGame("MechEnergy");
                    stage = 3; frame = Time.frameCount; return;
                }
                Assert(File.Exists(Path.Combine(Root, "mechs-energy-updated.png")), "real UI screenshots completed");
                Finish("PASS_MECH_ENERGY");
            }
            catch (Exception e)
            {
                done = true; File.WriteAllText(Path.Combine(Root, "failed"), e.ToString()); Application.Quit();
            }
        }
        void Finish(string result)
        {
            done = true; observing = false;
            File.WriteAllText(Path.Combine(Root, "complete"), result); Application.Quit();
        }
    }
}
