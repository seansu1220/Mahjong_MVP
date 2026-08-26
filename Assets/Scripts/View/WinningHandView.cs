using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.View
{
    // ============================================================
    // 結算時攤在中央的贏家牌型
    //
    // 由左至右分成三段，每段之間留出間隔：
    //   1. 手上剩下的牌（不含胡的那張）
    //   2. 吃碰槓露出來的副露，一組一組分開
    //   3. 胡的那一張，單獨擺在最右邊
    //
    // 這樣一眼就看得出這副牌是怎麼組成的、最後是靠哪一張成的。
    // ============================================================

    public class WinningHandView : MonoBehaviour
    {
        static readonly Vector2 TileSize = new Vector2(52f, 71f);
        const float TileGap = 3f;
        const float SectionGap = 34f;      // 手牌、副露、胡牌張三段之間的間隔
        const float MeldGap = 16f;         // 副露每一組之間的間隔

        static readonly Color BackdropColor = new Color(0f, 0f, 0f, 0.78f);
        static readonly Color CaptionColor = new Color(1f, 0.88f, 0.45f);

        RectTransform row;
        Image backdrop;
        Text caption;
        readonly List<GameObject> tiles = new List<GameObject>();

        public static WinningHandView Create(Transform parent)
        {
            var rect = UIFactory.CreateRect("WinningHandView", parent);
            UIFactory.Anchor(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             new Vector2(0f, 285f), new Vector2(1700f, 140f));

            var view = rect.gameObject.AddComponent<WinningHandView>();
            view.Build();
            view.Hide();
            return view;
        }

        void Build()
        {
            backdrop = UIFactory.CreateImage("Backdrop", transform, BackdropColor);
            UIFactory.Stretch(backdrop.rectTransform);

            caption = UIFactory.CreateText("Caption", transform, "", 22, CaptionColor);
            UIFactory.Anchor(caption.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                             new Vector2(0f, -6f), new Vector2(900f, 26f));

            row = UIFactory.CreateRect("Row", transform);
            UIFactory.Anchor(row, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                             new Vector2(0f, 12f), new Vector2(1700f, TileSize.y));
        }

        // ------------------------------------------------------------

        /// <summary>攤出贏家的牌。winningTile 會從手牌裡抽出來單獨擺在最後。</summary>
        public void Show(PlayerState winner, int winningTile)
        {
            Clear();
            if (winner == null) return;

            var concealed = CollectConcealed(winner, winningTile);
            var widths = MeasureSections(concealed.Count, winner.Melds, winningTile);
            float cursor = -widths.Total * 0.5f;

            cursor = LayoutTiles(concealed, cursor);
            if (winner.Melds.Count > 0) cursor += SectionGap;

            for (int i = 0; i < winner.Melds.Count; i++)
            {
                if (i > 0) cursor += MeldGap;
                cursor = LayoutMeld(winner.Melds[i], cursor);
            }

            if (winningTile != TileView.NoTile)
            {
                cursor += SectionGap;
                LayoutTiles(new List<int> { winningTile }, cursor);
            }

            caption.text = UiFont.SupportsChinese
                ? "手牌　→　吃碰槓　→　胡的那張"
                : "Hand  →  Melds  →  Winning tile";
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);

        // ------------------------------------------------------------

        static List<int> CollectConcealed(PlayerState winner, int winningTile)
        {
            var tiles = new List<int>();
            for (int tile = 0; tile < TileDef.KINDS; tile++)
                for (int count = 0; count < winner.ConcealedCounts[tile]; count++)
                    tiles.Add(tile);

            // 胡的那張要單獨擺，先從手牌裡拿掉一張
            if (winningTile != TileView.NoTile) tiles.Remove(winningTile);
            return tiles;
        }

        struct SectionWidths
        {
            public float Total;
        }

        static SectionWidths MeasureSections(int concealedCount, List<Meld> melds, int winningTile)
        {
            float step = TileSize.x + TileGap;
            float total = concealedCount * step;

            if (melds.Count > 0)
            {
                total += SectionGap;
                foreach (var meld in melds) total += meld.Tiles().Length * step + MeldGap;
                total -= MeldGap;
            }

            if (winningTile != TileView.NoTile) total += SectionGap + step;
            return new SectionWidths { Total = total };
        }

        float LayoutTiles(List<int> tileIds, float cursor)
        {
            foreach (int tile in tileIds)
            {
                CreateTile(tile, cursor, faceUp: true);
                cursor += TileSize.x + TileGap;
            }
            return cursor;
        }

        float LayoutMeld(Meld meld, float cursor)
        {
            var layout = MeldDisplay.Arrange(meld);
            for (int i = 0; i < layout.Tiles.Length; i++)
            {
                // 結算時連暗槓也全部翻開，看得到完整牌型比藏著重要
                CreateTile(layout.Tiles[i], cursor, faceUp: true);
                cursor += TileSize.x + TileGap;
            }
            return cursor;
        }

        void CreateTile(int tile, float x, bool faceUp)
        {
            var tileView = TileView.Create(row, tile, TileSize, faceUp);
            tileView.SetInteractable(false);
            UIFactory.Anchor(tileView.Rect, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
                             new Vector2(x, 0f), TileSize);
            tiles.Add(tileView.gameObject);
        }

        void Clear()
        {
            foreach (var tile in tiles)
            {
                if (tile == null) continue;
                tile.SetActive(false);
                Destroy(tile);
            }
            tiles.Clear();
        }
    }
}
