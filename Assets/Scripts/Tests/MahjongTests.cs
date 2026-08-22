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

        /// <summary>執行全部測試。全數通過回傳 true，方便離線跑道與 CI 判斷成敗。</summary>
        public static bool RunAll()
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
            t.TestWallTailDraw();
            t.TestWallSeedReproducible();
            t.TestWaitsExcludeExhaustedTiles();
            t.TestScoreQingyise();
            t.TestRonDoesNotCountAsConcealedTriplet();
            t.TestSelfDrawCountsAsConcealedTriplet();
            t.TestLianZhuangTai();
            t.TestInvalidWinningTileThrows();
            t.Report();
            return t.failed == 0;
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
            // 123萬 123萬 456萬 789萬 東東東 + 中中
            // 候選將牌依序為 1萬 / 2萬 / 3萬 / 東，四個都會拆解失敗，
            // 必須一路回溯到「中」才成立——這才真的測到將牌回溯。
            var h = Hand(0,0, 1,1, 2,2, 3,4,5, 6,7,8, 27,27,27, 31,31);
            Assert(WinChecker.CanWin(h, 0), "需回溯將牌的牌型");

            var patterns = WinChecker.AllPatterns(h, 0);
            Assert(patterns.Count == 1 && patterns[0].PairTile == 31, "唯一解的將牌應為中");
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
            while (!wall.IsEmpty) { var t = wall.Draw(); Bump(counts, t); total++; }

            Assert(total == 144, "牌山共 144 張");
            bool ok = true;
            for (int i = 0; i < TileDef.KINDS; i++)
                if (Get(counts, i) != 4) ok = false;
            for (int i = 0; i < TileDef.FLOWER_COUNT; i++)
                if (Get(counts, TileDef.FLOWER_BASE + i) != 1) ok = false;
            Assert(ok, "各牌張數正確（數牌字牌各4張、花牌各1張）");
        }

        static void Bump(Dictionary<int, int> map, int key)
            => map[key] = Get(map, key) + 1;

        static int Get(Dictionary<int, int> map, int key)
            => map.TryGetValue(key, out var v) ? v : 0;

        void TestWallTailDraw()
        {
            // 補花與槓後補牌要從牌尾取；頭尾交替取完 144 張，張數仍須完全正確
            var wall = new Wall(seed: 777);
            var counts = new Dictionary<int, int>();
            int total = 0;
            bool fromHead = true;
            while (!wall.IsEmpty)
            {
                Bump(counts, fromHead ? wall.Draw() : wall.DrawFromTail());
                total++;
                fromHead = !fromHead;
            }
            Assert(total == 144, "頭尾交替取牌共 144 張，不重複不遺漏");

            bool ok = true;
            for (int i = 0; i < TileDef.KINDS; i++) if (Get(counts, i) != 4) ok = false;
            for (int i = 0; i < TileDef.FLOWER_COUNT; i++)
                if (Get(counts, TileDef.FLOWER_BASE + i) != 1) ok = false;
            Assert(ok, "頭尾交替取牌後各牌張數仍正確");
        }

        void TestWallSeedReproducible()
        {
            // seed 傳 0 是隨機開局，但實際種子要留在 Seed，才有辦法重現同一副牌
            var first = new Wall(seed: 0);
            var replay = new Wall(seed: first.Seed);
            bool identical = true;
            while (!first.IsEmpty)
                if (first.Draw() != replay.Draw()) identical = false;
            Assert(identical, "用 Wall.Seed 可完整重現同一副牌");
        }

        void TestWaitsExcludeExhaustedTiles()
        {
            // 已暗槓四張九筒，手上再單吊九筒 -> 世上沒有第五張，這是死聽
            var hand = Hand(0,1,2, 3,4,5, 6,7,8, 9,10,11, 17);
            var melds = new List<Meld> { new Meld { Type = MeldType.AnKan, BaseTile = 17 } };

            Assert(WinChecker.GetWaits(hand, 1).Contains(17), "舊版多載只看手中張數，仍會列出九筒");
            Assert(!WinChecker.GetWaits(hand, melds).Contains(17), "扣除副露後，槓光的九筒不應算聽張");
        }

        // ---------- 台數 ----------

        void TestScoreQingyise()
        {
            // 全萬子：123萬 123萬 456萬 789萬 789萬 + 5萬5萬 = 17 張
            // （原本這個測試只給 16 張，永遠拆不出牌型，等於沒測到）
            var ctx = new WinContext
            {
                ConcealedHand = Hand(0,0, 1,1, 2,2, 3, 4,4,4, 5, 6,6, 7,7, 8),
                WinningTile = 8,
                IsSelfDraw = false,
                IsDealer = false
            };
            var r = ScoreCalculator.Calculate(ctx, FanTable.Default());
            Assert(r.Items.Any(i => i.Name == "清一色"), "清一色應被計入");
            Assert(!r.Items.Any(i => i.Name == "混一色"), "清一色不應同時計混一色");
            Log("     → " + r);
        }

        // 111萬 222萬 333萬 111筒 中中中 + 東東（16 張，胡「中」湊成 17 張）
        static WinContext FiveTripletsContext(bool selfDraw) => new WinContext
        {
            ConcealedHand = Hand(0,0,0, 1,1,1, 2,2,2, 9,9,9, 31,31, 27,27),
            WinningTile = 31,
            IsSelfDraw = selfDraw
        };

        void TestRonDoesNotCountAsConcealedTriplet()
        {
            var r = ScoreCalculator.Calculate(FiveTripletsContext(selfDraw: false), FanTable.Default());
            Assert(!r.Items.Any(i => i.Name == "五暗刻"), "放槍完成的刻子是明刻，不應算五暗刻");
            Assert(r.Items.Any(i => i.Name == "碰碰胡"), "碰碰胡仍應計入");
            Log("     → 放槍 " + r);
        }

        void TestSelfDrawCountsAsConcealedTriplet()
        {
            var r = ScoreCalculator.Calculate(FiveTripletsContext(selfDraw: true), FanTable.Default());
            Assert(r.Items.Any(i => i.Name == "五暗刻"), "自摸五組暗刻應算五暗刻");
            Log("     → 自摸 " + r);
        }

        void TestLianZhuangTai()
        {
            // 連 N 拉 N = N × 台數表設定值（預設 2 台）
            var table = FanTable.Default();
            int perStreak = table.ValueOf(FanId.LianZhuang);
            bool ok = true;
            for (int streak = 1; streak <= 3; streak++)
            {
                var ctx = new WinContext
                {
                    ConcealedHand = Hand(0,1,2, 3,4,5, 6,7,8, 9,10,11, 12,13, 27,27),
                    WinningTile = 14,
                    IsDealer = true,
                    DealerStreak = streak
                };
                var r = ScoreCalculator.Calculate(ctx, table);
                var item = r.Items.FirstOrDefault(i => i.Name == "連莊拉莊");
                if (item.Tai != streak * perStreak) ok = false;
            }
            Assert(ok, "連莊拉莊台數 = 連莊次數 × 台數表設定，不得重複乘算");
        }

        void TestInvalidWinningTileThrows()
        {
            bool threw = false;
            try
            {
                var ctx = new WinContext
                {
                    ConcealedHand = Hand(0, 1, 2),
                    WinningTile = TileDef.FLOWER_BASE   // 花牌不能當胡牌張
                };
                ScoreCalculator.Calculate(ctx, FanTable.Default());
            }
            catch (ArgumentException) { threw = true; }
            Assert(threw, "胡牌張傳入花牌 id 應拋 ArgumentException 而非索引越界");
        }
    }
}
