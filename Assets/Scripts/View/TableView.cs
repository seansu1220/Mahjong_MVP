using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.View
{
    // ============================================================
    // 牌桌
    //
    // 版面全部以畫面中心 (0,0) 為基準，1920x1080 下各區塊互不重疊：
    //
    //            上家資訊  y +310..+530
    //            上家牌河  y  +40..+220
    //   左家牌河            中央資訊            右家牌河
    //   x -470..-170     x -110..+110       x +170..+470
    //            自己牌河  y -240..-60
    //            動作按鈕  y -310..-245   （由 Bootstrap 擺放）
    //            自己手牌  y -524..-324   （由 HandView 擺放）
    //
    // 座位換算：玩家永遠在下方，其餘依逆時針排到右、上、左。
    // ============================================================

    public class TableView : MonoBehaviour
    {
        static readonly Vector2 DiscardTileSize = new Vector2(32f, 44f);
        static readonly Vector2 OpponentTileSize = new Vector2(24f, 33f);
        static readonly Vector2 OpponentMeldSize = new Vector2(30f, 41f);
        const int DiscardsPerRow = 9;
        const float DiscardGap = 1f;

        static readonly Vector2 DiscardAreaSize = new Vector2(300f, 180f);
        static readonly Vector2[] DiscardCentres =
        {
            new Vector2(0f, -150f),    // 自己
            new Vector2(320f, 0f),     // 右家
            new Vector2(0f, 130f),     // 上家
            new Vector2(-320f, 0f)     // 左家
        };

        /// <summary>發牌動畫的四個目標位置，順序與 DiscardCentres 相同</summary>
        public static readonly Vector2[] SeatAnchors =
        {
            new Vector2(0f, -430f),
            new Vector2(720f, -40f),
            new Vector2(0f, 430f),
            new Vector2(-720f, -40f)
        };

        static readonly Color PanelColor = new Color(0.07f, 0.20f, 0.15f, 0.80f);
        static readonly Color TextColor = new Color(0.90f, 0.92f, 0.88f);
        static readonly Color ActiveColor = new Color(1f, 0.85f, 0.35f);

        int humanSeat;
        SeatSlot[] slots;
        Text centreInfo;
        readonly List<GameObject> transientTiles = new List<GameObject>();

        class SeatSlot
        {
            public Text Header;
            public RectTransform HandRow;
            public RectTransform MeldRow;
            public RectTransform DiscardArea;
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
            slots = new SeatSlot[GameState.PlayerCount];

            // 自己只需要一行小標，手牌與副露由 HandView 負責
            slots[0] = BuildSlot(0, "SeatBottom", new Vector2(-780f, -298f), new Vector2(320f, 36f),
                                 showHand: false, headerAnchor: TextAnchor.MiddleLeft);
            slots[1] = BuildSlot(1, "SeatRight", new Vector2(715f, 30f), new Vector2(410f, 300f),
                                 showHand: true, headerAnchor: TextAnchor.MiddleCenter);
            slots[2] = BuildSlot(2, "SeatTop", new Vector2(0f, 420f), new Vector2(1000f, 220f),
                                 showHand: true, headerAnchor: TextAnchor.MiddleCenter);
            slots[3] = BuildSlot(3, "SeatLeft", new Vector2(-715f, 30f), new Vector2(410f, 300f),
                                 showHand: true, headerAnchor: TextAnchor.MiddleCenter);

            BuildCentre();
        }

        SeatSlot BuildSlot(int index, string name, Vector2 panelCentre, Vector2 panelSize,
                           bool showHand, TextAnchor headerAnchor)
        {
            var slot = new SeatSlot();

            var panel = UIFactory.CreateRect(name, transform);
            UIFactory.Anchor(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), panelCentre, panelSize);

            slot.Header = UIFactory.CreateText("Header", panel, "", 26, TextColor, headerAnchor);
            UIFactory.Anchor(slot.Header.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                             new Vector2(0f, -18f), new Vector2(panelSize.x, 34f));

            slot.HandRow = UIFactory.CreateRect("Hand", panel);
            UIFactory.Anchor(slot.HandRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                             new Vector2(0f, -60f), new Vector2(panelSize.x, showHand ? 40f : 0f));

            slot.MeldRow = UIFactory.CreateRect("Melds", panel);
            UIFactory.Anchor(slot.MeldRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                             new Vector2(0f, showHand ? -110f : -40f), new Vector2(panelSize.x, 45f));

            // 牌河獨立於資訊面板，直接掛在中央區塊的固定位置
            slot.DiscardArea = UIFactory.CreateRect(name + "Discards", transform);
            UIFactory.Anchor(slot.DiscardArea, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             DiscardCentres[index], DiscardAreaSize);
            return slot;
        }

        void BuildCentre()
        {
            var centre = UIFactory.CreateImage("Centre", transform, PanelColor);
            UIFactory.Anchor(centre.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             Vector2.zero, new Vector2(220f, 110f));

            centreInfo = UIFactory.CreateText("Info", centre.transform, "", 24, TextColor);
            UIFactory.Stretch(centreInfo.rectTransform, 8f);
        }

        // ------------------------------------------------------------

        public void Refresh(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            ClearTransientTiles();
            for (int offset = 0; offset < GameState.PlayerCount; offset++)
            {
                int seat = (humanSeat + offset) % GameState.PlayerCount;
                RefreshSeat(slots[offset], state, seat, showHand: offset != 0);
            }
            RefreshCentre(state);
        }

        void RefreshSeat(SeatSlot slot, GameState state, int seat, bool showHand)
        {
            var player = state.Players[seat];
            bool isTurn = state.CurrentPlayer == seat && state.Phase != GamePhase.Ended;

            slot.Header.text = BuildSeatHeader(state, player, seat);
            slot.Header.color = isTurn ? ActiveColor : TextColor;
            slot.Header.fontStyle = isTurn ? FontStyle.Bold : FontStyle.Normal;

            if (showHand) LayoutFaceDownHand(slot.HandRow, player.ConcealedTileCount);
            LayoutMelds(slot.MeldRow, player.Melds);
            LayoutDiscards(slot.DiscardArea, player.Discards);
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

        void LayoutFaceDownHand(RectTransform row, int tileCount)
        {
            float step = OpponentTileSize.x + 2f;
            float cursor = -(tileCount * step) * 0.5f;
            for (int i = 0; i < tileCount; i++)
            {
                var tileView = TileView.Create(row, TileView.NoTile, OpponentTileSize, faceUp: false);
                UIFactory.Anchor(tileView.Rect, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
                                 new Vector2(cursor, 0f), OpponentTileSize);
                tileView.SetInteractable(false);
                transientTiles.Add(tileView.gameObject);
                cursor += step;
            }
        }

        void LayoutMelds(RectTransform row, List<Meld> melds)
        {
            float cursor = 0f;
            foreach (var meld in melds)
            {
                int[] tiles = meld.Tiles();
                for (int i = 0; i < tiles.Length; i++)
                {
                    bool faceUp = !(meld.Type == MeldType.AnKan && (i == 0 || i == tiles.Length - 1));
                    var tileView = TileView.Create(row, tiles[i], OpponentMeldSize, faceUp);
                    UIFactory.Anchor(tileView.Rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                                     new Vector2(cursor, 0f), OpponentMeldSize);
                    tileView.SetInteractable(false);
                    transientTiles.Add(tileView.gameObject);
                    cursor += OpponentMeldSize.x + 1f;
                }
                cursor += 10f;
            }
        }

        void LayoutDiscards(RectTransform area, List<int> discards)
        {
            float stepX = DiscardTileSize.x + DiscardGap;
            float stepY = DiscardTileSize.y + DiscardGap;
            float rowOffset = (DiscardsPerRow - 1) * 0.5f;

            for (int i = 0; i < discards.Count; i++)
            {
                int column = i % DiscardsPerRow;
                int row = i / DiscardsPerRow;

                var tileView = TileView.Create(area, discards[i], DiscardTileSize);
                UIFactory.Anchor(tileView.Rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                 new Vector2((column - rowOffset) * stepX, -row * stepY),
                                 DiscardTileSize);
                tileView.SetInteractable(false);
                transientTiles.Add(tileView.gameObject);
            }
        }

        void RefreshCentre(GameState state)
        {
            bool chinese = UiFont.SupportsChinese;
            centreInfo.text = string.Format("{0} {1}\n{2} {3}\n{4} {5}",
                chinese ? "圈風" : "Round", WindName(state.RoundWind),
                chinese ? "牌山" : "Wall", state.Wall == null ? 0 : state.Wall.Remaining,
                chinese ? "連莊" : "Streak", state.DealerStreak);
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
