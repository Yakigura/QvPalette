using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using Yakigura.QvInkPreview;

namespace Yakigura.QvPalette
{
    /// <summary>パレットの選択色と太さを共有インクプレビューへ渡す。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QppPenPreview : UdonSharpBehaviour
    {
        [SerializeField] private QvInkPreview_Pen inkPreview;
        [SerializeField] private QppPenBinding binding;
        [SerializeField] private QppPalette palette;

        private int lastStyleId = -1;
        private int lastWidthIndex = -1;
        private int lastBindingRevision = -1;

        public void _UpdatePreview()
        {
            if (!Utilities.IsValid(inkPreview) || !Utilities.IsValid(binding) || !Utilities.IsValid(palette)) return;

            int styleId = binding.CurrentStyleId;
            int widthIndex = binding.CurrentWidthIndex;
            int revision = binding.LocalStyleRevision;
            if (styleId == lastStyleId && widthIndex == lastWidthIndex && revision == lastBindingRevision) return;

            lastStyleId = styleId;
            lastWidthIndex = widthIndex;
            lastBindingRevision = revision;
            inkPreview._SetOverrideGradient(binding.ResolveLocalGradient(styleId));
            inkPreview._SetOverrideWidth(palette.WidthOf(widthIndex));
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(QvInkPreview_Pen preview, QppPenBinding penBinding, QppPalette penPalette)
        {
            inkPreview = preview;
            binding = penBinding;
            palette = penPalette;
        }
#endif
    }
}
