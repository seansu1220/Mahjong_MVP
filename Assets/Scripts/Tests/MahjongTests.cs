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
            t.TestPinghuRequiresTwoSidedWait();
            t.TestPinghuRejectsEdgeWait();
            t.TestFlowerAndZhengHuaNotDoubled();
            t.TestDealHandSizes();
            t.TestNoFlowersLeftInHand();
            t.TestTileConservationAfterDeal();
            t.TestSeatWindsFollowDealer();
            t.TestSeatHelpers();
            t.TestGameStateCloneIsDeep();
            t.TestChiOnlyFromLeftPlayer();
            t.TestChiVariants();
            t.TestWinBeatsPon();
            t.TestNearestWinnerWins();
            t.TestClaimedTileLeavesDiscardPile();
            t.TestConcealedKanDrawsFromTail();
            t.TestAddedKanUpgradesPonMeld();
            t.TestCannotSelfDrawWinAfterPon();
            t.TestIllegalActionsAreRejected();
            t.TestFullGameNoClaims();
            t.TestFullGameWithClaims();
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

        // ---------- 平胡的兩面聽條件 ----------

        void TestPinghuRequiresTwoSidedWait()
        {
            // 123萬 456萬 789萬 123筒 + 9筒9筒 + 5筒6筒，放槍胡 7筒 -> 兩面聽（4筒/7筒）
            // 將牌必須是數牌，用字牌當將本來就不算平胡，會測不到兩面聽這個條件
            var ctx = new WinContext
            {
                ConcealedHand = Hand(0,1,2, 3,4,5, 6,7,8, 9,10,11, 17,17, 13,14),
                WinningTile = 15,
                IsSelfDraw = false
            };
            var r = ScoreCalculator.Calculate(ctx, FanTable.Default());
            Assert(r.Items.Any(i => i.Name == "平胡"), "全順子且兩面聽應計平胡");
        }

        void TestPinghuRejectsEdgeWait()
        {
            // 123萬 456萬 789萬 123筒 + 2萬2萬 + 8筒9筒，放槍胡 7筒 -> 邊張，不算平胡
            // 除了兩面聽以外的平胡條件（全順子、將是數牌、非自摸）全部成立，
            // 這樣失敗才確定是被兩面聽這一條擋掉的
            var ctx = new WinContext
            {
                ConcealedHand = Hand(0,1,1,1,2, 3,4,5, 6,7,8, 9,10,11, 16,17),
                WinningTile = 15,
                IsSelfDraw = false
            };
            var r = ScoreCalculator.Calculate(ctx, FanTable.Default());
            Assert(!r.Items.Any(i => i.Name == "平胡"), "邊張聽（89 聽 7）不應計平胡");
        }

        void TestFlowerAndZhengHuaNotDoubled()
        {
            // 東家摸到春(34)與梅(38)，兩張都是正花。本專案規則：每張花一律 1 台，正花不另加
            var ctx = new WinContext
            {
                ConcealedHand = Hand(0,1,2, 3,4,5, 6,7,8, 9,10,11, 12,13, 27,27),
                WinningTile = 14,
                SeatIndex = 0,
                Flowers = new List<int> { 34, 38 }
            };
            var r = ScoreCalculator.Calculate(ctx, FanTable.Default());
            var flower = r.Items.FirstOrDefault(i => i.Name == "花牌");
            Assert(flower.Tai == 2, "兩張花應為 2 台");
            Assert(!r.Items.Any(i => i.Name == "正花"), "正花不另外加計（本專案規則）");
        }

        // ---------- 牌局狀態 ----------

        void TestDealHandSizes()
        {
            var state = GameState.CreateNewHand(dealerIndex: 1, seed: 20260822);
            bool ok = state.Players[1].ConcealedTileCount == GameState.DealerHandSize;
            for (int seat = 0; seat < GameState.PlayerCount; seat++)
                if (seat != 1 && state.Players[seat].ConcealedTileCount != GameState.PlayerHandSize)
                    ok = false;
            Assert(ok, "發牌後莊家 17 張、閒家各 16 張");
        }

        void TestNoFlowersLeftInHand()
        {
            // 補花必須補到手上完全沒有花，而且補進來的花要繼續補
            bool ok = true;
            for (int seed = 1; seed <= 30; seed++)
            {
                var state = GameState.CreateNewHand(dealerIndex: seed % 4, seed: seed);
                for (int seat = 0; seat < GameState.PlayerCount; seat++)
                    if (state.Players[seat].ConcealedTileCount != state.ExpectedHandSize(seat))
                        ok = false;
            }
            Assert(ok, "補花後每家張數都補滿（連補 30 副牌驗證）");
        }

        void TestTileConservationAfterDeal()
        {
            // 手牌 + 花 + 牌山剩餘 = 144，一張都不能憑空生出或消失
            var state = GameState.CreateNewHand(dealerIndex: 0, seed: 555);
            int accounted = state.Wall.Remaining;
            for (int seat = 0; seat < GameState.PlayerCount; seat++)
                accounted += state.Players[seat].ConcealedTileCount + state.Players[seat].Flowers.Count;
            Assert(accounted == 144, "發牌補花後總牌數守恆為 144 張");
        }

        void TestSeatWindsFollowDealer()
        {
            // 莊家為東，之後逆時針依序南、西、北
            var state = GameState.CreateNewHand(dealerIndex: 2, seed: 99);
            bool ok = state.Players[2].SeatWind == TileDef.EAST
                   && state.Players[3].SeatWind == TileDef.SOUTH
                   && state.Players[0].SeatWind == TileDef.WEST
                   && state.Players[1].SeatWind == TileDef.NORTH;
            Assert(ok, "門風以莊家為東逆時針排列");
        }

        void TestSeatHelpers()
        {
            Assert(GameState.NextSeat(3) == 0, "下一家會繞回 0");
            Assert(GameState.SeatDistance(3, 1) == 2, "座位距離逆時針計算");
            Assert(GameState.IsNextSeatOf(0, 3), "0 是 3 的下家（吃只能吃下家）");
            Assert(!GameState.IsNextSeatOf(2, 3), "2 不是 3 的下家");
        }

        void TestGameStateCloneIsDeep()
        {
            var state = GameState.CreateNewHand(dealerIndex: 0, seed: 1234);
            state.Players[0].Melds.Add(new Meld { Type = MeldType.Pon, BaseTile = 5, FromPlayer = 1 });
            state.Players[0].Discards.Add(9);

            var copy = state.Clone();
            copy.Players[0].ConcealedCounts[0] += 7;
            copy.Players[0].Melds[0].BaseTile = 30;
            copy.Players[0].Discards.Add(11);
            copy.CurrentPlayer = 3;

            bool untouched = state.Players[0].ConcealedCounts[0] != copy.Players[0].ConcealedCounts[0]
                          && state.Players[0].Melds[0].BaseTile == 5
                          && state.Players[0].Discards.Count == 1
                          && state.CurrentPlayer == 0;
            Assert(untouched, "Clone 為深拷貝，改動副本不會影響真實局面");
            Assert(copy.Wall == null, "Clone 不複製牌山（AI 模擬不得偷看）");
        }

        // ---------- 流程引擎 ----------

        /// <summary>做出一個手牌清空、可自由擺設的局面，用來測特定情境。</summary>
        static GameState BlankBoard(int dealerIndex = 0, int seed = 4242)
        {
            var state = GameState.CreateNewHand(dealerIndex, seed: seed);
            for (int seat = 0; seat < GameState.PlayerCount; seat++)
            {
                Array.Clear(state.Players[seat].ConcealedCounts, 0, TileDef.KINDS);
                state.Players[seat].Flowers.Clear();
            }
            return state;
        }

        static void GiveTiles(PlayerState player, params int[] tiles)
        {
            foreach (int tile in tiles) player.AddTile(tile);
        }

        /// <summary>把局面擺成「某家剛打出一張牌，等其他家宣告」</summary>
        static void SetPendingDiscard(GameState state, int discarder, int tile)
        {
            state.Players[discarder].Discards.Add(tile);
            state.LastDiscardTile = tile;
            state.LastDiscardFrom = discarder;
            state.Phase = GamePhase.WaitingClaim;
        }

        static bool HasAction(List<GameAction> actions, ActionType type)
            => actions.Exists(a => a.Type == type);

        void TestChiOnlyFromLeftPlayer()
        {
            var state = BlankBoard();
            GiveTiles(state.Players[1], 1, 2);   // 下家有 2萬 3萬
            GiveTiles(state.Players[2], 1, 2);   // 對家也有，但不是下家
            SetPendingDiscard(state, discarder: 0, tile: 3);   // 0 打出 4萬

            var engine = new TurnEngine(state, FanTable.Default());
            Assert(HasAction(engine.GetClaimActions(1), ActionType.Chi), "下家可以吃");
            Assert(!HasAction(engine.GetClaimActions(2), ActionType.Chi), "非下家不能吃");
            Assert(!HasAction(engine.GetClaimActions(0), ActionType.Chi), "自己打的牌不能吃");
        }

        void TestChiVariants()
        {
            // 手上 3萬4萬6萬7萬，打出 5萬 -> 可組 345 / 456 / 567 三種
            var state = BlankBoard();
            GiveTiles(state.Players[1], 2, 3, 5, 6);
            SetPendingDiscard(state, discarder: 0, tile: 4);

            var engine = new TurnEngine(state, FanTable.Default());
            var chiOptions = engine.GetClaimActions(1).FindAll(a => a.Type == ActionType.Chi);
            Assert(chiOptions.Count == 3, "同一張牌的三種吃法都要列出");

            // 字牌不可吃
            var honorState = BlankBoard();
            GiveTiles(honorState.Players[1], TileDef.EAST, TileDef.SOUTH);
            SetPendingDiscard(honorState, discarder: 0, tile: TileDef.WEST);
            var honorEngine = new TurnEngine(honorState, FanTable.Default());
            Assert(!HasAction(honorEngine.GetClaimActions(1), ActionType.Chi), "字牌不能吃");
        }

        // 123萬 456萬 789萬 123筒 456筒 共 15 張 + 東 = 16 張，胡東成對將
        static readonly int[] WinsOnEastHand = { 0,1,2, 3,4,5, 6,7,8, 9,10,11, 12,13,14, 27 };

        void TestWinBeatsPon()
        {
            var state = BlankBoard();
            GiveTiles(state.Players[1], 27, 27, 0, 1);          // 下家可碰東
            GiveTiles(state.Players[2], WinsOnEastHand);        // 對家可胡東
            SetPendingDiscard(state, discarder: 0, tile: 27);

            var engine = new TurnEngine(state, FanTable.Default());
            var result = engine.ResolveClaims(new List<GameAction>
            {
                new GameAction { Type = ActionType.Pon, SeatIndex = 1, Tile = 27 },
                new GameAction { Type = ActionType.Win, SeatIndex = 2, Tile = 27 }
            });

            Assert(result.Success && result.WinnerSeat == 2, "胡的優先權高於碰");
            Assert(state.Phase == GamePhase.Ended && state.EndReason == GameEndReason.Win, "胡牌後牌局結束");
        }

        void TestNearestWinnerWins()
        {
            // 座位 1 與座位 3 同時可胡座位 0 打出的東，逆時針較近的 1 優先
            var state = BlankBoard();
            GiveTiles(state.Players[1], WinsOnEastHand);
            GiveTiles(state.Players[3], WinsOnEastHand);
            SetPendingDiscard(state, discarder: 0, tile: 27);

            var engine = new TurnEngine(state, FanTable.Default());
            var result = engine.ResolveClaims(new List<GameAction>
            {
                new GameAction { Type = ActionType.Win, SeatIndex = 3, Tile = 27 },
                new GameAction { Type = ActionType.Win, SeatIndex = 1, Tile = 27 }
            });

            Assert(result.WinnerSeat == 1, "多家可胡時取逆時針離打牌者最近的一家");
        }

        void TestClaimedTileLeavesDiscardPile()
        {
            var state = BlankBoard();
            GiveTiles(state.Players[1], 27, 27, 0, 1);
            SetPendingDiscard(state, discarder: 0, tile: 27);
            int discardsBefore = state.Players[0].Discards.Count;

            var engine = new TurnEngine(state, FanTable.Default());
            var result = engine.ResolveClaims(new List<GameAction>
            {
                new GameAction { Type = ActionType.Pon, SeatIndex = 1, Tile = 27 }
            });

            Assert(result.Success, "碰應成立");
            Assert(state.Players[0].Discards.Count == discardsBefore - 1, "被碰走的牌要從牌河移除");
            Assert(state.Players[1].Melds.Count == 1 && state.Players[1].ConcealedCounts[27] == 0,
                   "碰之後手上兩張進副露");
            Assert(state.CurrentPlayer == 1 && state.Phase == GamePhase.WaitingDiscard,
                   "碰完換宣告者出牌");
        }

        void TestConcealedKanDrawsFromTail()
        {
            var state = BlankBoard();
            GiveTiles(state.Players[0], 5, 5, 5, 5, 9, 10);
            state.Phase = GamePhase.WaitingDiscard;
            state.CurrentPlayer = 0;
            state.HasDrawnThisTurn = true;
            int wallBefore = state.Wall.Remaining;

            var engine = new TurnEngine(state, FanTable.Default());
            Assert(HasAction(engine.GetTurnActions(0), ActionType.AnKan), "手上四張應可暗槓");

            var result = engine.ApplyTurnAction(
                new GameAction { Type = ActionType.AnKan, SeatIndex = 0, Tile = 5 });

            Assert(result.Success && result.DrawnTile != GameState.NoTile, "暗槓後要補一張");
            Assert(state.Wall.Remaining < wallBefore, "補牌要從牌山取走");
            Assert(state.Players[0].ConcealedCounts[5] == 0, "四張都進副露");
            Assert(state.Players[0].Melds[0].Type == MeldType.AnKan, "副露記為暗槓");
            Assert(state.Players[0].IsConcealedHand, "暗槓不破門清");
            Assert(state.AwaitingKanReplacement, "槓後補牌狀態要標記，供槓上開花判定");
            Assert(state.Phase == GamePhase.WaitingDiscard && state.CurrentPlayer == 0,
                   "補完仍由同一家出牌");
        }

        void TestAddedKanUpgradesPonMeld()
        {
            var state = BlankBoard();
            state.Players[0].Melds.Add(new Meld { Type = MeldType.Pon, BaseTile = 12, FromPlayer = 2 });
            GiveTiles(state.Players[0], 12, 0, 1);
            state.Phase = GamePhase.WaitingDiscard;
            state.CurrentPlayer = 0;
            state.HasDrawnThisTurn = true;

            var engine = new TurnEngine(state, FanTable.Default());
            Assert(HasAction(engine.GetTurnActions(0), ActionType.AddKan), "碰過又摸到第四張應可加槓");

            var result = engine.ApplyTurnAction(
                new GameAction { Type = ActionType.AddKan, SeatIndex = 0, Tile = 12 });

            Assert(result.Success, "加槓應成立");
            Assert(state.Players[0].Melds[0].Type == MeldType.MinKan, "碰升級為明槓");
            Assert(state.Players[0].ConcealedCounts[12] == 0, "手上那張併入副露");
            Assert(!state.Players[0].IsConcealedHand, "明槓破門清");
        }

        void TestCannotSelfDrawWinAfterPon()
        {
            // 碰完之後手牌張數雖然也是 3n+2，但那不是自摸，不該出現胡的選項
            var state = BlankBoard();
            GiveTiles(state.Players[1], WinsOnEastHand);
            state.Players[1].AddTile(27);   // 補成 17 張，牌型已成
            state.Phase = GamePhase.WaitingDiscard;
            state.CurrentPlayer = 1;
            state.HasDrawnThisTurn = false;   // 剛吃碰完，不是摸進來的

            var engine = new TurnEngine(state, FanTable.Default());
            Assert(!HasAction(engine.GetTurnActions(1), ActionType.Win), "吃碰之後不能宣告自摸");

            state.HasDrawnThisTurn = true;
            Assert(HasAction(engine.GetTurnActions(1), ActionType.Win), "剛摸完牌則可宣告自摸");
        }

        void TestIllegalActionsAreRejected()
        {
            var state = BlankBoard();
            GiveTiles(state.Players[0], 0, 1, 2);
            state.Phase = GamePhase.WaitingDiscard;
            state.CurrentPlayer = 0;

            var engine = new TurnEngine(state, FanTable.Default());

            var wrongSeat = engine.ApplyTurnAction(
                new GameAction { Type = ActionType.Discard, SeatIndex = 2, Tile = 0 });
            Assert(!wrongSeat.Success && wrongSeat.Error != null, "不是自己的回合要被擋下並說明原因");

            var noSuchTile = engine.ApplyTurnAction(
                new GameAction { Type = ActionType.Discard, SeatIndex = 0, Tile = 30 });
            Assert(!noSuchTile.Success, "打出手上沒有的牌要被擋下");

            // 不合規的宣告（沒有那兩張卻宣告碰）要被過濾掉，不能改動局面
            SetPendingDiscard(state, discarder: 3, tile: 20);
            var bogus = engine.ResolveClaims(new List<GameAction>
            {
                new GameAction { Type = ActionType.Pon, SeatIndex = 0, Tile = 20 }
            });
            Assert(bogus.Success && state.Players[0].Melds.Count == 0, "假宣告要被過濾，視同沒有人宣告");
            Assert(state.CurrentPlayer == 0 && state.Phase == GamePhase.WaitingDraw,
                   "沒人宣告就換打牌者的下家摸牌");
        }

        // ----- 整局煙霧測試 -----

        static int TotalTilesInPlay(GameState state)
        {
            int total = state.Wall.Remaining;
            for (int seat = 0; seat < GameState.PlayerCount; seat++)
            {
                var player = state.Players[seat];
                total += player.ConcealedTileCount + player.Flowers.Count + player.Discards.Count;
                foreach (var meld in player.Melds) total += meld.Tiles().Length;
            }
            return total;
        }

        static GameAction ChooseTurnAction(List<GameAction> actions, bool greedy)
        {
            var win = actions.Find(a => a.Type == ActionType.Win);
            if (win != null) return win;
            if (greedy)
            {
                var kan = actions.Find(a => a.Type == ActionType.AnKan || a.Type == ActionType.AddKan);
                if (kan != null) return kan;
            }
            return actions.Find(a => a.Type == ActionType.Discard);
        }

        static List<GameAction> CollectClaims(TurnEngine engine, bool greedy)
        {
            var declarations = new List<GameAction>();
            for (int seat = 0; seat < GameState.PlayerCount; seat++)
            {
                if (seat == engine.State.LastDiscardFrom) continue;
                var options = engine.GetClaimActions(seat);
                var win = options.Find(a => a.Type == ActionType.Win);
                if (win != null) { declarations.Add(win); continue; }
                if (!greedy) continue;
                var claim = options.Find(a => a.Type != ActionType.Pass);
                if (claim != null) declarations.Add(claim);
            }
            return declarations;
        }

        /// <summary>把一局跑到結束，中途持續檢查牌數守恆。</summary>
        static bool RunWholeGame(TurnEngine engine, bool greedy, out string failure)
        {
            failure = null;
            var state = engine.State;

            for (int step = 0; step < 3000 && state.Phase != GamePhase.Ended; step++)
            {
                TurnResult result;
                if (state.Phase == GamePhase.WaitingDraw)
                {
                    result = engine.DrawForCurrentPlayer();
                }
                else if (state.Phase == GamePhase.WaitingDiscard)
                {
                    var chosen = ChooseTurnAction(engine.GetTurnActions(state.CurrentPlayer), greedy);
                    if (chosen == null) { failure = $"座位 {state.CurrentPlayer} 無任何合法動作"; return false; }
                    result = engine.ApplyTurnAction(chosen);
                }
                else if (state.Phase == GamePhase.WaitingClaim)
                {
                    result = engine.ResolveClaims(CollectClaims(engine, greedy));
                }
                else { failure = $"非預期的階段 {state.Phase}"; return false; }

                if (!result.Success) { failure = result.Error; return false; }

                int total = TotalTilesInPlay(state);
                if (total != 144) { failure = $"牌數不守恆，目前 {total} 張"; return false; }
            }

            if (state.Phase != GamePhase.Ended) { failure = "3000 步後牌局仍未結束"; return false; }
            return true;
        }

        void TestFullGameNoClaims()
        {
            bool ok = true;
            string failure = null;
            for (int seed = 1; seed <= 25 && ok; seed++)
            {
                var engine = new TurnEngine(GameState.CreateNewHand(seed % 4, seed: seed), FanTable.Default());
                ok = RunWholeGame(engine, greedy: false, out failure);
                if (!ok) Log($"     ! seed {seed}：{failure}");
            }
            Assert(ok, "25 局完整跑到結束，全程牌數守恆（不吃碰）");
        }

        void TestFullGameWithClaims()
        {
            bool ok = true;
            string failure = null;
            for (int seed = 1; seed <= 25 && ok; seed++)
            {
                var engine = new TurnEngine(GameState.CreateNewHand(seed % 4, seed: seed), FanTable.Default());
                ok = RunWholeGame(engine, greedy: true, out failure);
                if (!ok) Log($"     ! seed {seed}：{failure}");
            }
            Assert(ok, "25 局完整跑到結束，全程牌數守恆（積極吃碰槓）");
        }
    }
}
