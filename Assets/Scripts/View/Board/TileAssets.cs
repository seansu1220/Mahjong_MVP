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

        /// <summary>
        /// 白色牌面板佔整體厚度的比例，其餘是綠色牌背。
        ///
        /// 真牌就是薄薄一層白色面板貼在有色的牌身上，所以側邊看過去幾乎都是綠的。
        /// 這件事對排版很關鍵：桌上的牌是一張接一張往畫面深處排的，
        /// 露出來的就是後面那張的側邊——側邊是綠的才分得出兩張牌，
        /// 側邊要是白的就會跟前面那張的牌面連成一片（見 BoardLayout.DiscardStepAway）。
        /// </summary>
        public const float FrontDepthRatio = 0.28f;

        /// <summary>平躺那張牌照的俯角。跟牌桌的視角接近，看起來才像同一張桌上的牌。</summary>
        public const float LyingTiltDegrees = 55f;

        /// <summary>平躺那張圖的寬高比。2D 排版要先知道它多高。</summary>
        public static float LyingSpriteAspect
        {
            get
            {
                float tilt = LyingTiltDegrees * Mathf.Deg2Rad;
                return Width / (Height * Mathf.Sin(tilt) + Depth * Mathf.Cos(tilt));
            }
        }

        /// <summary>拍照用的環境光。與牌桌上的環境光同一個調子，拍出來才不會色差。</summary>
        static readonly Color BakeAmbient = new Color(0.42f, 0.45f, 0.43f);

        /// <summary>牌面貼圖的解析度</summary>
        const int FaceTextureWidth = 192;
        const int FaceTextureHeight = 248;

        /// <summary>烘焙用的臨時物件放得離牌桌很遠，主攝影機不會拍到</summary>
        static readonly Vector3 BakeOrigin = new Vector3(0f, 5000f, 0f);

        /// <summary>牌身的白色。上色時要乘在這個底色上，不能直接取代。</summary>
        public static readonly Color BodyColor = new Color(0.97f, 0.965f, 0.945f);

        /// <summary>
        /// 牌背的綠色。刻意比桌布亮一階也更飽和，
        /// 不然牌背貼在桌面上會整片糊在一起分不出來。
        /// </summary>
        public static readonly Color BackColor = new Color(0.20f, 0.63f, 0.40f);
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
        static Sprite ringSprite;
        static Material[] faceMaterials;
        static Texture2D[] faceTextures;
        static Sprite[] tileSprites;
        static Sprite[] lyingTileSprites;
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
        /// 一整張 3D 牌拍成的 2D 圖，給介面用。
        ///
        /// 自己的手牌、結算牌型、打牌提示都是 2D 的。與其在 UI 裡用色塊
        /// 拼出假的立體感（做過，看起來像貼紙），不如把真正的 3D 牌放到
        /// 離屏攝影機前拍一張——頂面、側面、光影全部是真的。
        /// 市面上的麻將遊戲用的也是這個做法。
        ///
        /// 只在開場拍一次，42 種牌各一張。
        /// </summary>
        public static Sprite TileSprite(int tile)
        {
            if (tileSprites == null) tileSprites = BakeAll(lying: false);
            if (tile < 0 || tile >= tileSprites.Length) return null;
            return tileSprites[tile];
        }

        /// <summary>
        /// 同一張牌，但是**平躺著、從斜上方拍**的那一張。
        ///
        /// 自己吃碰出來的牌用這張：真實牌桌上吃碰的牌是攤平放著的，
        /// 跟立在手上的牌一眼就分得出來。差別在牌的厚度露在哪一邊——
        /// 立著的露在上緣，平躺的露在下緣。
        /// </summary>
        public static Sprite LyingTileSprite(int tile)
        {
            if (lyingTileSprites == null) lyingTileSprites = BakeAll(lying: true);
            if (tile < 0 || tile >= lyingTileSprites.Length) return null;
            return lyingTileSprites[tile];
        }

        static Sprite[] BakeAll(bool lying)
        {
            var sprites = new Sprite[TotalKinds];

            var baker = new TileBaker(lying);
            for (int tile = 0; tile < TotalKinds; tile++) sprites[tile] = baker.Bake(tile);
            var sample = baker.CentreSample;
            baker.Dispose();

            Debug.Log(string.Format(
                "3D 牌已拍成 2D 圖（{0}）：{1} 種｜牌面中央亮度 RGB({2},{3},{4}) A{5}（越接近 255 越白）",
                lying ? "平躺" : "立著", TotalKinds, sample.r, sample.g, sample.b, sample.a));

            return sprites;
        }

        /// <summary>
        /// 把一張 3D 牌拍成去背的 2D 圖。
        /// 攝影機略高於牌並往下看一點點，這樣才看得到牌的頂面，
        /// 平視的話就跟純平面沒兩樣了。
        /// </summary>
        class TileBaker
        {
            const int TextureWidth = 176;
            const float Distance = 0.7f;

            /// <summary>立著拍時攝影機往下看幾度。太大牌面會被壓扁，太小就看不到上緣。</summary>
            const float StandingTiltDegrees = 11f;

            /// <summary>
            /// 畫面四周留的空白比例。留太多牌之間就會看起來有一圈空隙，
            /// 所以貼圖高度是照牌的實際比例算的，只留 2% 給反鋸齒。
            /// </summary>
            const float FramePadding = 1.02f;

            readonly int textureHeight;

            readonly RenderTexture renderTexture;
            readonly GameObject root;
            readonly Camera camera;
            readonly TileObject tile;
            readonly Color32[] onBlack;
            readonly Color32[] onWhite;
            readonly Texture2D scratch;

            readonly UnityEngine.Rendering.AmbientMode previousAmbientMode;
            readonly Color previousAmbient;

            readonly int centreX;
            readonly int centreY;
            readonly bool lying;

            /// <summary>最後拍的那張牌，正中央像素的亮度。純粹拿來對照桌上的牌亮不亮。</summary>
            public Color32 CentreSample { get; private set; }

            public TileBaker(bool lyingFlat)
            {
                lying = lyingFlat;

                float tilt = (lying ? LyingTiltDegrees : StandingTiltDegrees) * Mathf.Deg2Rad;
                float frameHeight = FrameHeight(tilt);

                // 貼圖比例照牌的實際比例走，牌才會填滿整張圖、四周不留空白
                textureHeight = Mathf.RoundToInt(TextureWidth * frameHeight / Width);

                renderTexture = new RenderTexture(TextureWidth, textureHeight, 16,
                                                  RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 4   // 牌的邊緣才不會有鋸齒
                };

                onBlack = new Color32[TextureWidth * textureHeight];
                onWhite = new Color32[TextureWidth * textureHeight];
                scratch = new Texture2D(TextureWidth, textureHeight, TextureFormat.RGBA32,
                                        mipChain: false);

                root = new GameObject("TileSpriteBaker");
                root.transform.position = BakeOrigin;

                tile = TileObject.Create(root.transform);
                // 立著：牌面朝 +Z，也就是朝向攝影機；平躺：牌面朝上。
                // 平躺時牌的上緣要朝 -Z：拍照的攝影機站在 +Z 往回看，
                // 朝 +Z 的話字會上下顛倒（牌桌上的攝影機在 -Z，所以那邊剛好相反）。
                tile.Place(Vector3.zero, lying
                    ? Quaternion.LookRotation(Vector3.up, Vector3.back)
                    : Quaternion.identity);

                camera = new GameObject("BakeCamera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform, worldPositionStays: false);

                camera.transform.localPosition =
                    new Vector3(0f, Distance * Mathf.Sin(tilt), Distance * Mathf.Cos(tilt));
                camera.transform.LookAt(root.transform.position);
                centreX = TextureWidth / 2;
                centreY = textureHeight / 2;

                camera.orthographic = true;
                camera.orthographicSize = frameHeight * 0.5f * FramePadding;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.targetTexture = renderTexture;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 3f;
                camera.enabled = false;   // 只在需要時手動 Render

                previousAmbientMode = RenderSettings.ambientMode;
                previousAmbient = RenderSettings.ambientLight;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = BakeAmbient;

                CreateKeyLight();
            }

            /// <summary>
            /// 拍照要自備燈光。
            ///
            /// 牌桌的燈是從斜上方打下來的，桌上的牌平躺、正面朝天，照得剛剛好；
            /// 但拍照時牌是立著的、正面朝著攝影機，那盞燈等於從牌的背後照過來，
            /// 正面只剩環境光——拍出來就是一張灰牌。
            ///
            /// 所以在攝影機這一側補一盞主燈，正面才會跟桌上的牌一樣亮。
            /// 燈掛在拍照用的根物件底下，拍完就跟著一起銷毀。
            /// </summary>
            void CreateKeyLight()
            {
                var lightObject = new GameObject("BakeKeyLight");
                lightObject.transform.SetParent(root.transform, worldPositionStays: false);

                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;

                // 從攝影機那一側往下照，角度要對著牌面：
                // 立著的牌面幾乎是垂直的，平躺的牌面是水平的，燈得跟著轉。
                light.transform.rotation = Quaternion.Euler(lying ? 62f : 28f, 200f, 0f);
                // 比牌桌主燈再亮一些：桌上的牌是正面朝天、正對主燈的，
                // 拍照時牌立著，同樣的強度打在正面上會偏暗一階。
                light.intensity = 1.15f;
                light.color = new Color(1f, 0.98f, 0.94f);
                light.shadows = LightShadows.None;
            }

            /// <summary>
            /// 斜著看的時候，牌在畫面上佔的高度是牌高與牌厚各自的投影相加。
            /// 立著與平躺，兩者的投影剛好對調。由它決定取景範圍與貼圖比例。
            /// </summary>
            float FrameHeight(float tilt)
                => lying
                    ? TileAssets.Height * Mathf.Sin(tilt) + TileAssets.Depth * Mathf.Cos(tilt)
                    : TileAssets.Height * Mathf.Cos(tilt) + TileAssets.Depth * Mathf.Sin(tilt);

            public Sprite Bake(int tileId)
            {
                tile.SetTile(tileId);

                // 拍兩次：一次黑底、一次白底。
                // 有牌擋著的地方兩張一模一樣，沒擋到的地方差整整一個黑白，
                // 兩張一減就得到精確的去背遮罩——不必去猜著色器有沒有正確寫出 alpha。
                RenderOnto(Color.black, onBlack);
                RenderOnto(Color.white, onWhite);

                var texture = new Texture2D(TextureWidth, textureHeight, TextureFormat.RGBA32, mipChain: true)
                {
                    name = "TileSprite" + tileId,
                    filterMode = FilterMode.Trilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 4
                };
                var pixels = Composite();
                CentreSample = pixels[centreY * TextureWidth + centreX];
                texture.SetPixels32(pixels);
                texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);

                return Sprite.Create(texture, new Rect(0f, 0f, TextureWidth, textureHeight),
                                     new Vector2(0.5f, 0.5f), pixelsPerUnit: 100f);
            }

            void RenderOnto(Color background, Color32[] target)
            {
                camera.backgroundColor = background;
                camera.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = renderTexture;

                scratch.ReadPixels(new Rect(0f, 0f, TextureWidth, textureHeight), 0, 0);
                scratch.Apply(updateMipmaps: false);
                scratch.GetPixels32().CopyTo(target, 0);

                RenderTexture.active = previous;
            }

            /// <summary>
            /// 由黑底與白底兩張算出每個像素的透明度與原色。
            /// 覆蓋率 a 的像素：黑底得到 a·C、白底得到 a·C + (1-a)，
            /// 兩者一減就是 1-a，再把顏色除回去即可。
            /// </summary>
            Color32[] Composite()
            {
                var result = new Color32[TextureWidth * textureHeight];
                var fallback = (Color32)BodyColor;

                for (int i = 0; i < result.Length; i++)
                {
                    float difference = Mathf.Max(
                        Mathf.Max(onWhite[i].r - onBlack[i].r, onWhite[i].g - onBlack[i].g),
                        onWhite[i].b - onBlack[i].b) / 255f;

                    float alpha = Mathf.Clamp01(1f - difference);
                    if (alpha < 0.004f)
                    {
                        // 透明處仍填牌身色，縮圖時邊緣才不會滲出黑邊
                        result[i] = new Color32(fallback.r, fallback.g, fallback.b, 0);
                        continue;
                    }

                    result[i] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(onBlack[i].r / alpha), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(onBlack[i].g / alpha), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(onBlack[i].b / alpha), 0, 255),
                        (byte)Mathf.RoundToInt(alpha * 255f));
                }
                return result;
            }

            public void Dispose()
            {
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbient;

                camera.targetTexture = null;
                Object.Destroy(scratch);
                Object.Destroy(root);
                renderTexture.Release();
                Object.Destroy(renderTexture);
            }
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

        /// <summary>
        /// 剛打出那張牌外面套的圓框。程式畫的環狀漸層，不用外部素材。
        /// 給 2D 介面用，所以是 Sprite 而不是材質。
        /// </summary>
        public static Sprite RingSprite
        {
            get
            {
                if (ringSprite != null) return ringSprite;

                var texture = CreateRingTexture();
                ringSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                                           new Vector2(0.5f, 0.5f), pixelsPerUnit: 100f);
                return ringSprite;
            }
        }

        /// <summary>
        /// 環狀漸層：亮度在某個半徑上最強，往內往外都淡掉，
        /// 看起來就是一圈發光的框而不是一塊圓餅。
        /// </summary>
        static Texture2D CreateRingTexture()
        {
            const int Size = 128;
            const float RingRadius = 0.80f;   // 佔半徑的比例
            const float RingWidth = 0.26f;

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, mipChain: false)
            {
                name = "RingTexture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[Size * Size];
            float centre = (Size - 1) * 0.5f;

            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float dx = (x - centre) / centre;
                    float dy = (y - centre) / centre;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);

                    float falloff = Mathf.Abs(distance - RingRadius) / RingWidth;
                    float alpha = Mathf.Clamp01(1f - falloff);
                    alpha *= alpha;                       // 收得快一點，框才不會糊
                    if (distance > 1f) alpha = 0f;        // 超出圓形範圍一律透明

                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return texture;
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

            faceTextures = new Texture2D[TotalKinds];

            int inkedCount = 0;
            var baker = new FaceBaker();
            for (int tile = 0; tile < TotalKinds; tile++)
            {
                bool hasInk;
                var texture = baker.Bake(tile, out hasInk);
                if (hasInk) inkedCount++;
                faceTextures[tile] = texture;

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

                // 拍出來的字是左右相反的，直接在這裡翻正。
                // 之前是在 3D 材質的 UV 上翻，但 2D 手牌用的是原始貼圖、吃不到那個翻轉，
                // 結果 2D 還是顛倒的。翻在貼圖本身，兩邊就都正確。
                hasInk = MirrorHorizontallyAndCheckInk(texture);
                texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);

                RenderTexture.active = previous;
                return texture;
            }

            /// <summary>
            /// 把貼圖左右翻正，順便回報上面有沒有明顯比底色深的像素
            /// （也就是字到底有沒有畫出來）。兩件事都要掃過整張圖，一起做省一次。
            /// </summary>
            static bool MirrorHorizontallyAndCheckInk(Texture2D texture)
            {
                const float DarkEnough = 0.55f;

                var pixels = texture.GetPixels32();
                int width = texture.width;
                bool hasInk = false;

                for (int row = 0; row < texture.height; row++)
                {
                    int start = row * width;
                    for (int column = 0; column < width / 2; column++)
                    {
                        int left = start + column;
                        int right = start + width - 1 - column;
                        var swap = pixels[left];
                        pixels[left] = pixels[right];
                        pixels[right] = swap;
                    }

                    if (hasInk) continue;
                    for (int column = 0; column < width; column += 3)
                    {
                        var pixel = pixels[start + column];
                        float brightness = (pixel.r + pixel.g + pixel.b) / (3f * 255f);
                        if (brightness < DarkEnough) { hasInk = true; break; }
                    }
                }

                texture.SetPixels32(pixels);
                return hasInk;
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
