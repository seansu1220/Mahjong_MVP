using UnityEngine;

namespace Mahjong.View.Board
{
    // ============================================================
    // 一張 3D 麻將牌
    //
    // 結構就是實體牌的結構，三個零件都掛在一個「沒有縮放」的空物件底下：
    //   Body  白色象牙牌身，一個縮放成牌形的立方體
    //   Face  牌面，貼在牌身正面（本地 +Z）外側一點點
    //   Back  牌背，貼在牌身背面（本地 -Z），綠色
    //
    // 零件不掛在立方體底下，是因為立方體是非等比縮放的，
    // 子物件的座標與尺寸都得反推換算，很容易算錯也難查。
    // 掛在未縮放的根物件上，所有數字就都是實際的世界尺寸。
    //
    // 牌身比牌面大一圈，所以不論從哪個角度看，
    // 牌面與牌背四周都會露出白色的牌身——這就是實體牌的樣子。
    //
    // 本地座標定義：X = 寬、Y = 牌面的高、Z = 厚度，牌面法線為 +Z。
    // ============================================================

    public class TileObject : MonoBehaviour
    {
        public const int NoTile = GameState.NoTile;

        /// <summary>牌面與牌背比牌身小的比例，露出來的白邊就是牌身</summary>
        const float PanelInset = 0.86f;

        /// <summary>面板浮出牌身表面的距離，避免與牌身共面閃爍</summary>
        const float PanelLift = 0.0015f;

        static readonly Color NormalTint = Color.white;
        static readonly Color SelectedTint = new Color(1f, 0.88f, 0.60f);
        static readonly Color ClaimTint = new Color(0.66f, 0.86f, 1f);
        static readonly Color DimTint = new Color(0.74f, 0.74f, 0.72f);

        MeshRenderer bodyRenderer;
        MeshRenderer faceRenderer;
        MeshRenderer backRenderer;
        MaterialPropertyBlock tintBlock;

        // MaterialPropertyBlock 設 _Color 是「取代」材質原本的顏色而不是相乘，
        // 所以要自己記住每一層的底色，上色時再乘上去，
        // 否則綠色牌背會被白色的 tint 整片蓋掉。
        Color faceBaseColor = Color.white;
        Vector3 basePosition;
        Quaternion baseRotation;

        /// <summary>這張牌的 id；蓋著或未知為 NoTile</summary>
        public int Tile { get; private set; } = NoTile;

        /// <summary>是不是玩家可以點的牌。只有自己手上輪到出牌時才會開。</summary>
        public bool Clickable { get; set; }

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

            bodyRenderer = CreateBody();
            faceRenderer = CreatePanel("Face", TileAssets.Depth * 0.5f + PanelLift, 0f);
            backRenderer = CreatePanel("Back", -(TileAssets.Depth * 0.5f + PanelLift), 180f);
            backRenderer.sharedMaterial = TileAssets.BackMaterial;

            tintBlock = new MaterialPropertyBlock();
            ApplyTint(NormalTint);
        }

        MeshRenderer CreateBody()
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            Destroy(body.GetComponent<Collider>());   // 點擊由根物件的 BoxCollider 負責

            body.transform.SetParent(transform, worldPositionStays: false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = new Vector3(TileAssets.Width, TileAssets.Height, TileAssets.Depth);

            var renderer = body.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = TileAssets.BodyMaterial;
            return renderer;
        }

        /// <summary>牌面／牌背都是一片薄薄的面片，浮在牌身表面外側</summary>
        MeshRenderer CreatePanel(string name, float z, float yaw)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Destroy(quad.GetComponent<Collider>());

            quad.transform.SetParent(transform, worldPositionStays: false);
            quad.transform.localPosition = new Vector3(0f, 0f, z);
            quad.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
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
            SetRendererColor(bodyRenderer, TileAssets.BodyColor * tint);
            SetRendererColor(faceRenderer, faceBaseColor * tint);
            SetRendererColor(backRenderer, TileAssets.BackColor * tint);
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
