#if UNITY_EDITOR
using UnityEngine;

namespace Yakigura.QvPalette.Editor
{
    public static class QppInkPreviewLogic
    {
        public static Material ResolveMaterial(QppInk ink, bool quest)
        {
            if (ink == null) return null;
            return quest && ink.questMaterial != null ? ink.questMaterial : ink.material;
        }

        public static bool HasMaterial(Material material) => material != null;

        public static float WidthOf(int index, float min, float max) => QppCommon.WidthOf(index, min, max);

        public static Color BackgroundColor(int background)
        {
            switch (background)
            {
                case 0: return new Color(0.08f, 0.08f, 0.08f, 1f);
                case 2: return new Color(0.9f, 0.9f, 0.9f, 1f);
                default: return new Color(0.45f, 0.45f, 0.45f, 1f);
            }
        }

        public static ulong MakeKey(int materialId, int materialVersion, Gradient gradient, float outlineRatio,
            Color outlineColor, Vector3 outlineShift, float width, int background, bool quest, bool rounded)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                Mix(ref hash, materialId); Mix(ref hash, materialVersion);
                Mix(ref hash, gradient == null ? -1 : (int)gradient.mode);
                if (gradient != null)
                {
                    var colors = gradient.colorKeys;
                    var alphas = gradient.alphaKeys;
                    Mix(ref hash, colors.Length);
                    foreach (var key in colors) { MixColor(ref hash, key.color); Mix(ref hash, key.time.GetHashCode()); }
                    Mix(ref hash, alphas.Length);
                    foreach (var key in alphas) { Mix(ref hash, key.alpha.GetHashCode()); Mix(ref hash, key.time.GetHashCode()); }
                }
                Mix(ref hash, outlineRatio.GetHashCode()); MixColor(ref hash, outlineColor);
                Mix(ref hash, outlineShift.x.GetHashCode()); Mix(ref hash, outlineShift.y.GetHashCode());
                Mix(ref hash, outlineShift.z.GetHashCode()); Mix(ref hash, width.GetHashCode());
                Mix(ref hash, background); Mix(ref hash, quest ? 1 : 0); Mix(ref hash, rounded ? 1 : 0);
                return hash;
            }
        }

        private static void MixColor(ref ulong hash, Color c)
        {
            Mix(ref hash, c.r.GetHashCode()); Mix(ref hash, c.g.GetHashCode());
            Mix(ref hash, c.b.GetHashCode()); Mix(ref hash, c.a.GetHashCode());
        }

        private static void Mix(ref ulong hash, int value)
        {
            unchecked { hash = (hash ^ (uint)value) * 1099511628211UL; }
        }
    }
}
#endif
