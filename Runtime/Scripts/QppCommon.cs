using UnityEngine;

namespace Yakigura.QvPalette
{
    public static class QppCommon
    {
        // 太さは最小〜最大を対数で 1024 段階（番号 0〜1023。台帳は 10 ビットで持つ）。
        // 256 段階では 1 段が約 1.4% あり、30mm が 29.8mm のようにずれて見えたため細かくした（1 段約 0.34%）。
        // 範囲は QppPalette のインスペクターで変えられる。ここの値は範囲の初期値（パレットがないときの既定）。
        public const int WIDTH_STEPS = 1024;
        public const int MAX_WIDTH_INDEX = WIDTH_STEPS - 1;
        public const float WIDTH_MIN = 0.003f;
        public const float WIDTH_MAX = 0.1f;
        // 約 5mm（IndexOfWidth(0.005) = 149）
        public const int DEFAULT_WIDTH_INDEX = 149;

        public static float WidthOf(int index)
        {
            return WidthOf(index, WIDTH_MIN, WIDTH_MAX);
        }

        public static float WidthOf(int index, float min, float max)
        {
            return min * Mathf.Pow(max / min, Mathf.Clamp(index, 0, MAX_WIDTH_INDEX) / (float)MAX_WIDTH_INDEX);
        }

        // ペンの大きさ（アバターの大きさに合わせる倍率）。スライダーは対数で、中央が等倍。
        public const float PEN_SCALE_MIN = 0.5f;
        public const float PEN_SCALE_MAX = 2f;
        /// <summary>この目の高さ（m）のアバターで等倍。自動で決めるときの基準。</summary>
        public const float PEN_SCALE_REFERENCE_EYE_HEIGHT = 1.5f;

        public static float ClampPenScale(float scale)
        {
            return Mathf.Clamp(scale, PEN_SCALE_MIN, PEN_SCALE_MAX);
        }

        public static float PenScaleOf(float t)
        {
            return PEN_SCALE_MIN * Mathf.Pow(PEN_SCALE_MAX / PEN_SCALE_MIN, Mathf.Clamp01(t));
        }

        public static float PenScalePositionOf(float scale)
        {
            return Mathf.Log(ClampPenScale(scale) / PEN_SCALE_MIN) / Mathf.Log(PEN_SCALE_MAX / PEN_SCALE_MIN);
        }

        /// <summary>0.05 刻みに丸める（等倍付近で止めやすくする）。</summary>
        public static float SnapPenScale(float scale)
        {
            return ClampPenScale(Mathf.Round(scale * 20f) / 20f);
        }

        public static string PenScaleTextOf(float scale)
        {
            int hundredths = Mathf.RoundToInt(scale * 100f);
            int frac = hundredths % 100;
            return "ペン " + (hundredths / 100) + "." + (frac < 10 ? "0" : "") + frac + "x";
        }

        public static int IndexOfWidth(float w)
        {
            return IndexOfWidth(w, WIDTH_MIN, WIDTH_MAX);
        }

        public static int IndexOfWidth(float w, float min, float max)
        {
            if (w <= 0f) return 0;
            return Mathf.Clamp(Mathf.RoundToInt(MAX_WIDTH_INDEX * Mathf.Log(w / min) / Mathf.Log(max / min)), 0, MAX_WIDTH_INDEX);
        }

        /// <summary>
        /// 太さは見た目で判別しにくいため mm 表記を併記する（Qvシリーズ構成 §8）。
        /// inkWidth はメートル単位なので 0.005 → "5.0 mm"。
        /// 書式指定子つきの文字列補間を避け、Udon で確実に動く整数演算で組み立てる。
        /// </summary>
        public static string MillimetersTextOf(int widthIndex)
        {
            return MillimetersText(WidthOf(widthIndex));
        }

        public static string MillimetersText(float width)
        {
            int tenth = Mathf.RoundToInt(width * 10000f);
            if (tenth < 0) tenth = 0;
            return (tenth / 10) + "." + (tenth % 10) + " mm";
        }

#if UNITY_STANDALONE
        public const bool IS_PC = true;
#else
        public const bool IS_PC = false;
#endif
    }
}
