using QvPen.UdonScript;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;

namespace Yakigura.QvPalette
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QppPenPickupListener : QvPen_PenCallbackListener
    {
        [SerializeField] private QppSetup setup;
        [SerializeField] private QppPenLedger ledger;
        [SerializeField] private QppPenState penState;
        [SerializeField] private QppPaletteUI ui;
        [SerializeField] private QppInkObserver observer;
        [SerializeField] private QvPen_PenManager penManager;
        [SerializeField] private QvPen_Pen pen;
        [SerializeField] private int penSlot;
        [SerializeField] private bool closeOnDrop = false;
        [SerializeField] private QppPenSize penSize;
        [SerializeField] private QppFavorites favorites;

        private void Start()
        {
            if (Utilities.IsValid(penManager))
            {
                penManager.Register(this);
            }
        }

        public override void OnPenPickup()
        {
            if (Utilities.IsValid(pen) && pen.IsUser)
            {
                // Late Joiner インク消失対策:
                // 後から入ったユーザーは penManager.InkId が初期値 0 のままになっており、
                // 既存インクと ID が重複すると QvPen_Pen.CreateInkInstance の重複ガードで描画確定後に消えてしまう。
                // プール内の既存最大 inkId および QvPen_Manager の登録キーを調べ、penManager.inkId を最新値以上に進めて修復する。
                if (Utilities.IsValid(penManager))
                {
                    int maxId = -1;
                    if (Utilities.IsValid(observer))
                    {
                        int poolMax = observer.GetMaxInkId(penSlot);
                        if (poolMax > maxId) maxId = poolMax;
                    }

                    var qvMgr = (QvPen_Manager)pen.GetProgramVariable("manager");
                    if (Utilities.IsValid(qvMgr))
                    {
                        var inkDictMap = (DataDictionary)qvMgr.GetProgramVariable("inkDictMap");
                        if (inkDictMap != null)
                        {
                            int penId = QvPenUtilities.Vector3ToInt32(pen.penIdVector);
                            if (inkDictMap.TryGetValue(penId, TokenType.DataDictionary, out var dictToken))
                            {
                                var dict = dictToken.DataDictionary;
                                var keys = dict.GetKeys();
                                int kCount = keys.Count;
                                for (int k = 0; k < kCount; k++)
                                {
                                    int kId = keys[k].Int;
                                    if (kId > maxId) maxId = kId;
                                }
                            }
                        }
                    }

                    if (penManager.InkId <= maxId)
                    {
                        penManager.SetProgramVariable("inkId", maxId + 1);
                    }
                }

                if (Utilities.IsValid(setup))
                {
                    setup._OnPenPickup(penSlot);
                }

                // 持った人の好みの大きさにする（全員に同期される）。
                if (Utilities.IsValid(penSize) && Utilities.IsValid(favorites))
                {
                    penSize._SetScale(favorites.PenScale);
                }

                if (Utilities.IsValid(ledger))
                {
                    ledger._OnPickupByLocalPlayer();
                }
            }
        }

        public override void OnPenDrop()
        {
            if (Utilities.IsValid(penState))
            {
                penState.OnPenDropByLocalPlayer();
            }

            if (closeOnDrop && Utilities.IsValid(ui) && !ui.IsPinned)
            {
                ui.CloseMenu();
            }
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(
            QppSetup s,
            QppPenLedger l,
            QppPenState ps,
            QppPaletteUI u,
            QppInkObserver obs,
            QvPen_PenManager pm,
            QvPen_Pen p,
            int slot,
            bool closeDrop)
        {
            setup = s;
            ledger = l;
            penState = ps;
            ui = u;
            observer = obs;
            penManager = pm;
            pen = p;
            penSlot = slot;
            closeOnDrop = closeDrop;
        }

        public void EditorSetPenSize(QppPenSize size, QppFavorites fav)
        {
            penSize = size;
            favorites = fav;
        }
#endif
    }
}
