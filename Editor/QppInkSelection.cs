#if UNITY_EDITOR
using System.Collections.Generic;

namespace Yakigura.QvPalette.Editor
{
    /// <summary>Palette Editor で選んだインクの操作（選択そのものは ReorderableList の複数選択）。</summary>
    public static class QppInkSelection
    {
        /// <summary>選んだインク（インク番号の集合）を消す。消した数を返す。</summary>
        public static int RemoveSelected<T>(List<T> inks, HashSet<int> selected)
        {
            int removed = 0;
            for (int i = inks.Count - 1; i >= 0; i--)
            {
                if (!selected.Contains(i)) continue;
                inks.RemoveAt(i);
                removed++;
            }
            return removed;
        }
    }
}
#endif
