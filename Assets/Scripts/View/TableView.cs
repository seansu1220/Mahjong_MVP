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
    //   上家名牌  y +508      上家手牌  y +466      上家副露  y +400
    //   牌山上排  y +234..+266
    //   上家牌河  y  +90..+220
    //   左右名牌  y +300      左右手牌  x ±650（直立）  左右副露  x ±(712..900)
    //   左家牌河  x -275..-105    中央資訊  x ±85    右家牌河  x +105..+275
    //   自己牌河  y -220..-90
    //   牌山下排  y -266..-234
    //   動作按鈕  y -332..-272（Bootstrap 擺）
    //   自己手牌  y -536..-338（HandView 擺）
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

        /// <summary>直立手牌彼此重疊一些，16 張才塞得進畫面高度</summary>
        const float VerticalHandStep = 22f;
        const float HorizontalHandStep = 32f;
        const int RevealColumnsForSideSeats = 2;

        /// <summary>四家牌河的中心位置，順序為自己、右、上、左</summary>
        static readonly Vector2[] DiscardCentres =
        {
            new Vector2(0f, -155f),
            new Vector2(190f, 0f),
            new Vector2(0f, 155f),
            new Vector2(-190f, 0f)
        };

        /// <summary>發牌與摸牌動畫的四個目標位置，順序同上</summary>
        public static readonly Vector2[] SeatAnchors =
        {
            new Vector2(0f, -430f),
            new Vector2(700f, 0f),
            new Vector2(0f, 450f),
            new Vector2(-700f, 0f)
        };

        static readonly Color PanelColor = new Color(0.06f, 0.18f, 0.13f, 0.82f);
        static readonly Color TextColor = new Color(0.90f, 0.92f, 0.88f);
        static readonly Color ActiveColor = new Color(1f, 0.85f, 0.35f);
        static readonly Color WinnerColor = new Color(1f, 0.55f, 0.40f);

        enum SeatOrientation { Bottom, Right, Top, Left }

        int humanSeat;
        SeatSlot[] slots;
        Text centreInfo;
        WallView wallView;
        readonly List<GameObject> transientTiles = new List<GameObject>();

        /// <summary>牌局結束後把三家的手牌翻開，看得到贏家到底做了什麼牌</summary>
        public bool RevealHands { get; set; }

        /// <summary>結算時要標出來的贏家座位，沒有則為 -1</summary>
        public int WinnerSeat { get; set; } = -1;

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

        /// <summary>下一張正常摸牌的位置，供摸牌動畫使用</summary>
        public Vector2 NextDrawPosition => wallView.HeadPosition;

        /// <summary>下一張補牌的位置（牌尾），供槓後補牌動畫使用</summary>
        public Vector2 NextReplacementPosition => wallView.TailPosition;

        /// <summary>某個座位在畫面上的方位，0 是自己（下方）</summary>
        public int DisplayIndexOf(int seat)
            => (seat - humanSeat + GameState.PlayerCount) % GameState.PlayerCount;

        void Build()
        {
            wallView = WallView.Create(transform);   // 先建，讓牌山墊在牌河底下

            slots = new SeatSlot[GameState.PlayerCount];
            slots[0] = BuildSlot(0, SeatOrientation.Bottom);
            slots[1] = BuildSlot(1, SeatOrientation.Right);
            slots[2] = BuildSlot(2, SeatOrientation.Top);
            slots[3] = BuildSlot(3, SeatOrientation.Left);

            BuildCentre();
        }

        SeatSlot BuildSlot(int index, SeatOrientation orientation)
        {
            var slot = new SeatSlot { Orientation = orientation };

            slot.Header = UIFactory.CreateText("Header" + orientation, transform, "", 26, TextColor);
            slot.HandRow = UIFactory.CreateRect("Hand" + orientation, transform);
            slot.MeldRow = UIFactory.CreateRect("Melds" + orientation, transform);
            PlaceSlotRegions(slot, orientation);

            slot.DiscardArea = UIFactory.CreateRect("Discards" + orientation, transform);
            var areaSize = IsVertical(orientation) ? SideDiscardArea : WideDiscardArea;
            UIFactory.Anchor(slot.DiscardArea, Centre, Centre, DiscardCentres[index], areaSize);
            return slot;
        }

        static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

        /// <summary>每個區塊都用畫面中心座標直接定位，不再靠面板相對排版，避免互相擠壓。</summary>
        static void PlaceSlotRegions(SeatSlot slot, SeatOrientation orientation)
        {
            switch (orientation)
            {
                case SeatOrientation.Bottom:
                    UIFactory.Anchor(slot.Header.rectTransform, Centre, Centre,
                                     new Vector2(-700f, -300f), new Vector2(420f, 34f));
                    slot.Header.alignment = TextAnchor.MiddleLeft;
                    UIFactory.Anchor(slot.HandRow, Centre, Centre, Vector2.zero, Vector2.zero);
                    UIFactory.Anchor(slot.MeldRow, Centre, Centre, Vector2.zero, Vector2.zero);
                    break;

                case SeatOrientation.Top:
                    UIFactory.Anchor(slot.Header.rectTransform, Centre, Centre,
                                     new Vector2(0f, 508f), new Vector2(900f, 34f));
                    UIFactory.Anchor(slot.HandRow, Centre, Centre,
                                     new Vector2(0f, 466f), new Vector2(1100f, 44f));
                    UIFactory.Anchor(slot.MeldRow, Centre, Centre,
                                     new Vector2(0f, 400f), new Vector2(1100f, 66f));
                    break;

                case SeatOrientation.Right:
                    UIFactory.Anchor(slot.Header.rectTransform, Centre, Centre,
                                     new Vector2(700f, 300f), new Vector2(420f, 34f));
                    UIFactory.Anchor(slot.HandRow, Centre, Centre,
                                     new Vector2(650f, 30f), new Vector2(60f, 400f));
                    UIFactory.Anchor(slot.MeldRow, Centre, Centre,
                                     new Vector2(806f, 30f), new Vector2(188f, 400f));
                    break;

                case SeatOrientation.Left:
                    UIFactory.Anchor(slot.Header.rectTransform, Centre, Centre,
                                     new Vector2(-700f, 300f), new Vector2(420f, 34f));
                    UIFactory.Anchor(slot.HandRow, Centre, Centre,
                                     new Vector2(-650f, 30f), new Vector2(60f, 400f));
                    UIFactory.Anchor(slot.MeldRow, Centre, Centre,
                                     new Vector2(-806f, 30f), new Vector2(188f, 400f));
                    break;
            }
        }

        static bool IsVertical(SeatOrientation orientation)
            => orientation == SeatOrientation.Left || orientation == SeatOrientation.Right;

        void BuildCentre()
        {
            var centre = UIFactory.CreateImage("Centre", transform, PanelColor);
            UIFactory.Anchor(centre.rectTransform, Centre, Centre, Vector2.zero, new Vector2(170f, 150f));

            centreInfo = UIFactory.CreateText("Info", centre.transform, "", 23, TextColor);
            UIFactory.Stretch(centreInfo.rectTransform, 8f);
        }

        // ------------------------------------------------------------

        public void Refresh(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            ClearTransientTiles();
            if (state.Wall != null) wallView.Refresh(state.Wall.DrawnFromHead, state.Wall.DrawnFromTail);

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
            slot.Header.color = seat == WinnerSeat ? WinnerColor : (isTurn ? ActiveColor : TextColor);
            slot.Header.fontStyle = (isTurn || seat == WinnerSeat) ? FontStyle.Bold : FontStyle.Normal;

            if (slot.Orientation != SeatOrientation.Bottom)
            {
                LayoutOpponentHand(slot, player);
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
            string winner = seat == WinnerSeat ? (chinese ? "　胡牌！" : "  WIN!") : "";
            return wind + dealer + you + flowers + winner;
        }

        // ---------- 對手手牌 ----------

        void LayoutOpponentHand(SeatSlot slot, PlayerState player)
        {
            if (RevealHands)
            {
                LayoutRevealedHand(slot, player);
                return;
            }

            bool vertical = IsVertical(slot.Orientation);
            int tileCount = player.ConcealedTileCount;
            float step = vertical ? VerticalHandStep : HorizontalHandStep;
            float start = -(tileCount - 1) * step * 0.5f;

            for (int i = 0; i < tileCount; i++)
            {
                var tileView = CreateTransientTile(slot.HandRow, TileView.NoTile, OpponentTileSize, false);
                Vector2 position = vertical
                    ? new Vector2(0f, -(start + i * step))
                    : new Vector2(start + i * step, 0f);
                UIFactory.Anchor(tileView.Rect, Centre, Centre, position, OpponentTileSize);
                if (vertical) tileView.Rect.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
        }

        /// <summary>
        /// 結算時翻開的手牌一律正立，看得清楚牌面比較重要。
        /// 左右兩家排不下一整排，改成兩欄。
        /// </summary>
        void LayoutRevealedHand(SeatSlot slot, PlayerState player)
        {
            var tiles = new List<int>();
            for (int tile = 0; tile < TileDef.KINDS; tile++)
                for (int count = 0; count < player.ConcealedCounts[tile]; count++)
                    tiles.Add(tile);

            bool vertical = IsVertical(slot.Orientation);
            int columns = vertical ? RevealColumnsForSideSeats : tiles.Count;
            if (columns <= 0) columns = 1;

            float stepX = OpponentTileSize.x + 2f;
            float stepY = OpponentTileSize.y + 2f;
            int rows = Mathf.CeilToInt(tiles.Count / (float)columns);

            for (int i = 0; i < tiles.Count; i++)
            {
                int column = i % columns;
                int row = i / columns;

                var tileView = CreateTransientTile(slot.HandRow, tiles[i], OpponentTileSize, true);
                UIFactory.Anchor(tileView.Rect, Centre, Centre,
                                 new Vector2((column - (columns - 1) * 0.5f) * stepX,
                                             -(row - (rows - 1) * 0.5f) * stepY),
                                 OpponentTileSize);
            }
        }

        // ---------- 副露 ----------

        /// <summary>
        /// 副露一律保持正立，只是左右兩家改成一組一列往下排。
        /// 立起來雖然更接近真實牌桌，但牌面文字會變成側躺看不清楚。
        /// </summary>
        void LayoutMelds(SeatSlot slot, List<Meld> melds)
        {
            bool vertical = IsVertical(slot.Orientation);
            float rowCursor = 0f;
            float columnCursor = 0f;

            foreach (var meld in melds)
            {
                var layout = MeldDisplay.Arrange(meld);
                for (int i = 0; i < layout.Tiles.Length; i++)
                {
                    bool faceDown = MeldDisplay.IsFaceDown(meld, i, layout.Tiles.Length);
                    var tileView = CreateTransientTile(slot.MeldRow, layout.Tiles[i],
                                                       OpponentMeldSize, !faceDown);

                    float offsetX = vertical ? i * (OpponentMeldSize.x + 1f) : columnCursor;
                    float offsetY = vertical ? -rowCursor : 0f;
                    var anchor = vertical ? new Vector2(0f, 1f) : new Vector2(0f, 0.5f);
                    UIFactory.Anchor(tileView.Rect, anchor, anchor,
                                     new Vector2(offsetX, offsetY), OpponentMeldSize);

                    if (!vertical) columnCursor += OpponentMeldSize.x + 1f;
                }

                if (vertical) rowCursor += OpponentMeldSize.y + 4f;
                else columnCursor += 12f;
            }
        }

        // ---------- 牌河 ----------

        void LayoutDiscardTiles(SeatSlot slot, List<int> discards)
        {
            int perRow = IsVertical(slot.Orientation) ? SideDiscardsPerRow : WideDiscardsPerRow;
            float stepX = DiscardTileSize.x + DiscardGap;
            float stepY = DiscardTileSize.y + DiscardGap;
            float rowOffset = (perRow - 1) * 0.5f;

            for (int i = 0; i < discards.Count; i++)
            {
                var tileView = CreateTransientTile(slot.DiscardArea, discards[i], DiscardTileSize, true);
                UIFactory.Anchor(tileView.Rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                 new Vector2((i % perRow - rowOffset) * stepX, -(i / perRow) * stepY),
                                 DiscardTileSize);
            }
        }

        // ---------- 中央 ----------

        void RefreshCentre(GameState state)
        {
            int remaining = state.Wall == null ? 0 : state.Wall.Remaining;

            if (!UiFont.SupportsChinese)
            {
                centreInfo.text = string.Format("{0} round\n{1} tiles left\ndealer streak {2}",
                    WindName(state.RoundWind), remaining, state.DealerStreak);
                return;
            }

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

        // ------------------------------------------------------------

        TileView CreateTransientTile(Transform parent, int tile, Vector2 size, bool faceUp)
        {
            var tileView = TileView.Create(parent, tile, size, faceUp);
            tileView.SetInteractable(false);
            transientTiles.Add(tileView.gameObject);
            return tileView;
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
