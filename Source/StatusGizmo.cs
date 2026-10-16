using RimWorld;
using UnityEngine;
using Verse;

namespace Monolyn.Mecha
{
    // Same grayscale bar language, Light translation and storage values as the native Monolyn UI.
    public sealed class Gizmo_ImmortalStatus : Gizmo
    {
        readonly Pawn_Immortal mecha;
        public Gizmo_ImmortalStatus(Pawn_Immortal mecha) { this.mecha = mecha; Order = -100f; }
        public override float GetWidth(float maxWidth) => 190f;
        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            // This read-only gizmo has no input controls. Labels/bars only paint on Repaint.
            if (Event.current.type != EventType.Repaint) return new GizmoResult(GizmoState.Clear);
            var state = mecha.UiState;
            state.Refresh();
            var rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Widgets.DrawWindowBackground(rect);
            var inner = rect.ContractedBy(6f);
            var oldFont = Text.Font; var oldAnchor = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 18f), state.HullLabel);
            var bar = new Rect(inner.x, inner.y + 22f, inner.width, 23f);
            Widgets.FillableBar(bar, state.LightFill, BaseContent.GreyTex, BaseContent.BlackTex, true);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bar, state.LightLabel);
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(inner.x, inner.y + 46f, inner.width, 18f), state.CargoLabel);
            TooltipHandler.TipRegion(rect, state.Tip);
            Text.Font = oldFont; Text.Anchor = oldAnchor;
            return new GizmoResult(GizmoState.Clear);
        }
    }
}
