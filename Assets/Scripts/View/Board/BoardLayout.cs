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
    //   手牌   排在牌山外側，立著；副露平躺接在同一列的右邊
    //          （只畫三家對手，自己的手牌與副露由 2D 負責）
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

        /// <summary>左右兩家的副露也是往畫面深處排的，理由同 DiscardStepAway</summary>
        public static float SideMeldStep => TileAssets.Height + TouchingGap;

        // 牌河的間距要明顯拉開，太貼會整片黏成一塊看不出是一張一張的牌
        public const float DiscardStepX = TileAssets.Width + 0.024f;

        /// <summary>讓兩張牌剛好不共面，避免邊界閃爍；小到看不出來</summary>
        const float TouchingGap = 0.002f;

        /// <summary>
        /// 往畫面深處排的那個方向：讓兩張牌**剛好挨著**。
        ///
        /// 試過拉開到看得見桌面，反而顯得散。而且攝影機是斜著往下看的，
        /// 只要間距超過牌高一點點，後面那張牌的前緣就會從前面那張的頂面上方
        /// 冒出一截白色——那截跟牌面同色，非但沒分開反而更糊。
        ///
        /// 貼著排的話兩張牌的頂面直接相鄰，界線交給牌面四周烘焙好的深色邊框
        /// （見 TileAssets.FaceEdgeColor），兩張牌的邊框並在一起就是一條清楚的溝。
        /// </summary>
        public static float DiscardStepAway => TileAssets.Height + TouchingGap;

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
        const float WallToHandGap = 0.20f;

        /// <summary>相鄰兩家的手牌列在轉角要留的空隙</summary>
        const float RowCornerGap = 0.04f;
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

        /// <summary>
        /// 手牌與副露同在一列，排在牌山外側。
        /// 用牌高（而不是手牌立著時的牌厚）算間隙，因為同一列上平躺的副露佔得比較多。
        /// </summary>
        public static float HandDistance =>
            WallDistance + WallTileHeight * 0.5f + TileAssets.Height * 0.5f + WallToHandGap;

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
        /// 一列（手牌 + 副露）最多能多寬。
        ///
        /// 四家的手牌列圍成一個方框，超過這個寬度相鄰兩家就會在轉角撞在一起。
        /// 手牌是立著的、副露是平躺的，平躺佔得比較多，所以用牌高抓保守值。
        /// </summary>
        public static float MaxRowWidth =>
            2f * (HandDistance - TileAssets.Height * 0.5f - RowCornerGap);

        /// <summary>
        /// 一列排不下就整列縮小，而不是讓它撞到隔壁那家。
        /// 副露一組要三張，吃碰多了那一列會比原本的手牌長得多，
        /// 固定尺寸一定會爆版；縮小是唯一不會出事的做法。
        /// </summary>
        public static float RowScale(float rowWidth)
            => rowWidth <= MaxRowWidth ? 1f : MaxRowWidth / rowWidth;

        public static Vector3 HandSlot(float x, float scale)
            => new Vector3(x, TileAssets.Height * scale * 0.5f, -HandDistance);

        /// <summary>副露跟手牌同一列，只是平躺著</summary>
        public static Vector3 MeldSlot(float x, float scale)
            => new Vector3(x, TileAssets.Depth * scale * 0.5f, -HandDistance);

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
