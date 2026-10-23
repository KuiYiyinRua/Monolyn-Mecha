using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Monolyn.Mecha
{
    // Warframes deliberately have no Need_MechEnergy. The vanilla Mechs table
    // assumes every row has one, so render their real light storage instead.
    [HarmonyPatch(typeof(PawnColumnWorker_Energy), nameof(PawnColumnWorker_Energy.DoCell))]
    static class MechaEnergyColumn
    {
        static bool Prefix(Rect rect, Pawn pawn)
        {
            if (!(pawn is Pawn_Immortal mecha)) return true;
            if (pawn.IsGestating()) return false;
            var light = mecha.Light;
            if (light == null) return false;

            float fill = light.maxLightP > 0f ? Mathf.Clamp01(light.currentLight / light.maxLightP) : 0f;
            Widgets.FillableBar(rect.ContractedBy(4f), fill,
                PawnColumnWorker_Energy.EnergyBarTex, BaseContent.ClearTex, false);
            var font = Text.Font;
            var anchor = Text.Anchor;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, Mathf.RoundToInt(light.currentLight) + " / " + Mathf.RoundToInt(light.maxLightP));
                TooltipHandler.TipRegion(rect, "Light".Translate());
            }
            finally
            {
                Text.Anchor = anchor;
                Text.Font = font;
            }
            return false;
        }
    }
}
