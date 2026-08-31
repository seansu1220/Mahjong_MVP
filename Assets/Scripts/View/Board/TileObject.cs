using UnityEngine;

namespace Mahjong.View.Board
{
    // ============================================================
    // 一張 3D 麻將牌
    //
    // 實體麻將牌是「白色象牙牌身」黏上「綠色牌背」兩塊料，
    // 綠色佔的是整個後半塊而不是薄薄一片——從側面看得到白綠的分界線。
    // 所以這裡也用兩塊立方體前後接起來：
    //
    //   BodyFront  前半塊，白色象牙，佔厚度的 58%
    //   BodyBack   後半塊，綠色牌背，佔厚度的 42%
    //   Face       牌面，一片薄面片，浮在前半塊的正面外側
    //
    // 三個零件都掛在一個「沒有縮放」的空物件底下，
    // 這樣所有數字都是實際的世界尺寸，不必反推換算。
    //
    // 本地座標定義：X = 寬、Y = 牌面的高、Z = 厚度，牌面法線為 +Z。
    // ============================================================

    public class TileObject : MonoBehaviour
    {
        public const int NoTile = GameState.NoTile;

        /// <summary>白色牌身佔整體厚度的比例，其餘是綠色牌背</summary>
        const float FrontDepthRatio = 0.58f;

        /// <summary>牌面比牌身小的比例，露出來的白邊就是牌身</summary>
        const float PanelInset = 0.86f;

        /// <summary>牌面浮出牌身表面的距離，避免與牌身共面閃爍</summary>
        const float PanelLift = 0.0015f;

        static readonly Color NormalTint = Color.white;
        static readonly Color SelectedTint = new Color(1f, 0.88f, 0.60f);
        static readonly Color ClaimTint = new Color(0.66f, 0.86f, 1f);
        static readonly Color DimTint = new Color(0.74f, 0.74f, 0.72f);

        MeshRenderer frontRenderer;
        MeshRenderer backRenderer;
        MeshRenderer faceRenderer;
        MaterialPropertyBlock tintBlock;

        // MaterialPropertyBlock 設 _Color 是「取代」材質原本的顏色而不是相乘，
        // 所以要自己記住每一塊的底色，上色時再乘上去，
        // 否則綠色牌背會被白色的 tint 整片蓋掉。
        Color faceBaseColor = Color.white;
        Vector3 basePosition;
        Quaternion baseRotation;

        /// <summary>這張牌的 id；蓋著或未知為 NoTile</summary>
        public int Tile { get; private set; } = NoTile;

        // ------------------------------------------------------------

        public static TileObject Create(Transform parent)
        {
            var root = new GameObject("Tile");
            root.transform.SetParent(parent, worldPositionStays: false);

            var view = root.AddComponent<TileObject>();
            view.Build();
            return view;
        }

        void Build()
        {
            var collider = gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(TileAssets.Width, TileAssets.Height, TileAssets.Depth);

            float frontDepth = TileAssets.Depth * FrontDepthRatio;
            float backDepth = TileAssets.Depth - frontDepth;

            // 兩塊前後接在一起：前半塊白、後半塊綠，接縫在厚度中間偏後
            frontRenderer = CreateBlock("BodyFront", frontDepth,
                                        (TileAssets.Depth - frontDepth) * 0.5f,
                                        TileAssets.BodyMaterial);
            backRenderer = CreateBlock("BodyBack", backDepth,
                                       -(TileAssets.Depth - backDepth) * 0.5f,
                                       TileAssets.BackMaterial);

            faceRenderer = CreateFacePanel();

            tintBlock = new MaterialPropertyBlock();
            ApplyTint(NormalTint);
        }

        MeshRenderer CreateBlock(string name, float depth, float z, Material material)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            Destroy(block.GetComponent<Collider>());   // 點擊由根物件的 BoxCollider 負責

            block.transform.SetParent(transform, worldPositionStays: false);
            block.transform.localPosition = new Vector3(0f, 0f, z);
            block.transform.localRotation = Quaternion.identity;
            block.transform.localScale = new Vector3(TileAssets.Width, TileAssets.Height, depth);

            var renderer = block.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        /// <summary>牌面是一片薄面片，浮在前半塊的正面外側</summary>
        MeshRenderer CreateFacePanel()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Face";
            Destroy(quad.GetComponent<Collider>());

            quad.transform.SetParent(transform, worldPositionStays: false);
            quad.transform.localPosition = new Vector3(0f, 0f, TileAssets.Depth * 0.5f + PanelLift);

            // 內建 Quad 的正面朝哪邊不保證每個 Unity 版本都一樣，交給 TileAssets 讀網格判斷
            quad.transform.localRotation = Quaternion.Euler(0f, TileAssets.PanelYaw(true), 0f);
            quad.transform.localScale = new Vector3(TileAssets.Width * PanelInset,
                                                    TileAssets.Height * PanelInset, 1f);

            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = TileAssets.BodyMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return renderer;
        }

        // ------------------------------------------------------------

        /// <summary>設定這張是什麼牌。NoTile 表示未知，牌面留白。</summary>
        public void SetTile(int tile)
        {
            Tile = tile;

            bool unknown = tile == NoTile;
            faceRenderer.sharedMaterial = unknown
                ? TileAssets.BodyMaterial
                : TileAssets.FaceMaterial(tile);

            // 牌面有貼圖時底色要是白的，貼圖顏色才不會被染到
            faceBaseColor = unknown ? TileAssets.BodyColor : Color.white;
            ApplyTint(NormalTint);
        }

        /// <summary>擺放位置與朝向。朝向由 BoardLayout 算好。</summary>
        public void Place(Vector3 position, Quaternion rotation)
        {
            basePosition = position;
            baseRotation = rotation;
            transform.localPosition = position;
            transform.localRotation = rotation;
        }

        /// <summary>把牌從原位抬起來，做出「被拿起來」的感覺。</summary>
        public void SetLift(Vector3 offset)
        {
            transform.localPosition = basePosition + offset;
            transform.localRotation = baseRotation;
        }

        // ------------------------------------------------------------
        // 上色。用 MaterialPropertyBlock 而不是換材質，才不會破壞合批。
        // ------------------------------------------------------------

        public void SetNormal() => ApplyTint(NormalTint);
        public void SetSelected() => ApplyTint(SelectedTint);
        public void SetClaimHighlight() => ApplyTint(ClaimTint);
        public void SetDimmed() => ApplyTint(DimTint);

        void ApplyTint(Color tint)
        {
            if (tintBlock == null) return;
            SetRendererColor(frontRenderer, TileAssets.BodyColor * tint);
            SetRendererColor(backRenderer, TileAssets.BackColor * tint);
            SetRendererColor(faceRenderer, faceBaseColor * tint);
        }

        void SetRendererColor(MeshRenderer renderer, Color color)
        {
            if (renderer == null) return;
            tintBlock.Clear();
            tintBlock.SetColor("_Color", color);
            renderer.SetPropertyBlock(tintBlock);
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }
    }
}
