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

        /// <summary>拍牌桌的攝影機，2D 提示要用它把世界座標投影到螢幕上</summary>
        public Camera BoardCamera { get { return boardCamera; } }

        int DisplayIndexOf(int seat)
            => (seat - humanSeat + GameState.PlayerCount) % GameState.PlayerCount;

        /// <summary>最後打出那張牌在桌上的位置，找不到就回傳 false</summary>
        public bool TryGetLastDiscardPosition(GameState state, out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;

            int from = state.LastDiscardFrom;
            if (state.LastDiscardTile == TileObject.NoTile) return false;
            if (from < 0 || from >= GameState.PlayerCount) return false;

            var player = state.Players[from];
            if (player == null || player.Discards.Count == 0) return false;

            int displayIndex = DisplayIndexOf(from);
            int columns = BoardLayout.DiscardColumns(player.Discards.Count, displayIndex);
            var local = BoardLayout.DiscardSlot(player.Discards.Count - 1, columns, displayIndex);
            worldPosition = BoardLayout.SeatRotation(displayIndex) * local;
            return true;
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
            int breakStack = BreakStack(state, total);
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
                // 缺口從開門的位置算起，繞一圈回來
                int physicalStack = (stack + breakStack) % total;

                for (int layer = 0; layer < layers; layer++)
                {
                    Vector3 position;
                    Quaternion rotation;
                    BoardLayout.WallStack(physicalStack, layer, out position, out rotation);

                    var tile = TakeTile();
                    tile.SetTile(TileObject.NoTile);
                    tile.SetScale(BoardLayout.WallTileScale);
                    tile.Place(position, rotation);
                }
            }
        }

        /// <summary>
        /// 開門的位置：牌山從哪一墩開始被摸走。
        ///
        /// 真牌桌是擲骰決定的，這裡用洗牌的亂數種子推——每一局的種子不同，
        /// 缺口就不會每一局都長在同一個角落。純粹是視覺效果，
        /// 牌本身早就在 Wall 裡洗好了，摸的順序不受影響。
        /// </summary>
        static int BreakStack(GameState state, int totalStacks)
        {
            int seed = state.Wall.Seed % totalStacks;
            return seed < 0 ? seed + totalStacks : seed;
        }

        void DrawSeat(GameState state, int seat, int displayIndex)
        {
            var player = state.Players[seat];
            if (player == null) return;

            // 自己的手牌與副露都由 2D 的 HandStrip 畫在畫面下緣，桌上不重複再畫一次。
            // 桌子近端剛好落在攝影機視野邊緣，畫在那裡也看不到。
            bool ownSeat = seat == humanSeat;
            var handList = ownSeat ? EmptyHand : CollectHand(player);
            var meldList = ownSeat ? EmptyMelds : player.Melds;

            float meldWidth = MeasureMelds(meldList, displayIndex);
            float rowWidth = handList.Count * BoardLayout.HandStep
                           + (meldList.Count > 0 ? BoardLayout.HandToMeldGap + meldWidth : 0f);

            // 吃碰多了整列會變長，排不下就整列縮小，免得撞到隔壁那家或超出桌面
            float scale = BoardLayout.RowScale(rowWidth);
            float cursor = -rowWidth * scale * 0.5f;

            DrawHand(handList, displayIndex, RevealHands, scale, ref cursor);

            if (meldList.Count > 0) cursor += BoardLayout.HandToMeldGap * scale;
            DrawMelds(meldList, displayIndex, scale, cursor);

            DrawDiscards(player.Discards, displayIndex);
        }

        static readonly List<Meld> EmptyMelds = new List<Meld>();

        static List<int> CollectHand(PlayerState player)
        {
            var tiles = new List<int>();
            for (int tile = 0; tile < TileDef.KINDS; tile++)
                for (int count = 0; count < player.ConcealedCounts[tile]; count++)
                    tiles.Add(tile);
            return tiles;
        }

        void DrawHand(List<int> tiles, int displayIndex, bool faceUp, float scale, ref float cursor)
        {
            float step = BoardLayout.HandStep * scale;

            foreach (int tileId in tiles)
            {
                var local = BoardLayout.HandSlot(cursor + step * 0.5f, scale);
                PlaceTile(faceUp ? tileId : TileObject.NoTile, displayIndex, local,
                          BoardLayout.StandingFacingOwner, scale);
                cursor += step;
            }
        }

        static float MeasureMelds(List<Meld> melds, int displayIndex)
        {
            if (melds.Count == 0) return 0f;

            float step = BoardLayout.MeldStepFor(displayIndex);
            float width = 0f;
            foreach (var meld in melds)
                width += meld.Tiles().Length * step + BoardLayout.MeldGroupGap;
            return width - BoardLayout.MeldGroupGap;
        }

        /// <summary>
        /// 副露平躺攤在自己面前，被吃碰走的那張排在整組中間。
        ///
        /// 左右兩家跟著座位轉，就跟真牌桌上擺在自己面前一樣，字是側著的但看得懂；
        /// 對家則要轉成正面朝向玩家，照真實方向擺的話字會上下顛倒。
        /// </summary>
        void DrawMelds(List<Meld> melds, int displayIndex, float scale, float cursor)
        {
            float step = BoardLayout.MeldStepFor(displayIndex) * scale;
            float tileScale = scale * BoardLayout.MeldTileScale;

            foreach (var meld in melds)
            {
                var layout = MeldDisplay.Arrange(meld);
                foreach (int tileId in layout.Tiles)
                {
                    var local = BoardLayout.MeldSlot(cursor + step * 0.5f, tileScale);

                    if (BoardLayout.IsSideSeat(displayIndex))
                        PlaceTile(tileId, displayIndex, local, BoardLayout.LyingFaceUp, tileScale);
                    else
                        PlaceTileFacingViewer(tileId, displayIndex, local, tileScale);
                    cursor += step;
                }
                cursor += BoardLayout.MeldGroupGap * scale;
            }
        }

        void DrawDiscards(List<int> discards, int displayIndex)
        {
            int columns = BoardLayout.DiscardColumns(discards.Count, displayIndex);
            for (int i = 0; i < discards.Count; i++)
                PlaceTileFacingViewer(discards[i], displayIndex,
                                      BoardLayout.DiscardSlot(i, columns, displayIndex),
                                      BoardLayout.DiscardTileScale);
        }

        void PlaceTile(int tileId, int displayIndex, Vector3 localPosition,
                       Quaternion localRotation, float scale)
        {
            Vector3 position;
            Quaternion rotation;
            BoardLayout.ToWorld(displayIndex, localPosition, localRotation, out position, out rotation);

            var tile = TakeTile();
            tile.SetTile(tileId);
            tile.SetScale(scale);
            tile.Place(position, rotation);
        }

        /// <summary>
        /// 位置排在那一家面前，但牌本身一律轉成正面朝向玩家。
        /// 照真實牌桌讓每家的牌朝向自己的話，對家打出的牌對玩家來說是上下顛倒的，
        /// 根本看不出打了什麼。牌河與副露都是要給所有人看的資訊，可讀性優先。
        /// </summary>
        void PlaceTileFacingViewer(int tileId, int displayIndex, Vector3 localPosition,
                                   float scale = 1f)
        {
            var position = BoardLayout.SeatRotation(displayIndex) * localPosition;

            var tile = TakeTile();
            tile.SetTile(tileId);
            tile.SetScale(scale);
            tile.Place(position, BoardLayout.LyingFaceUp);
        }

        // ------------------------------------------------------------
        // 牌物件池
        // ------------------------------------------------------------

        TileObject TakeTile()
        {
            if (usedTiles == pool.Count) pool.Add(TileObject.Create(tileRoot));

            var tile = pool[usedTiles++];
            tile.SetVisible(true);
            tile.SetScale(1f);     // 池子裡的牌可能上一輪是牌山的小牌
            tile.SetNormal();
            return tile;
        }

        void HideUnusedTiles()
        {
            for (int i = usedTiles; i < pool.Count; i++) pool[i].SetVisible(false);
        }
    }
}
