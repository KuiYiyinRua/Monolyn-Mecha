using System;
using System.Collections.Generic;
using System.Reflection;
using ASEL;
using HarmonyLib;
using Verse;
using Verse.Sound;

namespace Monolyn.Mecha
{
    public sealed class MechaSettings : ModSettings
    {
        public bool muteBlinkSounds;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref muteBlinkSounds, "muteBlinkSounds", false);
        }
    }

    // Scope the local audio preference to this mod's blink. Keep the native
    // effecters, teleport, jobs, cooldown and random calls intact.
    [HarmonyPatch]
    static class StalkerBlinkSoundScope
    {
        [ThreadStatic] internal static int depth;

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(CompAbilityEffect_SyncBlink), nameof(CompAbilityEffect_SyncBlink.Apply),
                new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo) });
            // In 1.7 the upstream automatic blink also calls Blink directly.
            var blink = AccessTools.DeclaredMethod(typeof(CompAbilityEffect_SyncBlink), "Blink", new[] { typeof(LocalTargetInfo) });
            if (blink != null) yield return blink;
        }

        static void Prefix(CompAbilityEffect_SyncBlink __instance, out bool __state)
        {
            __state = __instance is CompStalkerBlink && MechaMod.Settings?.muteBlinkSounds == true;
            if (__state) depth++;
        }

        static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state) depth--;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(SoundStarter), nameof(SoundStarter.PlayOneShot), new[] { typeof(SoundDef), typeof(SoundInfo) })]
    static class StalkerBlinkSoundVolume
    {
        static void Prefix(ref SoundInfo info)
        {
            if (StalkerBlinkSoundScope.depth > 0) info.volumeFactor = 0f;
        }
    }
}
