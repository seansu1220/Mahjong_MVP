using System;
using System.Collections.Generic;
using System.Linq;

namespace Mahjong.Tests
{
    /// <summary>
    /// 不依賴 NUnit，可直接在 Unity 中掛在空物件上執行，也可搬進 Test Runner。
    /// 這些測試是你驗收 Claude Code 產出的依據——每次改動規則引擎後都要全綠。
    /// </summary>
    public class MahjongTests
    {
        int passed = 0, failed = 0;

        public static void RunAll()
        {
            var t = new MahjongTests();
            t.TestBasicWin();
            t.TestAllTriplets();
            t.TestNotWin();
            t.TestWithMelds();
            t.TestHonorCannotFormRun();
            t.TestNoCrossSuitRun();
            t.TestAmbiguousDecomposition();
            t.TestPairBacktrack();
            t.TestSevenPairsRejected();
            t.TestSingleWait();
            t.TestTwoSidedWait();
            t.TestWallIntegrity();
            t.TestScoreQingyise();
            t.Report();
        }

        // ---------- 工具 ----------
        static int[] Hand(params int[] tiles)
        {
            var h = new int[TileDef.KINDS];
            foreach (var t in tiles) h[t]++;
            return h;
        }

        void Assert(bool cond, string name)
        {
            if (cond) { passed++; Log($"  PASS  {name}"); }
            else { failed++; Log($"  FAIL  {name}"); }
        }

        static void Log(string s)
        {
#if UNITY_5_3_OR_NEWER || UNITY_2017_1_OR_NEWER
            UnityEngine.Debug.Log(s);
#else
            Console.WriteLine(s);
#endif
        }

        void Report()
        {
            Log($"===== 通過 {passed} / 失敗 {failed} =====");
        }

        // ---------- 胡牌判定 ----------

        void TestBasicWin()
        {
            // 123 456 789 萬 + 123 456 筒 + 東東
            var h = Hand(0,1,2, 3,4,5, 6,7,8, 9,10,11, 12,13,14, 27,27);
            Assert(WinChecker.CanWin(h, 0), "標準五順子胡牌");
        }

        void TestAllTriplets()
        {
            // 111 222 333 萬 + 111 筒 + 東東東 + 中中
            var h = Hand(0,0,0, 1,1,1, 2,2,2, 9,9,9, 27,27,27, 31,31);
            Assert(WinChecker.CanWin(h, 0), "碰碰胡");
        }

        void TestNotWin()
        {
            var h = Hand(0,1,2, 3,4,5, 6,7,8, 9,10,11, 12,13,15, 27,27);
            Assert(!WinChecker.CanWin(h, 0), "未成牌應回傳 false");
        }

        void TestWithMelds()
        {
            // 已副露 2 組，手中剩 11 張
            var h = Hand(0,1,2, 3,4,5, 6,7,8, 27,27);
            Assert(WinChecker.CanWin(h, 2), "含副露 2 組的胡牌");
        }

        void TestHonorCannotFormRun()
        {
            // 東南西 不可視為順子
            var h = Hand(27,28,29, 0,1,2, 3,4,5, 6,7,8, 9,10,11, 31,31);
            Assert(!WinChecker.CanWin(h, 0), "字牌不可組成順子");
        }

        void TestNoCrossSuitRun()
        {
            // 8萬 9萬 1筒 不可視為順子
            var h = Hand(7,8,9, 0,1,2, 3,4,5, 10,11,12, 13,14,15, 31,31);
            Assert(!WinChecker.CanWin(h, 0), "順子不可跨花色");
        }

        void TestAmbiguousDecomposition()
        {
            // 333444555萬 可拆三刻子或三順子，兩種拆法都應成立
            var h = Hand(2,2,2, 3,3,3, 4,4,4, 5,6,7, 9,10,11, 8,8);
            Assert(WinChecker.CanWin(h, 0), "刻子/順子歧義牌型");

            var patterns = WinChecker.AllPatterns(h, 0);
            Assert(patterns.Count >= 2, "歧義牌型應產生多種拆解");
        }

        void TestPairBacktrack()
        {
            // 第一個候選將牌會導致失敗，需回溯換將
            var h = Hand(0,0, 0,1,2, 1,2,3, 3,4,5, 6,7,8, 9,9,9);
            Assert(WinChecker.CanWin(h, 0), "需回溯將牌的牌型");
        }

        void TestSevenPairsRejected()
        {
            // 台灣 16 張不採七對子（除非客戶台數表另訂）
            var h = Hand(0,0, 1,1, 2,2, 3,3, 4,4, 5,5, 6,6, 7,7, 8);
            Assert(!WinChecker.CanWin(h, 0), "七對子預設不成立");
        }

        // ---------- 聽牌 ----------

        void TestSingleWait()
        {
            var h = Hand(0,1,2, 3,4,5, 6,7,8, 9,10,11, 12,13,14, 27);
            var w = WinChecker.GetWaits(h, 0);
            Assert(w.Count == 1 && w[0] == 27, "單吊東");
        }

        void TestTwoSidedWait()
        {
            var h = Hand(0,1,2, 3,4,5, 6,7,8, 9,10,11, 27,27, 13,14);
            var w = WinChecker.GetWaits(h, 0);
            Assert(w.Count == 2 && w.Contains(12) && w.Contains(15), "兩面聽 5筒/8筒");
        }

        // ---------- 牌山 ----------

        void TestWallIntegrity()
        {
            var wall = new Wall(seed: 12345);
            var counts = new Dictionary<int, int>();
            int total = 0;
            while (!wall.IsEmpty) { var t = wall.Draw(); counts[t] = counts.GetValueOrDefault(t) + 1; total++; }

            Assert(total == 144, "牌山共 144 張");
            bool ok = true;
            for (int i = 0; i < TileDef.KINDS; i++)
                if (counts.GetValueOrDefault(i) != 4) ok = false;
            for (int i = 0; i < TileDef.FLOWER_COUNT; i++)
                if (counts.GetValueOrDefault(TileDef.FLOWER_BASE + i) != 1) ok = false;
            Assert(ok, "各牌張數正確（數牌字牌各4張、花牌各1張）");
        }

        // ---------- 台數 ----------

        void TestScoreQingyise()
        {
            var ctx = new WinContext
            {
                ConcealedHand = Hand(0,1,2, 3,4,5, 6,7,8, 0,1,2, 3,4, 8),
                WinningTile = 8,
                IsSelfDraw = false,
                IsDealer = false
            };
            var r = ScoreCalculator.Calculate(ctx, FanTable.Default());
            bool hasQing = r.Items.Any(i => i.Name == "清一色");
            Assert(hasQing, "清一色應被計入");
            Log("     → " + r);
        }
    }
}
