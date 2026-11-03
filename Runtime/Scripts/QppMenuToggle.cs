using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakigura.QvPalette
{
    /// <summary>
    /// パレット 1 枚ぶんの表示制御。
    ///
    /// パレットはペンごとに用意せず、持ったペンに動的にバインドする（Qvシリーズ構成 §8）。
    /// パネルは手ごとに 1 枚で、左手のペンは左側のパネル、右手のペンは右側のパネルが受け持つ。
    ///
    /// 表示のきっかけは「ペンを持つこと」であり、ジェスチャーもキーも消費しない。
    /// §4 の入力予約表では M キーと右スティック上下は QvShape の予約であり、
    /// 後発ツールはスティックを使わないこと、と定められている。
    ///
    /// ペンを置いても閉じない。置いた後も設定を読み書きできる方が使いやすいため
    /// （ペンの設定はワールド全体で共有されるので、持っていない間の変更にも意味がある）。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QppMenuToggle : UdonSharpBehaviour
    {
        public const int SIDE_RIGHT = 0;
        public const int SIDE_LEFT = 1;

        [SerializeField] private Transform panelRoot;
        [SerializeField] private QppPaletteUI ui;
        [SerializeField] private QppPointerProbe probe;
        [SerializeField] private QppPenBinding[] bindings;

        [Tooltip("このパネルが受け持つ手。0 = 右手、1 = 左手。Desktop では右手側だけを使う。")]
        [SerializeField] private int handSide = SIDE_RIGHT;

        [Tooltip("手動トグル用のデスクトップキー。既定は None（入力を消費しない）。" +
                 "他アセットと衝突した場合にワールド制作者が変更できるよう公開している。")]
        [SerializeField] private KeyCode desktopToggleKey = KeyCode.None;

        [SerializeField] private float spawnDistance = 0.6f;

        [Tooltip("ペンを持ったときに自動でパレットを開くか（既定 false: 絵の具パレット型ツールで手動配置する）。")]
        [SerializeField] private bool autoOpenOnPenPickup = false;

        [Tooltip("持ち手に合わせた左右のずらし幅（メートル）。")]
        [SerializeField] private float handSideOffset = 0.22f;

        private bool wasHoldingPen;
        private bool initialized;
        private bool isUserInVR;

        public int HandSide => handSide;

        private void Start()
        {
            var localPlayer = Networking.LocalPlayer;
            isUserInVR = Utilities.IsValid(localPlayer) && localPlayer.IsUserInVR();
        }

        public void _PollInput()
        {
            if (!Utilities.IsValid(panelRoot) || !Utilities.IsValid(ui)) return;

            if (!initialized)
            {
                initialized = true;
                var localPlayer = Networking.LocalPlayer;
                isUserInVR = Utilities.IsValid(localPlayer) && localPlayer.IsUserInVR();
            }

            int heldSlot = FindHeldPenSlot();
            bool holding = heldSlot >= 0;

            if (holding)
            {
                // 持った瞬間にそのペンへバインドする。持ち替えれば追従する。
                ui._BindTo(heldSlot);

                if (autoOpenOnPenPickup && !wasHoldingPen)
                {
                    _OpenMenu();
                }
            }

            // ペンを置いても閉じない（開いたままその場に残す）。
            wasHoldingPen = holding;

            if (desktopToggleKey != KeyCode.None && Input.GetKeyDown(desktopToggleKey))
            {
                _ToggleMenu();
            }
        }

        /// <summary>
        /// このパネルが受け持つ手で、ローカルプレイヤーが持っているペンのスロットを返す。無ければ -1。
        /// Desktop は手の区別が無いため、右手側パネルだけがすべてのペンを受け持つ。
        /// </summary>
        private int FindHeldPenSlot()
        {
            if (bindings == null) return -1;

            for (int i = 0; i < bindings.Length; i++)
            {
                var b = bindings[i];
                if (!Utilities.IsValid(b) || !b.IsHeldByLocalPlayer) continue;

                if (!isUserInVR)
                {
                    if (handSide != SIDE_RIGHT) continue;
                    return i;
                }

                bool inLeftHand = b.IsHeldInLeftHand;
                if (inLeftHand == (handSide == SIDE_LEFT))
                {
                    return i;
                }
            }

            return -1;
        }

        public void _ToggleMenu()
        {
            if (!Utilities.IsValid(panelRoot)) return;

            if (panelRoot.gameObject.activeSelf)
            {
                if (Utilities.IsValid(ui) && ui.IsPinned) return;
                _CloseMenu();
            }
            else
            {
                _OpenMenu();
            }
        }

        public void _OpenMenu()
        {
            if (!Utilities.IsValid(panelRoot)) return;

            bool pinned = Utilities.IsValid(ui) && ui.IsPinned;

            // ピン留め中は再配置しない（開いた位置に留まる）。
            if (!pinned)
            {
                PlaceAtHeadFront();
            }

            panelRoot.gameObject.SetActive(true);
            if (Utilities.IsValid(probe)) probe.ResetPrevValid();

            // 触れる前に設定内容が読める状態にする（§8）。
            if (Utilities.IsValid(ui)) ui._RefreshUI();
        }

        public void _CloseMenu()
        {
            if (!Utilities.IsValid(panelRoot)) return;
            panelRoot.gameObject.SetActive(false);
        }

        private void PlaceAtHeadFront()
        {
            var localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer)) return;

            var head = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);

            // VR で両手にペンを持つと 2 枚出るため、左右に振り分けて手と位置を対応させる。
            float side = 0f;
            if (isUserInVR)
            {
                side = (handSide == SIDE_LEFT) ? -handSideOffset : handSideOffset;
            }

            Vector3 pos = head.position
                        + head.rotation * Vector3.forward * spawnDistance
                        + head.rotation * Vector3.right * side;

            panelRoot.position = pos;
            panelRoot.LookAt(head.position);

            // §4.3 規約: パネルのローカル +Z はプレイヤー側を向いていなければならない。
            float d = Vector3.Dot(panelRoot.forward, head.position - panelRoot.position);
            if (d < 0f)
            {
                Debug.LogError("[QPP] panelRoot +Z points away from player! See §4.3 convention.");
            }
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(
            Transform root,
            QppPaletteUI u,
            QppPointerProbe p,
            QppPenBinding[] bs,
            int side,
            KeyCode key,
            float dist,
            float sideOffset,
            bool autoOpen = false)
        {
            panelRoot = root;
            ui = u;
            probe = p;
            bindings = bs;
            handSide = side;
            desktopToggleKey = key;
            spawnDistance = dist;
            handSideOffset = sideOffset;
            autoOpenOnPenPickup = autoOpen;
        }
#endif
    }
}
