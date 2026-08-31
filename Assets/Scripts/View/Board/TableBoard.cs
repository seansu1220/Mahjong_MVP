using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mahjong.View.Board
{
    // ============================================================
    // 3D 牌桌
    //
    // 桌面、牌山、三家對手的手牌、四家的副露與牌河都是真的 3D 物件，
    // 由一台俯視攝影機拍下來。
    //
    // **自己的手牌不在這裡畫**——攝影機俯視牌桌，立著的牌只看得到上緣，
    // 把牌後仰又很不自然。自己的手牌交給 2D 的 HandStrip 畫在畫面下緣，
    // 牌面共用同一份烘焙貼圖，所以兩邊的牌長得一模一樣。
    //
    // 這一層只負責把局面畫出來，規則判定一律交給 Core。
    //
    // 牌物件採用共用池：每次重畫只是搬位置與換材質，不重新生成，
    // 否則光牌山就有 144 張，每摸一次牌都全部重建會很浪費。
    // ============================================================

    public class TableBoard : MonoBehaviour
    {
        // 桌布壓得比牌背深，牌擺上去才不會糊在一起
        static readonly Color TableTopColor = new Color(0.05f, 0.24f, 0.17f);
        static readonly Color TableRimColor = new Color(0.16f, 0.10f, 0.06f);
        static readonly Color AmbientColor = new Color(0.42f, 0.45f, 0.43f);

        static readonly List<int> EmptyHand = new List<int>();

        int humanSeat;
        Camera boardCamera;
        Transform tileRoot;

        readonly List<TileObject> pool = new List<TileObject>();
        int usedTiles;

        /// <summary>牌局結束後把三家的手牌翻開</summary>
        public bool RevealHands { get; set; }

        /// <summary>結算時要標出來的贏家座位，沒有則為 -1</summary>
        public int WinnerSeat { get; set; } = -1;

        // ------------------------------------------------------------

        public static TableBoard Create(int humanSeat)
        {
            var root = new GameObject("MahjongTable");
            var board = root.AddComponent<TableBoard>();
            board.humanSeat = humanSeat;
            board.Build();
            return board;
        }

        void Build()
        {
            SetUpCamera();
            SetUpLighting();
            BuildTableSurface();

            tileRoot = new GameObject("Tiles").transform;
            tileRoot.SetParent(transform, worldPositionStays: false);
        }

        void SetUpCamera()
        {
            boardCamera = Camera.main;
            if (boardCamera == null)
            {
                var cameraObject = new GameObject("Board Camera", typeof(Camera));
                cameraObject.tag = "MainCamera";
                boardCamera = cameraObject.GetComponent<Camera>();
            }

            boardCamera.transform.position = BoardLayout.CameraPosition;
            boardCamera.transform.LookAt(BoardLayout.CameraTarget);
            boardCamera.fieldOfView = BoardLayout.CameraFieldOfView;
            boardCamera.orthographic = false;
            boardCamera.clearFlags = CameraClearFlags.SolidColor;
            boardCamera.backgroundColor = new Color(0.05f, 0.09f, 0.07f);
            boardCamera.nearClipPlane = 0.1f;
            boardCamera.farClipPlane = 60f;
        }

        void SetUpLighting()
        {
            // 環境光調亮一點，牌的背光面才不會黑成一片
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = AmbientColor;

            var light = FindObjectOfType<Light>();
            if (light == null || light.type != LightType.Directional)
            {
                var lightObject = new GameObject("Board Light");
                light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
            }

            // 從左上前方打下來，牌的右下會落影，立體感才出得來
            light.transform.rotation = Quaternion.Euler(52f, -28f, 0f);
            light.intensity = 0.95f;
            light.color = new Color(1f, 0.98f, 0.94f);
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.45f;
        }

        void BuildTableSurface()
        {
            CreateSlab("TableTop", BoardLayout.TableSize,
                       -BoardLayout.TableThickness * 0.5f, TableTopColor);
            CreateSlab("TableRim", BoardLayout.TableSize + 0.7f,
                       -BoardLayout.TableThickness * 1.4f, TableRimColor);
        }

        void CreateSlab(string name, float size, float y, Color color)
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = name;
            slab.transform.SetParent(transform, worldPositionStays: false);
            slab.transform.localPosition = new Vector3(0f, y, 0f);
            slab.transform.localScale = new Vector3(size, BoardLayout.TableThickness, size);
            Destroy(slab.GetComponent<Collider>());   // 桌面不參與點擊

            var shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            var material = new Material(shader) { color = color };
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.1f);
            slab.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        // ------------------------------------------------------------
        // 重畫
        // ------------------------------------------------------------

        public void Refresh(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            usedTiles = 0;
            DrawWall(state);

            for (int offset = 0; offset < BoardLayout.SeatCount; offset++)
            {
                int seat = (humanSeat + offset) % BoardLayout.SeatCount;
                DrawSeat(state, seat, offset);
            }

            HideUnusedTiles();
        }

        void DrawWall(GameState state)
        {
            if (state.Wall == null) return;

            int total = BoardLayout.TotalWallStacks;
            int headStack = state.Wall.DrawnFromHead / BoardLayout.WallTilesPerStack;
            int tailStack = total - 1 - state.Wall.DrawnFromTail / BoardLayout.WallTilesPerStack;
            bool headHalfTaken = state.Wall.DrawnFromHead % BoardLayout.WallTilesPerStack != 0;
            bool tailHalfTaken = state.Wall.DrawnFromTail % BoardLayout.WallTilesPerStack != 0;

            for (int stack = Mathf.Max(0, headStack); stack <= tailStack && stack < total; stack++)
            {
                int layers = BoardLayout.WallTilesPerStack;
                if (stack == headStack && headHalfTaken) layers--;
                if (stack == tailStack && tailHalfTaken) layers--;

                // 上層先被摸走，所以只剩一張時畫的是下層那張
                for (int layer = 0; layer < layers; layer++)
                {
                    Vector3 position;
                    Quaternion rotation;
                    BoardLayout.WallStack(stack, layer, out position, out rotation);

                    var tile = TakeTile();
                    tile.SetTile(TileObject.NoTile);
                    tile.Place(position, rotation);
                }
            }
        }

        void DrawSeat(GameState state, int seat, int displayIndex)
        {
            var player = state.Players[seat];
            if (player == null) return;

            float meldWidth = MeasureMelds(player.Melds);

            // 自己的手牌由 2D 的 HandStrip 畫在畫面下緣，桌上不重複再畫一次
            var handList = seat == humanSeat ? EmptyHand : CollectHand(player);

            float cursor = BoardLayout.HandRowStartX(handList.Count, meldWidth);
            DrawHand(handList, displayIndex, RevealHands, ref cursor);

            if (player.Melds.Count > 0) cursor += BoardLayout.HandToMeldGap;
            DrawMelds(player.Melds, displayIndex, cursor);

            DrawDiscards(player.Discards, displayIndex);
        }

        static List<int> CollectHand(PlayerState player)
        {
            var tiles = new List<int>();
            for (int tile = 0; tile < TileDef.KINDS; tile++)
                for (int count = 0; count < player.ConcealedCounts[tile]; count++)
                    tiles.Add(tile);
            return tiles;
        }

        void DrawHand(List<int> tiles, int displayIndex, bool faceUp, ref float cursor)
        {
            foreach (int tileId in tiles)
            {
                var local = BoardLayout.HandSlot(cursor + BoardLayout.HandStep * 0.5f);
                PlaceTile(faceUp ? tileId : TileObject.NoTile, displayIndex, local,
                          BoardLayout.StandingFacingOwner);
                cursor += BoardLayout.HandStep;
            }
        }

        static float MeasureMelds(List<Meld> melds)
        {
            if (melds.Count == 0) return 0f;

            float width = 0f;
            foreach (var meld in melds)
                width += meld.Tiles().Length * BoardLayout.MeldStep + BoardLayout.MeldGroupGap;
            return width - BoardLayout.MeldGroupGap;
        }

        /// <summary>副露平躺攤在自己面前，被吃碰走的那張排在整組中間。</summary>
        void DrawMelds(List<Meld> melds, int displayIndex, float cursor)
        {
            foreach (var meld in melds)
            {
                var layout = MeldDisplay.Arrange(meld);
                foreach (int tileId in layout.Tiles)
                {
                    var local = BoardLayout.MeldSlot(cursor + BoardLayout.MeldStep * 0.5f);
                    PlaceTile(tileId, displayIndex, local, BoardLayout.LyingFaceUp);
                    cursor += BoardLayout.MeldStep;
                }
                cursor += BoardLayout.MeldGroupGap;
            }
        }

        void DrawDiscards(List<int> discards, int displayIndex)
        {
            int columns = BoardLayout.DiscardColumns(discards.Count);
            for (int i = 0; i < discards.Count; i++)
                PlaceTile(discards[i], displayIndex, BoardLayout.DiscardSlot(i, columns),
                          BoardLayout.LyingFaceUp);
        }

        void PlaceTile(int tileId, int displayIndex, Vector3 localPosition, Quaternion localRotation)
        {
            Vector3 position;
            Quaternion rotation;
            BoardLayout.ToWorld(displayIndex, localPosition, localRotation, out position, out rotation);

            var tile = TakeTile();
            tile.SetTile(tileId);
            tile.Place(position, rotation);
        }

        // ------------------------------------------------------------
        // 牌物件池
        // ------------------------------------------------------------

        TileObject TakeTile()
        {
            if (usedTiles == pool.Count) pool.Add(TileObject.Create(tileRoot));

            var tile = pool[usedTiles++];
            tile.SetVisible(true);
            tile.SetNormal();
            return tile;
        }

        void HideUnusedTiles()
        {
            for (int i = usedTiles; i < pool.Count; i++) pool[i].SetVisible(false);
        }
    }
}
