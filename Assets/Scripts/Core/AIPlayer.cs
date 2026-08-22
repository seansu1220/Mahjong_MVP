using System;
using System.Collections.Generic;

namespace Mahjong
{
    // ============================================================
    // 電腦玩家
    //
    // 原型階段的目標是「打起來像個人」，不是打得強：
    //   出牌   —— 算出每張打掉之後的向聽數，選最能推進牌型的那張
    //   吃碰槓 —— 只有真的能讓向聽數變好才叫，不會為了叫而叫
    //   胡     —— 一律胡，不做見逃
    //
    // 目前不做防守（不看牌河判斷別人聽什麼），那屬於付費階段項目。
    //
    // 一律在複製的計數陣列上試算，不會改動真實局面。
    // ============================================================

    public class AIPlayer
    {
        /// <summary>叫牌至少要讓向聽數進步這麼多才值得。設 1 表示不做沒賺頭的吃碰。</summary>
        public int MinimumClaimGain = 1;

        readonly int seatIndex;
        readonly Random rng;

        /// <param name="seatIndex">這個 AI 坐哪一家</param>
        /// <param name="seed">打散同分選擇用。同樣的 seed 會有同樣的打法，方便重現。</param>
        public AIPlayer(int seatIndex, int seed = 0)
        {
            if (seatIndex < 0 || seatIndex >= GameState.PlayerCount)
                throw new ArgumentOutOfRangeException(nameof(seatIndex),
                    $"座位必須是 0~{GameState.PlayerCount - 1}，實際為 {seatIndex}");

            this.seatIndex = seatIndex;
            rng = new Random(seed == 0 ? seatIndex + 1 : seed);
        }

        public int SeatIndex => seatIndex;

        // ------------------------------------------------------------
        // 輪到自己：打牌、暗槓、加槓或自摸
        // ------------------------------------------------------------

        public GameAction ChooseTurnAction(GameState state, IReadOnlyList<GameAction> options)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (options == null || options.Count == 0) return null;

            var win = FindAction(options, ActionType.Win);
            if (win != null) return win;   // 能自摸就自摸

            var player = state.Players[seatIndex];
            var hand = (int[])player.ConcealedCounts.Clone();
            int meldCount = player.Melds.Count;

            var bestDiscard = ChooseBestDiscard(options, hand, meldCount, player, state,
                                                out int bestDiscardShanten);

            var kan = ChooseWorthwhileKan(options, hand, meldCount, bestDiscardShanten);
            return kan ?? bestDiscard;
        }

        /// <summary>逐一試打每張候選牌，取打完之後向聽數最小的那張。</summary>
        GameAction ChooseBestDiscard(IReadOnlyList<GameAction> options, int[] hand, int meldCount,
                                     PlayerState player, GameState state, out int bestShanten)
        {
            GameAction best = null;
            bestShanten = int.MaxValue;
            int bestPriority = int.MinValue;

            foreach (var option in options)
            {
                if (option.Type != ActionType.Discard || hand[option.Tile] == 0) continue;

                hand[option.Tile]--;
                int shanten = ShantenCalculator.Calculate(hand, meldCount);
                hand[option.Tile]++;

                int priority = DiscardPriority(hand, option.Tile, player, state);
                if (shanten > bestShanten) continue;
                if (shanten == bestShanten && priority <= bestPriority) continue;

                best = option;
                bestShanten = shanten;
                bestPriority = priority;
            }
            return best;
        }

        /// <summary>槓不能讓牌型變差。划算就槓，多摸一張還可能多幾台。</summary>
        GameAction ChooseWorthwhileKan(IReadOnlyList<GameAction> options, int[] hand,
                                       int meldCount, int bestDiscardShanten)
        {
            foreach (var option in options)
            {
                int shantenAfterKan;

                if (option.Type == ActionType.AnKan)
                {
                    if (hand[option.Tile] < WinChecker.TILES_PER_KIND) continue;
                    hand[option.Tile] -= WinChecker.TILES_PER_KIND;
                    shantenAfterKan = ShantenCalculator.Calculate(hand, meldCount + 1);
                    hand[option.Tile] += WinChecker.TILES_PER_KIND;
                }
                else if (option.Type == ActionType.AddKan)
                {
                    // 加槓只是把手上那張併進已經碰好的那組，副露組數不變
                    if (hand[option.Tile] == 0) continue;
                    hand[option.Tile]--;
                    shantenAfterKan = ShantenCalculator.Calculate(hand, meldCount);
                    hand[option.Tile]++;
                }
                else continue;

                if (shantenAfterKan <= bestDiscardShanten) return option;
            }
            return null;
        }

        // ------------------------------------------------------------
        // 別人打牌：要不要吃碰槓胡
        // ------------------------------------------------------------

        public GameAction ChooseClaimAction(GameState state, IReadOnlyList<GameAction> options)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (options == null || options.Count == 0) return null;

            var win = FindAction(options, ActionType.Win);
            if (win != null) return win;   // 能胡就胡，不見逃

            var player = state.Players[seatIndex];
            var hand = (int[])player.ConcealedCounts.Clone();
            int meldCount = player.Melds.Count;
            int currentShanten = ShantenCalculator.Calculate(hand, meldCount);

            GameAction best = null;
            int bestShanten = int.MaxValue;

            foreach (var option in options)
            {
                if (!TryApplyClaim(hand, option, out int tilesRemoved)) continue;

                // 吃碰之後手上會多一組副露，且必須打掉一張，所以要看打完之後的向聽數
                int shanten = option.Type == ActionType.MinKan
                    ? ShantenCalculator.Calculate(hand, meldCount + 1)
                    : BestShantenAfterDiscard(hand, meldCount + 1);

                UndoClaim(hand, option, tilesRemoved);

                if (shanten < bestShanten)
                {
                    bestShanten = shanten;
                    best = option;
                }
            }

            bool worthwhile = best != null && currentShanten - bestShanten >= MinimumClaimGain;
            return worthwhile ? best : FindAction(options, ActionType.Pass);
        }

        /// <summary>在計數陣列上試著吃碰槓。成功回傳 true，並記下拿掉幾張以便還原。</summary>
        static bool TryApplyClaim(int[] hand, GameAction option, out int tilesRemoved)
        {
            tilesRemoved = 0;
            switch (option.Type)
            {
                case ActionType.Pon:
                    if (hand[option.Tile] < 2) return false;
                    hand[option.Tile] -= 2;
                    tilesRemoved = 2;
                    return true;

                case ActionType.MinKan:
                    if (hand[option.Tile] < 3) return false;
                    hand[option.Tile] -= 3;
                    tilesRemoved = 3;
                    return true;

                case ActionType.Chi:
                    for (int offset = 0; offset < 3; offset++)
                    {
                        int tile = option.ChiBaseTile + offset;
                        if (tile == option.Tile) continue;
                        if (hand[tile] == 0) { UndoChi(hand, option, offset); return false; }
                        hand[tile]--;
                        tilesRemoved++;
                    }
                    return true;

                default: return false;
            }
        }

        static void UndoClaim(int[] hand, GameAction option, int tilesRemoved)
        {
            if (option.Type == ActionType.Chi) UndoChi(hand, option, 3);
            else hand[option.Tile] += tilesRemoved;
        }

        /// <summary>把吃已經扣掉的牌加回去。upToOffset 為當初處理到第幾個位置。</summary>
        static void UndoChi(int[] hand, GameAction option, int upToOffset)
        {
            for (int offset = 0; offset < upToOffset; offset++)
            {
                int tile = option.ChiBaseTile + offset;
                if (tile != option.Tile) hand[tile]++;
            }
        }

        /// <summary>手上是 3n+2 張時，打掉最好的那張之後能到幾向聽。</summary>
        static int BestShantenAfterDiscard(int[] hand, int meldCount)
        {
            int best = int.MaxValue;
            for (int tile = 0; tile < TileDef.KINDS; tile++)
            {
                if (hand[tile] == 0) continue;
                hand[tile]--;
                int shanten = ShantenCalculator.Calculate(hand, meldCount);
                hand[tile]++;
                if (shanten < best) best = shanten;
            }
            return best;
        }

        // ------------------------------------------------------------
        // 同分時打哪張：分數越高越該打
        // ------------------------------------------------------------

        int DiscardPriority(int[] hand, int tile, PlayerState player, GameState state)
        {
            const int PairedHonor = 30;
            const int ValuableHonor = 80;
            const int UselessHonor = 100;
            const int SimpleBase = 60;
            const int NeighbourWeight = 10;

            if (TileDef.IsHonor(tile))
            {
                if (hand[tile] >= 2) return PairedHonor;   // 成對的字牌留著當將或等碰
                bool valuable = TileDef.IsDragon(tile)
                             || tile == player.SeatWind
                             || tile == state.RoundWind;
                return valuable ? ValuableHonor : UselessHonor;
            }

            // 數牌看左右兩格內有沒有同伴，越孤立越該打
            int rank = TileDef.GetRank(tile);
            int neighbours = hand[tile] >= 2 ? 2 : 0;
            for (int offset = -2; offset <= 2; offset++)
            {
                if (offset == 0) continue;
                int neighbourRank = rank + offset;
                if (neighbourRank < 1 || neighbourRank > 9) continue;
                neighbours += hand[tile + offset];
            }

            int distanceFromCentre = Math.Abs(5 - rank);   // 越靠邊用途越少
            return SimpleBase - neighbours * NeighbourWeight + distanceFromCentre + rng.Next(0, 3);
        }

        static GameAction FindAction(IReadOnlyList<GameAction> options, ActionType type)
        {
            foreach (var option in options)
                if (option.Type == type) return option;
            return null;
        }
    }
}
