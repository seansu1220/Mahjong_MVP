using UnityEngine;

namespace Mahjong.View.Board
{
    // ============================================================
    // 牌桌的世界座標配置
    //
    // 先以「自己這一家」為基準把所有位置算在座位本地座標，
    // 再依座位方位整個繞 Y 軸轉 90 度的倍數，四家就都排好了。
    //
    // 座位本地座標（自己在近端，面向 -Z）：
    //   手牌   z = -2.42   立著，牌面朝 -Z（只畫三家對手，自己的手牌由 2D 負責）
    //   副露   z = -2.02   平躺攤開，牌面朝上
    //   牌河   z = -1.40 起往桌心排，平躺，牌面朝上
    //   牌山   離桌心 1.86，四邊圍成方框
    //
    // 方位順序：0 自己（近端）、1 右手邊的下家、2 對家、3 左家，即逆時針。
    // 攝影機從自己身後上方俯視，所以近端的自己最大、對家因透視自然變小。
    // ============================================================

    public static class BoardLayout
    {
        public const int SeatCount = GameState.PlayerCount;

        // ---- 各區塊離桌心多遠 ----
        public const float HandDistance = 2.42f;
        public const float MeldDistance = 2.02f;
        // 一邊 18 墩共 3.67 寬，離桌心 1.86 四角才剛好接起來
        public const float WallDistance = 1.86f;
        public const float FirstDiscardRow = 1.40f;

        // ---- 排列間距 ----
        public const float HandStep = TileAssets.Width + 0.006f;
        public const float MeldStep = TileAssets.Width + 0.004f;
        public const float MeldGroupGap = 0.075f;
        // 牌河的間距要明顯拉開，太貼會整片黏成一塊看不出是一張一張的牌
        public const float DiscardStepX = TileAssets.Width + 0.024f;
        public const float DiscardStepZ = TileAssets.Height + 0.060f;
        public const float WallStep = TileAssets.Width + 0.004f;

        public const int DiscardsPerRow = 8;
        public const int WallStacksPerSide = 18;
        public const int WallTilesPerStack = 2;

        /// <summary>手牌與副露之間留的空隙</summary>
        public const float HandToMeldGap = 0.09f;

        // 純粹往上抬。原本還往自己這邊挪，但攝影機是俯視的，
        // 往自己挪在畫面上看起來就是「往下移」，反而不像被拿起來。
        /// <summary>選取的牌抬起來的高度</summary>
        public static readonly Vector3 SelectedLift = new Vector3(0f, 0.14f, 0f);

        /// <summary>叫牌提示抬得比選取淺一些</summary>
        public static readonly Vector3 ClaimLift = new Vector3(0f, 0.07f, 0f);

        // ---- 牌桌本體 ----
        public const float TableSize = 5.8f;
        public const float TableThickness = 0.16f;

        // ---- 攝影機（想調整視角就改這三個值）----
        // 自己的手牌改由 2D 畫在畫面下緣，所以視線往前壓一點，
        // 把牌桌整體帶高，下緣空出來給手牌那一排。
        public static readonly Vector3 CameraPosition = new Vector3(0f, 3.55f, -4.10f);
        public static readonly Vector3 CameraTarget = new Vector3(0f, 0f, -1.05f);
        public const float CameraFieldOfView = 44f;

        // ------------------------------------------------------------
        // 基本朝向
        // ------------------------------------------------------------

        /// <summary>
        /// 立著、牌面朝向自己（本地 -Z）。三家對手的手牌都是這樣正常立著。
        ///
        /// 自己的手牌不在這裡畫——攝影機俯視牌桌，立著的牌只看得到上緣，
        /// 把牌後仰又很不自然，所以自己的手牌交給 2D 的 HandStrip 畫在畫面下緣。
        /// </summary>
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

        /// <summary>
        /// 手牌後仰之後高度會變矮、厚度會佔到高度，
        /// 中心點要跟著調整，牌才會剛好貼在桌面上而不是陷進去或浮起來。
        /// </summary>
        public static Vector3 HandSlot(float x)
            => new Vector3(x, TileAssets.Height * 0.5f, -HandDistance);

        public static Vector3 MeldSlot(float x)
            => new Vector3(x, TileAssets.Depth * 0.5f, -MeldDistance);

        public static Vector3 DiscardSlot(int index)
        {
            int column = index % DiscardsPerRow;
            int row = index / DiscardsPerRow;
            float x = (column - (DiscardsPerRow - 1) * 0.5f) * DiscardStepX;
            float z = -FirstDiscardRow + row * DiscardStepZ;
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
                                            TileAssets.Depth * (0.5f + layer),
                                            -WallDistance);
            ToWorld(side, localPosition, LyingFaceDown, out position, out rotation);
        }
    }
}
