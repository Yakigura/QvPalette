using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakigura.QvPalette
{
    /// <summary>
    /// パレットパネル上の汎用操作ボタン（閉じる、スタンドに戻す、ページ送り）。
    /// VRでの近接タッチおよびDesktopでのInteract（クリック）の双方に対応。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QppControlButton : UdonSharpBehaviour
    {
        public const int TYPE_CLOSE = 0;
        public const int TYPE_RESET_STAND = 1;
        public const int TYPE_PREV_PAGE = 2;
        public const int TYPE_NEXT_PAGE = 3;

        [SerializeField] private TextMeshPro label;
        [SerializeField] private Collider touchCollider;
        [SerializeField] private QppPaletteUI ui;
        [SerializeField] private string targetEventName = "_OnCloseButtonClicked";
        [SerializeField] private int actionType = TYPE_CLOSE;
        [SerializeField] private Vector2 rectCenter;
        [SerializeField] private Vector2 rectSize = new Vector2(0.04f, 0.04f);
        [Tooltip("ボタンの下地。タグボタンでは選んでいるかどうかを色で示す。")]
        [SerializeField] private Renderer background;

        private MaterialPropertyBlock backgroundBlock;

        public int ActionType => actionType;
        public Vector2 RectCenter => rectCenter;
        public Vector2 RectSize => rectSize;

        private void Start()
        {
            if (Networking.LocalPlayer != null && Networking.LocalPlayer.IsUserInVR())
            {
                DisableInteractive = true;
            }
        }

        /// <summary>タグボタンのように、実行時に表示を変えるボタン用。減光中の不透明度は保つ。</summary>
        public void SetLabel(string text, Color color)
        {
            if (!Utilities.IsValid(label)) return;
            label.text = text;
            color.a = label.color.a;
            label.color = color;
        }

        public void SetBackgroundColor(Color color)
        {
            if (!Utilities.IsValid(background)) return;
            if (backgroundBlock == null) backgroundBlock = new MaterialPropertyBlock();
            backgroundBlock.Clear();
            background.GetPropertyBlock(backgroundBlock);
            backgroundBlock.SetColor("_Color", color);
            background.SetPropertyBlock(backgroundBlock);
        }

        // 1 回の操作で 2 回届くことがある（Desktop はクリックが Interact とパネルのレイ判定の両方に届く。
        // VR はペン先とペンを持つ手の指先が同時に触れる）。ページ送りやタグの切り替えが 2 回進まないよう、
        // 直後の 2 回目は無視する。
        private const float RepeatGuardSeconds = 0.25f;
        private float lastTriggerTime = -1f;

        public void _Trigger()
        {
            if (lastTriggerTime >= 0f && Time.unscaledTime - lastTriggerTime < RepeatGuardSeconds) return;
            lastTriggerTime = Time.unscaledTime;
            _OnClick();
        }

        public void _OnClick()
        {
            if (!Utilities.IsValid(ui)) return;

            if (!string.IsNullOrEmpty(targetEventName))
            {
                ui.SendCustomEvent(targetEventName);
                return;
            }

            switch (actionType)
            {
                case TYPE_CLOSE:
                    ui._OnCloseButtonClicked();
                    break;
                case TYPE_RESET_STAND:
                    ui._OnResetToStandClicked();
                    break;
                case TYPE_PREV_PAGE:
                    ui._PrevPage();
                    break;
                case TYPE_NEXT_PAGE:
                    ui._NextPage();
                    break;
            }
        }

        public override void Interact()
        {
            if (Networking.LocalPlayer != null && Networking.LocalPlayer.IsUserInVR())
                return;
            _Trigger();
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(
            TextMeshPro lbl,
            Collider col,
            QppPaletteUI u,
            string eventName,
            Vector2 center,
            Vector2 size)
        {
            label = lbl;
            touchCollider = col;
            ui = u;
            targetEventName = eventName;
            rectCenter = center;
            rectSize = size;

            if (eventName == "_OnCloseButtonClicked") actionType = TYPE_CLOSE;
            else if (eventName == "_OnResetToStandClicked") actionType = TYPE_RESET_STAND;
            else if (eventName == "_PrevPage") actionType = TYPE_PREV_PAGE;
            else if (eventName == "_NextPage") actionType = TYPE_NEXT_PAGE;
        }

        public void EditorSetBackground(Renderer plate)
        {
            background = plate;
        }

        public void EditorSetReferences(QppPaletteUI u, int type, Vector2 center, Vector2 size)
        {
            ui = u;
            actionType = type;
            rectCenter = center;
            rectSize = size;
            switch (type)
            {
                case TYPE_CLOSE: targetEventName = "_OnCloseButtonClicked"; break;
                case TYPE_RESET_STAND: targetEventName = "_OnResetToStandClicked"; break;
                case TYPE_PREV_PAGE: targetEventName = "_PrevPage"; break;
                case TYPE_NEXT_PAGE: targetEventName = "_NextPage"; break;
            }
        }
#endif
    }
}
