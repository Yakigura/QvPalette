using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace Yakigura.QvPalette
{
    /// <summary>
    /// かわいいミニ絵の具パレット型の召喚・移動ツール。
    /// 
    /// ペンスタンドの横に配置され、プレイヤーが掴んで好きな位置（キャンバス横や空中）に持っていくと、
    /// その場所に操作用パレットパネルが展開されます。
    /// ペンを持っても離してもパレットは勝手に消えず、自由な場所に置いて使えます。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QppPaletteTool : UdonSharpBehaviour
    {
        [Header("Linked Palette UI")]
        [SerializeField] private Transform panelRoot;
        [SerializeField] private QppPaletteUI ui;
        [SerializeField] private QppPointerProbe probe;

        [Header("Placement Settings")]
        [Tooltip("ツールからパレットパネルの中心までのオフセット（ツールの上方向）。ツール本体とパネルが近すぎないよう十分な距離を確保する。")]
        [SerializeField] private float panelHeightOffset = 0.35f;
        [Tooltip("ツールを手放したときに自動でパネルを展開するか。")]
        [SerializeField] private bool openOnDrop = false;
        [Tooltip("ツールをこの秒数より短く掴んで離したら「クリック」とみなし、パネルの表示・非表示を切り替える。" +
                 "それより長く掴んでいる間は、パネルを出したままツールと一緒に動かす（位置・角度の調整）。")]
        [SerializeField] private float shortClickSeconds = 0.35f;
        [Tooltip("デスクトップでは、パネルの向きを左右（水平方向）と上下（垂直方向）の 2 方向だけにする（画面に対して斜めに傾かない）。")]
        [SerializeField] private bool desktopPanelYawPitchOnly = true;

        [Header("パネルの表示位置")]
        [Tooltip("ツールの子オブジェクト「PanelPosition」。シーン上で動かす・回すと、パネルはその位置・向きに出る。" +
                 "中の水色の板（編集時だけ表示）がパネルの大きさと見る側を示す。空なら見る人の方を向く自動配置。")]
        [SerializeField] private Transform panelAnchor;

        [Header("Spawn Reset")]
        [SerializeField] private Vector3 spawnPosition;
        [SerializeField] private Quaternion spawnRotation;

        private Rigidbody rb;
        private VRCPickup pickup;
        private bool isHeld = false;
        private bool isPanelOpen = false;
        private bool hasIndexTriggerSample;
        private bool indexTriggerPressed;
        private int lastInputToggleFrame = -1;
        private bool lastInputToggleWasPolled;
        private float lastInputToggleTime = -1f;
        private Vector3 panelLocalPositionOffset;
        private Quaternion panelLocalRotationOffset;
        private bool hasPanelOffset;

        // デスクトップで持つと VRChat が持ち方を決めるため、見た目（Visual）だけをカメラに向け直す。
        // 絵の具の面を手前に、上端を奥へ 45° 倒す。パネルの位置・向きはツール本体基準のまま変わらない。
        private const float DesktopHeldTiltDegrees = 45f;
        private Transform visual;
        private Quaternion visualBaseLocalRotation = Quaternion.identity;
        // 持った直後の数フレームだけ向きを合わせる（VRChat が手の位置へ動かし終えるのを待つ）。
        // 毎フレーム合わせると、ホイールドラッグなどで回しても見た目とパネルがカメラの方へ戻ってしまう。
        private const int FaceVisualFrames = 3;
        private int faceVisualFramesLeft;
        // デスクトップでは VRChat が持つたびに決まった向きへ置き直すので、手放したときの
        // 「カメラから見た見た目の向き」を覚えておき、次に持ったときにその向きで持つ。
        private bool hasDesktopHeldAngle;
        private Quaternion desktopHeldAngle = Quaternion.identity;

        public bool IsPanelOpen => isPanelOpen;
        public bool IsHeld => isHeld;

        private void Start()
        {
            rb = GetComponent<Rigidbody>();
            pickup = GetComponent<VRCPickup>();
            // Apply to existing bound scenes as well as newly generated tools.
            if (Utilities.IsValid(pickup)) pickup.AutoHold = VRCPickup.AutoHoldMode.No;

            // シーン上で置き直された姿勢を初期姿勢とする（バインド時の値が古くても、
            // 「ツールの上」の基準と [戻す] の戻り先が実際の配置からずれない）。
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;

            visual = transform.Find("Visual");
            if (Utilities.IsValid(visual)) visualBaseLocalRotation = visual.localRotation;

            if (Utilities.IsValid(rb))
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }

        private float pickupTime = -1f;
        // テスト用（編集モードにはプレイヤーがいないため、デスクトップとして扱う）。
        private bool treatAsDesktop;

        public override void OnPickup()
        {
            isHeld = true;
            pickupTime = Time.time;
            hasIndexTriggerSample = false;
            if (Utilities.IsValid(rb)) rb.isKinematic = false;
            if (isPanelOpen && Utilities.IsValid(panelRoot))
            {
                // Keep the same tool-to-panel pose across VRChat pickup snapping,
                // so the panel stays above the tool when it is grabbed again.
                if (!hasPanelOffset) CapturePanelOffset();
                ApplyHeldPanelPose();
            }

            // Desktop has no independent Use action with AutoHold disabled.
            var localPlayer = Networking.LocalPlayer;
            bool desktop = Utilities.IsValid(localPlayer) && !localPlayer.IsUserInVR();
            faceVisualFramesLeft = desktop ? FaceVisualFrames : 0;
            if (!desktop) RestoreVisual();
            // 掴んだ瞬間には開閉しない（掴んだまま動かすときはパネルを出したまま）。開閉は手放したときに判定する。
        }

        public override void OnDrop()
        {
            bool wasHeld = isHeld;
            bool shortClick = wasHeld && pickupTime >= 0f && Time.time - pickupTime <= shortClickSeconds;
            if (isHeld) RememberDesktopHeldAngle();
            if (isHeld && isPanelOpen) ApplyHeldPanelPose();
            isHeld = false;
            pickupTime = -1f;
            hasIndexTriggerSample = false;
            indexTriggerPressed = false;

            // Match QvGrab's hover-on-drop: stop at the released pose.
            if (Utilities.IsValid(rb))
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.useGravity = false;
                rb.isKinematic = true;
            }

            if (shortClick)
            {
                // 短いクリック: パネルの表示・非表示を切り替える。
                _TogglePanel();
            }
            else if (openOnDrop && !isPanelOpen)
            {
                DeployPanelAtCurrentPosition();
            }
        }

        public override void PostLateUpdate()
        {
            _FollowHeldPanel();
            if (isHeld && faceVisualFramesLeft > 0)
            {
                faceVisualFramesLeft--;
                FaceVisualToViewer();
                // 見た目の向きが変わったので、パネル（見た目の子の表示位置に出す）も合わせ直す。
                _FollowHeldPanel();
            }
        }

        private void RememberDesktopHeldAngle()
        {
            if (!Utilities.IsValid(visual)) return;
            var localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer) || localPlayer.IsUserInVR()) return;
            var head = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            desktopHeldAngle = Quaternion.Inverse(head.rotation) * visual.rotation;
            hasDesktopHeldAngle = true;
        }

        /// <summary>
        /// 前に手放したときの角度があればその角度で持つ。なければ絵の具の面をカメラへ向け、上端を奥へ倒す。
        /// </summary>
        private void FaceVisualToViewer()
        {
            if (!Utilities.IsValid(visual)) return;
            var localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer)) return;
            var head = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            if (hasDesktopHeldAngle)
            {
                visual.rotation = head.rotation * desktopHeldAngle;
                return;
            }
            Vector3 toViewer = head.position - visual.position;
            if (toViewer.sqrMagnitude < 1e-6f) return;
            Vector3 right = head.rotation * Vector3.right;
            Vector3 normal = Quaternion.AngleAxis(DesktopHeldTiltDegrees, right) * toViewer.normalized;
            Vector3 topEdge = Vector3.Cross(right, normal);
            if (topEdge.sqrMagnitude < 1e-6f) return;
            visual.rotation = Quaternion.LookRotation(topEdge.normalized, normal);
        }

        private void RestoreVisual()
        {
            if (Utilities.IsValid(visual)) visual.localRotation = visualBaseLocalRotation;
        }

        public override void OnPickupUseDown()
        {
            // デスクトップは掴むクリックがそのまま Use にも届くので、ここでは切り替えない（短いクリックと P キーで切り替える）。
            var localPlayer = Networking.LocalPlayer;
            if (Utilities.IsValid(localPlayer) && !localPlayer.IsUserInVR()) return;
            if (isHeld) TogglePanelFromInput(false);
        }

        public override void InputGrab(bool value, UdonInputEventArgs args)
        {
            if (value || !isHeld || !Utilities.IsValid(pickup)) return;

            // Legacy pickup versions can ignore AutoHold.No on VR controllers.
            // Release only this pickup's hand, using VRChat's remappable Grab input.
            bool holdingHandReleased =
                (pickup.currentHand == VRC_Pickup.PickupHand.Left && args.handType == HandType.LEFT) ||
                (pickup.currentHand == VRC_Pickup.PickupHand.Right && args.handType == HandType.RIGHT);
            if (holdingHandReleased) pickup.Drop();
        }

        private void Update()
        {
            if (!isHeld || !Utilities.IsValid(pickup)) return;

            var localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer)) return;
            if (!localPlayer.IsUserInVR())
            {
                // Right-click is VRChat's Drop input; keep visibility on a separate key.
                if (Input.GetKeyDown(KeyCode.P)) _TogglePanel();
                return;
            }

            float trigger;
            if (pickup.currentHand == VRC_Pickup.PickupHand.Left)
                trigger = Input.GetAxis("Oculus_CrossPlatform_PrimaryIndexTrigger");
            else if (pickup.currentHand == VRC_Pickup.PickupHand.Right)
                trigger = Input.GetAxis("Oculus_CrossPlatform_SecondaryIndexTrigger");
            else
                return;

            bool pressed = trigger > 0.6f;
            if (hasIndexTriggerSample && pressed && !indexTriggerPressed)
                TogglePanelFromInput(true);

            indexTriggerPressed = pressed;
            hasIndexTriggerSample = true;
        }

        private void TogglePanelFromInput(bool polled)
        {
            if (lastInputToggleWasPolled != polled &&
                (lastInputToggleFrame == Time.frameCount || Time.time - lastInputToggleTime < 0.15f)) return;
            lastInputToggleFrame = Time.frameCount;
            lastInputToggleWasPolled = polled;
            lastInputToggleTime = Time.time;
            _TogglePanel();
        }

        public void _TogglePanel()
        {
            if (isPanelOpen)
            {
                _ClosePanel();
            }
            else
            {
                DeployPanelAtCurrentPosition();
            }
        }

        /// <summary>
        /// 現在のツールの位置・向きに合わせてパレットパネルを展開する。
        /// </summary>
        public void DeployPanelAtCurrentPosition()
        {
            if (!Utilities.IsValid(panelRoot)) return;

            // 表示位置（PanelPosition）があれば毎回そこに出す（前回の位置を引き継がない）。
            if (hasPanelOffset && !Utilities.IsValid(panelAnchor))
                ApplyHeldPanelPose();
            else
                UpdatePanelPosition();
            CapturePanelOffset();

            panelRoot.gameObject.SetActive(true);
            isPanelOpen = true;
            PulseHoldingHand();

            if (Utilities.IsValid(probe)) probe.ResetPrevValid();
            if (Utilities.IsValid(ui))
            {
                ui._RefreshUI();
                ui._BeginShowTransition();
            }
        }

        /// <summary>VR で持っている手を短く振動させ、パネルが出たことを手でも分かるようにする。</summary>
        private void PulseHoldingHand()
        {
            if (!isHeld || !Utilities.IsValid(pickup)) return;
            var localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer) || !localPlayer.IsUserInVR()) return;
            if (pickup.currentHand == VRC_Pickup.PickupHand.None) return;
            localPlayer.PlayHapticEventInHand(pickup.currentHand, 0.08f, 0.5f, 200f);
        }

        public void _ClosePanel()
        {
            if (!Utilities.IsValid(panelRoot)) return;

            if (Utilities.IsValid(ui)) ui._BeginHideTransition();
            else panelRoot.gameObject.SetActive(false);
            isPanelOpen = false;
        }

        /// <summary>
        /// ツールの位置・プレイヤーの向きに基づいてパネルの位置と回転を合わせる。
        /// </summary>
        public void UpdatePanelPosition()
        {
            if (!Utilities.IsValid(panelRoot)) return;

            if (Utilities.IsValid(panelAnchor))
            {
                // シーン上で置いた「PanelPosition」の位置・向きに出す（ツールの子なのでツールと一緒に動く）。
                PlaceAtAnchor();
                return;
            }

            // パネルは常にツールの真上（ツール自身の上方向）に出す。
            // 傾けて持っていても、ツールから見た位置関係は変わらない。
            Quaternion toolFrame = transform.rotation * Quaternion.Inverse(spawnRotation);
            Vector3 panelPos = transform.position + toolFrame * Vector3.up * panelHeightOffset;

            // パネルはツールに対して垂直に立て、ツールの上方向を軸に回して見る人の方へ向ける。
            // （ワールドの水平で向きを決めてからツールの傾きを掛けると、ツールに対して斜めにずれるため）
            Vector3 toolUp = toolFrame * Vector3.up;
            Vector3 facing = toolFrame * Vector3.forward;
            var localPlayer = Networking.LocalPlayer;
            if (Utilities.IsValid(localPlayer))
            {
                var head = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                Vector3 toHead = Vector3.ProjectOnPlane(head.position - panelPos, toolUp);
                if (toHead.sqrMagnitude > 1e-4f) facing = toHead.normalized;
            }

            panelRoot.rotation = Quaternion.LookRotation(facing, toolUp);
            panelRoot.position = panelPos;
        }

        /// <summary>
        /// VRChat が Pickup の位置を更新した後、ポインター判定より前に呼ぶ。
        /// </summary>
        public void _FollowHeldPanel()
        {
            if (!isHeld || !isPanelOpen || !Utilities.IsValid(panelRoot)) return;

            ApplyHeldPanelPose();
        }

        public void _OnPanelPoseEdited()
        {
            if (isHeld && isPanelOpen) CapturePanelOffset();
        }

        private void CapturePanelOffset()
        {
            if (!Utilities.IsValid(panelRoot)) return;
            panelLocalPositionOffset = Quaternion.Inverse(transform.rotation) * (panelRoot.position - transform.position);
            panelLocalRotationOffset = Quaternion.Inverse(transform.rotation) * panelRoot.rotation;
            hasPanelOffset = true;
        }

        private void ApplyHeldPanelPose()
        {
            if (Utilities.IsValid(panelAnchor))
            {
                // 表示位置はツールの見た目（Visual）の子。デスクトップで見た目の向きを変えても、見た目との関係は変わらない。
                PlaceAtAnchor();
                return;
            }
            panelRoot.position = transform.position + transform.rotation * panelLocalPositionOffset;
            panelRoot.rotation = ConstrainPanelRotation(transform.rotation * panelLocalRotationOffset);
        }

        /// <summary>
        /// デスクトップでは、パネルの向きを「左右の向き」と「上下の傾き」だけにする（ひねり＝画面に対する傾きを取り除く）。
        /// マウスでツールを回すと 3 方向に回ってしまい、パネルの角度を合わせにくいため。
        /// </summary>
        /// <summary>
        /// 表示位置（PanelPosition）にパネルを置く。デスクトップで向きを 2 方向に絞るときは、
        /// 位置も「ひねりを取り除いた向き」を基準に置き直す（向きだけ直すと、ツールを回したときに
        /// パネルの位置だけがツールの周りを回り込み、ツールとの位置関係がずれて見えるため）。
        /// </summary>
        private void PlaceAtAnchor()
        {
            Quaternion anchorRot = panelAnchor.rotation;
            Quaternion rot = ConstrainPanelRotation(anchorRot);
            Transform parent = panelAnchor.parent;
            if (!Utilities.IsValid(parent) || Quaternion.Angle(rot, anchorRot) < 0.01f)
            {
                panelRoot.position = panelAnchor.position;
                panelRoot.rotation = FaceViewer(panelRoot.position, anchorRot);
                return;
            }

            // 親（ツールの見た目）をひねりのない向きに置いたとして、Unity で決めた位置関係のまま置く。
            Quaternion parentRot = rot * Quaternion.Inverse(panelAnchor.localRotation);
            Vector3 offset = Vector3.Scale(parent.lossyScale, panelAnchor.localPosition);
            panelRoot.position = parent.position + parentRot * offset;
            panelRoot.rotation = FaceViewer(panelRoot.position, rot);
        }

        /// <summary>
        /// 見る人に背を向けているパネルは、表裏が反対に見える。その場合だけ、パネルの上方向を軸に 180 度回して表を向ける。
        /// （デスクトップで見た目の向きが変わると、置いた位置の向きが反対になることがある。）
        /// </summary>
        private Quaternion FaceViewer(Vector3 panelPos, Quaternion rotation)
        {
            var localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer)) return rotation;
            var head = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 toHead = head.position - panelPos;
            if (Vector3.Dot(rotation * Vector3.forward, toHead) >= 0f) return rotation;
            return rotation * Quaternion.Euler(0f, 180f, 0f);
        }

        private Quaternion ConstrainPanelRotation(Quaternion rotation)
        {
            if (!desktopPanelYawPitchOnly) return rotation;
            var localPlayer = Networking.LocalPlayer;
            bool desktop = treatAsDesktop || (Utilities.IsValid(localPlayer) && !localPlayer.IsUserInVR());
            if (!desktop) return rotation;

            Vector3 facing = rotation * Vector3.forward;
            // 真上・真下を向いたときは、上下の基準が決まらないので、そのままにする。
            if (Mathf.Abs(Vector3.Dot(facing, Vector3.up)) > 0.98f) return rotation;
            return Quaternion.LookRotation(facing, Vector3.up);
        }

        /// <summary>
        /// ペンスタンド横の初期位置にリセットする。
        /// </summary>
        public void _ResetToSpawn()
        {
            // 持っている最中でも戻せるよう、先に手放す。
            if (isHeld && Utilities.IsValid(pickup)) pickup.Drop();
            _ClosePanel();
            hasPanelOffset = false;

            transform.position = spawnPosition;
            transform.rotation = spawnRotation;
            faceVisualFramesLeft = 0;
            RestoreVisual();

            if (Utilities.IsValid(rb))
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(
            Transform pRoot,
            QppPaletteUI pUi,
            QppPointerProbe pProbe,
            Vector3 spawnPos,
            Quaternion spawnRot)
        {
            panelRoot = pRoot;
            ui = pUi;
            probe = pProbe;
            spawnPosition = spawnPos;
            spawnRotation = spawnRot;
            openOnDrop = false;
        }

        public Transform EditorPanelRoot => panelRoot;
        public Transform EditorPanelAnchor => panelAnchor;

        public void EditorTreatAsDesktop(bool desktop)
        {
            treatAsDesktop = desktop;
        }

        public void EditorSetShortClickSeconds(float seconds)
        {
            shortClickSeconds = seconds;
        }

        public void EditorSetPanelAnchor(Transform anchor)
        {
            panelAnchor = anchor;
        }

        /// <summary>見る人の向きで決める従来の配置を、指定した見る位置で計算する（固定配置の初期値用）。</summary>
        public void EditorAutoPanelPose(Vector3 viewerPosition, out Vector3 localPosition, out Vector3 localEulerAngles)
        {
            Vector3 toolUp = Vector3.up;
            Vector3 panelPos = transform.position + toolUp * panelHeightOffset;
            Vector3 facing = Vector3.ProjectOnPlane(viewerPosition - panelPos, toolUp);
            if (facing.sqrMagnitude < 1e-4f) facing = Vector3.ProjectOnPlane(-transform.forward, toolUp);
            if (facing.sqrMagnitude < 1e-4f) facing = Vector3.back;
            Quaternion rot = Quaternion.LookRotation(facing.normalized, toolUp);
            Quaternion inv = Quaternion.Inverse(transform.rotation);
            localPosition = inv * (panelPos - transform.position);
            localEulerAngles = (inv * rot).eulerAngles;
        }
#endif
    }
}
