using System;
using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.View
{
    // ============================================================
    // 一張牌的外觀
    //
    // 牌面完全用程式繪製：圓角白底 + 文字，不使用任何外部圖片素材。
    // 外部麻將牌素材多為 CC BY-SA 授權，商業營運有 share-alike 風險。
    //
    // 之後要換成美術素材，只要改這個檔案，其他地方都不用動。
    // ============================================================

    public class TileView : MonoBehaviour
    {
        public const int NoTile = GameState.NoTile;

        static readonly Color FaceColor = new Color(0.98f, 0.97f, 0.93f);
        static readonly Color BackColor = new Color(0.18f, 0.42f, 0.32f);
        static readonly Color BackPattern = new Color(0.24f, 0.52f, 0.40f);
        static readonly Color SelectedTint = new Color(1f, 0.94f, 0.70f);
        static readonly Color DimTint = new Color(0.72f, 0.72f, 0.70f);

        static readonly Color ManColor = new Color(0.72f, 0.14f, 0.14f);
        static readonly Color PinColor = new Color(0.12f, 0.36f, 0.68f);
        static readonly Color SouColor = new Color(0.10f, 0.48f, 0.26f);
        static readonly Color HonorColor = new Color(0.15f, 0.15f, 0.18f);
        static readonly Color DragonRed = new Color(0.78f, 0.12f, 0.12f);
        static readonly Color DragonGreen = new Color(0.08f, 0.50f, 0.24f);
        static readonly Color DragonBlue = new Color(0.15f, 0.32f, 0.62f);

        Image background;
        Text rankLabel;
        Text suitLabel;
        Button button;

        /// <summary>這個 View 顯示的牌 id；蓋著的牌為 NoTile</summary>
        public int Tile { get; private set; } = NoTile;

        public RectTransform Rect => (RectTransform)transform;

        /// <summary>被點擊時通知外部。View 層只負責捕捉事件，不做任何規則判斷。</summary>
        public event Action<TileView> Clicked;

        // ------------------------------------------------------------

        /// <summary>建立一張牌。faceUp 為 false 時畫成牌背。</summary>
        public static TileView Create(Transform parent, int tile, Vector2 size, bool faceUp = true)
        {
            var rect = UIFactory.CreateRect("Tile", parent);
            rect.sizeDelta = size;

            var view = rect.gameObject.AddComponent<TileView>();
            view.Build(size);
            view.SetTile(tile, faceUp);
            return view;
        }

        void Build(Vector2 size)
        {
            background = UIFactory.CreateImage("Face", transform, FaceColor, rounded: true, raycast: true);
            UIFactory.Stretch(background.rectTransform);

            button = gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => { if (Clicked != null) Clicked(this); });

            int rankSize = Mathf.RoundToInt(size.y * 0.42f);
            int suitSize = Mathf.RoundToInt(size.y * 0.30f);

            rankLabel = UIFactory.CreateText("Rank", background.transform, "", rankSize, HonorColor);
            UIFactory.Anchor(rankLabel.rectTransform, new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.5f),
                             Vector2.zero, new Vector2(size.x, size.y * 0.5f));

            suitLabel = UIFactory.CreateText("Suit", background.transform, "", suitSize, HonorColor);
            UIFactory.Anchor(suitLabel.rectTransform, new Vector2(0.5f, 0.28f), new Vector2(0.5f, 0.5f),
                             Vector2.zero, new Vector2(size.x, size.y * 0.5f));
        }

        // ------------------------------------------------------------

        public void SetTile(int tile, bool faceUp = true)
        {
            Tile = tile;

            if (!faceUp || tile == NoTile)
            {
                ShowBack();
                return;
            }

            background.color = FaceColor;
            var face = TileFace.For(tile);
            SetLabel(rankLabel, face.Rank, face.Color, face.RankIsLarge);
            SetLabel(suitLabel, face.Suit, face.Color, isLarge: false);
        }

        void ShowBack()
        {
            background.color = BackColor;
            SetLabel(rankLabel, "", BackPattern, isLarge: false);
            SetLabel(suitLabel, "◆", BackPattern, isLarge: false);   // 牌背的菱形花樣
        }

        static void SetLabel(Text label, string content, Color color, bool isLarge)
        {
            label.text = content;
            label.color = color;
            label.fontStyle = isLarge ? FontStyle.Bold : FontStyle.Normal;
        }

        /// <summary>選取狀態：手牌點一下先選取，再點一次才打出。</summary>
        public void SetSelected(bool selected)
        {
            if (Tile == NoTile) return;
            background.color = selected ? SelectedTint : FaceColor;
        }

        /// <summary>不能點的時候整張變灰，讓玩家一眼看出現在不是他的回合。</summary>
        public void SetInteractable(bool interactable)
        {
            button.interactable = interactable;
            if (Tile == NoTile) return;
            background.color = interactable ? FaceColor : DimTint;
        }

        public void SetSize(Vector2 size) => Rect.sizeDelta = size;

        // ============================================================
        // 牌面內容
        //
        // 中文字型不一定拿得到（WebGL 若沒自備字型就沒有中文），
        // 所以牌面文字分兩套：有中文就用中文，沒有就自動換成英文代號。
        // ============================================================

        public struct Face
        {
            public string Rank;
            public string Suit;
            public Color Color;
            public bool RankIsLarge;
        }

        public static class TileFace
        {
            static readonly string[] SuitNamesChinese = { "萬", "筒", "條" };
            static readonly string[] SuitNamesLatin = { "W", "T", "B" };

            static readonly string[] HonorNamesChinese = { "東", "南", "西", "北", "中", "發", "白" };
            static readonly string[] HonorNamesLatin = { "E", "S", "W", "N", "C", "F", "P" };

            static readonly string[] FlowerNamesChinese = { "春", "夏", "秋", "冬", "梅", "蘭", "竹", "菊" };
            static readonly string[] FlowerNamesLatin = { "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8" };

            public static Face For(int tile)
            {
                if (TileDef.IsFlower(tile)) return FlowerFace(tile);
                if (TileDef.IsHonor(tile)) return HonorFace(tile);
                return SuitFace(tile);
            }

            static Face SuitFace(int tile)
            {
                int suitIndex = tile / 9;
                var colors = new[] { ManColor, PinColor, SouColor };
                string suitName = UiFont.SupportsChinese
                    ? SuitNamesChinese[suitIndex]
                    : SuitNamesLatin[suitIndex];

                return new Face
                {
                    Rank = TileDef.GetRank(tile).ToString(),
                    Suit = suitName,
                    Color = colors[suitIndex],
                    RankIsLarge = true
                };
            }

            static Face HonorFace(int tile)
            {
                int index = tile - TileDef.EAST;
                string name = UiFont.SupportsChinese ? HonorNamesChinese[index] : HonorNamesLatin[index];

                Color color = HonorColor;
                if (tile == TileDef.RED) color = DragonRed;
                else if (tile == TileDef.GREEN) color = DragonGreen;
                else if (tile == TileDef.WHITE) color = DragonBlue;

                // 字牌只有一個字，放大置中，下半部留白
                return new Face { Rank = name, Suit = "", Color = color, RankIsLarge = true };
            }

            static Face FlowerFace(int tile)
            {
                int index = tile - TileDef.FLOWER_BASE;
                string name = UiFont.SupportsChinese ? FlowerNamesChinese[index] : FlowerNamesLatin[index];
                return new Face
                {
                    Rank = name,
                    Suit = "",
                    Color = index < 4 ? DragonGreen : DragonRed,
                    RankIsLarge = true
                };
            }
        }
    }
}
