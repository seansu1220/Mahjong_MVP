using System;
using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.View
{
    // ============================================================
    // 用程式建立 uGUI 元件的共用工具
    //
    // 專案規定 UI 一律由程式生成：場景裡不放任何 Prefab，
    // 也不需要到 Inspector 拖拉引用。
    // ============================================================

    public static class UIFactory
    {
        /// <summary>
        /// 圓角九宮格圖，牌面與按鈕都用它。
        /// 用程式畫出來而不是拿 Unity 的 UI/Skin/UISprite——那是編輯器資源，
        /// 執行期會取到 null，牌就變成直角方塊了。
        /// </summary>
        public static Sprite RoundedSprite
        {
            get
            {
                if (roundedSprite == null) roundedSprite = CreateRoundedSprite(SpriteSize, CornerRadius);
                return roundedSprite;
            }
        }
        static Sprite roundedSprite;

        const int SpriteSize = 32;
        const int CornerRadius = 8;

        /// <summary>畫一張帶消鋸齒圓角的白色方塊，四邊留 radius 當九宮格邊界。</summary>
        static Sprite CreateRoundedSprite(int size, int radius)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "RoundedRect"
            };

            var pixels = new Color32[size * size];
            float inner = size - 1 - radius;

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // 把座標夾到「直邊區域」，量出離圓心多遠就知道在不在圓角內
                    float nearestX = Mathf.Clamp(x, radius, inner);
                    float nearestY = Mathf.Clamp(y, radius, inner);
                    float distance = Mathf.Sqrt((x - nearestX) * (x - nearestX)
                                              + (y - nearestY) * (y - nearestY));

                    float coverage = Mathf.Clamp01(radius + 0.5f - distance);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(coverage * 255f));
                }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                                 pixelsPerUnit: 100f, extrude: 0, meshType: SpriteMeshType.FullRect,
                                 border: new Vector4(radius, radius, radius, radius));
        }

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.localScale = Vector3.one;
            return rect;
        }

        public static Image CreateImage(string name, Transform parent, Color color,
                                        bool rounded = true, bool raycast = false)
        {
            var rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            if (rounded)
            {
                image.sprite = RoundedSprite;
                image.type = Image.Type.Sliced;
            }
            return image;
        }

        public static Text CreateText(string name, Transform parent, string content,
                                      int fontSize, Color color,
                                      TextAnchor anchor = TextAnchor.MiddleCenter,
                                      FontStyle style = FontStyle.Normal)
        {
            var rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = UiFont.Current;
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = anchor;
            text.fontStyle = style;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        public static Button CreateButton(string name, Transform parent, string label,
                                          Vector2 size, Color background, Color labelColor,
                                          Action onClick)
        {
            var image = CreateImage(name, parent, background, rounded: true, raycast: true);
            image.rectTransform.sizeDelta = size;

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(() => onClick());

            var text = CreateText("Label", image.transform, label,
                                  Mathf.RoundToInt(size.y * 0.42f), labelColor);
            Stretch(text.rectTransform);
            return button;
        }

        /// <summary>讓子物件填滿父物件</summary>
        public static void Stretch(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        /// <summary>把元件釘在畫布的某個位置</summary>
        public static void Anchor(RectTransform rect, Vector2 anchor, Vector2 pivot,
                                  Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }
    }

    // ============================================================
    // 字型解析
    //
    // 麻將牌面要顯示中文（萬筒條、東南西北中發白），
    // 但 Unity 內建字型只有英數字，取得中文字型分三條路：
    //
    //   1. 專案內自備字型 Assets/Resources/MahjongFont.ttf
    //      —— WebGL 唯一可行的方式，因為字型會被打包進建置結果
    //   2. 作業系統字型
    //      —— 編輯器與桌機版可用，WebGL 取不到系統字型
    //   3. Unity 內建字型
    //      —— 只有英數字，此時牌面自動改用英文代號
    //
    // 目前狀態可由 SupportsChinese 查詢，Bootstrap 會據此決定牌面樣式。
    // ============================================================

    public static class UiFont
    {
        /// <summary>放在 Assets/Resources/ 底下的字型檔名（不含副檔名）</summary>
        public const string BundledFontName = "MahjongFont";

        static readonly string[] PreferredOsFonts =
        {
            "Microsoft JhengHei UI", "Microsoft JhengHei", "微軟正黑體",
            "Microsoft YaHei UI", "Microsoft YaHei",
            "PingFang TC", "Heiti TC", "Noto Sans CJK TC", "Noto Sans TC", "SimHei"
        };

        static Font cached;
        static bool resolved;
        static bool supportsChinese;
        static string sourceDescription = "尚未解析";

        public static Font Current
        {
            get { EnsureResolved(); return cached; }
        }

        /// <summary>目前的字型能不能顯示中文</summary>
        public static bool SupportsChinese
        {
            get { EnsureResolved(); return supportsChinese; }
        }

        /// <summary>字型來源說明，顯示在畫面角落方便除錯</summary>
        public static string SourceDescription
        {
            get { EnsureResolved(); return sourceDescription; }
        }

        static void EnsureResolved()
        {
            if (resolved) return;
            resolved = true;

            var bundled = Resources.Load<Font>(BundledFontName);
            if (bundled != null)
            {
                cached = bundled;
                supportsChinese = true;
                sourceDescription = "專案內字型 " + BundledFontName;
                return;
            }

            string osFontName = FindInstalledChineseFont();
            if (osFontName != null)
            {
                var osFont = Font.CreateDynamicFontFromOSFont(osFontName, 48);
                if (osFont != null)
                {
                    cached = osFont;
                    supportsChinese = true;
                    sourceDescription = "系統字型 " + osFontName + "（WebGL 需改用專案內字型）";
                    return;
                }
            }

            cached = LoadBuiltinFont();
            supportsChinese = false;
            sourceDescription = "Unity 內建字型，牌面改用英文代號";
        }

        /// <summary>找出系統裡第一個可用的中文字型。WebGL 取不到系統字型，會回傳 null。</summary>
        static string FindInstalledChineseFont()
        {
            string[] installed;
            try { installed = Font.GetOSInstalledFontNames(); }
            catch (Exception) { return null; }

            if (installed == null || installed.Length == 0) return null;

            foreach (string preferred in PreferredOsFonts)
                foreach (string candidate in installed)
                    if (string.Equals(candidate, preferred, StringComparison.OrdinalIgnoreCase))
                        return candidate;
            return null;
        }

        static Font LoadBuiltinFont()
        {
            // Unity 2022 起內建字型改名為 LegacyRuntime.ttf。
            // 注意：傳舊名稱 "Arial.ttf" 進去會直接拋 ArgumentException 而不是回傳 null，
            // 所以不能拿它當備援，只能包起來確保取不到時也不會讓整個 UI 掛掉。
            try
            {
                return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (Exception error)
            {
                Debug.LogWarning("取不到 Unity 內建字型，文字將無法顯示：" + error.Message);
                return null;
            }
        }
    }
}
