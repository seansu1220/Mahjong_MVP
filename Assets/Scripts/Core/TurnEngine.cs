using System;
using System.Collections.Generic;

namespace Mahjong
{
    // ============================================================
    // 流程引擎
    //
    // 負責推進牌局：摸牌 → 打牌 → 其他三家宣告吃碰槓胡 → 比優先權 → 換下一家。
    // 純 C#，不含任何 UnityEngine 依賴，也不做任何等待或非同步。
    //
    // 呼叫端（UI 或測試）的主迴圈長這樣：
    //
    //   while (state.Phase != GamePhase.Ended)
    //   {
    //       switch (state.Phase)
    //       {
    //           case GamePhase.WaitingDraw:
    //               engine.DrawForCurrentPlayer();
    //               break;
    //           case GamePhase.WaitingDiscard:
    //               var options = engine.GetTurnActions(state.CurrentPlayer);
    //               engine.ApplyTurnAction(玩家或 AI 選的那個);
    //               break;
    //           case GamePhase.WaitingClaim:
    //               engine.ResolveClaims(其他三家各自的宣告);
    //               break;
    //       }
    //   }
    //
    // 引擎不會自己跑迴圈，所以 View 層可以用 coroutine 一步一步餵，
    // 中間插入動畫與等待玩家點擊，都不影響規則判定。
    // ============================================================

    public enum ActionType
    {
        Pass,       // 過
        Discard,    // 打出一張
        Chi,        // 吃（只能吃下家打出的牌）
        Pon,        // 碰
        MinKan,     // 大明槓：別人打出第四張
        AnKan,      // 暗槓：自己手上四張
        AddKan,     // 加槓：已經碰過，之後摸到第四張
        Win         // 胡（自摸或放槍）
    }

    /// <summary>一個動作。由引擎產生候選，呼叫端挑一個丟回來。</summary>
    public class GameAction
    {
        public ActionType Type;
        public int SeatIndex;

        /// <summary>動作的目標牌。Discard 為打出的牌，其餘為被吃碰槓胡的那張。</summary>
        public int Tile = GameState.NoTile;

        /// <summary>吃：所組成順子的最小張。其他動作不使用。</summary>
        public int ChiBaseTile = GameState.NoTile;

        public override string ToString()
        {
            string tileName = Tile == GameState.NoTile ? "" : TileDef.Name(Tile);
            switch (Type)
            {
                case ActionType.Pass: return $"座位{SeatIndex} 過";
                case ActionType.Discard: return $"座位{SeatIndex} 打 {tileName}";
                case ActionType.Chi:
                    return $"座位{SeatIndex} 吃 {tileName}（{TileDef.Name(ChiBaseTile)} 起）";
                default: return $"座位{SeatIndex} {Type} {tileName}";
            }
        }
    }

    /// <summary>每一步的結果。規則層不印訊息，所有輸出都靠回傳值。</summary>
    public class TurnResult
    {
        public bool Success = true;
        public string Error;

        public GameAction Applied;

        /// <summary>這一步摸進來的牌，沒摸牌則為 NoTile</summary>
        public int DrawnTile = GameState.NoTile;

        /// <summary>本步驟自動補了幾張花</summary>
        public int FlowersDrawn;

        public GameEndReason EndReason = GameEndReason.None;
        public int WinnerSeat = -1;
        public ScoreResult Score;

        public static TurnResult Fail(string reason) => new TurnResult { Success = false, Error = reason };
    }

    // ============================================================

    public class TurnEngine
    {
        public GameState State { get; private set; }
        public FanTable FanTable { get; private set; }

        public TurnEngine(GameState state, FanTable fanTable)
        {
            State = state ?? throw new ArgumentNullException(nameof(state), "牌局狀態不可為 null");
            FanTable = fanTable ?? throw new ArgumentNullException(nameof(fanTable), "台數表不可為 null");
        }

        // ------------------------------------------------------------
        // 摸牌
        // ------------------------------------------------------------

        /// <summary>輪到的玩家從牌頭摸一張，摸到花自動從牌尾補。牌山抽乾則流局。</summary>
        public TurnResult DrawForCurrentPlayer()
        {
            if (State.Phase != GamePhase.WaitingDraw)
                return TurnResult.Fail($"現在不是摸牌階段（目前為 {State.Phase}）");

            int seat = State.CurrentPlayer;
            int flowersBefore = State.Players[seat].Flowers.Count;
            int tile = State.DrawTile(seat);

            if (tile == GameState.NoTile) return EndByExhaustion();

            State.Phase = GamePhase.WaitingDiscard;
            State.HasDrawnThisTurn = true;
            State.AwaitingKanReplacement = false;
            return new TurnResult
            {
                DrawnTile = tile,
                FlowersDrawn = State.Players[seat].Flowers.Count - flowersBefore
            };
        }

        // ------------------------------------------------------------
        // 自己回合可做的動作
        // ------------------------------------------------------------

        /// <summary>輪到自己時的合法動作：打牌、暗槓、加槓、自摸胡。</summary>
        public List<GameAction> GetTurnActions(int seat)
        {
            var actions = new List<GameAction>();
            if (State.Phase != GamePhase.WaitingDiscard || seat != State.CurrentPlayer) return actions;

            var player = State.Players[seat];

            // 自摸：只有剛摸完牌才算。吃碰之後手牌雖然也是 3n+2 張，那不是自摸。
            if (State.HasDrawnThisTurn && CanWinWithCurrentHand(seat))
                actions.Add(new GameAction { Type = ActionType.Win, SeatIndex = seat });

            for (int tile = 0; tile < TileDef.KINDS; tile++)
            {
                if (player.ConcealedCounts[tile] == 0) continue;

                if (player.ConcealedCounts[tile] == WinChecker.TILES_PER_KIND)
                    actions.Add(new GameAction { Type = ActionType.AnKan, SeatIndex = seat, Tile = tile });

                if (FindPonMeld(player, tile) != null)
                    actions.Add(new GameAction { Type = ActionType.AddKan, SeatIndex = seat, Tile = tile });

                actions.Add(new GameAction { Type = ActionType.Discard, SeatIndex = seat, Tile = tile });
            }
            return actions;
        }

        /// <summary>執行自己回合的動作</summary>
        public TurnResult ApplyTurnAction(GameAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (State.Phase != GamePhase.WaitingDiscard)
                return TurnResult.Fail($"現在不是出牌階段（目前為 {State.Phase}）");
            if (action.SeatIndex != State.CurrentPlayer)
                return TurnResult.Fail($"現在輪到座位 {State.CurrentPlayer}，座位 {action.SeatIndex} 不能行動");

            switch (action.Type)
            {
                case ActionType.Discard: return ApplyDiscard(action);
                case ActionType.AnKan: return ApplyConcealedKan(action);
                case ActionType.AddKan: return ApplyAddedKan(action);
                case ActionType.Win: return ApplySelfDrawWin(action);
                default: return TurnResult.Fail($"{action.Type} 不是自己回合可做的動作");
            }
        }

        TurnResult ApplyDiscard(GameAction action)
        {
            var player = State.Players[action.SeatIndex];
            if (action.Tile < 0 || action.Tile >= TileDef.KINDS)
                return TurnResult.Fail($"打出的牌 id {action.Tile} 不合法");
            if (player.ConcealedCounts[action.Tile] == 0)
                return TurnResult.Fail($"座位 {action.SeatIndex} 手上沒有 {TileDef.Name(action.Tile)}，不能打出");

            player.RemoveTile(action.Tile);
            player.Discards.Add(action.Tile);

            State.LastDiscardTile = action.Tile;
            State.LastDiscardFrom = action.SeatIndex;
            State.HasDrawnThisTurn = false;
            State.AwaitingKanReplacement = false;
            State.Phase = GamePhase.WaitingClaim;
            return new TurnResult { Applied = action };
        }

        TurnResult ApplyConcealedKan(GameAction action)
        {
            var player = State.Players[action.SeatIndex];
            if (player.ConcealedCounts[action.Tile] != WinChecker.TILES_PER_KIND)
                return TurnResult.Fail($"座位 {action.SeatIndex} 手上不是四張 {TileDef.Name(action.Tile)}，不能暗槓");

            for (int i = 0; i < WinChecker.TILES_PER_KIND; i++) player.RemoveTile(action.Tile);
            player.Melds.Add(new Meld
            {
                Type = MeldType.AnKan,
                BaseTile = action.Tile,
                FromPlayer = action.SeatIndex
            });
            return DrawKanReplacement(action);
        }

        TurnResult ApplyAddedKan(GameAction action)
        {
            var player = State.Players[action.SeatIndex];
            var ponMeld = FindPonMeld(player, action.Tile);
            if (ponMeld == null)
                return TurnResult.Fail($"座位 {action.SeatIndex} 沒有碰過 {TileDef.Name(action.Tile)}，不能加槓");

            player.RemoveTile(action.Tile);
            ponMeld.Type = MeldType.MinKan;   // 碰升級成明槓
            return DrawKanReplacement(action);
        }

        /// <summary>槓完從牌尾補一張。補到花會繼續補，補完仍由同一家出牌。</summary>
        TurnResult DrawKanReplacement(GameAction action)
        {
            int seat = action.SeatIndex;
            int flowersBefore = State.Players[seat].Flowers.Count;
            int tile = State.DrawReplacementTile(seat);

            if (tile == GameState.NoTile) return EndByExhaustion();

            State.AwaitingKanReplacement = true;   // 供槓上開花判定
            State.HasDrawnThisTurn = true;
            State.Phase = GamePhase.WaitingDiscard;
            return new TurnResult
            {
                Applied = action,
                DrawnTile = tile,
                FlowersDrawn = State.Players[seat].Flowers.Count - flowersBefore
            };
        }

        TurnResult ApplySelfDrawWin(GameAction action)
        {
            int seat = action.SeatIndex;
            if (!State.HasDrawnThisTurn)
                return TurnResult.Fail("自摸只能在剛摸完牌時宣告");
            if (!CanWinWithCurrentHand(seat))
                return TurnResult.Fail($"座位 {seat} 目前的手牌不成胡");

            int winningTile = FindSelfDrawWinningTile(seat);
            if (winningTile == GameState.NoTile)
                return TurnResult.Fail($"座位 {seat} 找不到可作為胡牌張的牌");

            var context = BuildWinContext(seat, winningTile, isSelfDraw: true);
            return EndByWin(seat, context, action);
        }

        // ------------------------------------------------------------
        // 別人打牌後的宣告
        // ------------------------------------------------------------

        /// <summary>某家打牌後，指定座位可以宣告的動作（含「過」）。</summary>
        public List<GameAction> GetClaimActions(int seat)
        {
            var actions = new List<GameAction> { new GameAction { Type = ActionType.Pass, SeatIndex = seat } };
            if (State.Phase != GamePhase.WaitingClaim) return actions;

            int tile = State.LastDiscardTile;
            int discarder = State.LastDiscardFrom;
            if (seat == discarder || tile == GameState.NoTile) return actions;

            var player = State.Players[seat];

            if (CanWinByClaiming(seat, tile))
                actions.Add(new GameAction { Type = ActionType.Win, SeatIndex = seat, Tile = tile });

            if (player.ConcealedCounts[tile] >= 3)
                actions.Add(new GameAction { Type = ActionType.MinKan, SeatIndex = seat, Tile = tile });

            if (player.ConcealedCounts[tile] >= 2)
                actions.Add(new GameAction { Type = ActionType.Pon, SeatIndex = seat, Tile = tile });

            // 吃只能吃上家打出的牌，也就是自己必須是打牌者的下家
            if (GameState.IsNextSeatOf(seat, discarder))
                foreach (int chiBase in FindChiBases(player, tile))
                    actions.Add(new GameAction
                    {
                        Type = ActionType.Chi,
                        SeatIndex = seat,
                        Tile = tile,
                        ChiBaseTile = chiBase
                    });

            return actions;
        }

        /// <summary>
        /// 結算三家的宣告，依優先權決定誰得手：胡 &gt; 碰/槓 &gt; 吃。
        /// 多家同時可胡時，逆時針方向離打牌者近的那家優先。
        /// 沒有人宣告就換下一家摸牌。
        /// </summary>
        public TurnResult ResolveClaims(IEnumerable<GameAction> declarations)
        {
            if (State.Phase != GamePhase.WaitingClaim)
                return TurnResult.Fail($"現在不是宣告階段（目前為 {State.Phase}）");

            var valid = FilterLegalDeclarations(declarations);
            var chosen = PickByPriority(valid);

            if (chosen == null) return PassToNextSeat();

            switch (chosen.Type)
            {
                case ActionType.Win:
                    // 先組情境（放槍時 ConcealedHand 不含胡牌張），再把牌從牌河移進胡牌者手中，
                    // 結算畫面才看得到完整的 17 張，牌數也才守恆。
                    var context = BuildWinContext(chosen.SeatIndex, chosen.Tile, isSelfDraw: false);
                    RemoveClaimedTileFromDiscards();
                    State.Players[chosen.SeatIndex].AddTile(chosen.Tile);
                    return EndByWin(chosen.SeatIndex, context, chosen);

                case ActionType.Pon: return ApplyPon(chosen);
                case ActionType.MinKan: return ApplyOpenKan(chosen);
                case ActionType.Chi: return ApplyChi(chosen);
                default: return PassToNextSeat();
            }
        }

        /// <summary>只保留真正合法的宣告，防止呼叫端送進不合規的動作。</summary>
        List<GameAction> FilterLegalDeclarations(IEnumerable<GameAction> declarations)
        {
            var valid = new List<GameAction>();
            if (declarations == null) return valid;

            foreach (var declared in declarations)
            {
                if (declared == null || declared.Type == ActionType.Pass) continue;
                foreach (var legal in GetClaimActions(declared.SeatIndex))
                {
                    if (legal.Type != declared.Type || legal.Tile != declared.Tile) continue;
                    if (declared.Type == ActionType.Chi && legal.ChiBaseTile != declared.ChiBaseTile) continue;
                    valid.Add(declared);
                    break;
                }
            }
            return valid;
        }

        /// <summary>胡 &gt; 碰/槓 &gt; 吃；同為胡時取逆時針離打牌者最近的一家。</summary>
        GameAction PickByPriority(List<GameAction> candidates)
        {
            GameAction best = null;
            int bestRank = int.MaxValue;
            int bestDistance = int.MaxValue;

            foreach (var candidate in candidates)
            {
                int rank = PriorityRank(candidate.Type);
                int distance = GameState.SeatDistance(State.LastDiscardFrom, candidate.SeatIndex);
                if (rank > bestRank) continue;
                if (rank == bestRank && distance >= bestDistance) continue;
                best = candidate;
                bestRank = rank;
                bestDistance = distance;
            }
            return best;
        }

        static int PriorityRank(ActionType type)
        {
            switch (type)
            {
                case ActionType.Win: return 0;
                case ActionType.MinKan:
                case ActionType.Pon: return 1;
                case ActionType.Chi: return 2;
                default: return int.MaxValue;
            }
        }

        TurnResult PassToNextSeat()
        {
            State.CurrentPlayer = GameState.NextSeat(State.LastDiscardFrom);
            State.Phase = GamePhase.WaitingDraw;
            State.HasDrawnThisTurn = false;
            return new TurnResult();
        }

        TurnResult ApplyPon(GameAction action)
        {
            var player = State.Players[action.SeatIndex];
            player.RemoveTile(action.Tile);
            player.RemoveTile(action.Tile);
            player.Melds.Add(new Meld
            {
                Type = MeldType.Pon,
                BaseTile = action.Tile,
                FromPlayer = State.LastDiscardFrom,
                ClaimedTile = action.Tile
            });
            return HandOverTurnTo(action, drawReplacement: false);
        }

        TurnResult ApplyOpenKan(GameAction action)
        {
            var player = State.Players[action.SeatIndex];
            for (int i = 0; i < 3; i++) player.RemoveTile(action.Tile);
            player.Melds.Add(new Meld
            {
                Type = MeldType.MinKan,
                BaseTile = action.Tile,
                FromPlayer = State.LastDiscardFrom,
                ClaimedTile = action.Tile
            });
            return HandOverTurnTo(action, drawReplacement: true);
        }

        TurnResult ApplyChi(GameAction action)
        {
            var player = State.Players[action.SeatIndex];
            for (int offset = 0; offset < 3; offset++)
            {
                int tile = action.ChiBaseTile + offset;
                if (tile != action.Tile) player.RemoveTile(tile);
            }
            player.Melds.Add(new Meld
            {
                Type = MeldType.Chi,
                BaseTile = action.ChiBaseTile,
                FromPlayer = State.LastDiscardFrom,
                ClaimedTile = action.Tile
            });
            return HandOverTurnTo(action, drawReplacement: false);
        }

        /// <summary>吃碰槓成立後，牌從打牌者的牌河拿走，接著由宣告者出牌。</summary>
        TurnResult HandOverTurnTo(GameAction action, bool drawReplacement)
        {
            RemoveClaimedTileFromDiscards();
            State.CurrentPlayer = action.SeatIndex;
            State.LastDiscardTile = GameState.NoTile;
            State.HasDrawnThisTurn = false;
            State.AwaitingKanReplacement = false;
            State.Phase = GamePhase.WaitingDiscard;

            return drawReplacement ? DrawKanReplacement(action) : new TurnResult { Applied = action };
        }

        /// <summary>被吃碰槓走的牌不留在牌河裡</summary>
        void RemoveClaimedTileFromDiscards()
        {
            var discarder = State.Players[State.LastDiscardFrom];
            int lastIndex = discarder.Discards.Count - 1;
            if (lastIndex >= 0 && discarder.Discards[lastIndex] == State.LastDiscardTile)
                discarder.Discards.RemoveAt(lastIndex);
        }

        // ------------------------------------------------------------
        // 結束
        // ------------------------------------------------------------

        TurnResult EndByWin(int seat, WinContext context, GameAction action)
        {
            State.Phase = GamePhase.Ended;
            State.EndReason = GameEndReason.Win;
            return new TurnResult
            {
                Applied = action,
                EndReason = GameEndReason.Win,
                WinnerSeat = seat,
                Score = ScoreCalculator.Calculate(context, FanTable)
            };
        }

        TurnResult EndByExhaustion()
        {
            State.Phase = GamePhase.Ended;
            State.EndReason = GameEndReason.Exhausted;
            return new TurnResult { EndReason = GameEndReason.Exhausted };
        }

        // ------------------------------------------------------------
        // 判定用的小工具
        // ------------------------------------------------------------

        bool CanWinWithCurrentHand(int seat)
        {
            var player = State.Players[seat];
            return WinChecker.CanWin(player.ConcealedCounts, player.Melds.Count);
        }

        bool CanWinByClaiming(int seat, int tile)
        {
            var player = State.Players[seat];
            if (player.ConcealedCounts[tile] >= WinChecker.TILES_PER_KIND) return false;

            player.ConcealedCounts[tile]++;
            bool canWin = WinChecker.CanWin(player.ConcealedCounts, player.Melds.Count);
            player.ConcealedCounts[tile]--;
            return canWin;
        }

        /// <summary>
        /// 自摸時要知道「哪一張是胡牌張」，因為台數表要靠它判斷獨聽與明暗刻。
        /// 手上任一張抽掉之後仍聽該張，它就可以當胡牌張。
        /// </summary>
        int FindSelfDrawWinningTile(int seat)
        {
            var player = State.Players[seat];
            for (int tile = 0; tile < TileDef.KINDS; tile++)
            {
                if (player.ConcealedCounts[tile] == 0) continue;

                player.ConcealedCounts[tile]--;
                bool stillWaiting = WinChecker.GetWaits(player.ConcealedCounts, player.Melds.Count).Contains(tile);
                player.ConcealedCounts[tile]++;
                if (stillWaiting) return tile;
            }
            return GameState.NoTile;
        }

        /// <summary>組出給 ScoreCalculator 的情境。ConcealedHand 一律不含胡牌張。</summary>
        WinContext BuildWinContext(int seat, int winningTile, bool isSelfDraw)
        {
            var player = State.Players[seat];
            var concealed = (int[])player.ConcealedCounts.Clone();
            if (isSelfDraw) concealed[winningTile]--;   // 自摸時該張已在手上，先扣掉

            return new WinContext
            {
                ConcealedHand = concealed,
                Melds = new List<Meld>(player.Melds),
                Flowers = new List<int>(player.Flowers),
                WinningTile = winningTile,
                IsSelfDraw = isSelfDraw,
                IsDealer = seat == State.DealerIndex,
                DealerStreak = State.DealerStreak,
                SeatWind = player.SeatWind,
                RoundWind = State.RoundWind,
                AfterKan = isSelfDraw && State.AwaitingKanReplacement,
                LastTile = isSelfDraw && State.IsWallExhausted,
                // 正花是看門風而不是座位編號：莊家為東時對應春與梅
                SeatIndex = player.SeatWind - TileDef.EAST
            };
        }

        static Meld FindPonMeld(PlayerState player, int tile)
            => player.Melds.Find(m => m.Type == MeldType.Pon && m.BaseTile == tile);

        /// <summary>這張牌能組成哪幾種順子。回傳每種順子的最小張。</summary>
        static List<int> FindChiBases(PlayerState player, int tile)
        {
            var bases = new List<int>();
            if (TileDef.IsHonor(tile) || TileDef.IsFlower(tile)) return bases;

            int rank = TileDef.GetRank(tile);
            var counts = player.ConcealedCounts;

            // 這張當順子的最大張，例如手上 45 吃 6
            if (rank >= 3 && counts[tile - 2] > 0 && counts[tile - 1] > 0) bases.Add(tile - 2);
            // 這張當中間張，例如手上 46 吃 5
            if (rank >= 2 && rank <= 8 && counts[tile - 1] > 0 && counts[tile + 1] > 0) bases.Add(tile - 1);
            // 這張當最小張，例如手上 56 吃 4
            if (rank <= 7 && counts[tile + 1] > 0 && counts[tile + 2] > 0) bases.Add(tile);

            return bases;
        }
    }
}
