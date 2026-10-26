using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using Monolyn.Mecha;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace CaravanRidingTests
{
    [StaticConstructorOnStartup]
    static class Setup
    {
        static Setup()
        {
            if (!GenCommandLine.CommandLineArgPassed("mm-riding-test")) return;
            var harmony = new Harmony("local.monolyn.ridingprobe");
            harmony.Patch(AccessTools.Method(typeof(WorldGenerator), "GenerateWorld"),
                prefix: new HarmonyMethod(typeof(Setup), nameof(Push)),
                finalizer: new HarmonyMethod(typeof(Setup), nameof(Pop)));
            harmony.CreateReversePatcher(AccessTools.Method(typeof(CaravanTicksPerMoveUtility), "GetTicksPerMove",
                new[] { typeof(List<Pawn>), typeof(float), typeof(float), typeof(bool), typeof(StringBuilder) }),
                new HarmonyMethod(typeof(Setup), nameof(VanillaTicks))).Patch();
        }
        static void Push(ref string seedString) { seedString = "CaravanRiding261026"; Rand.PushState(261026); }
        static void Pop() => Rand.PopState();
        public static int VanillaTicks(List<Pawn> pawns, float massUsage, float massCapacity, bool isShuttle, StringBuilder explanation)
            => throw new NotImplementedException("Reverse patch not installed");
    }

    // Isolated runtime regression; this assembly never ships with the mod.
    public sealed class Probe : GameComponent
    {
        readonly bool active = GenCommandLine.CommandLineArgPassed("mm-riding-test");
        readonly bool baseline = GenCommandLine.CommandLineArgPassed("mm-riding-baseline");
        string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        bool done;
        int passed, failures;
        Map map;
        PlanetTile destination;
        public Probe(Game game) { }
        void Check(bool ok, string message, bool regression = false)
        {
            bool expected = !ok && baseline && regression;
            string line = (ok ? "PASS " : expected ? "EXPECTED_BASELINE_FAILURE " : "FAIL ") + message;
            File.AppendAllText(Path.Combine(Root, "evidence.txt"), line + "\n");
            Log.Message("MM_RIDING " + line);
            if (expected) failures++;
            else if (!ok) throw new Exception(message);
            else passed++;
        }
        Pawn NewPawn(string kind)
        {
            var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDef.Named(kind), Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            foreach (var hediff in pawn.health.hediffSet.hediffs.ToArray()) pawn.health.RemoveHediff(hediff);
            if (pawn is Pawn_Immortal warframe) warframe.Light.currentLight = warframe.LightMax;
            return pawn;
        }
        List<TransferableOneWay> Transfers(IEnumerable<Pawn> pawns, int selected)
            => pawns.Select(p => { var t = new TransferableOneWay(); t.things.Add(p); t.AdjustTo(selected); return t; }).ToList();
        void SpeedCase(string name, List<Pawn> pawns, int expected, bool changed)
        {
            var explanation = new StringBuilder();
            int actual = CaravanTicksPerMoveUtility.GetTicksPerMove(pawns, 0f, 1000f, false, explanation);
            Check(actual == expected, name + " native ticks expected=" + expected + " actual=" + actual, changed);
            Check(CaravanTicksPerMoveUtility.GetTicksPerMove(pawns, 900f, 1000f) == actual,
                name + " cargo does not alter fixed mechanical base speed");
            float perDay = 60000f / expected;
            var selected = Transfers(pawns, 1);
            var preview = new StringBuilder();
            float ui = TilesPerDayCalculator.ApproxTilesPerDay(selected, 0f, 1000f, map.Tile, destination, false, preview);
            Check(Mathf.Abs(ui - perDay) < 0.001f, name + " formation speed=" + ui + " expected=" + perDay, true);
            Check(Mathf.Abs(TilesPerDayCalculator.ApproxTilesPerDayLeftAfterTransfer(Transfers(pawns, 0), 0f, 1000f,
                map.Tile, destination, false) - perDay) < 0.001f, name + " remaining caravan preview", true);
            Check(Mathf.Abs(TilesPerDayCalculator.ApproxTilesPerDay(pawns.Select(p => new ThingCount(p, 1)).ToList(),
                0f, 1000f, map.Tile, destination, false) - perDay) < 0.001f, name + " trade preview", true);
            if (changed)
                Check(explanation.ToString().Contains("MultiplierFromRiddenAnimals".Translate().ToString()),
                    name + " explanation includes riding factor", true);
            File.AppendAllText(Path.Combine(Root, "explanations.txt"), name + "\n" + explanation + "\nUI:\n" + preview + "\n\n");
        }
        void Run()
        {
            map = Find.CurrentMap;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(typeof(Pawn_Immortal).Assembly.Location))
                Check(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") == File.ReadAllText(Path.Combine(Root, "core.sha256")).Trim(),
                    "loaded frozen candidate " + typeof(Pawn_Immortal).Module.ModuleVersionId);
            var neighbors = new List<PlanetTile>(); Find.WorldGrid.GetTileNeighbors(map.Tile, neighbors);
            destination = neighbors.First(t => !Find.World.Impassable(t));
            var rider = NewPawn("Colonist");
            var second = NewPawn("Colonist");
            var giant = (Pawn_Colossus)NewPawn("MM_Colossus");
            var extra = NewPawn("MM_Colossus");
            Check(giant.IsCaravanRideable() && Mathf.Approximately(giant.GetStatValue(StatDefOf.CaravanRidingSpeedFactor), 1.6f), "native Colossus riding eligibility and 160% stat");
            SpeedCase("one rider / Colossus", new List<Pawn> { rider, giant }, 1071, true);
            SpeedCase("two riders / Colossus", new List<Pawn> { rider, second, giant }, 1319, true);
            SpeedCase("one rider / two Colossi", new List<Pawn> { rider, giant, extra }, 1071, true);
            SpeedCase("no rider / Colossus", new List<Pawn> { giant }, 1714, false);
            foreach (var kind in new[] { "MM_Immortal", "MM_Disruptor", "MM_Reaver" })
            {
                var unit = (Pawn_Immortal)NewPawn(kind);
                int expected = kind == "MM_Reaver" ? 9375 : kind == "MM_Disruptor" ? 1250 : 1071;
                SpeedCase(kind + " ridden", new List<Pawn> { rider, unit }, expected, true);
                SpeedCase(kind + " mixed with Colossus", new List<Pawn> { rider, giant, unit }, expected, true);
            }
            var horse = NewPawn("Horse");
            horse.ageTracker.AgeBiologicalTicks = 5L * 3600000;
            Check(horse.IsCaravanRideable(), "adult native horse is rideable");
            var ordinary = new List<Pawn> { rider, horse };
            int nativeTicks = Setup.VanillaTicks(ordinary, 10f, 1000f, false, null);
            Check(CaravanTicksPerMoveUtility.GetTicksPerMove(ordinary, 10f, 1000f) == nativeTicks, "ordinary caravan preserves native calculation");
            var nativeDescription = new StringBuilder();
            int mixedNative = Setup.VanillaTicks(new List<Pawn> { rider, second, giant, horse }, 0f, 0f, false, nativeDescription);
            int soloNative = Setup.VanillaTicks(new List<Pawn> { rider }, 0f, 0f, false, null);
            int mixedExpected = Mathf.RoundToInt(60000f / (35f * ((float)soloNative / mixedNative)));
            int mixedActual = CaravanTicksPerMoveUtility.GetTicksPerMove(new List<Pawn> { rider, second, giant, horse }, 0f, 0f);
            Check(Math.Abs(mixedActual - mixedExpected) <= 1, "horse plus warframe agrees with native rider allocation", true);
            var transfers = Transfers(new[] { rider, giant }, 1);
            transfers[1].AdjustTo(0);
            float removed = TilesPerDayCalculator.ApproxTilesPerDay(transfers, 0f, 1000f, map.Tile, destination, false);
            float onlyRider = TilesPerDayCalculator.ApproxTilesPerDay(Transfers(new[] { rider }, 1), 0f, 1000f, map.Tile, destination, false);
            Check(Mathf.Approximately(removed, onlyRider), "deselected warframe does not boost formation preview");

            var caravan = CaravanMaker.MakeCaravan(new[] { rider, giant }, Faction.OfPlayer, map.Tile, true);
            int runtimeExpected = baseline ? 1714 : 1071;
            Check(Caravan_PathFollower.CostToMove(caravan, map.Tile, destination) == 1071, "actual world edge cost uses riding bonus", true);
            Check(Caravan_PathFollower.CostToMove(caravan, map.Tile, map.Tile) == 0, "same tile cost remains zero");
            Check(Mathf.Abs(TilesPerDayCalculator.ApproxTilesPerDay(caravan) - 60000f / 1071) < 0.001f,
                "world caravan speed display matches actual cost", true);
            Check(!caravan.NightResting, "mechanical caravan still ignores night rest");
            Check(caravan.pather.StartPath(destination, null, true), "native world path starts");
            for (int i = 0; i < 10 && caravan.pather.nextTileCostLeft <= 0f && caravan.Tile != destination; i++) caravan.pather.PatherTickInterval(1);
            Check(caravan.pather.nextTileCostTotal == runtimeExpected, "path follower schedules expected full edge cost " + caravan.pather.nextTileCostTotal);
            float energy = giant.Light.currentLight, left = caravan.pather.nextTileCostLeft;
            caravan.pather.PatherTickInterval(100);
            Check(Mathf.Abs(caravan.pather.nextTileCostLeft - (left - 100)) < 0.001f, "100 actual world ticks consume 100 movement cost");
            Check(Mathf.Abs(energy - giant.Light.currentLight - 2f) < 0.01f, "world light consumption remains 1200/day");
            giant.Light.currentLight = 0;
            left = caravan.pather.nextTileCostLeft;
            caravan.pather.PatherTickInterval(100);
            Check(caravan.CantMove && caravan.pather.nextTileCostLeft == left, "empty light storage still stops travel");
            giant.Light.currentLight = giant.LightMax;
            for (int i = 0; i < runtimeExpected + 10 && caravan.Tile != destination; i++) caravan.pather.PatherTickInterval(1);
            Check(caravan.Tile == destination && !caravan.pather.Moving, "native caravan completes world tile traversal");
            Check(!baseline || failures >= 10, "baseline reproduces missing bonuses and misleading previews");
            File.WriteAllText(Path.Combine(Root, "complete"), "PASS=" + passed + " EXPECTED_BASELINE_FAILURE=" + failures);
        }
        public override void GameComponentUpdate()
        {
            if (!active || done || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return;
            done = true;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            try { Run(); }
            catch (Exception e) { File.WriteAllText(Path.Combine(Root, "failed"), e.ToString()); Log.Error("MM_RIDING " + e); }
            Application.Quit();
        }
    }
}
