using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.View
{
    // ============================================================
    // 牌桌
    //
    // 版面以畫面中心 (0,0) 為基準，1920x1080 下各區塊互不重疊：
    //
    //                      上家資訊     y +290..+530
    //                      牌山上排     y +234..+266
    //                      上家牌河     y  +90..+220
    //   左家       牌山     左家牌河   中央資訊   右家牌河    牌山      右家
    //   資訊      左排      x -275..-105  x ±85   x +105..+275  右排     資訊
    //                      自己牌河     y -220..-90
    //                      牌山下排     y -266..-234
    //                      動作按鈕     y -329..-271（Bootstrap 擺）
    //                      自己手牌     y -536..-331（HandView 擺）
    //
    // 座位換算：玩家永遠在下方，其餘依逆時針排到右、上、左。
    // 左右兩家的手牌是直立的，跟實際坐在牌桌側面看到的一樣。
    // ============================================================

    public class TableView : MonoBehaviour
    {
        static readonly Vector2 DiscardTileSize = new Vector2(34f, 46f);
        static readonly Vector2 OpponentTileSize = new Vector2(30f, 41f);
        static readonly Vector2 OpponentMeldSize = new Vector2(46f, 63f);

        static readonly Vector2 WideDiscardArea = new Vector2(310f, 132f);
        static readonly Vector2 SideDiscardArea = new Vector2(170f, 132f);
        const int WideDiscardsPerRow = 8;
        const int SideDiscardsPerRow = 4;
        const float DiscardGap = 2f;

        /// <summary>四家牌河的中心位置，順序為自己、右、上、左</summary>
        static readonly Vector2[] DiscardCentres =
        {
            new Vector2(0f, -155f),
            new Vector2(190f, 0f),
            new Vector2(0f, 155f),
            new Vector2(-190f, 0f)
        };

        /// <summary>發牌動畫的四個目標位置，順序同上</summary>
        public static readonly Vector2[] SeatAnchors =
        {
            new Vector2(0f, -430f),
            new Vector2(760f, -40f),
            new Vector2(0f, 430f),
            new Vector2(-760f, -40f)
        };

        static readonly Color PanelColor = new Color(0.06f, 0.18f, 0.13f, 0.82f);
        static readonly Color TextColor = new Color(0.90f, 0.92f, 0.88f);
        static readonly Color ActiveColor = new Color(1f, 0.85f, 0.35f);

        /// <summary>對手的手牌與副露要不要立起來擺</summary>
        enum SeatOrientation { Bottom, Right, Top, Left }

        int humanSeat;
        SeatSlot[] slots;
        Text centreInfo;
        WallView wallView;
        readonly List<GameObject> transientTiles = new List<GameObject>();

        class SeatSlot
        {
            public Text Header;
            public RectTransform HandRow;
            public RectTransform MeldRow;
            public RectTransform DiscardArea;
            public SeatOrientation Orientation;
        }

        // ------------------------------------------------------------

        public static TableView Create(Transform parent, int humanSeat)
        {
            var rect = UIFactory.CreateRect("TableView", parent);
            UIFactory.Stretch(rect);

            var view = rect.gameObject.AddComponent<TableView>();
            view.humanSeat = humanSeat;
            view.Build();
            return view;
        }

        void Build()
        {
            // 牌山先建，讓它墊在牌河底下
            wallView = WallView.Create(transform);

            slots = new SeatSlot[GameState.PlayerCount];
            slots[0] = BuildSlot(0, SeatOrientation.Bottom, "SeatBottom",
                                 new Vector2(-790f, -300f), new Vector2(300f, 36f));
            slots[1] = BuildSlot(1, SeatOrientation.Right, "SeatRight",
                                 new Vector2(755f, 40f), new Vector2(370f, 470f));
            slots[2] = BuildSlot(2, SeatOrientation.Top, "SeatTop",
                                 new Vector2(0f, 410f), new Vector2(1040f, 240f));
            slots[3] = BuildSlot(3, SeatOrientation.Left, "SeatLeft",
                                 new Vector2(-755f, 40f), new Vector2(370f, 470f));

            BuildCentre();
        }

        SeatSlot BuildSlot(int index, SeatOrientation orientation, string name,
                           Vector2 panelCentre, Vector2 panelSize)
        {
            var slot = new SeatSlot { Orientation = orientation };

            var panel = UIFactory.CreateRect(name, transform);
            UIFactory.Anchor(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), panelCentre, panelSize);

            var headerAnchor = orientation == SeatOrientation.Bottom
                ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
            slot.Header = UIFactory.CreateText("Header", panel, "", 26, TextColor, headerAnchor);
            UIFactory.Anchor(slot.Header.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                             new Vector2(0f, -18f), new Vector2(panelSize.x, 34f));

            slot.HandRow = UIFactory.CreateRect("Hand", panel);
            slot.MeldRow = UIFactory.CreateRect("Melds", panel);
            LayoutSlotRows(slot, panelSize, orientation);

            // 牌河獨立於資訊面板，直接放在牌山圍出來的中央區塊
            slot.DiscardArea = UIFactory.CreateRect(name + "Discards", transform);
            var areaSize = IsVertical(orientation) ? SideDiscardArea : WideDiscardArea;
            UIFactory.Anchor(slot.DiscardArea, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             DiscardCentres[index], areaSize);
            return slot;
        }

        static void LayoutSlotRows(SeatSlot slot, Vector2 panelSize, SeatOrientation orientation)
        {
            if (orientation == SeatOrientation.Bottom)
            {
                // 自己的手牌與副露由 HandView 負責，這裡只留一行小標
                UIFactory.Anchor(slot.HandRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                 Vector2.zero, Vector2.zero);
                UIFactory.Anchor(slot.MeldRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                 Vector2.zero, Vector2.zero);
                return;
            }

            if (IsVertical(orientation))
            {
                // 左右兩家：手牌直立成一排，副露排在旁邊
                UIFactory.Anchor(slot.HandRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                 new Vector2(-70f, -50f), new Vector2(60f, 380f));
                UIFactory.Anchor(slot.MeldRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                 new Vector2(90f, -50f), new Vector2(200f, 380f));
                return;
            }

            UIFactory.Anchor(slot.HandRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                             new Vector2(0f, -62f), new Vector2(panelSize.x, 44f));
            UIFactory.Anchor(slot.MeldRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                             new Vector2(0f, -120f), new Vector2(panelSize.x, 66f));
        }

        static bool IsVertical(SeatOrientation orientation)
            => orientation == SeatOrientation.Left || orientation == SeatOrientation.Right;

        void BuildCentre()
        {
            var centre = UIFactory.CreateImage("Centre", transform, PanelColor);
            UIFactory.Anchor(centre.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             Vector2.zero, new Vector2(170f, 150f));

            centreInfo = UIFactory.CreateText("Info", centre.transform, "", 23, TextColor);
            UIFactory.Stretch(centreInfo.rectTransform, 8f);
        }

        // ------------------------------------------------------------

        public void Refresh(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            ClearTransientTiles();
            wallView.Refresh(state.Wall == null ? 0 : state.Wall.Remaining);

            for (int offset = 0; offset < GameState.PlayerCount; offset++)
            {
                int seat = (humanSeat + offset) % GameState.PlayerCount;
                RefreshSeat(slots[offset], state, seat);
            }
            RefreshCentre(state);
        }

        void RefreshSeat(SeatSlot slot, GameState state, int seat)
        {
            var player = state.Players[seat];
            bool isTurn = state.CurrentPlayer == seat && state.Phase != GamePhase.Ended;

            slot.Header.text = BuildSeatHeader(state, player, seat);
            slot.Header.color = isTurn ? ActiveColor : TextColor;
            slot.Header.fontStyle = isTurn ? FontStyle.Bold : FontStyle.Normal;

            if (slot.Orientation != SeatOrientation.Bottom)
            {
                LayoutFaceDownHand(slot, player.ConcealedTileCount);
                LayoutMelds(slot, player.Melds);
            }
            LayoutDiscardTiles(slot, player.Discards);
        }

        string BuildSeatHeader(GameState state, PlayerState player, int seat)
        {
            bool chinese = UiFont.SupportsChinese;
            string wind = WindName(player.SeatWind) + (chinese ? "家" : "");
            string dealer = seat == state.DealerIndex ? (chinese ? "・莊" : " (D)") : "";
            string you = seat == humanSeat ? (chinese ? "・你" : " YOU") : "";
            string flowers = player.Flowers.Count > 0
                ? (chinese ? "・花" : " F") + player.Flowers.Count
                : "";
            return wind + dealer + you + flowers;
        }

        /// <summary>對手蓋著的手牌。左右兩家要立起來擺，跟坐在側邊看到的一樣。</summary>
        void LayoutFaceDownHand(SeatSlot slot, int tileCount)
        {
            bool vertical = IsVertical(slot.Orientation);
            float step = (vertical ? OpponentTileSize.x : OpponentTileSize.x) + 2f;
            float start = -(tileCount - 1) * step * 0.5f;

            for (int i = 0; i < tileCount; i++)
            {
                var tileView = TileView.Create(slot.HandRow, TileView.NoTile, OpponentTileSize, faceUp: false);
                tileView.SetInteractable(false);

                Vector2 position = vertical
                    ? new Vector2(0f, -(start + i * step))
                    : new Vector2(start + i * step, 0f);
                UIFactory.Anchor(tileView.Rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                 position, OpponentTileSize);
                if (vertical) tileView.Rect.localRotation = Quaternion.Euler(0f, 0f, 90f);

                transientTiles.Add(tileView.gameObject);
            }
        }

        /// <summary>
        /// 副露一律保持正立，只是左右兩家改成由上往下排。
        /// 立起來雖然更接近真實牌桌，但牌面文字會變成側躺看不清楚。
        /// </summary>
        void LayoutMelds(SeatSlot slot, List<Meld> melds)
        {
            bool vertical = IsVertical(slot.Orientation);
            float cursor = 0f;

            foreach (var meld in melds)
            {
                int[] tiles = meld.Tiles();
                for (int i = 0; i < tiles.Length; i++)
                {
                    bool faceUp = !(meld.Type == MeldType.AnKan && (i == 0 || i == tiles.Length - 1));
                    var tileView = TileView.Create(slot.MeldRow, tiles[i], OpponentMeldSize, faceUp);
                    tileView.SetInteractable(false);

                    Vector2 position = vertical
                        ? new Vector2(0f, -cursor)
                        : new Vector2(cursor, 0f);
                    var anchor = vertical ? new Vector2(0.5f, 1f) : new Vector2(0f, 0.5f);
                    UIFactory.Anchor(tileView.Rect, anchor, anchor, position, OpponentMeldSize);

                    transientTiles.Add(tileView.gameObject);
                    cursor += (vertical ? OpponentMeldSize.y : OpponentMeldSize.x) + 1f;
                }
                cursor += 12f;
            }
        }

        void LayoutDiscardTiles(SeatSlot slot, List<int> discards)
        {
            int perRow = IsVertical(slot.Orientation) ? SideDiscardsPerRow : WideDiscardsPerRow;
            float stepX = DiscardTileSize.x + DiscardGap;
            float stepY = DiscardTileSize.y + DiscardGap;
            float rowOffset = (perRow - 1) * 0.5f;

            for (int i = 0; i < discards.Count; i++)
            {
                int column = i % perRow;
                int row = i / perRow;

                var tileView = TileView.Create(slot.DiscardArea, discards[i], DiscardTileSize);
                tileView.SetInteractable(false);
                UIFactory.Anchor(tileView.Rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                 new Vector2((column - rowOffset) * stepX, -row * stepY),
                                 DiscardTileSize);
                transientTiles.Add(tileView.gameObject);
            }
        }

        void RefreshCentre(GameState state)
        {
            int remaining = state.Wall == null ? 0 : state.Wall.Remaining;

            if (!UiFont.SupportsChinese)
            {
                centreInfo.text = string.Format("{0} round\n{1} tiles left\ndealer streak {2}",
                    WindName(state.RoundWind), remaining, state.DealerStreak);
                return;
            }

            // 「圈風 東」「牌山 76」看不懂在說什麼，改成完整句子
            string streak = state.DealerStreak > 0
                ? "\n莊家連 " + state.DealerStreak + " 拉 " + state.DealerStreak
                : "";
            centreInfo.text = string.Format("{0}風圈\n剩 {1} 張{2}",
                TileDef.Name(state.RoundWind), remaining, streak);
        }

        static string WindName(int wind)
        {
            if (UiFont.SupportsChinese) return TileDef.Name(wind);
            switch (wind)
            {
                case TileDef.EAST: return "E";
                case TileDef.SOUTH: return "S";
                case TileDef.WEST: return "W";
                default: return "N";
            }
        }

        void ClearTransientTiles()
        {
            // Destroy 要到影格結束才生效，先關掉才不會跟新畫的牌疊在一起
            foreach (var tile in transientTiles)
            {
                if (tile == null) continue;
                tile.SetActive(false);
                Destroy(tile);
            }
            transientTiles.Clear();
        }
    }
}
