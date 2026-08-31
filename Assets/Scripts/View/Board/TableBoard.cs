using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mahjong.View.Board
{
    // ============================================================
    // 3D 牌桌
    //
    // 取代原本用 uGUI 圖層疊出來的 2D 牌桌：桌面、牌山、四家的手牌、
    // 副露與牌河全部是真的 3D 物件，由一台俯視攝影機拍下來。
    //
    // 這一層只負責「把局面畫出來」與「把點擊丟出去」，
    // 規則判定一律交給 Core，跟原本的分層一樣。
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

        int humanSeat;
        Camera boardCamera;
        Transform tileRoot;

        readonly List<TileObject> pool = new List<TileObject>();
        int usedTiles;

        readonly List<TileObject> handTiles = new List<TileObject>();
        readonly HashSet<int> claimHighlightSlots = new HashSet<int>();

        TileObject selectedTile;
        bool handInteractable;

        /// <summary>玩家決定打出這張牌</summary>
        public event Action<int> TileChosen;

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
            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "TableTop";
            table.transform.SetParent(transform, worldPositionStays: false);
            table.transform.localPosition = new Vector3(0f, -BoardLayout.TableThickness * 0.5f, 0f);
            table.transform.localScale = new Vector3(BoardLayout.TableSize,
                                                     BoardLayout.TableThickness,
                                                     BoardLayout.TableSize);
            Destroy(table.GetComponent<Collider>());   // 桌面不參與點擊
            table.GetComponent<MeshRenderer>().sharedMaterial = CreateSurfaceMaterial(TableTopColor);

            var rim = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rim.name = "TableRim";
            rim.transform.SetParent(transform, worldPositionStays: false);
            rim.transform.localPosition = new Vector3(0f, -BoardLayout.TableThickness * 1.4f, 0f);
            rim.transform.localScale = new Vector3(BoardLayout.TableSize + 0.7f,
                                                   BoardLayout.TableThickness,
                                                   BoardLayout.TableSize + 0.7f);
            Destroy(rim.GetComponent<Collider>());
            rim.GetComponent<MeshRenderer>().sharedMaterial = CreateSurfaceMaterial(TableRimColor);
        }

        static Material CreateSurfaceMaterial(Color color)
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            var material = new Material(shader) { color = color };
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.1f);
            return material;
        }

        // ------------------------------------------------------------
        // 重畫
        // ------------------------------------------------------------

        public void Refresh(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            usedTiles = 0;
            handTiles.Clear();
            selectedTile = null;

            DrawWall(state);
            for (int offset = 0; offset < BoardLayout.SeatCount; offset++)
            {
                int seat = (humanSeat + offset) % BoardLayout.SeatCount;
                DrawSeat(state, seat, offset);
            }

            DrawWinningShowcase();
            HideUnusedTiles();
            ApplyHandVisuals();
        }

        // ------------------------------------------------------------
        // 結算時攤在桌心的贏家牌型
        // ------------------------------------------------------------

        const float ShowcaseZ = -0.10f;
        const float ShowcaseSectionGap = 0.14f;
        const float ShowcaseMeldGap = 0.08f;

        PlayerState showcaseWinner;
        int showcaseWinningTile = TileObject.NoTile;

        /// <summary>攤出贏家的牌：手牌 → 副露 → 胡的那張，段與段之間留空隙。</summary>
        public void ShowWinningHand(PlayerState winner, int winningTile)
        {
            showcaseWinner = winner;
            showcaseWinningTile = winningTile;
        }

        public void HideWinningHand()
        {
            showcaseWinner = null;
            showcaseWinningTile = TileObject.NoTile;
        }

        void DrawWinningShowcase()
        {
            if (showcaseWinner == null) return;

            var concealed = CollectHand(showcaseWinner);
            if (showcaseWinningTile != TileObject.NoTile) concealed.Remove(showcaseWinningTile);

            float width = MeasureShowcase(concealed.Count, showcaseWinner.Melds);
            float cursor = -width * 0.5f;

            cursor = PlaceShowcaseTiles(concealed, cursor);

            foreach (var meld in showcaseWinner.Melds)
            {
                cursor += ShowcaseSectionGap;
                var layout = MeldDisplay.Arrange(meld);
                cursor = PlaceShowcaseTiles(new List<int>(layout.Tiles), cursor);
            }

            if (showcaseWinningTile != TileObject.NoTile)
            {
                cursor += ShowcaseSectionGap;
                PlaceShowcaseTiles(new List<int> { showcaseWinningTile }, cursor);
            }
        }

        static float MeasureShowcase(int concealedCount, List<Meld> melds)
        {
            float width = concealedCount * BoardLayout.HandStep;
            foreach (var meld in melds)
                width += ShowcaseSectionGap + meld.Tiles().Length * BoardLayout.HandStep;
            return width;
        }

        float PlaceShowcaseTiles(List<int> tileIds, float cursor)
        {
            foreach (int tileId in tileIds)
            {
                var tile = TakeTile();
                tile.SetTile(tileId);
                tile.Clickable = false;
                var slot = BoardLayout.HandSlot(cursor + BoardLayout.HandStep * 0.5f);
                tile.Place(new Vector3(slot.x, slot.y, ShowcaseZ), BoardLayout.HandRotation);
                tile.SetNormal();
                cursor += BoardLayout.HandStep;
            }
            return cursor;
        }

        void DrawWall(GameState state)
        {
            if (state.Wall == null) return;

            int drawnFromHead = state.Wall.DrawnFromHead;
            int drawnFromTail = state.Wall.DrawnFromTail;
            int total = BoardLayout.TotalWallStacks;

            int headStack = drawnFromHead / BoardLayout.WallTilesPerStack;
            int tailStack = total - 1 - drawnFromTail / BoardLayout.WallTilesPerStack;
            bool headHalfTaken = drawnFromHead % BoardLayout.WallTilesPerStack != 0;
            bool tailHalfTaken = drawnFromTail % BoardLayout.WallTilesPerStack != 0;

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
                    tile.Clickable = false;
                    tile.Place(position, rotation);
                    tile.SetNormal();
                }
            }
        }

        void DrawSeat(GameState state, int seat, int displayIndex)
        {
            var player = state.Players[seat];
            if (player == null) return;

            bool isHuman = seat == humanSeat;
            bool faceUp = isHuman || RevealHands;

            var meldWidths = MeasureMelds(player.Melds);
            var handList = CollectHand(player);

            float cursor = BoardLayout.HandRowStartX(handList.Count, meldWidths);
            DrawHand(handList, displayIndex, isHuman, faceUp, ref cursor);

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

        void DrawHand(List<int> tiles, int displayIndex, bool isHuman, bool faceUp, ref float cursor)
        {
            foreach (int tileId in tiles)
            {
                var local = BoardLayout.HandSlot(cursor + BoardLayout.HandStep * 0.5f);
                Vector3 position;
                Quaternion rotation;
                BoardLayout.ToWorld(displayIndex, local, BoardLayout.HandRotation,
                                    out position, out rotation);

                var tile = TakeTile();
                tile.SetTile(faceUp ? tileId : TileObject.NoTile);
                tile.Clickable = isHuman;
                tile.Place(position, rotation);
                tile.SetNormal();

                if (isHuman) handTiles.Add(tile);
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
                    Vector3 position;
                    Quaternion rotation;
                    BoardLayout.ToWorld(displayIndex, local, BoardLayout.LyingFaceUp,
                                        out position, out rotation);

                    var tile = TakeTile();
                    tile.SetTile(tileId);
                    tile.Clickable = false;
                    tile.Place(position, rotation);
                    tile.SetNormal();

                    cursor += BoardLayout.MeldStep;
                }
                cursor += BoardLayout.MeldGroupGap;
            }
        }

        void DrawDiscards(List<int> discards, int displayIndex)
        {
            for (int i = 0; i < discards.Count; i++)
            {
                Vector3 position;
                Quaternion rotation;
                BoardLayout.ToWorld(displayIndex, BoardLayout.DiscardSlot(i), BoardLayout.LyingFaceUp,
                                    out position, out rotation);

                var tile = TakeTile();
                tile.SetTile(discards[i]);
                tile.Clickable = false;
                tile.Place(position, rotation);
                tile.SetNormal();
            }
        }

        // ------------------------------------------------------------
        // 牌物件池
        // ------------------------------------------------------------

        TileObject TakeTile()
        {
            if (usedTiles == pool.Count) pool.Add(TileObject.Create(tileRoot));

            var tile = pool[usedTiles++];
            tile.SetVisible(true);
            return tile;
        }

        void HideUnusedTiles()
        {
            for (int i = usedTiles; i < pool.Count; i++) pool[i].SetVisible(false);
        }

        // ------------------------------------------------------------
        // 玩家操作
        // ------------------------------------------------------------

        public void SetHandInteractable(bool value)
        {
            handInteractable = value;
            if (!value) selectedTile = null;
            ApplyHandVisuals();
        }

        /// <summary>標出手上會被拿去湊成那一組的牌。傳 null 表示清掉提示。</summary>
        public void SetClaimHighlight(int[] tileCounts)
        {
            claimHighlightSlots.Clear();

            if (tileCounts != null)
            {
                var remaining = (int[])tileCounts.Clone();
                for (int i = 0; i < handTiles.Count; i++)
                {
                    int tileId = handTiles[i].Tile;
                    if (tileId < 0 || tileId >= TileDef.KINDS || remaining[tileId] <= 0) continue;
                    remaining[tileId]--;
                    claimHighlightSlots.Add(i);
                }
            }
            ApplyHandVisuals();
        }

        public void ClearClaimHighlight() => SetClaimHighlight(null);

        /// <summary>
        /// 三種狀態會互相覆蓋，套用順序固定為：可否點擊 → 叫牌提示 → 已選取。
        /// 叫牌提示要蓋過變灰，因為考慮要不要碰的時候手牌本來就不能點。
        /// </summary>
        void ApplyHandVisuals()
        {
            for (int i = 0; i < handTiles.Count; i++)
            {
                var tile = handTiles[i];
                bool isSelected = tile == selectedTile;
                bool isClaimTile = claimHighlightSlots.Contains(i);

                if (handInteractable) tile.SetNormal();
                else tile.SetDimmed();
                if (isClaimTile) tile.SetClaimHighlight();
                if (isSelected) tile.SetSelected();

                // 選到的牌整張抬起來，跟真的把牌拿出來一樣
                tile.SetLift(isSelected ? BoardLayout.SelectedLift
                           : (isClaimTile ? BoardLayout.ClaimLift : Vector3.zero));
            }
        }

        void Update()
        {
            if (!handInteractable || !Input.GetMouseButtonDown(0)) return;
            if (boardCamera == null) return;

            RaycastHit hit;
            if (!Physics.Raycast(boardCamera.ScreenPointToRay(Input.mousePosition), out hit, 100f)) return;

            var tile = hit.collider.GetComponentInParent<TileObject>();
            if (tile == null || !tile.Clickable) return;

            OnTileClicked(tile);
        }

        /// <summary>點一下選取，再點同一張才真的打出，避免手滑打錯牌。</summary>
        void OnTileClicked(TileObject tile)
        {
            if (selectedTile == tile)
            {
                int chosen = tile.Tile;
                selectedTile = null;
                if (TileChosen != null) TileChosen(chosen);
                return;
            }

            selectedTile = tile;
            ApplyHandVisuals();
        }
    }
}
