using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.View.Board
{
    // ============================================================
    // 3D 牌的共用資源：尺寸、材質、牌面貼圖
    //
    // 牌面為什麼要先烘焙成貼圖，而不是直接用 3D 文字？
    // Unity 內建的 3D 文字（TextMesh）用的是 GUI/Text Shader，
    // 那支著色器寫死 ZTest Always，會穿透擋在前面的物件顯示出來，
    // 玩家手牌後面的牌河文字就會浮在手牌上。
    // 所以開場先把 42 種牌面各畫成一張貼圖，之後就是單純的 3D 材質，
    // 深度排序完全正確。
    //
    // 全部程式生成，不使用任何外部圖片素材。
    // ============================================================

    public static class TileAssets
    {
        // ---- 牌的尺寸（世界單位，比例接近實體麻將牌 20 x 26 x 15 mm）----
        public const float Width = 0.200f;
        public const float Height = 0.260f;
        public const float Depth = 0.150f;

        /// <summary>牌面貼圖的解析度</summary>
        const int FaceTextureWidth = 192;
        const int FaceTextureHeight = 248;

        /// <summary>烘焙用的臨時物件放得離牌桌很遠，主攝影機不會拍到</summary>
        static readonly Vector3 BakeOrigin = new Vector3(0f, 5000f, 0f);

        /// <summary>牌身的白色。上色時要乘在這個底色上，不能直接取代。</summary>
        public static readonly Color BodyColor = new Color(0.94f, 0.93f, 0.88f);

        /// <summary>牌背的綠色</summary>
        public static readonly Color BackColor = new Color(0.13f, 0.47f, 0.29f);
        static readonly Color FaceBackground = new Color(0.985f, 0.978f, 0.955f);

        static readonly Color ManColor = new Color(0.70f, 0.13f, 0.13f);
        static readonly Color PinColor = new Color(0.11f, 0.34f, 0.66f);
        static readonly Color SouColor = new Color(0.09f, 0.46f, 0.25f);
        static readonly Color HonorColor = new Color(0.13f, 0.13f, 0.16f);
        static readonly Color DragonRed = new Color(0.76f, 0.11f, 0.11f);
        static readonly Color DragonGreen = new Color(0.07f, 0.48f, 0.23f);
        static readonly Color DragonBlue = new Color(0.14f, 0.30f, 0.60f);

        static Material bodyMaterial;
        static Material backMaterial;
        static Material[] faceMaterials;
        static float? panelYawTowardPositiveZ;

        static readonly string[] SuitNamesChinese = { "萬", "筒", "條" };
        static readonly string[] SuitNamesLatin = { "W", "T", "B" };
        static readonly string[] HonorNamesChinese = { "東", "南", "西", "北", "中", "發", "白" };
        static readonly string[] HonorNamesLatin = { "E", "S", "W", "N", "C", "F", "P" };
        static readonly string[] FlowerNamesChinese = { "春", "夏", "秋", "冬", "梅", "蘭", "竹", "菊" };
        static readonly string[] FlowerNamesLatin = { "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8" };

        static int TotalKinds => TileDef.KINDS + TileDef.FLOWER_COUNT;

        // ------------------------------------------------------------

        /// <summary>牌身（白色象牙部分）</summary>
        public static Material BodyMaterial
        {
            get
            {
                if (bodyMaterial == null) bodyMaterial = CreateMaterial("TileBody", BodyColor);
                return bodyMaterial;
            }
        }

        /// <summary>牌背（綠色那一面）</summary>
        public static Material BackMaterial
        {
            get
            {
                if (backMaterial == null) backMaterial = CreateMaterial("TileBack", BackColor);
                return backMaterial;
            }
        }

        /// <summary>某一種牌的牌面材質。同種牌共用同一份，方便合批。</summary>
        public static Material FaceMaterial(int tile)
        {
            EnsureFaceMaterials();
            if (tile < 0 || tile >= faceMaterials.Length) return BodyMaterial;
            return faceMaterials[tile];
        }

        /// <summary>
        /// 內建 Quad 的正面要轉幾度才會朝向指定方向。
        ///
        /// Unity 內建 Quad 的法線是朝 -Z 的，如果照直覺把牌面放在 +Z 側又不轉，
        /// 它的正面會朝著牌身內部，被背面剔除掉——畫面上就只剩白色的牌身。
        /// 這個方向不保證每個 Unity 版本都一樣，所以不用記的，直接讀網格法線判斷。
        /// </summary>
        public static float PanelYaw(bool faceTowardPositiveZ)
        {
            if (!panelYawTowardPositiveZ.HasValue)
                panelYawTowardPositiveZ = MeasureQuadYaw();

            float towardPositiveZ = panelYawTowardPositiveZ.Value;
            return faceTowardPositiveZ ? towardPositiveZ : towardPositiveZ + 180f;
        }

        static float MeasureQuadYaw()
        {
            var probe = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var mesh = probe.GetComponent<MeshFilter>().sharedMesh;
            var normals = mesh == null ? null : mesh.normals;
            Object.Destroy(probe);

            bool alreadyFacesPositiveZ = normals != null && normals.Length > 0 && normals[0].z > 0f;
            return alreadyFacesPositiveZ ? 0f : 180f;
        }

        static Material CreateMaterial(string name, Color color)
        {
            var shader = FindLitShader();
            var material = new Material(shader) { name = name, color = color };

            // 麻將牌是霧面材質，反光壓低才不會像塑膠
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.18f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            return material;
        }

        /// <summary>
        /// 取受光的著色器。WebGL 建置若把 Standard 剝離掉，這裡會退回不受光的版本，
        /// 畫面會變平但不會整個變成紫色（找不到著色器的樣子）。
        /// 正式建置前要把 Standard 加進 Project Settings 的 Always Included Shaders。
        /// </summary>
        static Shader FindLitShader()
        {
            var shader = Shader.Find("Standard");
            if (shader != null) return shader;

            Debug.LogWarning("找不到 Standard 著色器，牌面改用不受光的版本；"
                             + "WebGL 建置前請將 Standard 加入 Always Included Shaders。");
            return Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default");
        }

        // ------------------------------------------------------------
        // 牌面烘焙
        // ------------------------------------------------------------

        static void EnsureFaceMaterials()
        {
            if (faceMaterials != null) return;
            faceMaterials = new Material[TotalKinds];

            int inkedCount = 0;
            var baker = new FaceBaker();
            for (int tile = 0; tile < TotalKinds; tile++)
            {
                bool hasInk;
                var texture = baker.Bake(tile, out hasInk);
                if (hasInk) inkedCount++;

                var material = CreateMaterial("TileFace" + tile, Color.white);
                material.mainTexture = texture;
                faceMaterials[tile] = material;
            }
            baker.Dispose();

            ReportBakeResult(inkedCount);
        }

        /// <summary>
        /// 牌面烘焙是最容易靜默失敗的一段：拍出來全白時畫面上看不出差別，
        /// 只會覺得「牌面沒東西」。所以一律回報結果，出問題時直接看得出是哪一關。
        /// </summary>
        static void ReportBakeResult(int inkedCount)
        {
            string summary = string.Format(
                "牌面烘焙：{0} 種牌，其中 {1} 種有畫到內容｜字型 {2}｜支援中文 {3}｜著色器 {4}",
                TotalKinds, inkedCount, UiFont.SourceDescription, UiFont.SupportsChinese,
                FindLitShader() == null ? "找不到" : FindLitShader().name);

            if (inkedCount == TotalKinds) Debug.Log(summary);
            else Debug.LogWarning(summary + "｜牌面沒有全部畫出來，請把這行貼給我");
        }

        /// <summary>
        /// 用一台離屏攝影機把 uGUI 畫出來的牌面拍成貼圖。
        /// 只在開場跑一次，之後牌面就是普通材質。
        /// </summary>
        class FaceBaker
        {
            readonly RenderTexture renderTexture;
            readonly GameObject root;
            readonly Camera camera;
            readonly Text rankLabel;
            readonly Text suitLabel;

            public FaceBaker()
            {
                renderTexture = new RenderTexture(FaceTextureWidth, FaceTextureHeight, 0,
                                                  RenderTextureFormat.ARGB32);

                root = new GameObject("TileFaceBaker");
                root.transform.position = BakeOrigin;

                camera = new GameObject("BakeCamera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform, worldPositionStays: false);

                // uGUI 的內容面向 +Z，所以攝影機要站在 +Z 那一側回頭看，
                // 站到 -Z 去會看到背面，字會左右相反。
                camera.transform.localPosition = new Vector3(0f, 0f, 10f);
                camera.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                camera.orthographic = true;
                camera.orthographicSize = FaceTextureHeight * 0.5f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = FaceBackground;
                camera.targetTexture = renderTexture;
                camera.enabled = false;   // 只在需要時手動 Render

                var canvasObject = new GameObject("BakeCanvas", typeof(Canvas));
                canvasObject.transform.SetParent(root.transform, worldPositionStays: false);
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = camera;

                var canvasRect = (RectTransform)canvasObject.transform;
                canvasRect.sizeDelta = new Vector2(FaceTextureWidth, FaceTextureHeight);
                canvasRect.localPosition = Vector3.zero;

                rankLabel = CreateLabel(canvasRect, "Rank", Mathf.RoundToInt(FaceTextureHeight * 0.40f),
                                        new Vector2(0f, FaceTextureHeight * 0.20f));
                suitLabel = CreateLabel(canvasRect, "Suit", Mathf.RoundToInt(FaceTextureHeight * 0.30f),
                                        new Vector2(0f, -FaceTextureHeight * 0.22f));
            }

            static Text CreateLabel(RectTransform parent, string name, int fontSize, Vector2 position)
            {
                var label = UIFactory.CreateText(name, parent, "", fontSize, Color.black);
                UIFactory.Anchor(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                 position, new Vector2(FaceTextureWidth, FaceTextureHeight * 0.5f));
                return label;
            }

            public Texture2D Bake(int tile, out bool hasInk)
            {
                ApplyFace(tile);

                // Canvas 的網格是在畫面更新時才重建的。剛改完文字就直接 Render()
                // 會拍到還沒建好的畫面，結果就是一片空白，所以要先強制更新。
                var font = UiFont.Current;
                if (font != null)
                {
                    font.RequestCharactersInTexture(rankLabel.text, rankLabel.fontSize);
                    font.RequestCharactersInTexture(suitLabel.text, suitLabel.fontSize);
                }
                Canvas.ForceUpdateCanvases();

                camera.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = renderTexture;

                var texture = new Texture2D(FaceTextureWidth, FaceTextureHeight,
                                            TextureFormat.RGB24, mipChain: true)
                {
                    name = "TileFace" + tile,
                    filterMode = FilterMode.Trilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 4
                };
                texture.ReadPixels(new Rect(0f, 0f, FaceTextureWidth, FaceTextureHeight), 0, 0);

                // 先確認真的畫到東西了，再把貼圖釋放成不可讀
                hasInk = HasVisibleInk(texture);
                texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);

                RenderTexture.active = previous;
                return texture;
            }

            /// <summary>檢查貼圖上有沒有明顯比底色深的像素，也就是字有沒有畫出來</summary>
            static bool HasVisibleInk(Texture2D texture)
            {
                const int SampleStep = 4;
                const float DarkEnough = 0.55f;

                var pixels = texture.GetPixels32();
                for (int i = 0; i < pixels.Length; i += SampleStep)
                {
                    var pixel = pixels[i];
                    float brightness = (pixel.r + pixel.g + pixel.b) / (3f * 255f);
                    if (brightness < DarkEnough) return true;
                }
                return false;
            }

            void ApplyFace(int tile)
            {
                string rank, suit;
                Color color;
                DescribeFace(tile, out rank, out suit, out color);

                rankLabel.text = rank;
                rankLabel.color = color;
                suitLabel.text = suit;
                suitLabel.color = color;

                // 只有一個字的牌（字牌、花牌）要置中放大，不能沿用數牌的上下兩欄
                bool singleGlyph = string.IsNullOrEmpty(rank);
                var rect = suitLabel.rectTransform;
                rect.anchoredPosition = singleGlyph
                    ? Vector2.zero
                    : new Vector2(0f, -FaceTextureHeight * 0.22f);
                suitLabel.fontSize = Mathf.RoundToInt(
                    FaceTextureHeight * (singleGlyph ? 0.52f : 0.30f));
            }

            public void Dispose()
            {
                camera.targetTexture = null;
                Object.Destroy(root);
                renderTexture.Release();
                Object.Destroy(renderTexture);
            }
        }

        // ------------------------------------------------------------

        static void DescribeFace(int tile, out string rank, out string suit, out Color color)
        {
            bool chinese = UiFont.SupportsChinese;

            if (TileDef.IsFlower(tile))
            {
                int index = tile - TileDef.FLOWER_BASE;
                rank = "";
                suit = chinese ? FlowerNamesChinese[index] : FlowerNamesLatin[index];
                color = index < 4 ? DragonGreen : DragonRed;
                return;
            }

            if (TileDef.IsHonor(tile))
            {
                int index = tile - TileDef.EAST;
                rank = "";
                suit = chinese ? HonorNamesChinese[index] : HonorNamesLatin[index];
                color = HonorColor;
                if (tile == TileDef.RED) color = DragonRed;
                else if (tile == TileDef.GREEN) color = DragonGreen;
                else if (tile == TileDef.WHITE) color = DragonBlue;
                return;
            }

            int suitIndex = tile / 9;
            rank = TileDef.GetRank(tile).ToString();
            suit = chinese ? SuitNamesChinese[suitIndex] : SuitNamesLatin[suitIndex];
            color = suitIndex == 0 ? ManColor : (suitIndex == 1 ? PinColor : SouColor);
        }
    }
}
