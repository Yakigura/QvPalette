using UdonSharp;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDKBase;

namespace Yakigura.QvPalette
{
    /// <summary>
    /// ペン本体と、ペンの下に出るクリアボタンの大きさ（アバターの大きさに合わせて変える）。
    ///
    /// ペンを大きくするとペン先の位置も動くので、持っている人の倍率を全員に同期する
    /// （同期しないと、ほかの人からは線がペン先からずれて見える）。
    /// 色・太さの同期（QppPenState）とは別のオブジェクトにして、倍率の変更で再適用パスが走らないようにしている。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class QppPenSize : UdonSharpBehaviour
    {
        [SerializeField] private Transform penBody;
        [SerializeField] private Transform clearButton;
        [SerializeField] private PositionConstraint clearButtonConstraint;
        [Tooltip("ペンの見た目のメッシュ。長さ方向以外（太さ）をさらに大きく変える。ペン先の位置は変わらない。")]
        [SerializeField] private Transform penMesh;
        [Tooltip("penMesh の長さ方向の軸（0=X, 1=Y, 2=Z）。")]
        [SerializeField] private int penMeshLengthAxis = 1;

        // ペンは細長く、全体を同じ倍率にすると長さしか変わらないように見えるため、太さは倍率をさらに強める。
        // 例: 2 倍で長さ 2 倍・太さ約 2.8 倍、0.5 倍で長さ 0.5 倍・太さ約 0.35 倍。
        private const float ThicknessExtraExponent = 0.5f;

        [UdonSynced] private float syncedScale = 1f;

        private bool cached;
        private Vector3 basePenScale = Vector3.one;
        private Vector3 baseMeshScale = Vector3.one;
        private Vector3 baseClearScale = Vector3.one;
        private Vector3 baseClearOffset;
        private float appliedScale = -1f;
        private float lastSerializeTime = -1f;
        private bool serializeScheduled;

        public float Scale => syncedScale;

        private void Start()
        {
            Apply();
        }

        private void Cache()
        {
            if (cached) return;
            cached = true;
            if (Utilities.IsValid(penBody)) basePenScale = penBody.localScale;
            if (Utilities.IsValid(penMesh)) baseMeshScale = penMesh.localScale;
            if (Utilities.IsValid(clearButton)) baseClearScale = clearButton.localScale;
            if (Utilities.IsValid(clearButtonConstraint)) baseClearOffset = clearButtonConstraint.translationOffset;
        }

        /// <summary>ペンを持っている本人が呼ぶ。全員に同期する。</summary>
        public void _SetScale(float scale)
        {
            scale = QppCommon.ClampPenScale(scale);
            if (Mathf.Abs(scale - syncedScale) < 0.001f && appliedScale > 0f) return;

            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
            syncedScale = scale;
            Apply();

            // スライダーを動かしている間は送りすぎないよう、0.2 秒に 1 回へ間引く。
            if (Time.time - lastSerializeTime >= 0.2f && !serializeScheduled)
            {
                lastSerializeTime = Time.time;
                RequestSerialization();
            }
            else if (!serializeScheduled)
            {
                serializeScheduled = true;
                SendCustomEventDelayedSeconds(nameof(_FlushSerialization), 0.2f);
            }
        }

        public void _FlushSerialization()
        {
            serializeScheduled = false;
            if (!Networking.IsOwner(gameObject)) return;
            lastSerializeTime = Time.time;
            RequestSerialization();
        }

        public override void OnDeserialization()
        {
            Apply();
        }

        private void Apply()
        {
            Cache();
            float s = QppCommon.ClampPenScale(syncedScale);
            if (Mathf.Approximately(s, appliedScale)) return;
            appliedScale = s;

            if (Utilities.IsValid(penBody)) penBody.localScale = basePenScale * s;
            if (Utilities.IsValid(penMesh))
            {
                float k = Mathf.Pow(s, ThicknessExtraExponent);
                Vector3 m = baseMeshScale;
                if (penMeshLengthAxis != 0) m.x *= k;
                if (penMeshLengthAxis != 1) m.y *= k;
                if (penMeshLengthAxis != 2) m.z *= k;
                penMesh.localScale = m;
            }
            if (Utilities.IsValid(clearButton)) clearButton.localScale = baseClearScale * s;
            // クリアボタンはペンの少し下に出る。その距離もペンの大きさに合わせる。
            if (Utilities.IsValid(clearButtonConstraint)) clearButtonConstraint.translationOffset = baseClearOffset * s;
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(Transform pen, Transform clear, PositionConstraint clearConstraint,
            Transform mesh, int meshLengthAxis)
        {
            penMesh = mesh;
            penMeshLengthAxis = meshLengthAxis;
            penBody = pen;
            clearButton = clear;
            clearButtonConstraint = clearConstraint;
        }
#endif
    }
}
