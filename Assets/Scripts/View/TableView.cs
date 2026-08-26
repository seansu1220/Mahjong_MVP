using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.View
{
    // ============================================================
    // 牌桌
    //
    // 版面分兩種定位方式：
    //   牌山與牌河 —— 釘在畫面正中心，因為它們是牌桌的中央
    //   三家對手   —— 釘在畫面對應的那一邊
    //
    // 對手釘邊而不是釘中心，是因為畫布縮放採「鎖定高度」，
    // 畫面越寬左右兩邊離中心就越遠，釘邊才會一直貼著自己那一側。
    //
    // 中央區塊座標（1080 高固定）：
    //   牌山上排 y +234..+266    上家牌河 y  +90..+220
    //   左家牌河 x -275..-105    中央資訊 x ±85    右家牌河 x +105..+275
    //   自己牌河 y -220..-90     牌山下排 y -266..-234
    //
    // 座位換算：玩家永遠在下方，其餘依逆時針排到右、上、左。
    // 左右兩家的手牌與副露都是橫躺的，跟坐在牌桌側面看到的一樣；
    // 副露排在手牌與桌心之間，也就是那一家的正前方。
    // ============================================================

    public class TableView : MonoBehaviour
    {
        static readonly Vector2 DiscardTileSize = new Vector2(34f, 46f);
        static readonly Vector2 TopTileSize = new Vector2(30f, 41f);
        static readonly Vector2 TopMeldSize = new Vector2(46f, 63f);

        // 左右兩家的牌是橫躺的，往下排時佔用的高度是「牌寬」，
        // 所以間距必須大於牌寬才不會疊在一起。
        static readonly Vector2 SideTileSize = new Vector2(22f, 30f);
        const float SideTileStep = 25f;      // > SideTileSize.x，兩牌之間留 3px
        const float SideMeldGroupGap = 12f;
        const float TopTileStep = 32f;       // > TopTileSize.x

        static readonly Vector2 WideDiscardArea = new Vector2(310f, 132f);
        static readonly Vector2 SideDiscardArea = new Vector2(170f, 132f);
        const int WideDiscardsPerRow = 8;
        const int SideDiscardsPerRow = 4;
        const float DiscardGap = 2f;
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

        static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);
        static readonly Vector2 LeftEdge = new Vector2(0f, 0.5f);
        static readonly Vector2 RightEdge = new Vector2(1f, 0.5f);
        static readonly Vector2 TopEdge = new Vector2(0.5f, 1f);
        static readonly Vector2 BottomRight = new Vector2(1f, 0f);

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

        public Vector2 NextDrawPosition => wallView.HeadPosition;
        public Vector2 NextReplacementPosition => wallView.TailPosition;

        /// <summary>某個座位在畫面上的方位，0 是自己（下方）</summary>
        public int DisplayIndexOf(int seat)
            => (seat - humanSeat + GameState.PlayerCount) % GameState.PlayerCount;

        void Build()
        {
            wallView = WallView.Create(transform);   // 先建，讓牌山墊在牌河底下

            slots = new SeatSlot[GameState.PlayerCount];
            for (int index = 0; index < GameState.PlayerCount; index++)
                slots[index] = BuildSlot(index, (SeatOrientation)index);

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
            var areaSize = IsSide(orientation) ? SideDiscardArea : WideDiscardArea;
            UIFactory.Anchor(slot.DiscardArea, Centre, Centre, DiscardCentres[index], areaSize);
            return slot;
        }

        /// <summary>
        /// 三家對手各自釘在畫面的那一邊，這樣畫面變寬變窄都會跟著貼邊，
        /// 不會像釘中心那樣在寬螢幕上被推出可視範圍。
        /// </summary>
        static void PlaceSlotRegions(SeatSlot slot, SeatOrientation orientation)
        {
            switch (orientation)
            {
                case SeatOrientation.Bottom:
                    // 名牌移到右下角，跟左下角的花牌區分開，兩邊都不會互相遮住
                    UIFactory.Anchor(slot.Header.rectTransform, BottomRight, BottomRight,
                                     new Vector2(-24f, 212f), new Vector2(320f, 34f));
                    slot.Header.alignment = TextAnchor.MiddleRight;
                    UIFactory.Anchor(slot.HandRow, Centre, Centre, Vector2.zero, Vector2.zero);
                    UIFactory.Anchor(slot.MeldRow, Centre, Centre, Vector2.zero, Vector2.zero);
                    break;

                case SeatOrientation.Top:
                    UIFactory.Anchor(slot.Header.rectTransform, TopEdge, TopEdge,
                                     new Vector2(0f, -22f), new Vector2(900f, 34f));
                    UIFactory.Anchor(slot.HandRow, TopEdge, TopEdge,
                                     new Vector2(0f, -62f), new Vector2(1100f, 44f));
                    UIFactory.Anchor(slot.MeldRow, TopEdge, TopEdge,
                                     new Vector2(0f, -114f), new Vector2(1100f, 70f));
                    break;

                case SeatOrientation.Right:
                    UIFactory.Anchor(slot.Header.rectTransform, RightEdge, RightEdge,
                                     new Vector2(-180f, 250f), new Vector2(320f, 34f));
                    UIFactory.Anchor(slot.HandRow, RightEdge, RightEdge,
                                     new Vector2(-64f, 10f), new Vector2(60f, 460f));
                    // 副露排在手牌與桌心之間，也就是這一家的正前方
                    UIFactory.Anchor(slot.MeldRow, RightEdge, RightEdge,
                                     new Vector2(-142f, 10f), new Vector2(60f, 460f));
                    break;

                case SeatOrientation.Left:
                    UIFactory.Anchor(slot.Header.rectTransform, LeftEdge, LeftEdge,
                                     new Vector2(180f, 250f), new Vector2(320f, 34f));
                    UIFactory.Anchor(slot.HandRow, LeftEdge, LeftEdge,
                                     new Vector2(64f, 10f), new Vector2(60f, 460f));
                    UIFactory.Anchor(slot.MeldRow, LeftEdge, LeftEdge,
                                     new Vector2(142f, 10f), new Vector2(60f, 460f));
                    break;
            }
        }

        static bool IsSide(SeatOrientation orientation)
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

            bool side = IsSide(slot.Orientation);
            var size = side ? SideTileSize : TopTileSize;
            float step = side ? SideTileStep : TopTileStep;

            int tileCount = player.ConcealedTileCount;
            float start = -(tileCount - 1) * step * 0.5f;

            for (int i = 0; i < tileCount; i++)
            {
                var tileView = CreateTransientTile(slot.HandRow, TileView.NoTile, size, false);
                Vector2 position = side
                    ? new Vector2(0f, -(start + i * step))
                    : new Vector2(start + i * step, 0f);
                UIFactory.Anchor(tileView.Rect, Centre, Centre, position, size);
                if (side) tileView.Rect.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
        }

        /// <summary>
        /// 結算時翻開的手牌一律正立，看得清楚牌面比擺法真實重要。
        /// 左右兩家排不下一整排，改成兩欄。
        /// </summary>
        void LayoutRevealedHand(SeatSlot slot, PlayerState player)
        {
            var tiles = CollectTiles(player.ConcealedCounts);
            bool side = IsSide(slot.Orientation);
            var size = side ? SideTileSize : TopTileSize;

            int columns = side ? RevealColumnsForSideSeats : Mathf.Max(1, tiles.Count);
            int rows = Mathf.CeilToInt(tiles.Count / (float)columns);
            float stepX = size.x + 2f;
            float stepY = size.y + 2f;

            for (int i = 0; i < tiles.Count; i++)
            {
                var tileView = CreateTransientTile(slot.HandRow, tiles[i], size, true);
                UIFactory.Anchor(tileView.Rect, Centre, Centre,
                                 new Vector2((i % columns - (columns - 1) * 0.5f) * stepX,
                                             -(i / columns - (rows - 1) * 0.5f) * stepY),
                                 size);
            }
        }

        static List<int> CollectTiles(int[] counts)
        {
            var tiles = new List<int>();
            for (int tile = 0; tile < TileDef.KINDS; tile++)
                for (int count = 0; count < counts[tile]; count++)
                    tiles.Add(tile);
            return tiles;
        }

        // ---------- 副露 ----------

        /// <summary>
        /// 左右兩家的副露跟他們的手牌同方向（橫躺），並排在手牌與桌心之間，
        /// 也就是那一家的正前方，跟真的把牌推出去攤在面前一樣。
        /// </summary>
        void LayoutMelds(SeatSlot slot, List<Meld> melds)
        {
            bool side = IsSide(slot.Orientation);
            var size = side ? SideTileSize : TopMeldSize;
            float step = side ? SideTileStep : size.x + 1f;
            float groupGap = side ? SideMeldGroupGap : 12f;

            float cursor = 0f;
            float total = MeasureMeldExtent(melds, step, groupGap);
            float start = -total * 0.5f;

            foreach (var meld in melds)
            {
                var layout = MeldDisplay.Arrange(meld);
                for (int i = 0; i < layout.Tiles.Length; i++)
                {
                    bool faceDown = MeldDisplay.IsFaceDown(meld, i, layout.Tiles.Length);
                    var tileView = CreateTransientTile(slot.MeldRow, layout.Tiles[i], size, !faceDown);

                    float along = start + cursor;
                    Vector2 position = side ? new Vector2(0f, -along) : new Vector2(along, 0f);
                    UIFactory.Anchor(tileView.Rect, Centre, Centre, position, size);
                    if (side) tileView.Rect.localRotation = Quaternion.Euler(0f, 0f, 90f);

                    cursor += step;
                }
                cursor += groupGap;
            }
        }

        static float MeasureMeldExtent(List<Meld> melds, float step, float groupGap)
        {
            float total = 0f;
            foreach (var meld in melds) total += meld.Tiles().Length * step + groupGap;
            return Mathf.Max(0f, total - groupGap);
        }

        // ---------- 牌河 ----------

        void LayoutDiscardTiles(SeatSlot slot, List<int> discards)
        {
            int perRow = IsSide(slot.Orientation) ? SideDiscardsPerRow : WideDiscardsPerRow;
            float stepX = DiscardTileSize.x + DiscardGap;
            float stepY = DiscardTileSize.y + DiscardGap;
            float rowOffset = (perRow - 1) * 0.5f;

            for (int i = 0; i < discards.Count; i++)
            {
                var tileView = CreateTransientTile(slot.DiscardArea, discards[i], DiscardTileSize, true);
                UIFactory.Anchor(tileView.Rect, TopEdge, TopEdge,
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
