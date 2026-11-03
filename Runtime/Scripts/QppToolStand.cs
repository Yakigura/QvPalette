using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace Yakigura.QvPalette
{
    /// <summary>
    /// パレットツールの元の位置に置くスタンド。長押し（クリックしたまま押し続ける）でツールを元の位置に戻す。
    /// 押している間は上面の円が満ちていき、満ちたら戻す。途中で離すと何もしない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QppToolStand : UdonSharpBehaviour
    {
        [SerializeField] private QppPaletteTool tool;
        [Tooltip("押している間に満ちていく表示（上面の円）。")]
        [SerializeField] private Transform fill;
        [SerializeField] private TextMeshPro label;
        [Tooltip("戻すまでに押し続ける時間（秒）。")]
        [SerializeField] private float holdSeconds = 1.0f;
        [SerializeField] private string interactionText = "長押しでツールを戻す";

        private Vector3 fillBaseScale = Vector3.one;
        private bool pressing;
        private float pressStart;

        private void Start()
        {
            InteractionText = interactionText;
            if (Utilities.IsValid(fill))
            {
                fillBaseScale = fill.localScale;
                SetFill(0f);
            }
        }

        public override void Interact()
        {
            pressing = true;
            pressStart = Time.time;
            SetFill(0f);
        }

        // 押していたボタン（デスクトップはクリック、VR はトリガー）を離したら取りやめる。
        public override void InputUse(bool value, UdonInputEventArgs args)
        {
            if (!value && pressing) Cancel();
        }

        private void Update()
        {
            if (!pressing) return;
            float t = (Time.time - pressStart) / Mathf.Max(holdSeconds, 0.05f);
            if (t < 1f)
            {
                SetFill(t);
                return;
            }

            Cancel();
            if (Utilities.IsValid(tool)) tool._ResetToSpawn();
        }

        private void Cancel()
        {
            pressing = false;
            SetFill(0f);
        }

        private void SetFill(float t)
        {
            if (!Utilities.IsValid(fill)) return;
            bool visible = t > 0.001f;
            if (fill.gameObject.activeSelf != visible) fill.gameObject.SetActive(visible);
            fill.localScale = new Vector3(fillBaseScale.x * t, fillBaseScale.y, fillBaseScale.z * t);
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(QppPaletteTool t, Transform fillTr, TextMeshPro lbl)
        {
            tool = t;
            fill = fillTr;
            label = lbl;
        }
#endif
    }
}
