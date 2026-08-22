using System;
using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.View
{
    // ============================================================
    // 一張牌的外觀
    //
    // 立體感全部用圖層疊出來，不使用任何外部圖片素材：
    //
    //   Shadow    投影，往右下偏移
    //   Body      牌身側面，比牌面深一階
    //   Face      牌面，四邊內縮、底部內縮更多，露出的部分就是牌的厚度
    //   Highlight 牌面頂端的一道亮邊，模擬光從上方打下來
    //
    // 之後要換成美術素材，只要改這個檔案，其他地方都不用動。
    // ============================================================

    public class TileView : MonoBehaviour
    {
        public const int NoTile = GameState.NoTile;

        // ---- 牌面配色 ----
        static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.34f);
        static readonly Color BodyColor = new Color(0.78f, 0.73f, 0.61f);   // 牌的側面
        static readonly Color FaceColor = new Color(0.985f, 0.972f, 0.930f);
        static readonly Color HighlightColor = new Color(1f, 1f, 1f, 0.55f);

        static readonly Color BackBodyColor = new Color(0.10f, 0.30f, 0.20f);
        static readonly Color BackFaceColor = new Color(0.16f, 0.45f, 0.31f);
        static readonly Color BackPatternColor = new Color(0.24f, 0.60f, 0.42f);

        static readonly Color SelectedFace = new Color(1f, 0.95f, 0.74f);
        static readonly Color SelectedBody = new Color(0.82f, 0.72f, 0.44f);
        static readonly Color DimFace = new Color(0.74f, 0.73f, 0.70f);
        static readonly Color DimBody = new Color(0.58f, 0.57f, 0.54f);

        // ---- 牌面文字顏色 ----
        static readonly Color ManColor = new Color(0.70f, 0.13f, 0.13f);
        static readonly Color PinColor = new Color(0.11f, 0.34f, 0.66f);
        static readonly Color SouColor = new Color(0.09f, 0.46f, 0.25f);
        static readonly Color HonorColor = new Color(0.13f, 0.13f, 0.16f);
        static readonly Color DragonRed = new Color(0.76f, 0.11f, 0.11f);
        static readonly Color DragonGreen = new Color(0.07f, 0.48f, 0.23f);
        static readonly Color DragonBlue = new Color(0.14f, 0.30f, 0.60f);

        // ---- 立體感的比例 ----
        const float ShadowOffset = 0.055f;   // 投影偏移，相對牌高
        const float SideInset = 0.055f;      // 牌面左右內縮
        const float TopInset = 0.045f;       // 牌面頂端內縮
        const float ThicknessRatio = 0.135f; // 牌面底部內縮，露出來的就是厚度

        Image shadow;
        Image body;
        Image face;
        Image highlight;
        Text rankLabel;
        Text suitLabel;
        Button button;
        bool faceUp = true;
        Vector2 baseSize;

        /// <summary>這個 View 顯示的牌 id；蓋著的牌為 NoTile</summary>
        public int Tile { get; private set; } = NoTile;

        public RectTransform Rect => (RectTransform)transform;

        /// <summary>被點擊時通知外部。View 層只捕捉事件，不做任何規則判斷。</summary>
        public event Action<TileView> Clicked;

        // ------------------------------------------------------------

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
            baseSize = size;
            float thickness = size.y * ThicknessRatio;
            float sideInset = size.x * SideInset;
            float topInset = size.y * TopInset;
            float shadowOffset = size.y * ShadowOffset;

            shadow = UIFactory.CreateImage("Shadow", transform, ShadowColor);
            UIFactory.Stretch(shadow.rectTransform);
            shadow.rectTransform.anchoredPosition = new Vector2(shadowOffset * 0.6f, -shadowOffset);

            body = UIFactory.CreateImage("Body", transform, BodyColor, rounded: true, raycast: true);
            UIFactory.Stretch(body.rectTransform);

            face = UIFactory.CreateImage("Face", body.transform, FaceColor);
            face.rectTransform.anchorMin = Vector2.zero;
            face.rectTransform.anchorMax = Vector2.one;
            face.rectTransform.offsetMin = new Vector2(sideInset, thickness);
            face.rectTransform.offsetMax = new Vector2(-sideInset, -topInset);

            highlight = UIFactory.CreateImage("Highlight", face.transform, HighlightColor);
            UIFactory.Anchor(highlight.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                             new Vector2(0f, -2f), new Vector2(size.x - sideInset * 4f, size.y * 0.06f));

            button = gameObject.AddComponent<Button>();
            button.targetGraphic = body;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => { if (Clicked != null) Clicked(this); });

            BuildLabels(size);
        }

        void BuildLabels(Vector2 size)
        {
            int rankSize = Mathf.Max(10, Mathf.RoundToInt(size.y * 0.40f));
            int suitSize = Mathf.Max(9, Mathf.RoundToInt(size.y * 0.28f));

            rankLabel = UIFactory.CreateText("Rank", face.transform, "", rankSize, HonorColor);
            UIFactory.Anchor(rankLabel.rectTransform, new Vector2(0.5f, 0.70f), new Vector2(0.5f, 0.5f),
                             Vector2.zero, new Vector2(size.x, size.y * 0.5f));

            suitLabel = UIFactory.CreateText("Suit", face.transform, "", suitSize, HonorColor);
            UIFactory.Anchor(suitLabel.rectTransform, new Vector2(0.5f, 0.26f), new Vector2(0.5f, 0.5f),
                             Vector2.zero, new Vector2(size.x, size.y * 0.5f));
        }

        // ------------------------------------------------------------

        public void SetTile(int tile, bool faceUpValue = true)
        {
            Tile = tile;
            faceUp = faceUpValue && tile != NoTile;

            if (!faceUp)
            {
                ShowBack();
                return;
            }

            ApplyColors(FaceColor, BodyColor);
            var content = TileFace.For(tile);
            SetLabel(rankLabel, content.Rank, content.Color);
            SetLabel(suitLabel, content.Suit, content.Color);
            ApplyLabelLayout(singleGlyph: string.IsNullOrEmpty(content.Rank));
        }

        /// <summary>
        /// 數牌是「數字在上、花色在下」兩欄；
        /// 字牌與花牌只有一個字，要置中並放大，否則會偏在下半部看起來很怪。
        /// </summary>
        void ApplyLabelLayout(bool singleGlyph)
        {
            float pivotY = singleGlyph ? 0.5f : 0.26f;
            int fontSize = singleGlyph
                ? Mathf.Max(10, Mathf.RoundToInt(baseSize.y * 0.46f))
                : Mathf.Max(9, Mathf.RoundToInt(baseSize.y * 0.28f));

            var rect = suitLabel.rectTransform;
            rect.anchorMin = new Vector2(0.5f, pivotY);
            rect.anchorMax = new Vector2(0.5f, pivotY);
            rect.anchoredPosition = Vector2.zero;
            suitLabel.fontSize = fontSize;
        }

        void ShowBack()
        {
            ApplyColors(BackFaceColor, BackBodyColor);
            SetLabel(rankLabel, "", BackPatternColor);
            SetLabel(suitLabel, "◆", BackPatternColor);
            ApplyLabelLayout(singleGlyph: true);
        }

        void ApplyColors(Color faceColor, Color bodyColor)
        {
            face.color = faceColor;
            body.color = bodyColor;
        }

        static void SetLabel(Text label, string content, Color color)
        {
            label.text = content;
            label.color = color;
        }

        /// <summary>選取狀態：手牌點一下先選取，再點一次才打出。</summary>
        public void SetSelected(bool selected)
        {
            if (!faceUp) return;
            ApplyColors(selected ? SelectedFace : FaceColor, selected ? SelectedBody : BodyColor);
        }

        /// <summary>不能點時整張變灰，讓玩家一眼看出現在不是他的回合。</summary>
        public void SetInteractable(bool interactable)
        {
            button.interactable = interactable;
            if (!faceUp) return;
            ApplyColors(interactable ? FaceColor : DimFace, interactable ? BodyColor : DimBody);
        }

        /// <summary>發牌動畫用：整張牌的透明度</summary>
        public void SetAlpha(float alpha)
        {
            SetImageAlpha(shadow, alpha * 0.34f);
            SetImageAlpha(body, alpha);
            SetImageAlpha(face, alpha);
            SetImageAlpha(highlight, alpha * 0.55f);
            SetTextAlpha(rankLabel, alpha);
            SetTextAlpha(suitLabel, alpha);
        }

        static void SetImageAlpha(Image image, float alpha)
        {
            var color = image.color;
            color.a = alpha;
            image.color = color;
        }

        static void SetTextAlpha(Text text, float alpha)
        {
            var color = text.color;
            color.a = alpha;
            text.color = color;
        }

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
                return new Face
                {
                    Rank = TileDef.GetRank(tile).ToString(),
                    Suit = UiFont.SupportsChinese ? SuitNamesChinese[suitIndex] : SuitNamesLatin[suitIndex],
                    Color = colors[suitIndex]
                };
            }

            static Face HonorFace(int tile)
            {
                int index = tile - TileDef.EAST;
                Color color = HonorColor;
                if (tile == TileDef.RED) color = DragonRed;
                else if (tile == TileDef.GREEN) color = DragonGreen;
                else if (tile == TileDef.WHITE) color = DragonBlue;

                // 字牌只有一個字，放在牌面中央
                return new Face
                {
                    Rank = "",
                    Suit = UiFont.SupportsChinese ? HonorNamesChinese[index] : HonorNamesLatin[index],
                    Color = color
                };
            }

            static Face FlowerFace(int tile)
            {
                int index = tile - TileDef.FLOWER_BASE;
                return new Face
                {
                    Rank = "",
                    Suit = UiFont.SupportsChinese ? FlowerNamesChinese[index] : FlowerNamesLatin[index],
                    Color = index < 4 ? DragonGreen : DragonRed
                };
            }
        }
    }
}
