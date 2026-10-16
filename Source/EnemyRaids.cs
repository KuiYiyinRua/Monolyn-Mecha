using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ASEL;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Monolyn.Mecha
{
    public sealed class MechaRaidExtension : DefModExtension
    {
        public float selectionWeight;
        public float minRaidPoints;
        public PawnKindDef pilotKind;
    }

    public static class EnemyMechaRaids
    {
        public static bool IsHostileMonolyn(Faction faction)
            => faction != null && !faction.IsPlayer
            && (faction.def.defName == "UrielCult" || faction.def.defName == "RaphaelCult")
            && faction.HostileTo(Faction.OfPlayer);

        public static bool Eligible(PawnGroupMakerParms parms)
            => parms != null && parms.groupKind == PawnGroupKindDefOf.Combat
            && parms.raidStrategy != null && !parms.inhabitants && IsHostileMonolyn(parms.faction);

        // Let the lord retain staging, breaching, kidnapping and retreat orders. Custom
        // tactics only take over an ordinary assault; weapons still tick while escorting.
        public static bool MayFight(Pawn pawn)
        {
            if (!IsHostileMonolyn(pawn.Faction)) return false;
            if (pawn.GetLord() == null) return true;
            var duty = pawn.mindState.duty?.def;
            return duty == DutyDefOf.AssaultColony || duty == DutyDefOf.AssaultThing;
        }

        public static void Prepare(Pawn pawn)
        {
            var config = pawn.kindDef.GetModExtension<MechaRaidExtension>();
            if (config == null || !IsHostileMonolyn(pawn.Faction)) return;
            if (pawn is Pawn_Immortal mecha)
            {
                if (mecha.raidPrepared) return;
                if (mecha.Pilot == null)
                {
                    var pilot = PawnGenerator.GeneratePawn(new PawnGenerationRequest(config.pilotKind, pawn.Faction,
                        forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
                        canGeneratePawnRelations: false, mustBeCapableOfViolence: true,
                        allowPregnant: false, allowAddictions: false, fixedBiologicalAge: 25f,
                        developmentalStages: DevelopmentalStage.Adult));
                    if (pilot.def.defName != "MNRace" || !mecha.pilots.TryAdd(pilot))
                        throw new InvalidOperationException("[Monolyn Mecha] Could not seat a Monolyn raid pilot.");
                    if (pilot.needs.food != null) pilot.needs.food.CurLevelPercentage = 1f;
                    if (pilot.needs.rest != null) pilot.needs.rest.CurLevelPercentage = 1f;
                }
                mecha.Light.maxLightP = mecha.LightMax;
                mecha.Light.currentLight = mecha.LightMax;
                mecha.autoFire = true;
                if (mecha is Pawn_Reaver reaver)
                {
                    reaver.missiles = Pawn_Reaver.MagazineCapacity;
                    reaver.autoFabricate = true;
                }
                mecha.raidPrepared = true;
            }
            // Non-player constructs use the native mech energy rules; no colony light is spent.
            var stalker = pawn.GetComp<CompStalker>();
            if (stalker != null) stalker.autoCombat = true;
            var guardian = pawn.GetComp<CompGuardian>();
            if (guardian != null) { guardian.autoCombat = true; guardian.autoRestore = true; }
        }
    }

    [HarmonyPatch(typeof(PawnGroupMakerUtility), nameof(PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints))]
    static class MonolynRaidOptions
    {
        static void Prefix(ref List<PawnGenOption> options, PawnGroupMakerParms groupParms)
        {
            if (!EnemyMechaRaids.Eligible(groupParms)) return;
            // Clone per call: faction defs, traders, settlements and friendly raids stay intact.
            var result = new List<PawnGenOption>(options);
            float weight = options.Where(o => o.kind.GetModExtension<MechaRaidExtension>() == null)
                .Sum(o => Math.Max(0f, o.selectionWeight));
            foreach (var kind in DefDatabase<PawnKindDef>.AllDefsListForReading.OrderBy(k => k.defName))
            {
                var config = kind.GetModExtension<MechaRaidExtension>();
                if (config != null && !result.Any(o => o.kind == kind))
                    result.Add(new PawnGenOption { kind = kind, selectionWeight = weight * config.selectionWeight });
            }
            options = result;
        }
    }

    [HarmonyPatch(typeof(PawnGroupMakerUtility), nameof(PawnGroupMakerUtility.PawnGenOptionValid))]
    static class MonolynRaidEligibility
    {
        static void Postfix(PawnGenOption o, PawnGroupMakerParms groupParms,
            List<PawnGenOptionWithXenotype> chosenOptions, ref bool __result)
        {
            var config = o.kind.GetModExtension<MechaRaidExtension>();
            if (!__result || config == null) return;
            __result = EnemyMechaRaids.Eligible(groupParms) && groupParms.points >= config.minRaidPoints;
            if (!__result) return;
            // Keep at least half the budget for the native force. The vanilla selector
            // still pays full combatPower (including the seated pilot) and enforces caps.
            float used = chosenOptions == null ? 0 : chosenOptions
                .Where(x => x.Option.kind.GetModExtension<MechaRaidExtension>() != null).Sum(x => x.Cost);
            __result = used + o.Cost <= groupParms.points * .5f;
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    static class MonolynRaidCrew
    {
        static void Postfix(Pawn __result) { if (__result != null) EnemyMechaRaids.Prepare(__result); }
    }

    [HarmonyPatch(typeof(PawnGroupMakerUtility), nameof(PawnGroupMakerUtility.GetOptions))]
    static class MonolynRaidBudget
    {
        static void Postfix(PawnGroupMakerParms groupParms, List<PawnGenOptionWithXenotype> chosenOptions,
            List<PawnGenOptionWithXenotype> __result)
        {
            if (!EnemyMechaRaids.Eligible(groupParms)) return;
            float used = chosenOptions == null ? 0 : chosenOptions
                .Where(x => x.Option.kind.GetModExtension<MechaRaidExtension>() != null).Sum(x => x.Cost);
            // GetOptions knows the actual xenotype-adjusted cost; checking only a kind's
            // base cost would let an ideology's cost multiplier exceed the half budget.
            __result.RemoveAll(x => x.Option.kind.GetModExtension<MechaRaidExtension>() != null
                && used + x.Cost > groupParms.points * .5f);
        }
    }

    // The upstream distributor scans nearby receivers without a faction check. Block
    // both discovery and transfer so an attacker cannot draw power from the player's grid.
    [HarmonyPatch]
    static class MechaRaidLightIsolation
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(CompLightDistributor), nameof(CompLightDistributor.RegisterReceiver));
            yield return AccessTools.Method(typeof(CompLightDistributor), nameof(CompLightDistributor.RegisterReceiverNoSound));
            yield return AccessTools.Method(typeof(CompLightDistributor), "TransferLight");
        }
        static bool Prefix(CompLightReceiver receiver)
            => !(receiver.parent is Pawn_Immortal) || receiver.parent.Faction == Faction.OfPlayer;
    }

    [HarmonyPatch(typeof(CompLightReceiver), nameof(CompLightReceiver.AddDistributor))]
    static class MechaRaidLightConnection
    {
        static bool Prefix(CompLightReceiver __instance)
            => !(__instance.parent is Pawn_Immortal) || __instance.parent.Faction == Faction.OfPlayer;
    }
}
