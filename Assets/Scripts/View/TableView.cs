using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.View
{
    // ============================================================
    // 牌桌
    //
    // 顯示三家對手（門風、手牌張數、副露、花）、四家的牌河，
    // 以及中央的圈風／莊家／牌山剩餘與提示訊息。
    //
    // 座位換算：玩家永遠在下方，其餘依逆時針排到右、上、左。
    // ============================================================

    public class TableView : MonoBehaviour
    {
        static readonly Vector2 DiscardTileSize = new Vector2(38f, 52f);
        static readonly Vector2 OpponentTileSize = new Vector2(26f, 36f);
        static readonly Vector2 OpponentMeldSize = new Vector2(30f, 41f);
        const int DiscardsPerRow = 9;

        static readonly Color PanelColor = new Color(0.10f, 0.24f, 0.18f, 0.85f);
        static readonly Color TextColor = new Color(0.90f, 0.92f, 0.88f);
        static readonly Color HighlightColor = new Color(1f, 0.85f, 0.35f);

        int humanSeat;
        SeatPanel[] panels;
        Text centreInfo;
        Text messageLabel;
        readonly List<GameObject> transientTiles = new List<GameObject>();

        // ------------------------------------------------------------

        class SeatPanel
        {
            public RectTransform Root;
            public Text Header;
            public RectTransform HandRow;     // 對手蓋著的手牌
            public RectTransform MeldRow;     // 副露
            public RectTransform DiscardArea; // 牌河
        }

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
            panels = new SeatPanel[GameState.PlayerCount];

            // 相對位置：0 = 自己（下），1 = 右，2 = 上，3 = 左
            panels[0] = BuildSeatPanel("SeatBottom", new Vector2(0.5f, 0f), new Vector2(0f, 250f),
                                       new Vector2(900f, 150f), showHand: false);
            panels[1] = BuildSeatPanel("SeatRight", new Vector2(1f, 0.5f), new Vector2(-30f, 60f),
                                       new Vector2(330f, 460f), showHand: true);
            panels[2] = BuildSeatPanel("SeatTop", new Vector2(0.5f, 1f), new Vector2(0f, -30f),
                                       new Vector2(900f, 250f), showHand: true);
            panels[3] = BuildSeatPanel("SeatLeft", new Vector2(0f, 0.5f), new Vector2(30f, 60f),
                                       new Vector2(330f, 460f), showHand: true);

            BuildCentre();
        }

        SeatPanel BuildSeatPanel(string name, Vector2 anchor, Vector2 offset, Vector2 size, bool showHand)
        {
            var panel = new SeatPanel();
            panel.Root = UIFactory.CreateRect(name, transform);
            UIFactory.Anchor(panel.Root, anchor, anchor, offset, size);

            panel.Header = UIFactory.CreateText("Header", panel.Root, "", 26, TextColor);
            UIFactory.Anchor(panel.Header.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                             Vector2.zero, new Vector2(size.x, 34f));

            panel.HandRow = UIFactory.CreateRect("Hand", panel.Root);
            UIFactory.Anchor(panel.HandRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                             new Vector2(0f, -38f), new Vector2(size.x, showHand ? 40f : 0f));

            panel.MeldRow = UIFactory.CreateRect("Melds", panel.Root);
            UIFactory.Anchor(panel.MeldRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                             new Vector2(0f, showHand ? -82f : -38f), new Vector2(size.x, 45f));

            panel.DiscardArea = UIFactory.CreateRect("Discards", panel.Root);
            UIFactory.Anchor(panel.DiscardArea, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                             new Vector2(0f, showHand ? -132f : -88f),
                             new Vector2(size.x, size.y - 140f));
            return panel;
        }

        void BuildCentre()
        {
            var centre = UIFactory.CreateImage("Centre", transform, PanelColor);
            UIFactory.Anchor(centre.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             new Vector2(0f, 40f), new Vector2(340f, 190f));

            centreInfo = UIFactory.CreateText("Info", centre.transform, "", 26, TextColor);
            UIFactory.Anchor(centreInfo.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             new Vector2(0f, 18f), new Vector2(320f, 130f));

            messageLabel = UIFactory.CreateText("Message", transform, "", 30, HighlightColor);
            UIFactory.Anchor(messageLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             new Vector2(0f, -85f), new Vector2(900f, 44f));
        }

        // ------------------------------------------------------------

        public void SetMessage(string message) => messageLabel.text = message ?? "";

        public void Refresh(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            ClearTransientTiles();
            for (int offset = 0; offset < GameState.PlayerCount; offset++)
            {
                int seat = (humanSeat + offset) % GameState.PlayerCount;
                RefreshSeat(panels[offset], state, seat, showHand: offset != 0);
            }
            RefreshCentre(state);
        }

        void RefreshSeat(SeatPanel panel, GameState state, int seat, bool showHand)
        {
            var player = state.Players[seat];
            bool isTurn = state.CurrentPlayer == seat && state.Phase != GamePhase.Ended;

            panel.Header.text = BuildSeatHeader(state, player, seat);
            panel.Header.color = isTurn ? HighlightColor : TextColor;

            if (showHand) LayoutFaceDownHand(panel.HandRow, player.ConcealedTileCount);
            LayoutMelds(panel.MeldRow, player.Melds);
            LayoutDiscards(panel.DiscardArea, player.Discards);
        }

        string BuildSeatHeader(GameState state, PlayerState player, int seat)
        {
            string wind = WindName(player.SeatWind);
            string dealerMark = seat == state.DealerIndex ? (UiFont.SupportsChinese ? "（莊）" : " (D)") : "";
            string flowers = player.Flowers.Count > 0
                ? (UiFont.SupportsChinese ? "  花×" : "  F x") + player.Flowers.Count
                : "";
            string you = seat == humanSeat ? (UiFont.SupportsChinese ? "  你" : "  YOU") : "";
            return wind + dealerMark + you + flowers;
        }

        void LayoutFaceDownHand(RectTransform row, int tileCount)
        {
            float width = tileCount * (OpponentTileSize.x + 2f);
            float cursor = -width * 0.5f;
            for (int i = 0; i < tileCount; i++)
            {
                var tileView = TileView.Create(row, TileView.NoTile, OpponentTileSize, faceUp: false);
                UIFactory.Anchor(tileView.Rect, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
                                 new Vector2(cursor, 0f), OpponentTileSize);
                tileView.SetInteractable(false);
                transientTiles.Add(tileView.gameObject);
                cursor += OpponentTileSize.x + 2f;
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
            for (int i = 0; i < discards.Count; i++)
            {
                int column = i % DiscardsPerRow;
                int rowIndex = i / DiscardsPerRow;

                var tileView = TileView.Create(area, discards[i], DiscardTileSize);
                UIFactory.Anchor(tileView.Rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                 new Vector2((column - (DiscardsPerRow - 1) * 0.5f) * (DiscardTileSize.x + 2f),
                                             -rowIndex * (DiscardTileSize.y + 2f)),
                                 DiscardTileSize);
                tileView.SetInteractable(false);
                transientTiles.Add(tileView.gameObject);
            }
        }

        void RefreshCentre(GameState state)
        {
            string wallLabel = UiFont.SupportsChinese ? "牌山剩餘" : "Wall";
            string roundLabel = UiFont.SupportsChinese ? "圈風" : "Round";
            string streakLabel = UiFont.SupportsChinese ? "連莊" : "Streak";

            centreInfo.text = string.Format("{0} {1}\n{2} {3}\n{4} {5}",
                roundLabel, WindName(state.RoundWind),
                wallLabel, state.Wall == null ? 0 : state.Wall.Remaining,
                streakLabel, state.DealerStreak);
        }

        static string WindName(int wind)
        {
            if (UiFont.SupportsChinese) return TileDef.Name(wind);
            switch (wind)
            {
                case TileDef.EAST: return "East";
                case TileDef.SOUTH: return "South";
                case TileDef.WEST: return "West";
                default: return "North";
            }
        }

        void ClearTransientTiles()
        {
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
