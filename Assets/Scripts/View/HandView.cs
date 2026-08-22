using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.View
{
    // ============================================================
    // 玩家自己的手牌
    //
    // 互動方式：點一下選取（牌會抬起來），再點同一張才打出。
    // 兩段式是為了避免手滑打錯牌。
    //
    // View 層只負責顯示與捕捉點擊，要不要能打、打了會怎樣一律交給 Bootstrap 與 TurnEngine。
    // ============================================================

    public class HandView : MonoBehaviour
    {
        public static readonly Vector2 TileSize = new Vector2(64f, 88f);
        const float TileGap = 4f;
        const float DrawnTileGap = 26f;   // 剛摸進來的那張跟其他牌隔開
        const float SelectedLift = 16f;
        static readonly Vector2 MeldTileSize = new Vector2(44f, 60f);
        static readonly Vector2 FlowerTileSize = new Vector2(38f, 52f);

        RectTransform handRow;
        RectTransform meldRow;
        RectTransform flowerRow;
        Text flowerLabel;

        readonly List<TileView> handTiles = new List<TileView>();
        readonly List<GameObject> decorations = new List<GameObject>();

        int selectedTile = TileView.NoTile;
        bool interactable;

        /// <summary>玩家確定要打出這張牌</summary>
        public event Action<int> TileChosen;

        // ------------------------------------------------------------

        public static HandView Create(Transform parent)
        {
            var rect = UIFactory.CreateRect("HandView", parent);
            UIFactory.Anchor(rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                             new Vector2(0f, 24f), new Vector2(1800f, 210f));

            var view = rect.gameObject.AddComponent<HandView>();
            view.Build();
            return view;
        }

        void Build()
        {
            handRow = UIFactory.CreateRect("HandRow", transform);
            UIFactory.Anchor(handRow, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                             new Vector2(0f, 0f), new Vector2(1800f, TileSize.y + SelectedLift));

            meldRow = UIFactory.CreateRect("MeldRow", transform);
            UIFactory.Anchor(meldRow, new Vector2(1f, 1f), new Vector2(1f, 1f),
                             new Vector2(0f, 0f), new Vector2(900f, MeldTileSize.y));

            flowerRow = UIFactory.CreateRect("FlowerRow", transform);
            UIFactory.Anchor(flowerRow, new Vector2(0f, 1f), new Vector2(0f, 1f),
                             new Vector2(0f, 0f), new Vector2(600f, FlowerTileSize.y));

            flowerLabel = UIFactory.CreateText("FlowerLabel", flowerRow, "", 20,
                                               new Color(0.85f, 0.85f, 0.80f), TextAnchor.MiddleLeft);
            UIFactory.Anchor(flowerLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                             Vector2.zero, new Vector2(90f, FlowerTileSize.y));
        }

        // ------------------------------------------------------------

        /// <summary>依目前手牌重畫。justDrawnTile 會被排到最右邊並隔開。</summary>
        public void Refresh(PlayerState player, int justDrawnTile = TileView.NoTile)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));

            selectedTile = TileView.NoTile;
            ClearChildren();

            var ordered = BuildOrderedHand(player, justDrawnTile);
            LayoutHandRow(ordered, justDrawnTile);
            LayoutMelds(player.Melds);
            LayoutFlowers(player.Flowers);
            SetInteractable(interactable);
        }

        /// <summary>手牌由小到大排序；剛摸的那張抽出來放最後。</summary>
        static List<int> BuildOrderedHand(PlayerState player, int justDrawnTile)
        {
            var ordered = new List<int>();
            for (int tile = 0; tile < TileDef.KINDS; tile++)
                for (int count = 0; count < player.ConcealedCounts[tile]; count++)
                    ordered.Add(tile);

            if (justDrawnTile != TileView.NoTile && ordered.Remove(justDrawnTile))
                ordered.Add(justDrawnTile);
            return ordered;
        }

        void LayoutHandRow(List<int> ordered, int justDrawnTile)
        {
            bool hasDrawnTile = justDrawnTile != TileView.NoTile && ordered.Count > 0;
            int concealedCount = hasDrawnTile ? ordered.Count - 1 : ordered.Count;

            float width = ordered.Count * TileSize.x + Mathf.Max(0, ordered.Count - 1) * TileGap
                        + (hasDrawnTile ? DrawnTileGap : 0f);
            float cursor = -width * 0.5f;

            for (int i = 0; i < ordered.Count; i++)
            {
                if (hasDrawnTile && i == concealedCount) cursor += DrawnTileGap;

                var tileView = TileView.Create(handRow, ordered[i], TileSize);
                UIFactory.Anchor(tileView.Rect, new Vector2(0.5f, 0f), new Vector2(0f, 0f),
                                 new Vector2(cursor, 0f), TileSize);
                tileView.Clicked += OnTileClicked;
                handTiles.Add(tileView);

                cursor += TileSize.x + TileGap;
            }
        }

        void LayoutMelds(List<Meld> melds)
        {
            float cursor = 0f;
            for (int i = melds.Count - 1; i >= 0; i--)
            {
                var meld = melds[i];
                int[] tiles = meld.Tiles();
                for (int t = tiles.Length - 1; t >= 0; t--)
                {
                    cursor -= MeldTileSize.x + 2f;
                    // 暗槓蓋著兩張，讓人看得出是暗的
                    bool faceUp = !(meld.Type == MeldType.AnKan && (t == 0 || t == tiles.Length - 1));
                    var tileView = TileView.Create(meldRow, tiles[t], MeldTileSize, faceUp);
                    UIFactory.Anchor(tileView.Rect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                                     new Vector2(cursor, 0f), MeldTileSize);
                    tileView.SetInteractable(false);
                    decorations.Add(tileView.gameObject);
                }
                cursor -= 14f;   // 組與組之間留空
            }
        }

        void LayoutFlowers(List<int> flowers)
        {
            flowerLabel.text = flowers.Count > 0
                ? (UiFont.SupportsChinese ? "花 " + flowers.Count : "Flowers " + flowers.Count)
                : "";

            float cursor = 96f;
            foreach (int flower in flowers)
            {
                var tileView = TileView.Create(flowerRow, flower, FlowerTileSize);
                UIFactory.Anchor(tileView.Rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                                 new Vector2(cursor, 0f), FlowerTileSize);
                tileView.SetInteractable(false);
                decorations.Add(tileView.gameObject);
                cursor += FlowerTileSize.x + 3f;
            }
        }

        // ------------------------------------------------------------

        void OnTileClicked(TileView tileView)
        {
            if (!interactable) return;

            if (selectedTile == tileView.Tile)
            {
                var chosen = tileView.Tile;
                selectedTile = TileView.NoTile;
                if (TileChosen != null) TileChosen(chosen);
                return;
            }

            selectedTile = tileView.Tile;
            RefreshSelectionVisuals();
        }

        void RefreshSelectionVisuals()
        {
            foreach (var tileView in handTiles)
            {
                bool selected = tileView.Tile == selectedTile;
                tileView.SetSelected(selected);

                var position = tileView.Rect.anchoredPosition;
                position.y = selected ? SelectedLift : 0f;
                tileView.Rect.anchoredPosition = position;
            }
        }

        public void SetInteractable(bool value)
        {
            interactable = value;
            foreach (var tileView in handTiles) tileView.SetInteractable(value);
            if (!value)
            {
                selectedTile = TileView.NoTile;
                RefreshSelectionVisuals();
            }
        }

        void ClearChildren()
        {
            // Destroy 要到影格結束才生效，先關掉才不會跟新畫的牌疊在一起
            foreach (var tileView in handTiles)
            {
                if (tileView == null) continue;
                tileView.gameObject.SetActive(false);
                Destroy(tileView.gameObject);
            }
            handTiles.Clear();

            foreach (var decoration in decorations)
            {
                if (decoration == null) continue;
                decoration.SetActive(false);
                Destroy(decoration);
            }
            decorations.Clear();
        }
    }
}
