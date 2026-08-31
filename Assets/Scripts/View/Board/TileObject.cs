using UnityEngine;

namespace Mahjong.View.Board
{
    // ============================================================
    // 一張 3D 麻將牌
    //
    // 結構就是實體牌的結構：
    //   Body  白色象牙牌身，是一個縮放過的立方體，也負責接點擊
    //   Face  牌面，貼在牌身正面（本地 +Z）前方一點點
    //   Back  牌背，貼在牌身背面（本地 -Z），綠色
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
        const float PanelOffset = 0.512f;   // 略大於 0.5，讓面板浮在牌身表面前方

        static readonly Color NormalTint = Color.white;
        static readonly Color SelectedTint = new Color(1f, 0.86f, 0.52f);
        static readonly Color ClaimTint = new Color(0.62f, 0.84f, 1f);
        static readonly Color DimTint = new Color(0.72f, 0.72f, 0.70f);

        MeshRenderer bodyRenderer;
        MeshRenderer faceRenderer;
        MeshRenderer backRenderer;
        MaterialPropertyBlock tintBlock;

        /// <summary>這張牌的 id；蓋著或未知為 NoTile</summary>
        public int Tile { get; private set; } = NoTile;

        /// <summary>是不是玩家可以點的牌。只有自己手上輪到出牌時才會開。</summary>
        public bool Clickable { get; set; }

        // ------------------------------------------------------------

        public static TileObject Create(Transform parent)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Tile";
            body.transform.SetParent(parent, worldPositionStays: false);
            body.transform.localScale = new Vector3(TileAssets.Width, TileAssets.Height, TileAssets.Depth);

            var view = body.AddComponent<TileObject>();
            view.Build(body);
            return view;
        }

        void Build(GameObject body)
        {
            bodyRenderer = body.GetComponent<MeshRenderer>();
            bodyRenderer.sharedMaterial = TileAssets.BodyMaterial;
            bodyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            faceRenderer = CreatePanel("Face", new Vector3(0f, 0f, PanelOffset), 0f);
            backRenderer = CreatePanel("Back", new Vector3(0f, 0f, -PanelOffset), 180f);
            backRenderer.sharedMaterial = TileAssets.BackMaterial;

            tintBlock = new MaterialPropertyBlock();
        }

        /// <summary>牌面／牌背都是一片薄薄的面片，貼在牌身表面前方</summary>
        MeshRenderer CreatePanel(string name, Vector3 localPosition, float yaw)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Destroy(quad.GetComponent<Collider>());   // 點擊只由牌身負責

            quad.transform.SetParent(transform, worldPositionStays: false);
            quad.transform.localPosition = localPosition;
            quad.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            quad.transform.localScale = new Vector3(PanelInset, PanelInset, 1f);

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
            faceRenderer.sharedMaterial = tile == NoTile
                ? TileAssets.BodyMaterial
                : TileAssets.FaceMaterial(tile);
        }

        /// <summary>擺放位置與朝向。朝向由 BoardLayout 算好。</summary>
        public void Place(Vector3 position, Quaternion rotation)
        {
            transform.localPosition = position;
            transform.localRotation = rotation;
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
            tintBlock.Clear();
            tintBlock.SetColor("_Color", tint);
            bodyRenderer.SetPropertyBlock(tintBlock);
            faceRenderer.SetPropertyBlock(tintBlock);
            backRenderer.SetPropertyBlock(tintBlock);
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }
    }
}
