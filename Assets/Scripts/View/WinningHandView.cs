using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.View
{
    // ============================================================
    // 結算時的贏家牌型（2D）
    //
    // 由左至右分成三段，段與段之間留出明顯的間隔：
    //   1. 手上剩下的牌（不含胡的那張）
    //   2. 吃碰槓露出來的副露，每一組之間也再分開
    //   3. 胡的那一張，單獨擺在最右邊
    //
    // 用 2D 畫而不是在牌桌上擺 3D 物件：結算是要讓玩家把牌看清楚，
    // 攤在桌上會被透視壓扁，也會跟桌上原有的牌疊在一起。
    // 牌面共用 TileAssets 烘焙好的那份貼圖，跟桌上的牌長得一模一樣。
    // ============================================================

    public class WinningHandView : MonoBehaviour
    {
        static readonly Vector2 TileSize = new Vector2(64f, 87f);
        const float TileGap = 4f;
        const float SectionGap = 40f;      // 手牌、副露、胡牌張三段之間
        const float MeldGap = 22f;         // 副露每一組之間
        const float FaceInset = 5f;

        const float TopEdgeRatio = 0.14f;

        static readonly Color BackdropColor = new Color(0f, 0f, 0f, 0.78f);
        static readonly Color TileColor = new Color(0.965f, 0.955f, 0.915f);
        static readonly Color TopEdgeColor = new Color(0.82f, 0.88f, 0.93f);

        RectTransform row;
        readonly List<GameObject> tiles = new List<GameObject>();

        public static WinningHandView Create(Transform parent)
        {
            var rect = UIFactory.CreateRect("WinningHandView", parent);
            UIFactory.Anchor(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             new Vector2(0f, 300f), new Vector2(1780f, TileSize.y + 26f));

            var view = rect.gameObject.AddComponent<WinningHandView>();
            view.Build();
            view.Hide();
            return view;
        }

        void Build()
        {
            var backdrop = UIFactory.CreateImage("Backdrop", transform, BackdropColor);
            UIFactory.Stretch(backdrop.rectTransform);

            row = UIFactory.CreateRect("Row", transform);
            UIFactory.Anchor(row, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             Vector2.zero, new Vector2(1780f, TileSize.y));
        }

        // ------------------------------------------------------------

        /// <summary>攤出贏家的牌。winningTile 會從手牌裡抽出來單獨擺在最後。</summary>
        public void Show(PlayerState winner, int winningTile)
        {
            Clear();
            if (winner == null) return;

            var concealed = CollectConcealed(winner, winningTile);
            float cursor = -MeasureTotalWidth(concealed.Count, winner.Melds, winningTile) * 0.5f;

            cursor = PlaceTiles(concealed, cursor);

            // 手牌與第一組副露之間用大間隔，副露彼此之間用小間隔
            for (int i = 0; i < winner.Melds.Count; i++)
            {
                cursor += i == 0 ? SectionGap : MeldGap;
                var layout = MeldDisplay.Arrange(winner.Melds[i]);
                cursor = PlaceTiles(new List<int>(layout.Tiles), cursor);
            }

            if (winningTile >= 0)
            {
                cursor += SectionGap;
                PlaceTiles(new List<int> { winningTile }, cursor);
            }

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
            if (winningTile >= 0) tiles.Remove(winningTile);
            return tiles;
        }

        static float MeasureTotalWidth(int concealedCount, List<Meld> melds, int winningTile)
        {
            float step = TileSize.x + TileGap;
            float total = concealedCount * step;

            if (melds.Count > 0)
            {
                total += SectionGap + (melds.Count - 1) * MeldGap;
                foreach (var meld in melds) total += meld.Tiles().Length * step;
            }

            if (winningTile >= 0) total += SectionGap + step;
            return total;
        }

        float PlaceTiles(List<int> tileIds, float cursor)
        {
            foreach (int tile in tileIds)
            {
                CreateTile(tile, cursor);
                cursor += TileSize.x + TileGap;
            }
            return cursor;
        }

        void CreateTile(int tile, float x)
        {
            var body = UIFactory.CreateImage("Tile", row, TileColor);
            UIFactory.Anchor(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
                             new Vector2(x, 0f), TileSize);

            // 跟手牌一樣加一條頂面，看起來才像立體的牌而不是貼紙
            float topHeight = TileSize.y * TopEdgeRatio;
            var top = UIFactory.CreateImage("TopEdge", body.transform, TopEdgeColor, rounded: false);
            UIFactory.Anchor(top.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                             new Vector2(0f, -2f), new Vector2(TileSize.x - 6f, topHeight));

            var face = UIFactory.CreateImage("Face", body.transform, Color.white, rounded: false);
            face.sprite = Board.TileAssets.FaceSprite(tile);
            face.preserveAspect = true;
            face.rectTransform.anchorMin = Vector2.zero;
            face.rectTransform.anchorMax = Vector2.one;
            face.rectTransform.offsetMin = new Vector2(FaceInset, FaceInset);
            face.rectTransform.offsetMax = new Vector2(-FaceInset, -(topHeight + 4f));

            tiles.Add(body.gameObject);
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
