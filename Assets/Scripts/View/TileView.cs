using System;
using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.View
{
    // ============================================================
    // 一張牌的外觀
    //
    // 真實的麻將牌是「白色牌身 + 綠色牌背」兩層黏在一起，
    // 所以不管正面反面，四邊都會看到白色的牌身。這裡照這個結構疊圖層：
    //
    //   Shadow   投影，往右下偏移
    //   Outline  比牌身大一圈的深色邊，讓相鄰的牌不會糊成一片
    //   Body     白色牌身。牌面內縮後露出來的部分就是牌的厚度
    //   Edge     牌身底部的暗帶，讓厚度看起來有受光差異
    //   Panel    正面是米白牌面、反面是綠色牌背，都比牌身小一圈
    //   Gloss    面板頂端的亮邊，模擬光從左上打下來
    //
    // 反面的綠色面板刻意內縮更多，白色牌身在四周露出一圈，
    // 這正是實體牌背看起來的樣子，疊起來時下層的白邊也會露出來。
    //
    // 全部用程式繪製，不使用任何外部圖片素材。
    // 之後要換成美術素材，只要改這個檔案。
    // ============================================================

    public class TileView : MonoBehaviour
    {
        public const int NoTile = GameState.NoTile;

        // ---- 牌身（白色部分）----
        static readonly Color BodyColor = new Color(0.925f, 0.915f, 0.875f);
        static readonly Color EdgeShadeColor = new Color(0.66f, 0.64f, 0.58f);
        static readonly Color OutlineColor = new Color(0.20f, 0.18f, 0.14f, 0.55f);
        static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.32f);
        static readonly Color GlossColor = new Color(1f, 1f, 1f, 0.5f);

        // ---- 面板 ----
        static readonly Color FaceColor = new Color(0.995f, 0.990f, 0.965f);
        static readonly Color BackColor = new Color(0.145f, 0.500f, 0.310f);

        static readonly Color SelectedFace = new Color(1f, 0.94f, 0.70f);
        static readonly Color SelectedBody = new Color(0.98f, 0.90f, 0.62f);
        static readonly Color ClaimFace = new Color(0.80f, 0.93f, 1f);
        static readonly Color ClaimBody = new Color(0.74f, 0.86f, 0.96f);
        static readonly Color DimFace = new Color(0.76f, 0.75f, 0.72f);
        static readonly Color DimBody = new Color(0.70f, 0.69f, 0.66f);

        // ---- 牌面文字顏色 ----
        static readonly Color ManColor = new Color(0.70f, 0.13f, 0.13f);
        static readonly Color PinColor = new Color(0.11f, 0.34f, 0.66f);
        static readonly Color SouColor = new Color(0.09f, 0.46f, 0.25f);
        static readonly Color HonorColor = new Color(0.13f, 0.13f, 0.16f);
        static readonly Color DragonRed = new Color(0.76f, 0.11f, 0.11f);
        static readonly Color DragonGreen = new Color(0.07f, 0.48f, 0.23f);
        static readonly Color DragonBlue = new Color(0.14f, 0.30f, 0.60f);

        // ---- 立體感比例（都相對牌的尺寸）----
        const float ShadowOffset = 0.06f;
        const float OutlineExpand = 0.028f;
        const float EdgeBandRatio = 0.10f;    // 牌身底部暗帶的高度

        // 正面：面板略為內縮，底部留多一點當厚度
        const float FaceSideInset = 0.06f;
        const float FaceTopInset = 0.05f;
        const float FaceBottomInset = 0.16f;

        // 反面：綠色牌背內縮更多，四周白色牌身露得更明顯
        const float BackSideInset = 0.15f;
        const float BackTopInset = 0.11f;
        const float BackBottomInset = 0.22f;

        Image shadow;
        Image outline;
        Image body;
        Image edgeBand;
        Image panel;
        Image gloss;
        Text rankLabel;
        Text suitLabel;
        Button button;

        Vector2 baseSize;
        bool faceUp = true;
        float shade = 1f;

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

            shadow = UIFactory.CreateImage("Shadow", transform, ShadowColor);
            UIFactory.Stretch(shadow.rectTransform);
            shadow.rectTransform.anchoredPosition =
                new Vector2(size.x * ShadowOffset * 0.5f, -size.y * ShadowOffset);

            outline = UIFactory.CreateImage("Outline", transform, OutlineColor);
            UIFactory.Stretch(outline.rectTransform, -size.x * OutlineExpand);

            body = UIFactory.CreateImage("Body", transform, BodyColor, rounded: true, raycast: true);
            UIFactory.Stretch(body.rectTransform);

            // 牌身底部的暗帶：厚度的下緣受光少，壓深一點才立體
            edgeBand = UIFactory.CreateImage("Edge", body.transform, EdgeShadeColor);
            UIFactory.Anchor(edgeBand.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                             new Vector2(0f, 1f), new Vector2(size.x * 0.9f, size.y * EdgeBandRatio));

            panel = UIFactory.CreateImage("Panel", body.transform, FaceColor);

            gloss = UIFactory.CreateImage("Gloss", panel.transform, GlossColor);
            UIFactory.Anchor(gloss.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                             new Vector2(0f, -2f), new Vector2(size.x * 0.72f, size.y * 0.055f));

            button = gameObject.AddComponent<Button>();
            button.targetGraphic = body;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => { if (Clicked != null) Clicked(this); });
        }

        /// <summary>
        /// 文字只有正面才需要。牌山一次要畫上百張蓋著的牌，
        /// 不建文字物件可以省下大量 UI 元件。
        /// </summary>
        void EnsureLabels()
        {
            if (rankLabel != null) return;

            rankLabel = UIFactory.CreateText("Rank", panel.transform, "",
                                             Mathf.Max(10, Mathf.RoundToInt(baseSize.y * 0.40f)),
                                             HonorColor);
            UIFactory.Anchor(rankLabel.rectTransform, new Vector2(0.5f, 0.70f), new Vector2(0.5f, 0.5f),
                             Vector2.zero, new Vector2(baseSize.x, baseSize.y * 0.5f));

            suitLabel = UIFactory.CreateText("Suit", panel.transform, "",
                                             Mathf.Max(9, Mathf.RoundToInt(baseSize.y * 0.28f)),
                                             HonorColor);
            UIFactory.Anchor(suitLabel.rectTransform, new Vector2(0.5f, 0.26f), new Vector2(0.5f, 0.5f),
                             Vector2.zero, new Vector2(baseSize.x, baseSize.y * 0.5f));
        }

        // ------------------------------------------------------------

        public void SetTile(int tile, bool faceUpValue = true)
        {
            Tile = tile;
            faceUp = faceUpValue && tile != NoTile;

            ApplyPanelInsets();
            ApplyColors(faceUp ? FaceColor : BackColor, BodyColor);

            if (!faceUp)
            {
                if (rankLabel != null) SetLabels("", "", HonorColor);
                return;
            }

            EnsureLabels();
            var content = TileFace.For(tile);
            SetLabels(content.Rank, content.Suit, content.Color);
            ApplyLabelLayout(singleGlyph: string.IsNullOrEmpty(content.Rank));
        }

        /// <summary>反面的綠色牌背內縮更多，讓白色牌身在四周露出一圈</summary>
        void ApplyPanelInsets()
        {
            float sideInset = baseSize.x * (faceUp ? FaceSideInset : BackSideInset);
            float topInset = baseSize.y * (faceUp ? FaceTopInset : BackTopInset);
            float bottomInset = baseSize.y * (faceUp ? FaceBottomInset : BackBottomInset);

            var rect = panel.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(sideInset, bottomInset);
            rect.offsetMax = new Vector2(-sideInset, -topInset);
        }

        void SetLabels(string rank, string suit, Color color)
        {
            if (rankLabel == null) return;
            rankLabel.text = rank;
            rankLabel.color = color;
            suitLabel.text = suit;
            suitLabel.color = color;
        }

        /// <summary>
        /// 數牌是「數字在上、花色在下」兩欄；
        /// 字牌與花牌只有一個字，要置中並放大，否則會偏在下半部看起來很怪。
        /// </summary>
        void ApplyLabelLayout(bool singleGlyph)
        {
            if (suitLabel == null) return;

            float pivotY = singleGlyph ? 0.5f : 0.26f;
            suitLabel.fontSize = singleGlyph
                ? Mathf.Max(10, Mathf.RoundToInt(baseSize.y * 0.46f))
                : Mathf.Max(9, Mathf.RoundToInt(baseSize.y * 0.28f));

            var rect = suitLabel.rectTransform;
            rect.anchorMin = new Vector2(0.5f, pivotY);
            rect.anchorMax = new Vector2(0.5f, pivotY);
            rect.anchoredPosition = Vector2.zero;
        }

        void ApplyColors(Color panelColor, Color bodyColor)
        {
            panel.color = panelColor * shade;
            body.color = bodyColor * shade;
            edgeBand.color = EdgeShadeColor * shade;
        }

        // ------------------------------------------------------------

        /// <summary>選取狀態：手牌點一下先選取，再點一次才打出。</summary>
        public void SetSelected(bool selected)
        {
            if (!faceUp) return;
            ApplyColors(selected ? SelectedFace : FaceColor, selected ? SelectedBody : BodyColor);
        }

        /// <summary>叫牌提示：標出手上會被拿去湊成那一組的牌，用冷色跟暖色的「已選取」區隔。</summary>
        public void SetClaimHighlight(bool highlighted)
        {
            if (!faceUp) return;
            ApplyColors(highlighted ? ClaimFace : FaceColor, highlighted ? ClaimBody : BodyColor);
        }

        /// <summary>不能點時整張變灰，讓玩家一眼看出現在不是他的回合。</summary>
        public void SetInteractable(bool interactable)
        {
            button.interactable = interactable;
            if (!faceUp) return;
            ApplyColors(interactable ? FaceColor : DimFace, interactable ? BodyColor : DimBody);
        }

        /// <summary>
        /// 整張壓暗。牌山下層那張要壓暗一點，疊起來才有上下層的分別。
        /// </summary>
        public void SetShade(float value)
        {
            shade = Mathf.Clamp01(value);
            ApplyColors(faceUp ? FaceColor : BackColor, BodyColor);
        }

        /// <summary>發牌與摸牌動畫用：整張牌的透明度</summary>
        public void SetAlpha(float alpha)
        {
            SetImageAlpha(shadow, alpha * ShadowColor.a);
            SetImageAlpha(outline, alpha * OutlineColor.a);
            SetImageAlpha(body, alpha);
            SetImageAlpha(edgeBand, alpha);
            SetImageAlpha(panel, alpha);
            SetImageAlpha(gloss, alpha * GlossColor.a);
            SetTextAlpha(rankLabel, alpha);
            SetTextAlpha(suitLabel, alpha);
        }

        static void SetImageAlpha(Image image, float alpha)
        {
            if (image == null) return;
            var color = image.color;
            color.a = alpha;
            image.color = color;
        }

        static void SetTextAlpha(Text text, float alpha)
        {
            if (text == null) return;
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
