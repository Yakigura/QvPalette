using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakigura.QvPalette
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QppPaletteUI : UdonSharpBehaviour
    {
        [Header("Hierarchy")]
        [SerializeField] private Transform panelRoot;
        [SerializeField] private QppPalette palette;
        // 操作のログ（クリック、バインドなど）を出す。公開版の既定はオフ。警告とエラーは常に出る。
        [SerializeField] private bool debugLog = false;

        [Header("Pens (パレットは 1 枚で、持ったペンに動的にバインドする)")]
        [SerializeField] private QppPenBinding[] bindings;
        [SerializeField] private QppPenState[] penStates;

        [Header("UI Components")]
        [SerializeField] private QppSwatchButton[] swatchButtons;
        [SerializeField] private QppFavoriteButton[] favoriteButtons;
        [SerializeField] private QppFavorites favorites;
        [SerializeField] private QppPenSizeSlider penSizeSlider;
        [Tooltip("bindings と同じ並び。ペンごとの大きさ。")]
        [SerializeField] private QppPenSize[] penSizes;
        [SerializeField] private QppWidthSlider widthSlider;
        [SerializeField] private QppWidthPresetButton[] widthPresetButtons;
        [Tooltip("太さプリセットの値（mm）。スライドに加えて固定値へ一発で切り替える。")]
        [SerializeField] private float[] widthPresetMillimeters = new float[] { 3f, 5f, 10f, 30f };
        [SerializeField] private TextMeshPro pageText;
        [SerializeField] private TextMeshProUGUI pageTextUGUI;
        [SerializeField] private TextMeshPro widthText;
        [SerializeField] private GameObject busyIndicator;
        [SerializeField] private GameObject readOnlyIndicator;
        [SerializeField] private GameObject pinIndicator;
        [Tooltip("バインド中のペンの台帳が満杯で、色・太さを切り替えられないときに出す表示。")]
        [SerializeField] private GameObject fullIndicator;
        [Tooltip("ペン先・指先が今どの操作対象に触れているかを示す枠。")]
        [SerializeField] private Transform focusHighlight;
        [SerializeField] private Renderer focusRenderer;
        [SerializeField] private Color focusColor = new Color(1f, 1f, 1f, 0.22f);
        [SerializeField] private Color focusFullColor = new Color(1f, 1f, 1f, 0.55f);
        [SerializeField] private Color focusTriggeredColor = new Color(0.25f, 0.95f, 1f, 0.75f);
        [SerializeField] private QppPaletteTool paletteTool;
        [SerializeField] private QppControlButton[] controlButtons;
        [Tooltip("タグ絞り込みボタン。0 番は「すべて」、i 番はタグ i-1。")]
        [SerializeField] private QppControlButton[] tagButtons;

        [Header("Open / Close Effect")]
        [Tooltip("開くときにツールから飛び出す絵の具の粒。")]
        [SerializeField] private ParticleSystem openEffect;
        [SerializeField] private float showSeconds = 0.34f;
        [SerializeField] private float hideSeconds = 0.2f;
        [Tooltip("開き始めの傾き（度）。ツールの上で起き上がるように見せる。")]
        [SerializeField] private float showTiltDegrees = -70f;
        [SerializeField] private Color tagColor = new Color(0.95f, 0.95f, 1f, 1f);
        [SerializeField] private Color tagActiveColor = new Color(0.3f, 0.95f, 1f, 1f);
        [Tooltip("タグボタンの下地の色（選んでいないとき / 選んでいるとき）。")]
        [SerializeField] private Color tagPlateColor = new Color(0.25f, 0.27f, 0.33f, 1f);
        [SerializeField] private Color tagActivePlateColor = new Color(0.1f, 0.45f, 0.6f, 1f);

        [Header("3-State Display (Qvシリーズ構成 §3)")]
        [Tooltip("非操作中（＝ペンで描画中）に減光する対象。")]
        [SerializeField] private Renderer[] dimRenderers;
        [Tooltip("非操作中に減光する文字。")]
        [SerializeField] private TextMeshPro[] dimTexts;
        [Tooltip("非操作中に当たり判定を切る対象。原則 4 により操作していない UI は当たり判定を持たない。")]
        [SerializeField] private Collider[] touchColliders;
        [Tooltip("通常時のパネル板の不透明度。かなり透明な半透明表現。")]
        [SerializeField] private float panelOpacity = 0.35f;
        [Tooltip("非操作中（描画中）のパネル板の不透明度。")]
        [SerializeField] private float inactiveOpacity = 0.15f;
        [Tooltip("非操作中の文字の不透明度。読めなくならないよう板より高めにする。")]
        [SerializeField] private float inactiveTextOpacity = 0.6f;
        [SerializeField] private float fadeSeconds = 0.15f;

        [Header("Settings")]
        [SerializeField] private float swatchPreviewWidth = 0.015f;
        [Tooltip("お気に入りへ登録する長押し時間（秒）。")]
        [SerializeField] private float favoriteHoldSeconds = 2.0f;

        private int currentPage = 0;

        // タグ絞り込み（ローカルのみ）。filteredStyles に表示する styleId を並べ、16 個ずつページにする。
        private int activeTag = -1;
        private int[] filteredStyles;
        private int filteredCount;
        private bool filterBuilt;
        private int currentSelectedStyleId = 0;
        private int currentWidthIndex = QppCommon.DEFAULT_WIDTH_INDEX;
        private bool isPinned = false;

        private Gradient[] previewGradients;
        private GradientColorKey[] previewColorKeys;
        private GradientAlphaKey[] previewAlphaKeys;
        private MaterialPropertyBlock propertyBlock;
        private float busyShowUntil = -1f;

        private Color[] dimBaseColors;
        private float currentOpacity = 0.7f;
        private float appliedOpacity = -1f;
        private bool lastInteractive = true;
        private bool collidersInitialized;
        private Transform transitionDisplayRoot;
        private Vector3 transitionDisplayScale = Vector3.one;
        private Vector3 transitionDisplayPos;
        private Quaternion transitionDisplayRot = Quaternion.identity;
        private Vector3 transitionFromLocal;
        private bool transitionActive;
        private bool transitionClosing;
        private float transitionElapsed;
        private float transitionStartScale = 1f;
        private const float HiddenDisplayScale = 0.05f;

        private int favContactSlot = -1;
        private int favContactFrame = -1;
        private float favHoldTimer = 0f;
        private float favContactTime = -1f;
        // 長押し中に一瞬途切れても離したとはみなさない（途切れて呼び出しが走ると、登録済みのインクに
        // 切り替わってしまい、上書き登録できなくなるため）。
        private const float FavoriteReleaseGraceSeconds = 0.3f;
        // これより長く触れてから離したときは呼び出さない（登録しかけてやめた操作）。
        private const float FavoriteTapMaxSeconds = 0.5f;
        private bool favSavedThisContact = false;

        private int lastWidthTextIndex = -1;
        private bool isUserInVR;

        private const int GRAD_SLIDER = 16;
        private const int GRAD_FAVORITE_BASE = 17;
        private const int GRAD_COUNT = 25;

        private int activeSlot = -1;

        public Transform PanelRoot => panelRoot;
        public QppPaletteTool PaletteTool => paletteTool;
        public int ActiveSlot => activeSlot;

        /// <summary>今このパレットがバインドされているペン。未バインドなら null。</summary>
        public QppPenBinding[] Bindings => bindings;
        public QppPenBinding ActiveBinding =>
            (bindings != null && activeSlot >= 0 && activeSlot < bindings.Length) ? bindings[activeSlot] : null;

        private QppPenState ActiveState =>
            (penStates != null && activeSlot >= 0 && activeSlot < penStates.Length) ? penStates[activeSlot] : null;
        public QppSwatchButton[] SwatchButtons => swatchButtons;
        public QppFavoriteButton[] FavoriteButtons => favoriteButtons;
        public QppWidthSlider WidthSlider => widthSlider;
        public QppPenSizeSlider PenSizeSlider => penSizeSlider;
        private const float PenScaleApplyDelay = 0.5f;
        // 数値だけだと何の太さか分かりにくいので、線の太さであることを添える（フォント依存を避けるため ASCII 表記）。
        private const string WidthLabelPrefix = "Width ";
        private float penScaleChangedAt;
        private bool penScaleApplyScheduled;
        public float CurrentPenScale => Utilities.IsValid(favorites) ? favorites.PenScale : 1f;
        public QppWidthPresetButton[] WidthPresetButtons => widthPresetButtons;
        public QppControlButton[] ControlButtons => controlButtons;
        public int CurrentWidthIndex => currentWidthIndex;
        public int CurrentSelectedStyleId => currentSelectedStyleId;
        public bool IsPinned => isPinned;

        private void Start()
        {
            isPinned = true; // 空間固定がデフォルト（絵の具パレット型）
            if (Utilities.IsValid(palette)) currentWidthIndex = palette.DefaultWidthIndex;
            previewGradients = new Gradient[GRAD_COUNT];
            for (int i = 0; i < GRAD_COUNT; i++)
            {
                previewGradients[i] = new Gradient();
            }

            previewColorKeys = new GradientColorKey[2];
            previewAlphaKeys = new GradientAlphaKey[2];
            previewAlphaKeys[0] = new GradientAlphaKey(1f, 0f);
            previewAlphaKeys[1] = new GradientAlphaKey(1f, 1f);

            propertyBlock = new MaterialPropertyBlock();

            var localPlayer = Networking.LocalPlayer;
            isUserInVR = Utilities.IsValid(localPlayer) && localPlayer.IsUserInVR();

            CacheDimBaseColors();

            if (Utilities.IsValid(panelRoot))
            {
                Vector3 scale = panelRoot.localScale;
                if (Mathf.Abs(scale.x - 1f) > 0.01f || Mathf.Abs(scale.y - 1f) > 0.01f || Mathf.Abs(scale.z - 1f) > 0.01f)
                {
                    Debug.LogWarning("[QPP] panelRoot localScale is not (1,1,1). §4.3 convention requires (1,1,1).");
                }
            }

            if (Utilities.IsValid(pinIndicator))
                pinIndicator.SetActive(isPinned);

            // 一度もペンを持っていない間は、どのペンにも紐づけない（知らないうちに 1 本目のペンを変えないため）。
            RefreshUI();
        }

        private void OnEnable()
        {
            RefreshUI();
        }

        private void CacheTransitionRoot()
        {
            if (Utilities.IsValid(transitionDisplayRoot) || !Utilities.IsValid(panelRoot)) return;
            transitionDisplayRoot = panelRoot.Find("DisplayRoot");
            if (Utilities.IsValid(transitionDisplayRoot))
            {
                transitionDisplayScale = transitionDisplayRoot.localScale;
                transitionDisplayPos = transitionDisplayRoot.localPosition;
                transitionDisplayRot = transitionDisplayRoot.localRotation;
            }
        }

        /// <summary>ツールの位置（パネルのローカル座標）。ツールが無ければパネルの少し下。</summary>
        private Vector3 ToolLocalPosition()
        {
            if (Utilities.IsValid(paletteTool) && Utilities.IsValid(panelRoot))
                return panelRoot.InverseTransformPoint(paletteTool.transform.position);
            return transitionDisplayPos + Vector3.down * 0.1f;
        }

        // Only the display moves: the panel pose stays where the tool put it, and touch is off until the end.
        public void _BeginShowTransition()
        {
            CacheTransitionRoot();
            transitionClosing = false;
            transitionActive = Utilities.IsValid(transitionDisplayRoot);
            transitionElapsed = 0f;
            if (transitionActive)
            {
                transitionStartScale = transitionDisplayScale.x != 0f
                    ? transitionDisplayRoot.localScale.x / transitionDisplayScale.x
                    : 1f;
                if (transitionStartScale >= 0.999f)
                    transitionStartScale = HiddenDisplayScale;
                transitionFromLocal = ToolLocalPosition();
                transitionDisplayRoot.localScale = transitionDisplayScale * transitionStartScale;
                transitionDisplayRoot.localPosition = transitionFromLocal;
            }

            // 板と文字を透明から立ち上げる（フェードは UpdateDisplayState が行う）。
            currentOpacity = 0f;
            appliedOpacity = -1f;

            if (Utilities.IsValid(openEffect))
            {
                if (Utilities.IsValid(paletteTool)) openEffect.transform.position = paletteTool.transform.position;
                openEffect.Play();
            }
            collidersInitialized = false;
        }

        public void _BeginHideTransition()
        {
            CacheTransitionRoot();
            transitionClosing = true;
            transitionActive = Utilities.IsValid(transitionDisplayRoot);
            transitionElapsed = 0f;
            if (transitionActive)
            {
                transitionStartScale = transitionDisplayScale.x != 0f
                    ? transitionDisplayRoot.localScale.x / transitionDisplayScale.x
                    : 1f;
                transitionFromLocal = ToolLocalPosition();
            }
            ApplyColliderState(false);
            if (!transitionActive && Utilities.IsValid(panelRoot))
                panelRoot.gameObject.SetActive(false);
        }

        private void UpdateTransition()
        {
            if (!transitionActive) return;
            transitionElapsed += Time.deltaTime;
            float duration = transitionClosing ? hideSeconds : showSeconds;
            float t = duration > 0.001f ? Mathf.Clamp01(transitionElapsed / duration) : 1f;

            float scale;
            float move;
            float tilt;
            if (transitionClosing)
            {
                // 吸い込まれるように加速して縮む。
                float e = t * t * t;
                scale = Mathf.Lerp(transitionStartScale, HiddenDisplayScale, e);
                move = 1f - e;
                tilt = showTiltDegrees * e;
            }
            else
            {
                // ツールから飛び出し、少し行き過ぎてから収まる（ease-out-back）。
                float u = t - 1f;
                float back = 1f + 2.70158f * u * u * u + 1.70158f * u * u;
                float cubic = 1f + u * u * u;
                scale = Mathf.LerpUnclamped(transitionStartScale, 1f, back);
                move = cubic;
                tilt = showTiltDegrees * (1f - cubic);
            }

            transitionDisplayRoot.localScale = transitionDisplayScale * scale;
            transitionDisplayRoot.localPosition = Vector3.LerpUnclamped(transitionFromLocal, transitionDisplayPos, move);
            transitionDisplayRoot.localRotation = transitionDisplayRot * Quaternion.Euler(tilt, 0f, 0f);
            if (transitionElapsed < duration) return;

            transitionActive = false;
            transitionDisplayRoot.localScale = transitionDisplayScale;
            transitionDisplayRoot.localPosition = transitionDisplayPos;
            transitionDisplayRoot.localRotation = transitionDisplayRot;
            if (transitionClosing && Utilities.IsValid(panelRoot))
                panelRoot.gameObject.SetActive(false);
        }

        private void CacheDimBaseColors()
        {
            if (dimRenderers == null)
            {
                dimBaseColors = new Color[0];
                return;
            }

            dimBaseColors = new Color[dimRenderers.Length];
            for (int i = 0; i < dimRenderers.Length; i++)
            {
                Color c = Color.white;
                if (Utilities.IsValid(dimRenderers[i]) && dimRenderers[i].sharedMaterial != null)
                {
                    c = dimRenderers[i].sharedMaterial.color;
                }
                dimBaseColors[i] = c;
            }
        }

        /// <summary>
        /// 「開・待機」と「開・非操作中」の判定。QvPalette の非操作中はペンで描画している間。
        /// 描画中に色や太さを触れないようにするロックも兼ねる。
        /// </summary>
        /// <summary>バインド中のペンが今まさに描いているか（＝§3 でいう「非操作中」）。</summary>
        private bool IsPenDrawing
        {
            get
            {
                var b = ActiveBinding;
                return Utilities.IsValid(b) && b.IsDrawing;
            }
        }

        /// <summary>
        /// 操作を受け付けるか。
        ///
        /// 描画中に当たり判定を切るのは、VR でペン先がスウォッチを薙ぎ払う誤爆を防ぐため
        /// （§3 原則 4）。Desktop はクリックがそのまま QvPen の描画開始でもあるので、
        /// ここで遮断すると「押した瞬間に押せなくなる」状態になってしまう。
        /// したがって入力の遮断は VR のみとし、減光は両方で行う。
        /// </summary>
        public bool CanOperate()
        {
            if (!gameObject.activeInHierarchy) return false;
            if (transitionActive || transitionClosing) return false;
            if (isUserInVR && IsPenDrawing) return false;
            return true;
        }

        private bool CanChangeActivePen()
        {
            if (!CanOperate()) return false;
            var binding = ActiveBinding;
            return !Utilities.IsValid(binding) || binding.CanEdit;
        }

        /// <summary>
        /// ペンを持った瞬間にそのペンへバインドする（Qvシリーズ構成 §8）。
        /// バインドと同時に、そのペンの現在の色と太さをパレットUIに読み込んで反映する。
        /// </summary>
        public void _BindTo(int slot)
        {
            if (slot == activeSlot) return;
            if (bindings == null || slot < 0 || slot >= bindings.Length) return;

            activeSlot = slot;

            var b = ActiveBinding;
            if (hasPendingSelection)
            {
                // 紐づいていない間に選んだ色・太さを、このペンに反映する（ペンの元の色は上書き）。
                pendingSelectionSince = Time.time;
                TryApplyPendingSelection();
                RefreshUI();
            }
            else if (Utilities.IsValid(b))
            {
                _RefreshSelection(b.CurrentStyleId, b.CurrentWidthIndex);
            }
            else
            {
                RefreshUI();
            }

            if (debugLog) Debug.Log($"[QPP] Palette bound to pen slot {slot}.");
        }

        /// <summary>バインド中のペンの同期状態が変わったときに QppPenState から呼ばれる。</summary>
        public void _OnPenStateChanged(int slot, int styleId, int widthIndex)
        {
            if (slot != activeSlot) return;
            // 紐づいていない間に選んだものを反映する前は、ペン側の色でパレットの選択を上書きしない。
            if (hasPendingSelection) return;
            _RefreshSelection(styleId, widthIndex);
        }

        /// <summary>
        /// 触れている対象の後ろに枠を出す。留める時間が満ちるほど明るくなり、決定した瞬間に色が変わる。
        /// center / size は DisplayRoot のローカル座標（各ボタンの RectCenter / RectSize と同じ）。
        /// </summary>
        public void _SetFocus(Vector2 center, Vector2 size, float progress)
        {
            if (!Utilities.IsValid(focusHighlight)) return;
            if (!focusHighlight.gameObject.activeSelf) focusHighlight.gameObject.SetActive(true);

            // ボタン（z=0）と板（z=+0.006）の間に置き、ボタンの縁から少しはみ出させる。
            focusHighlight.localPosition = new Vector3(center.x, center.y, 0.003f);
            focusHighlight.localScale = new Vector3(size.x + 0.008f, size.y + 0.008f, 1f);

            if (!Utilities.IsValid(focusRenderer)) return;
            Color c = progress >= 1f ? focusTriggeredColor : Color.Lerp(focusColor, focusFullColor, progress);
            if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
            propertyBlock.Clear();
            propertyBlock.SetColor("_Color", c);
            focusRenderer.SetPropertyBlock(propertyBlock);
        }

        public void _ClearFocus()
        {
            if (Utilities.IsValid(focusHighlight) && focusHighlight.gameObject.activeSelf)
                focusHighlight.gameObject.SetActive(false);
        }

        public void _ShowLedgerFullFor(int slot)
        {
            if (slot != activeSlot) return;
            if (Utilities.IsValid(fullIndicator)) fullIndicator.SetActive(true);
        }

        private void RefreshFullIndicator()
        {
            if (!Utilities.IsValid(fullIndicator)) return;
            var state = ActiveState;
            fullIndicator.SetActive(Utilities.IsValid(state) && state.IsLedgerFull);
        }

        public void _ShowSyncBusyFor(int slot)
        {
            if (slot != activeSlot) return;
            _ShowSyncBusy();
        }

        /// <summary>
        /// パレット展開中、ローカルプレイヤーが持っているペンを自動検知してバインドする。
        /// 右手・左手問わず、持っているペンに自動追従する。
        /// </summary>
        private void PollActivePenBinding()
        {
            if (bindings == null || bindings.Length == 0) return;

            int heldSlot = -1;
            for (int i = 0; i < bindings.Length; i++)
            {
                var b = bindings[i];
                if (Utilities.IsValid(b) && b.IsHeldByLocalPlayer)
                {
                    heldSlot = i;
                    break;
                }
            }

            if (heldSlot >= 0)
            {
                if (heldSlot != activeSlot)
                {
                    _BindTo(heldSlot);
                }
            }
            else
            {
                // 置いたあとも、最後に持ったペンに紐づけたままにする（デスクトップはペンを置いてから色を選ぶため）。
                // ただし、そのペンをほかの人が持ったら紐づけを外す（人が使っている・使い終えたペンを知らずに変えないため）。
                var bound = ActiveBinding;
                if (Utilities.IsValid(bound) && bound.IsHeldByAnyone && !bound.IsHeldByLocalPlayer)
                    _Unbind();
            }

            // 紐づいていない間に選んだものは、持ったペンの台帳が届いてから反映する。
            if (hasPendingSelection && activeSlot >= 0) TryApplyPendingSelection();
        }

        private void Update()
        {
            PollActivePenBinding();
            UpdateTransition();
            UpdateDisplayState();
            UpdateFavoriteHold();
            _PollDesktopInteraction();
        }

        /// <summary>
        /// 3 状態表示（§3）。不透明度はフェード、当たり判定は即座に切り替える。
        /// </summary>
        private void UpdateDisplayState()
        {
            bool interactive = CanOperate();

            // 減光するのは VR のみ。Desktop はクリックがそのまま描画開始なので、
            // 操作しようとした瞬間にパネルが薄くなって読めなくなってしまう。
            bool dimmed = isUserInVR && IsPenDrawing;
            float target = dimmed ? inactiveOpacity : panelOpacity;
            if (fadeSeconds > 0.001f)
            {
                currentOpacity = Mathf.MoveTowards(currentOpacity, target, Time.deltaTime / fadeSeconds);
            }
            else
            {
                currentOpacity = target;
            }

            if (Mathf.Abs(currentOpacity - appliedOpacity) > 0.004f)
            {
                appliedOpacity = currentOpacity;
                ApplyOpacity(currentOpacity, dimmed ? inactiveTextOpacity : 1f);
            }

            if (interactive != lastInteractive || !collidersInitialized)
            {
                lastInteractive = interactive;
                collidersInitialized = true;
                ApplyColliderState(interactive);
            }
        }

        private void ApplyOpacity(float opacity, float textOpacity)
        {
            if (dimRenderers != null && dimBaseColors != null)
            {
                for (int i = 0; i < dimRenderers.Length && i < dimBaseColors.Length; i++)
                {
                    var r = dimRenderers[i];
                    if (!Utilities.IsValid(r)) continue;

                    Color c = dimBaseColors[i];
                    c.a = opacity;
                    propertyBlock.Clear();
                    r.GetPropertyBlock(propertyBlock);
                    propertyBlock.SetColor("_Color", c);
                    r.SetPropertyBlock(propertyBlock);
                }
            }

            // TextMeshPro.alpha は Udon に公開されていないため、color の a を書き換える。
            if (dimTexts != null)
            {
                for (int i = 0; i < dimTexts.Length; i++)
                {
                    var t = dimTexts[i];
                    if (!Utilities.IsValid(t)) continue;

                    Color c = t.color;
                    if (Mathf.Abs(c.a - textOpacity) < 0.004f) continue;

                    c.a = textOpacity;
                    t.color = c;
                }
            }
        }

        private void ApplyColliderState(bool interactive)
        {
            if (touchColliders == null) return;
            for (int i = 0; i < touchColliders.Length; i++)
            {
                if (Utilities.IsValid(touchColliders[i]))
                    touchColliders[i].enabled = interactive;
            }
        }

        public void _OnSwatchClicked(int slotIndex)
        {
            if (!CanChangeActivePen()) return;

            int styleId = StyleAt(currentPage, slotIndex);
            if (!Utilities.IsValid(palette) || styleId < 0 || styleId >= palette.EntryCount) return;

            if (debugLog) Debug.Log($"[QPP] _OnSwatchClicked slot={slotIndex}, styleId={styleId}");

            currentSelectedStyleId = styleId;
            ApplyToActivePen();

            RefreshUI();
        }

        public void _OnWidthChanged(int widthIndex)
        {
            if (!CanChangeActivePen()) return;

            currentWidthIndex = widthIndex;
            ApplyToActivePen();

            if (Utilities.IsValid(widthSlider) && Utilities.IsValid(palette))
            {
                widthSlider.UpdateSliderVisual(currentWidthIndex);
                widthSlider.UpdatePreviewLine(
                    palette,
                    currentSelectedStyleId,
                    currentWidthIndex,
                    previewGradients[GRAD_SLIDER],
                    previewColorKeys,
                    previewAlphaKeys,
                    propertyBlock);
            }

            RefreshWidthText();
        }

        /// <summary>太さプリセット（既定 1.0 / 3.0 / 5.0 / 10.0 mm）。</summary>
        public void _OnWidthPresetClicked(int presetIndex)
        {
            if (!CanChangeActivePen()) return;
            if (widthPresetMillimeters == null) return;
            if (presetIndex < 0 || presetIndex >= widthPresetMillimeters.Length) return;

            int widthIndex = WidthIndexOfMillimeters(widthPresetMillimeters[presetIndex]);
            _OnWidthChanged(widthIndex);
            RefreshWidthPresets();
        }

        private int WidthIndexOfMillimeters(float mm)
        {
            return Utilities.IsValid(palette) ? palette.IndexOfWidth(mm * 0.001f) : QppCommon.IndexOfWidth(mm * 0.001f);
        }

        private string WidthTextOf(int widthIndex)
        {
            return Utilities.IsValid(palette) ? palette.MillimetersTextOf(widthIndex) : QppCommon.MillimetersTextOf(widthIndex);
        }

        private void RefreshWidthPresets()
        {
            if (widthPresetButtons == null || widthPresetMillimeters == null) return;

            for (int i = 0; i < widthPresetButtons.Length; i++)
            {
                var pb = widthPresetButtons[i];
                if (!Utilities.IsValid(pb)) continue;

                int idx = pb.PresetIndex;
                if (idx < 0 || idx >= widthPresetMillimeters.Length) continue;

                int widthIndex = WidthIndexOfMillimeters(widthPresetMillimeters[idx]);
                // ボタンには決めた値をそのまま出す（太さは段階で持つので、ごくわずかに違うことがある）。
                pb.SetLabel(QppCommon.MillimetersText(widthPresetMillimeters[idx] * 0.001f));
                if (Utilities.IsValid(palette) && previewGradients != null && previewGradients.Length > GRAD_SLIDER)
                    pb.UpdateSample(palette, currentSelectedStyleId, palette.WidthOf(widthIndex),
                        previewGradients[GRAD_SLIDER], previewColorKeys, previewAlphaKeys, propertyBlock);
                pb.SetSelected(widthIndex == currentWidthIndex);
            }
        }

        // ------------------------------------------------------------------
        // お気に入り 8 スロット（§8）
        // 短く触れると呼び出し、長押しで現在の色と太さを登録する。
        // ------------------------------------------------------------------

        /// <summary>
        /// ペン先・指先・デスクトップのいずれかがスロットに触れている間、毎フレーム呼ばれる。
        /// </summary>
        public void _ReportFavoriteContact(int slot)
        {
            if (!CanOperate()) return;
            if (!Utilities.IsValid(favorites)) return;

            if (slot != favContactSlot)
            {
                ClearFavoriteFill();
                favContactSlot = slot;
                favHoldTimer = 0f;
                favSavedThisContact = false;
            }
            favContactFrame = Time.frameCount;
            favContactTime = Time.time;
        }

        private void UpdateFavoriteHold()
        {
            if (favContactSlot < 0) return;

            bool stillTouching = (Time.frameCount - favContactFrame) <= 1;

            if (stillTouching)
            {
                favHoldTimer += Time.deltaTime;
                float t = favoriteHoldSeconds > 0.001f ? favHoldTimer / favoriteHoldSeconds : 1f;
                SetFavoriteFill(favContactSlot, t);

                if (!favSavedThisContact && favHoldTimer >= favoriteHoldSeconds)
                {
                    favSavedThisContact = true;
                    _SaveFavorite(favContactSlot);
                }
                return;
            }

            if (Time.time - favContactTime < FavoriteReleaseGraceSeconds) return;

            // 短く触れて離したときだけ呼び出す。長押しの途中でやめたときは何もしない。
            if (!favSavedThisContact && favHoldTimer <= FavoriteTapMaxSeconds)
            {
                _ApplyFavorite(favContactSlot);
            }

            ClearFavoriteFill();
            favContactSlot = -1;
            favHoldTimer = 0f;
            favSavedThisContact = false;
        }

        private void SetFavoriteFill(int slot, float t)
        {
            if (favoriteButtons == null) return;
            for (int i = 0; i < favoriteButtons.Length; i++)
            {
                if (!Utilities.IsValid(favoriteButtons[i])) continue;
                favoriteButtons[i].SetFillProgress(favoriteButtons[i].SlotIndex == slot ? t : 0f);
            }
        }

        private void ClearFavoriteFill()
        {
            if (favoriteButtons == null) return;
            for (int i = 0; i < favoriteButtons.Length; i++)
            {
                if (Utilities.IsValid(favoriteButtons[i]))
                    favoriteButtons[i].SetFillProgress(0f);
            }
        }

        public void _ApplyFavorite(int slot)
        {
            if (!CanChangeActivePen()) return;
            if (!Utilities.IsValid(favorites) || !favorites.IsUsed(slot)) return;

            int styleId = favorites.StyleOf(slot);
            int widthIndex = favorites.WidthOf(slot);
            if (Utilities.IsValid(palette) && styleId >= palette.EntryCount) return;

            currentSelectedStyleId = styleId;
            currentWidthIndex = widthIndex;
            // 4×4 のページは変えない（見ていたページのまま、お気に入りのインクに切り替える）。

            if (ApplyToActivePen() && debugLog)
                Debug.Log($"[QPP] Favorite {slot} applied (style={styleId}, width={widthIndex}).");
            RefreshUI();
        }

        public void _SaveFavorite(int slot)
        {
            if (!Utilities.IsValid(favorites)) return;
            favorites._Save(slot, currentSelectedStyleId, currentWidthIndex);
            RefreshFavorites();
        }

        private bool ApplyToActivePen()
        {
            if (activeSlot < 0)
            {
                // どのペンにも紐づいていない。選んだものを覚えておき、次に自分が持ったペンへ反映する。
                hasPendingSelection = true;
                return true;
            }

            var b = ActiveBinding;
            if (Utilities.IsValid(b) && !b.CanEdit)
            {
                _RefreshSelection(b.CurrentStyleId, b.CurrentWidthIndex);
                return false;
            }

            var state = ActiveState;
            if (Utilities.IsValid(state))
            {
                if (!state._SetStyle(currentSelectedStyleId, currentWidthIndex))
                {
                    if (Utilities.IsValid(b)) _RefreshSelection(b.CurrentStyleId, b.CurrentWidthIndex);
                    else _RefreshSelection(state.SyncedStyleId, state.SyncedWidthIndex);
                    return false;
                }
                return true;
            }

            if (Utilities.IsValid(b))
            {
                b.ApplyLocal(currentSelectedStyleId, currentWidthIndex);
            }
            return true;
        }

        // ---- 紐づいていない間の選択 -------------------------------------------------
        // 紐づいていない間に選んだ色・太さ（currentSelectedStyleId / currentWidthIndex）を、次に自分が持ったペンへ反映する。
        // 途中入室の直後は台帳が届く前に反映すると、低すぎるインク番号から記録され、ほかの人の既存の線の色が
        // 変わるおそれがある。そのため、ペンの台帳が届いて追いつくまで（IsDelivered かつ IsCaughtUp）待つ。
        // 台帳がいつまでも届かないときは、選択が宙に浮かないよう PendingSelectionTimeout 秒で反映する。
        private bool hasPendingSelection;
        private float pendingSelectionSince = -1f;
        private const float PendingSelectionTimeout = 10f;

        public bool HasPendingSelection => hasPendingSelection;

        private void _Unbind()
        {
            activeSlot = -1;
            RefreshUI();
        }

        private bool IsActivePenLedgerReady()
        {
            var state = ActiveState;
            if (!Utilities.IsValid(state)) return true;
            var ledger = state.Ledger;
            if (!Utilities.IsValid(ledger)) return true;
            return ledger.IsDelivered && ledger.IsCaughtUp;
        }

        private void TryApplyPendingSelection()
        {
            if (!hasPendingSelection || activeSlot < 0) return;
            var b = ActiveBinding;
            if (Utilities.IsValid(b) && !b.CanEdit) return;
            if (!IsActivePenLedgerReady()
                && pendingSelectionSince >= 0f && Time.time - pendingSelectionSince < PendingSelectionTimeout)
                return;

            hasPendingSelection = false;
            pendingSelectionSince = -1f;
            ApplyToActivePen();
        }

        public void _RefreshSelection(int styleId, int widthIndex)
        {
            currentSelectedStyleId = styleId;
            currentWidthIndex = widthIndex;

            // 4×4 のページは自動では切り替えない（ペンを持ったとき・お気に入りを選んだとき・ペンのインクが
            // 変わったときも、見ているページのまま）。ページを変えるのはページ送りとタグの操作だけ。

            if (gameObject.activeInHierarchy)
            {
                RefreshUI();
            }
        }

        public void _ShowSyncBusy()
        {
            busyShowUntil = Time.time + 1.0f;
            if (Utilities.IsValid(busyIndicator))
                busyIndicator.SetActive(true);
        }

        public void _PollDesktopInteraction()
        {
            if (!gameObject.activeInHierarchy) return;
            if (panelRoot == null || !panelRoot.gameObject.activeSelf) return;
            // ペンを持つ前（activeSlot < 0）でも、選択は保留に入るので、スライダーのドラッグなどはここで処理する。
            // ここで止めると、ペンを持つまでスライダーが Interact のクリックだけになり、押すたびに一定量ずつ増える。

            var localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer) || localPlayer.IsUserInVR()) return;

            bool isDown = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.E);
            bool isHold = Input.GetMouseButton(0);
            if (!isDown && !isHold) return;

            var head = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Ray ray = new Ray(head.position, head.rotation * Vector3.forward);

            Transform disp = panelRoot.Find("DisplayRoot");
            Transform refTr = Utilities.IsValid(disp) ? disp : panelRoot;

            // Method 1: Direct Physics Raycast on known trigger colliders
            if (Physics.Raycast(ray, out RaycastHit hit, 3.0f, ~0, QueryTriggerInteraction.Collide))
            {
                if (isDown && swatchButtons != null)
                {
                    for (int i = 0; i < swatchButtons.Length; i++)
                    {
                        var sb = swatchButtons[i];
                        if (Utilities.IsValid(sb))
                        {
                            var col = sb.GetComponent<Collider>();
                            if (Utilities.IsValid(col) && hit.collider == col)
                            {
                                if (debugLog) Debug.Log($"[QPP] Physics Raycast hit Swatch {sb.SlotIndex}");
                                _OnSwatchClicked(sb.SlotIndex);
                                return;
                            }
                        }
                    }
                }

                if (isDown && controlButtons != null)
                {
                    for (int i = 0; i < controlButtons.Length; i++)
                    {
                        var cb = controlButtons[i];
                        if (Utilities.IsValid(cb))
                        {
                            var col = cb.GetComponent<Collider>();
                            if (Utilities.IsValid(col) && hit.collider == col)
                            {
                                cb._Trigger();
                                return;
                            }
                        }
                    }
                }

                if (Utilities.IsValid(widthSlider))
                {
                    var sCol = widthSlider.GetComponent<Collider>();
                    if (Utilities.IsValid(sCol) && hit.collider == sCol)
                    {
                        Vector3 hitLocal = refTr.InverseTransformPoint(hit.point);
                        widthSlider.OnProbeDrag(hitLocal.x);
                        return;
                    }
                }

                if (Utilities.IsValid(penSizeSlider))
                {
                    var pCol = penSizeSlider.GetComponent<Collider>();
                    if (Utilities.IsValid(pCol) && hit.collider == pCol)
                    {
                        Vector3 hitLocal = refTr.InverseTransformPoint(hit.point);
                        penSizeSlider.OnProbeDrag(hitLocal.x);
                        return;
                    }
                }
            }

            // Method 2: Mathematical plane intersection on DisplayRoot

            Plane plane = new Plane(refTr.forward, refTr.position);
            if (plane.Raycast(ray, out float enter))
            {
                if (enter > 3.0f) return;

                Vector3 hitWorld = ray.GetPoint(enter);
                Vector3 hitLocal = refTr.InverseTransformPoint(hitWorld);

                // 1. Swatch button click check
                if (isDown && swatchButtons != null)
                {
                    for (int i = 0; i < swatchButtons.Length; i++)
                    {
                        var sb = swatchButtons[i];
                        if (Utilities.IsValid(sb))
                        {
                            Vector2 diff = new Vector2(hitLocal.x - sb.RectCenter.x, hitLocal.y - sb.RectCenter.y);
                            if (Mathf.Abs(diff.x) <= sb.RectSize.x * 0.5f && Mathf.Abs(diff.y) <= sb.RectSize.y * 0.5f)
                            {
                                if (debugLog) Debug.Log($"[QPP] Plane Raycast clicked Swatch {i} (style={StyleAt(currentPage, i)})");
                                _OnSwatchClicked(i);
                                return;
                            }
                        }
                    }
                }

                // 2. Favorite slot: click to recall, hold to register
                if (favoriteButtons != null)
                {
                    for (int i = 0; i < favoriteButtons.Length; i++)
                    {
                        var fb = favoriteButtons[i];
                        if (!Utilities.IsValid(fb)) continue;
                        Vector2 fDiff = new Vector2(hitLocal.x - fb.RectCenter.x, hitLocal.y - fb.RectCenter.y);
                        if (Mathf.Abs(fDiff.x) <= fb.RectSize.x * 0.5f && Mathf.Abs(fDiff.y) <= fb.RectSize.y * 0.5f)
                        {
                            _ReportFavoriteContact(fb.SlotIndex);
                            return;
                        }
                    }
                }

                // 3. Width preset buttons
                if (isDown && widthPresetButtons != null)
                {
                    for (int i = 0; i < widthPresetButtons.Length; i++)
                    {
                        var pb = widthPresetButtons[i];
                        if (!Utilities.IsValid(pb)) continue;
                        Vector2 pDiff = new Vector2(hitLocal.x - pb.RectCenter.x, hitLocal.y - pb.RectCenter.y);
                        if (Mathf.Abs(pDiff.x) <= pb.RectSize.x * 0.5f && Mathf.Abs(pDiff.y) <= pb.RectSize.y * 0.5f)
                        {
                            _OnWidthPresetClicked(pb.PresetIndex);
                            return;
                        }
                    }
                }

                // 4. Width slider click & drag check
                if (Utilities.IsValid(widthSlider))
                {
                    Vector2 sDiff = new Vector2(hitLocal.x - widthSlider.SliderCenter.x, hitLocal.y - widthSlider.SliderCenter.y);
                    if (Mathf.Abs(sDiff.x) <= widthSlider.SliderSize.x * 0.5f && Mathf.Abs(sDiff.y) <= widthSlider.SliderSize.y * 0.5f)
                    {
                        widthSlider.OnProbeDrag(hitLocal.x);
                        return;
                    }
                }

                if (Utilities.IsValid(penSizeSlider))
                {
                    Vector2 pDiff = new Vector2(hitLocal.x - penSizeSlider.SliderCenter.x, hitLocal.y - penSizeSlider.SliderCenter.y);
                    if (Mathf.Abs(pDiff.x) <= penSizeSlider.SliderSize.x * 0.5f && Mathf.Abs(pDiff.y) <= penSizeSlider.SliderSize.y * 0.5f)
                    {
                        penSizeSlider.OnProbeDrag(hitLocal.x);
                        return;
                    }
                }

                // 5. Page click check (bottom page number area only, non-overlapping with slider)
                if (isDown && hitLocal.y >= -0.238f && hitLocal.y <= -0.198f && Mathf.Abs(hitLocal.x) <= 0.05f)
                {
                    if (debugLog) Debug.Log("[QPP] Desktop clicked PageText!");
                    _NextPage();
                    return;
                }

                // 6. Control buttons click check (Close, Reset to Stand, etc.)
                if (isDown && controlButtons != null)
                {
                    for (int i = 0; i < controlButtons.Length; i++)
                    {
                        var cb = controlButtons[i];
                        if (!Utilities.IsValid(cb) || !cb.gameObject.activeSelf) continue;
                        Vector2 cDiff = new Vector2(hitLocal.x - cb.RectCenter.x, hitLocal.y - cb.RectCenter.y);
                        if (Mathf.Abs(cDiff.x) <= cb.RectSize.x * 0.5f && Mathf.Abs(cDiff.y) <= cb.RectSize.y * 0.5f)
                        {
                            cb._Trigger();
                            return;
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // 公開 API（§11: ワールド制作者が SendCustomEvent で呼ぶものは "_" 始まり）
        // ------------------------------------------------------------------

        public void _NextPage()
        {
            if (!Utilities.IsValid(palette)) return;
            int maxPage = PageCount() - 1;
            if (currentPage < maxPage)
            {
                currentPage++;
            }
            else
            {
                currentPage = 0;
            }
            RefreshUI();
        }

        public void _PrevPage()
        {
            if (!Utilities.IsValid(palette)) return;
            // 1 ページ目で戻ると、最後のページへ（次へ送るときの、最後 → 1 ページ目と対になる）。
            currentPage = currentPage > 0 ? currentPage - 1 : PageCount() - 1;
            RefreshUI();
        }

        public void _TogglePin()
        {
            isPinned = !isPinned;
            if (Utilities.IsValid(pinIndicator))
                pinIndicator.SetActive(isPinned);
        }

        public void _CloseMenu()
        {
            if (Utilities.IsValid(paletteTool))
            {
                paletteTool._ClosePanel();
                return;
            }

            if (Utilities.IsValid(panelRoot))
            {
                panelRoot.gameObject.SetActive(false);
                return;
            }
            gameObject.SetActive(false);
        }

        public void _OnCloseButtonClicked()
        {
            _CloseMenu();
        }

        public void _OnResetToStandClicked()
        {
            if (Utilities.IsValid(paletteTool))
            {
                paletteTool._ResetToSpawn();
            }
            else
            {
                _CloseMenu();
            }
        }

        public void _RefreshUI()
        {
            RefreshUI();
        }

        /// <summary>ペンの大きさスライダー。好みとして保存し、自分が今持っているペンに反映する。</summary>
        public void _OnPenScaleChanged(float scale)
        {
            scale = QppCommon.ClampPenScale(scale);
            if (Utilities.IsValid(favorites))
            {
                if (favorites.HasSavedPenScale && Mathf.Approximately(favorites.PenScale, scale)) return;
                favorites._SavePenScale(scale);
            }
            if (Utilities.IsValid(penSizeSlider)) penSizeSlider.UpdateVisual(scale);

            // 動かしている間はペンを変えない（ペン先でスライダーを触っていると先端が動いてしまう）。
            // 最後に動かしてから少し待って、対象のペンに反映する。
            penScaleChangedAt = Time.time;
            if (!penScaleApplyScheduled)
            {
                penScaleApplyScheduled = true;
                SendCustomEventDelayedSeconds(nameof(_ApplyPendingPenScale), PenScaleApplyDelay);
            }
        }

        public void _ApplyPendingPenScale()
        {
            float wait = penScaleChangedAt + PenScaleApplyDelay - Time.time;
            if (wait > 0.02f)
            {
                SendCustomEventDelayedSeconds(nameof(_ApplyPendingPenScale), wait);
                return;
            }
            penScaleApplyScheduled = false;
            if (Utilities.IsValid(favorites)) _ApplyPenScaleToTargetPens(favorites.PenScale);
        }

        /// <summary>自分が持っているペンと、パレットで選んでいるペン（ほかの人が持っていなければ）に反映する。</summary>
        public void _ApplyPenScaleToTargetPens(float scale)
        {
            if (bindings == null || penSizes == null) return;
            for (int i = 0; i < bindings.Length && i < penSizes.Length; i++)
            {
                var b = bindings[i];
                if (!Utilities.IsValid(b) || !Utilities.IsValid(penSizes[i])) continue;
                if (b.IsHeldByLocalPlayer || (i == activeSlot && b.CanEdit))
                    penSizes[i]._SetScale(scale);
            }
        }

        private void RefreshPenSizeSlider()
        {
            if (Utilities.IsValid(penSizeSlider) && Utilities.IsValid(favorites))
                penSizeSlider.UpdateVisual(favorites.PenScale);
        }

        // 旧名（既存シーン・既存呼び出しとの互換のために残す。新規は "_" 付きを使う）
        public void NextPage() { _NextPage(); }
        public void PrevPage() { _PrevPage(); }
        public void TogglePin() { _TogglePin(); }
        public void CloseMenu() { _CloseMenu(); }

        public void RefreshUI()
        {
            if (previewGradients == null || previewGradients.Length < GRAD_COUNT) return;
            EnsureFilter();
            RefreshPenSizeSlider();
            if (currentPage > PageCount() - 1) currentPage = PageCount() - 1;
            if (currentPage < 0) currentPage = 0;

            if (swatchButtons != null && Utilities.IsValid(palette))
            {
                for (int i = 0; i < swatchButtons.Length && i < 16; i++)
                {
                    if (!Utilities.IsValid(swatchButtons[i])) continue;
                    int styleId = StyleAt(currentPage, i);
                    swatchButtons[i].ApplySwatchStyle(
                        palette,
                        styleId,
                        previewGradients[i],
                        previewColorKeys,
                        previewAlphaKeys,
                        propertyBlock,
                        swatchPreviewWidth);
                    swatchButtons[i].SetSelected(styleId == currentSelectedStyleId);
                }
            }

            RefreshFavorites();

            if (Utilities.IsValid(widthSlider) && Utilities.IsValid(palette))
            {
                widthSlider.UpdateSliderVisual(currentWidthIndex);
                widthSlider.UpdatePreviewLine(
                    palette,
                    currentSelectedStyleId,
                    currentWidthIndex,
                    previewGradients[GRAD_SLIDER],
                    previewColorKeys,
                    previewAlphaKeys,
                    propertyBlock);
            }

            RefreshWidthText();
            RefreshWidthPresets();

            if (Utilities.IsValid(palette))
            {
                string pText = $"{currentPage + 1}/{PageCount()}";
                if (Utilities.IsValid(pageText)) pageText.text = pText;
                if (Utilities.IsValid(pageTextUGUI)) pageTextUGUI.text = pText;
            }

            var activeBinding = ActiveBinding;
            bool isReadOnly = Utilities.IsValid(activeBinding) && !activeBinding.CanEdit;
            if (Utilities.IsValid(readOnlyIndicator))
                readOnlyIndicator.SetActive(isReadOnly);
            RefreshFullIndicator();
            RefreshTagButtons();

            // 選択表示などで文字色を書き直すと不透明度が 1 に戻るので、次の Update で塗り直させる。
            appliedOpacity = -1f;
        }

        private void RefreshFavorites()
        {
            if (favoriteButtons == null || !Utilities.IsValid(favorites) || !Utilities.IsValid(palette)) return;
            if (previewGradients == null || previewGradients.Length < GRAD_COUNT) return;

            for (int i = 0; i < favoriteButtons.Length; i++)
            {
                var fb = favoriteButtons[i];
                if (!Utilities.IsValid(fb)) continue;

                int slot = fb.SlotIndex;
                int gradIndex = GRAD_FAVORITE_BASE + Mathf.Clamp(slot, 0, QppFavorites.SLOT_COUNT - 1);

                fb.ApplyFavoriteStyle(
                    palette,
                    favorites.IsUsed(slot),
                    favorites.StyleOf(slot),
                    favorites.WidthOf(slot),
                    previewGradients[gradIndex],
                    previewColorKeys,
                    previewAlphaKeys,
                    propertyBlock);
            }
        }

        /// <summary>太さの mm 表記。値が変化したときだけ書き換える（§10 差分検知）。</summary>
        private void RefreshWidthText()
        {
            if (!Utilities.IsValid(widthText)) return;
            if (currentWidthIndex == lastWidthTextIndex) return;

            lastWidthTextIndex = currentWidthIndex;
            widthText.text = WidthLabelPrefix + WidthTextOf(currentWidthIndex);
        }

        private float nextFullCheckTime;

        // ------------------------------------------------------------------
        // タグ絞り込み
        // ------------------------------------------------------------------

        public int ActiveTag => activeTag;

        private void EnsureFilter()
        {
            if (!filterBuilt) RebuildFilter();
        }

        private void RebuildFilter()
        {
            filterBuilt = true;
            int count = Utilities.IsValid(palette) ? palette.EntryCount : 0;
            if (filteredStyles == null || filteredStyles.Length < count) filteredStyles = new int[Mathf.Max(count, 1)];
            filteredCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (activeTag < 0 || palette.EntryHasTag(i, activeTag))
                {
                    filteredStyles[filteredCount] = i;
                    filteredCount++;
                }
            }
        }

        /// <summary>今の絞り込みでのページ数（空でも 1）。</summary>
        public int PageCount()
        {
            EnsureFilter();
            return Mathf.Max(1, (filteredCount + 15) / 16);
        }

        /// <summary>ページ page の slot 番目に並ぶ styleId。無ければ -1。</summary>
        public int StyleAt(int page, int slot)
        {
            EnsureFilter();
            int idx = page * 16 + slot;
            if (slot < 0 || idx < 0 || idx >= filteredCount) return -1;
            return filteredStyles[idx];
        }

        private int FilteredIndexOf(int styleId)
        {
            for (int i = 0; i < filteredCount; i++)
            {
                if (filteredStyles[i] == styleId) return i;
            }
            return -1;
        }

        /// <summary>タグボタン index（0 = すべて）。同じタグをもう一度押すと「すべて」に戻る。</summary>
        public void _SelectTagButton(int index)
        {
            int tag = index - 1;
            if (tag == activeTag || index <= 0) tag = -1;
            if (Utilities.IsValid(palette) && tag >= palette.TagCount) return;

            activeTag = tag;
            RebuildFilter();
            currentPage = 0;
            int idx = FilteredIndexOf(currentSelectedStyleId);
            if (idx >= 0) currentPage = idx / 16;
            RefreshUI();
        }

        public void _OnTagButton0() { _SelectTagButton(0); }
        public void _OnTagButton1() { _SelectTagButton(1); }
        public void _OnTagButton2() { _SelectTagButton(2); }
        public void _OnTagButton3() { _SelectTagButton(3); }
        public void _OnTagButton4() { _SelectTagButton(4); }
        public void _OnTagButton5() { _SelectTagButton(5); }
        public void _OnTagButton6() { _SelectTagButton(6); }
        public void _OnTagButton7() { _SelectTagButton(7); }
        public void _OnTagButton8() { _SelectTagButton(8); }
        public void _OnTagButton9() { _SelectTagButton(9); }

        private void RefreshTagButtons()
        {
            if (tagButtons == null) return;
            int tagCount = Utilities.IsValid(palette) ? palette.TagCount : 0;
            for (int i = 0; i < tagButtons.Length; i++)
            {
                var b = tagButtons[i];
                if (!Utilities.IsValid(b)) continue;

                bool visible = i == 0 ? tagCount > 0 : i - 1 < tagCount;
                if (b.gameObject.activeSelf != visible) b.gameObject.SetActive(visible);
                if (!visible) continue;

                bool active = (i == 0 && activeTag < 0) || (i > 0 && activeTag == i - 1);
                string text = i == 0 ? "すべて" : palette.TagNameOf(i - 1);
                b.SetLabel(active ? "● " + text : text, active ? tagActiveColor : tagColor);
                b.SetBackgroundColor(active ? tagActivePlateColor : tagPlateColor);
            }
        }

        public void _PollBusyStatus()
        {
            if (busyShowUntil > 0f && Time.time >= busyShowUntil)
            {
                busyShowUntil = -1f;
                if (Utilities.IsValid(busyIndicator))
                    busyIndicator.SetActive(false);
            }

            // 満杯は他の人の切替でも起きるので、開いている間は定期的に見直す。
            if (Time.time >= nextFullCheckTime)
            {
                nextFullCheckTime = Time.time + 0.5f;
                if (Utilities.IsValid(panelRoot) && panelRoot.gameObject.activeSelf) RefreshFullIndicator();
            }
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(
            Transform root,
            QppPenBinding[] bs,
            QppPenState[] pss,
            QppPalette pal,
            QppSwatchButton[] swatches,
            QppFavoriteButton[] favButtons,
            QppFavorites favs,
            QppWidthSlider ws,
            QppWidthPresetButton[] presetButtons,
            TextMeshPro pt,
            TextMeshProUGUI ptu,
            TextMeshPro wt,
            GameObject busy,
            GameObject ro,
            GameObject pin,
            Renderer[] dimRends,
            TextMeshPro[] dimTxts,
            Collider[] cols,
            QppPaletteTool pTool = null,
            QppControlButton[] cButtons = null,
            GameObject full = null,
            Transform focus = null,
            QppControlButton[] tButtons = null,
            ParticleSystem effect = null)
        {
            tagButtons = tButtons;
            openEffect = effect;
            filterBuilt = false;
            fullIndicator = full;
            focusHighlight = focus;
            focusRenderer = focus != null ? focus.GetComponent<Renderer>() : null;
            panelRoot = root;
            bindings = bs;
            penStates = pss;
            palette = pal;
            swatchButtons = swatches;
            favoriteButtons = favButtons;
            favorites = favs;
            widthSlider = ws;
            widthPresetButtons = presetButtons;
            pageText = pt;
            pageTextUGUI = ptu;
            widthText = wt;
            busyIndicator = busy;
            readOnlyIndicator = ro;
            pinIndicator = pin;
            dimRenderers = dimRends;
            dimTexts = dimTxts;
            touchColliders = cols;
            paletteTool = pTool;
            controlButtons = cButtons;
            panelOpacity = 0.35f;
            inactiveOpacity = 0.15f;
        }

        public void EditorSetPaletteTool(QppPaletteTool pTool)
        {
            paletteTool = pTool;
        }

        public void EditorSetPenSize(QppPenSizeSlider slider, QppPenSize[] sizes)
        {
            penSizeSlider = slider;
            penSizes = sizes;
        }
#endif
    }
}
