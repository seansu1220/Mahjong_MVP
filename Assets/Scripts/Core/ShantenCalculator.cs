using System;

namespace Mahjong
{
    // ============================================================
    // 向聽數
    //
    // 「還要換幾張牌才會聽牌」。這是 AI 判斷該打哪張的依據。
    //   -1 = 已經成胡
    //    0 = 聽牌
    //    1 = 再換一張就聽牌
    //
    // 算法：把手牌盡量拆成「面子」與「搭子」，
    //   面子 = 已完成的順子或刻子，一組抵 2 分
    //   搭子 = 差一張就成面子的兩張牌（對子、兩面、嵌張），一組抵 1 分
    //   向聽數 = 2 × 還需面子數 − 2 × 面子數 − 搭子數
    // 另外整副牌只需要一組將，所以面子加搭子最多算到「還需面子數 + 1」組；
    // 若這些組合裡完全沒有對子，得再拆一組出來當將，向聽數 +1。
    //
    // 純函式：傳入的計數陣列不會被改動。
    // ============================================================

    public static class ShantenCalculator
    {
        /// <summary>已經成胡</summary>
        public const int Winning = -1;

        /// <summary>聽牌</summary>
        public const int Tenpai = 0;

        /// <summary>整副牌需要的面子數（台灣 16 張為 5 組面子 + 1 對將）</summary>
        public const int TotalSets = 5;

        /// <summary>
        /// 計算向聽數。
        /// </summary>
        /// <param name="concealedCounts">手中未副露的牌，長度 34 的計數陣列</param>
        /// <param name="meldCount">已副露組數</param>
        public static int Calculate(int[] concealedCounts, int meldCount)
        {
            if (concealedCounts == null)
                throw new ArgumentNullException(nameof(concealedCounts), "手牌計數陣列不可為 null");
            if (concealedCounts.Length != TileDef.KINDS)
                throw new ArgumentException(
                    $"手牌計數陣列長度必須為 {TileDef.KINDS}，實際為 {concealedCounts.Length}",
                    nameof(concealedCounts));

            int needSets = TotalSets - meldCount;
            if (needSets < 0)
                throw new ArgumentOutOfRangeException(nameof(meldCount),
                    $"副露組數不可超過 {TotalSets}，實際為 {meldCount}");

            var context = new SearchContext
            {
                Counts = (int[])concealedCounts.Clone(),
                NeedSets = needSets,
                Best = int.MaxValue
            };
            SearchSets(context, 0, 0);
            return context.Best;
        }

        /// <summary>是否已聽牌（向聽數為 0）</summary>
        public static bool IsTenpai(int[] concealedCounts, int meldCount)
            => Calculate(concealedCounts, meldCount) == Tenpai;

        // ------------------------------------------------------------

        sealed class SearchContext
        {
            public int[] Counts;
            public int NeedSets;
            public int Best;
        }

        /// <summary>先窮舉所有「拆出幾組完整面子」的方式</summary>
        static void SearchSets(SearchContext context, int index, int sets)
        {
            if (sets == context.NeedSets || index >= TileDef.KINDS)
            {
                SearchPartials(context, 0, sets, 0, hasPair: false);
                return;
            }

            var counts = context.Counts;
            if (counts[index] == 0)
            {
                SearchSets(context, index + 1, sets);
                return;
            }

            if (counts[index] >= 3)
            {
                counts[index] -= 3;
                SearchSets(context, index, sets + 1);
                counts[index] += 3;
            }

            if (CanFormRun(counts, index))
            {
                counts[index]--; counts[index + 1]--; counts[index + 2]--;
                SearchSets(context, index, sets + 1);
                counts[index]++; counts[index + 1]++; counts[index + 2]++;
            }

            SearchSets(context, index + 1, sets);
        }

        /// <summary>面子拆完後，把剩下的牌盡量湊成搭子</summary>
        static void SearchPartials(SearchContext context, int index, int sets, int partials, bool hasPair)
        {
            int freeBlocks = context.NeedSets + 1 - sets - partials;
            if (freeBlocks <= 0 || index >= TileDef.KINDS)
            {
                Evaluate(context, sets, partials, hasPair);
                return;
            }

            // 就算剩下的組合全部湊成搭子也贏不過目前最佳解，就不必再往下找
            if (2 * context.NeedSets - 2 * sets - partials - freeBlocks >= context.Best) return;

            var counts = context.Counts;
            if (counts[index] == 0)
            {
                SearchPartials(context, index + 1, sets, partials, hasPair);
                return;
            }

            if (counts[index] >= 2)   // 對子
            {
                counts[index] -= 2;
                SearchPartials(context, index, sets, partials + 1, hasPair: true);
                counts[index] += 2;
            }

            if (!TileDef.IsHonor(index))
            {
                int rank = TileDef.GetRank(index);

                if (rank <= 8 && counts[index + 1] > 0)   // 兩面或邊張，例如 34 或 89
                {
                    counts[index]--; counts[index + 1]--;
                    SearchPartials(context, index, sets, partials + 1, hasPair);
                    counts[index]++; counts[index + 1]++;
                }

                if (rank <= 7 && counts[index + 2] > 0)   // 嵌張，例如 35
                {
                    counts[index]--; counts[index + 2]--;
                    SearchPartials(context, index, sets, partials + 1, hasPair);
                    counts[index]++; counts[index + 2]++;
                }
            }

            SearchPartials(context, index + 1, sets, partials, hasPair);
        }

        static void Evaluate(SearchContext context, int sets, int partials, bool hasPair)
        {
            int shanten = 2 * context.NeedSets - 2 * sets - partials;

            // 面子與搭子已經佔滿所有組合，卻沒有任何一對可以當將，
            // 表示得拆掉一組重湊，多花一步。
            if (!hasPair && sets + partials == context.NeedSets + 1) shanten++;

            if (shanten < context.Best) context.Best = shanten;
        }

        static bool CanFormRun(int[] counts, int index)
            => !TileDef.IsHonor(index)
               && TileDef.GetRank(index) <= 7
               && counts[index + 1] > 0
               && counts[index + 2] > 0;
    }
}
