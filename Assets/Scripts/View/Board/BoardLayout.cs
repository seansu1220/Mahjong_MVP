using UnityEngine;

namespace Mahjong.View.Board
{
    // ============================================================
    // 牌桌的世界座標配置
    //
    // 先以「自己這一家」為基準把所有位置算在座位本地座標，
    // 再依方位整個繞 Y 軸轉 -90 度的倍數，四家就都排好了。
    // 方位順序：0 自己（近端）、1 右手邊的下家、2 對家、3 左家，即逆時針。
    //
    // 各區塊離桌心多遠**不寫死**，一律從牌的尺寸與墩數推算：
    //
    //   牌山   由「一邊排幾墩」決定，四角剛好接起來又不互相穿插
    //   副露   排在牌山外側，平躺攤開
    //   手牌   排在副露外側，立著（只畫三家對手，自己的手牌由 2D 負責）
    //   牌河   在牌山圍出來的內側，往桌心排
    //
    // 寫死距離的話，只要改了牌的大小或墩數，四個角就會重疊或裂開，
    // 而且很難看出是哪個數字沒跟著改。
    //
    // 攝影機從自己身後上方俯視，近端的自己最大、對家因透視自然變小。
    // ============================================================

    public static class BoardLayout
    {
        public const int SeatCount = GameState.PlayerCount;

        // ---- 排列間距 ----
        public const float HandStep = TileAssets.Width + 0.006f;
        public const float MeldStep = TileAssets.Width + 0.004f;
        public const float MeldGroupGap = 0.075f;

        /// <summary>
        /// 左右兩家的副露是往畫面深處排的，理由同 DiscardStepAway。
        /// </summary>
        public static float SideMeldStep => StepShowingSeam(MeldSeamHeight);

        // 牌河的間距要明顯拉開，太貼會整片黏成一塊看不出是一張一張的牌
        public const float DiscardStepX = TileAssets.Width + 0.024f;

        /// <summary>牌與牌之間在畫面上要露出來的綠邊有多高</summary>
        const float DiscardSeamHeight = 0.030f;

        /// <summary>副露是一組一組緊靠著的，綠邊細一點就夠</summary>
        const float MeldSeamHeight = 0.010f;

        /// <summary>
        /// 往畫面深處排的那個方向要留多少間距。
        ///
        /// 這個方向**不能照牌的實際尺寸算**。攝影機是斜著往下看的，
        /// 後面那張牌的前緣會頂上來蓋住前面那張牌的遠端，
        /// 牌與牌的界線就落在「後面那張的前緣」上。
        ///
        /// 而前緣由上而下是「白色牌身 58% + 綠色牌背 42%」。
        /// 只露出白的那段，它會跟前面那張的白色牌面連成一片，看不出是兩張牌；
        /// 露到綠的那段，界線就一清二楚。真實牌桌上的牌本來就是互相挨著的，
        /// 硬把牌拉開到能看見桌面反而顯得散。
        ///
        /// 所以間距 = 牌高 + 白色那段的投影 + 想露出的綠邊，
        /// 後兩項再除以 sin(俯角) 換回世界座標。視角改了它會自己跟著變。
        /// </summary>
        public static float DiscardStepAway => StepShowingSeam(DiscardSeamHeight);

        static float StepShowingSeam(float seamHeight)
            => TileAssets.Height
               + (FrontEdgeScreenHeight * TileAssets.FrontDepthRatio + seamHeight) / ViewSin;

        /// <summary>
        /// 牌山的牌畫得比場上的牌小一號。
        /// 真實牌山一邊 18 墩，照原尺寸排會把桌子撐得很大，
        /// 中間的牌河與副露就相對變小、看不清楚。
        /// 牌山只是背景，縮小它可以把整張桌子縮小、鏡頭拉近，
        /// 真正要看的牌就變大了。
        /// </summary>
        public const float WallTileScale = 0.62f;

        public static float WallTileWidth => TileAssets.Width * WallTileScale;
        public static float WallTileHeight => TileAssets.Height * WallTileScale;
        public static float WallTileDepth => TileAssets.Depth * WallTileScale;

        // 牌山每墩之間也留一點縫，才看得出是一墩一墩疊起來的
        public static float WallStep => WallTileWidth + 0.009f;

        public const int DiscardsPerRow = 8;

        /// <summary>牌河最多排幾列。超過就把每列加寬，不讓牌河往桌心蔓延過去。</summary>
        public const int MaxDiscardRows = 3;

        public const int WallStacksPerSide = 18;
        public const int WallTilesPerStack = 2;

        /// <summary>手牌與副露之間留的空隙</summary>
        public const float HandToMeldGap = 0.09f;

        // ---- 各區塊之間的留白 ----
        const float WallCornerGap = 0.03f;
        const float WallToMeldGap = 0.11f;
        const float MeldToHandGap = 0.13f;
        const float FirstDiscardInset = 0.16f;

        // ------------------------------------------------------------
        // 由牌的尺寸推算出來的距離
        // ------------------------------------------------------------

        /// <summary>牌山一邊從中點算起有多長（含最外那張牌的一半寬度）</summary>
        static float WallHalfLength =>
            (WallStacksPerSide - 1) * WallStep * 0.5f + WallTileWidth * 0.5f;

        /// <summary>
        /// 牌山離桌心多遠。相鄰兩邊在角落不能互相穿插，
        /// 所以這一邊的長度必須落在另一邊的內緣之外。
        /// </summary>
        public static float WallDistance =>
            WallHalfLength + WallTileHeight * 0.5f + WallCornerGap;

        /// <summary>副露平躺，排在牌山外側</summary>
        public static float MeldDistance =>
            WallDistance + WallTileHeight * 0.5f + TileAssets.Height * 0.5f + WallToMeldGap;

        /// <summary>對手的手牌立著，排在副露外側</summary>
        public static float HandDistance =>
            MeldDistance + TileAssets.Height * 0.5f + TileAssets.Depth * 0.5f + MeldToHandGap;

        /// <summary>牌河第一列離桌心多遠，從牌山內緣再往內縮一點</summary>
        static float FirstDiscardRow =>
            WallDistance - WallTileHeight * 0.5f - FirstDiscardInset;

        /// <summary>桌面要蓋得住最外圈的手牌</summary>
        public static float TableSize =>
            (HandDistance + TileAssets.Height * 0.5f + 0.35f) * 2f;

        public const float TableThickness = 0.16f;

        // ---- 攝影機（想調整視角就改這三個比例）----
        // 自己的手牌是 2D 畫在畫面下緣的，桌面近端不必留太多空間，
        // 所以視線只稍微往前壓，鏡頭盡量拉近讓桌上的牌看得清楚。
        //
        // 俯角不能太平：越平，平躺的牌在畫面上被壓得越扁，
        // 往畫面深處排的牌就越容易被後面那張蓋住（見 DiscardStepAway）。
        public static Vector3 CameraPosition =>
            new Vector3(0f, TableSize * 0.68f, -TableSize * 0.55f);

        public static Vector3 CameraTarget =>
            new Vector3(0f, 0f, -TableSize * 0.04f);

        public const float CameraFieldOfView = 46f;

        /// <summary>攝影機的視線方向</summary>
        static Vector3 ViewDirection => (CameraTarget - CameraPosition).normalized;

        /// <summary>俯角的 sin。直接從視線方向取，不必真的去算角度。</summary>
        static float ViewSin => Mathf.Max(-ViewDirection.y, 0.01f);

        /// <summary>俯角的 cos</summary>
        static float ViewCos => new Vector2(ViewDirection.x, ViewDirection.z).magnitude;

        /// <summary>平躺的牌，前緣在畫面上佔多高（白色那段與綠色那段合起來）</summary>
        static float FrontEdgeScreenHeight => TileAssets.Depth * ViewCos;

        // ------------------------------------------------------------
        // 基本朝向
        // ------------------------------------------------------------

        /// <summary>立著、牌面朝向自己（本地 -Z）。三家對手的手牌都是這樣正常立著。</summary>
        public static readonly Quaternion StandingFacingOwner =
            Quaternion.LookRotation(Vector3.back, Vector3.up);

        /// <summary>平躺、牌面朝上，字的上方朝向桌心（本地 +Z）</summary>
        public static readonly Quaternion LyingFaceUp =
            Quaternion.LookRotation(Vector3.up, Vector3.forward);

        /// <summary>平躺、牌面朝下（牌山用）</summary>
        public static readonly Quaternion LyingFaceDown =
            Quaternion.LookRotation(Vector3.down, Vector3.forward);

        /// <summary>
        /// 某個方位的整體旋轉。0 是自己（近端），1 是右手邊的下家，接著上家、左家。
        /// 繞 Y 軸要往負的方向轉：正 90 度會把下家轉到左邊去，順序就反了。
        /// </summary>
        public static Quaternion SeatRotation(int displayIndex)
            => Quaternion.AngleAxis(-90f * displayIndex, Vector3.up);

        /// <summary>把座位本地的擺放換算成世界座標</summary>
        public static void ToWorld(int displayIndex, Vector3 localPosition, Quaternion localRotation,
                                   out Vector3 position, out Quaternion rotation)
        {
            var seat = SeatRotation(displayIndex);
            position = seat * localPosition;
            rotation = seat * localRotation;
        }

        // ------------------------------------------------------------
        // 各區塊的座位本地座標
        // ------------------------------------------------------------

        /// <summary>
        /// 手牌與副露排在同一列：手牌在左、副露接在右邊，整列置中。
        /// 每吃碰一組手牌就少三張，所以整列寬度大致不變，跟真的打牌一樣。
        /// </summary>
        public static float HandRowStartX(int handCount, float meldsWidth)
        {
            float handWidth = handCount * HandStep;
            float total = handWidth + (meldsWidth > 0f ? HandToMeldGap + meldsWidth : 0f);
            return -total * 0.5f;
        }

        public static Vector3 HandSlot(float x)
            => new Vector3(x, TileAssets.Height * 0.5f, -HandDistance);

        public static Vector3 MeldSlot(float x)
            => new Vector3(x, TileAssets.Depth * 0.5f, -MeldDistance);

        /// <summary>左右兩家（畫面上的 1 與 3）</summary>
        public static bool IsSideSeat(int displayIndex)
            => displayIndex == 1 || displayIndex == 3;

        /// <summary>
        /// 排列間距要看牌在那個方向上實際佔多寬。
        ///
        /// 桌上的牌一律轉成正面朝向玩家，不再跟著座位旋轉，
        /// 所以左右兩家沿著自己本地 X 排的時候，那個方向對應到的是世界 Z，
        /// 也就是往畫面深處排——要用 DiscardStepAway 那套算法，
        /// 沿用牌寬的間距牌會互相遮住，看起來連成一整條。
        /// </summary>
        public static float MeldStepFor(int displayIndex)
            => IsSideSeat(displayIndex) ? SideMeldStep : MeldStep;

        /// <summary>
        /// 牌河要排幾欄。
        ///
        /// 上下兩家沿著畫面橫向排，間距小，牌打多了就把每一列加寬，
        /// 而不是往桌心再多排一列，否則牌河會蔓延到桌中間撞在一起。
        ///
        /// 左右兩家是沿著畫面深處排的，間距大得多（DiscardStepAway），
        /// 欄數固定，再多就排到牌山上去了；牌多了往桌心多排一列，
        /// 那個方向對他們來說反而是便宜的。
        /// </summary>
        public static int DiscardColumns(int discardCount, int displayIndex)
        {
            if (IsSideSeat(displayIndex)) return SideDiscardColumns;
            return Mathf.Max(DiscardsPerRow, Mathf.CeilToInt(discardCount / (float)MaxDiscardRows));
        }

        /// <summary>左右兩家的牌河固定幾欄：再多就會排到牌山上</summary>
        public const int SideDiscardColumns = 6;

        /// <summary>
        /// 牌河的格位。左右兩家的兩個方向要對調——理由同 MeldStepFor：
        /// 牌不再跟著座位轉，本地 X 對應到世界 Z，佔的是牌高。
        /// </summary>
        public static Vector3 DiscardSlot(int index, int columns, int displayIndex)
        {
            bool side = IsSideSeat(displayIndex);
            float alongStep = side ? DiscardStepAway : DiscardStepX;
            float awayStep = side ? DiscardStepX : DiscardStepAway;

            int column = index % columns;
            int row = index / columns;
            float x = (column - (columns - 1) * 0.5f) * alongStep;
            float z = -FirstDiscardRow + row * awayStep;
            return new Vector3(x, TileAssets.Depth * 0.5f, z);
        }

        // ------------------------------------------------------------
        // 牌山：四邊圍成方框，順序與出牌方向一致（逆時針）
        // ------------------------------------------------------------

        public static int TotalWallStacks => WallStacksPerSide * SeatCount;

        /// <summary>第 stackIndex 墩、第 layer 層的位置與朝向</summary>
        public static void WallStack(int stackIndex, int layer,
                                     out Vector3 position, out Quaternion rotation)
        {
            int side = Mathf.Clamp(stackIndex / WallStacksPerSide, 0, SeatCount - 1);
            int withinSide = stackIndex - side * WallStacksPerSide;

            float extent = (WallStacksPerSide - 1) * WallStep * 0.5f;
            float along = -extent + withinSide * WallStep;

            // 每一邊都沿著自己的本地 X 排，再整段轉到該邊，接起來就是連續的方框
            var localPosition = new Vector3(along,
                                            WallTileDepth * (0.5f + layer),
                                            -WallDistance);
            ToWorld(side, localPosition, LyingFaceDown, out position, out rotation);
        }
    }
}
