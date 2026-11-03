using System.Collections.Generic;
using UnityEngine;

namespace Yakigura.QvPalette.Editor
{
    /// <summary>
    /// 初回のセットアップでパレットに入れるインクを決める。ワールドに QvPen のペンがあれば、そのインクを引き継ぐ。
    /// なければ、色は自動で作らず、QvPen の標準のインク 1 個だけにする
    /// （実行時はインクが最低 1 個ないと styleId 0 が引けないため、空にはしない）。
    /// </summary>
    public static class QppInitialInks
    {
        public static List<QppInk> Build(List<QppInk> fromPens)
        {
            if (fromPens != null && fromPens.Count > 0) return fromPens;

            var ink = new QppInk
            {
                name = "Default",
                gradient = QppInkData.SolidGradient(Color.white),
                material = QppInkData.DefaultInkMaterial(false),
            };
            QppInkData.FillOutlineFromMaterial(ink);
            return new List<QppInk> { ink };
        }

        /// <summary>各インクに、色からの自動タグを付け、使われているタグ名の一覧を返す。</summary>
        public static string[] ApplyAutoTags(List<QppInk> inks)
        {
            var names = new List<string>();
            foreach (var ink in inks)
            {
                ink.tags = QppInkData.AutoTagNames(ink.gradient);
                foreach (string t in ink.tags)
                    if (!names.Contains(t)) names.Add(t);
            }
            return names.ToArray();
        }
    }
}
