using System;
using System.Collections.Generic;
using System.Text;

namespace Mahjong
{
    // ============================================================
    // 牌的定義
    // 編碼方式：用 int 表示牌種，方便用 int[34] 計數陣列做判定
    //   0  ~ 8  : 一萬 ~ 九萬
    //   9  ~ 17 : 一筒 ~ 九筒
    //   18 ~ 26 : 一條 ~ 九條
    //   27 ~ 33 : 東 南 西 北 中 發 白
    //   34 ~ 41 : 春 夏 秋 冬 梅 蘭 竹 菊（花牌，不進計數陣列）
    // ============================================================

    public enum Suit { Man = 0, Pin = 1, Sou = 2, Honor = 3, Flower = 4 }

    public static class TileDef
    {
        public const int KINDS = 34;        // 計數陣列長度（不含花）
        public const int FLOWER_BASE = 34;  // 花牌起始 id
        public const int FLOWER_COUNT = 8;

        // 字牌 id
        public const int EAST = 27, SOUTH = 28, WEST = 29, NORTH = 30;
        public const int RED = 31, GREEN = 32, WHITE = 33;

        public static Suit GetSuit(int id)
        {
            if (id >= FLOWER_BASE) return Suit.Flower;
            if (id >= 27) return Suit.Honor;
            return (Suit)(id / 9);
        }

        /// <summary>數牌回傳 1~9；字牌回傳 1~7；花牌回傳 1~8</summary>
        public static int GetRank(int id)
        {
            if (id >= FLOWER_BASE) return id - FLOWER_BASE + 1;
            if (id >= 27) return id - 27 + 1;
            return id % 9 + 1;
        }

        public static bool IsHonor(int id) => id >= 27 && id <= 33;
        public static bool IsFlower(int id) => id >= FLOWER_BASE;
        public static bool IsWind(int id) => id >= EAST && id <= NORTH;
        public static bool IsDragon(int id) => id >= RED && id <= WHITE;
        public static bool IsSimple(int id) => id < 27;

        /// <summary>么九牌：數牌的 1 和 9，以及所有字牌</summary>
        public static bool IsTerminalOrHonor(int id)
        {
            if (IsHonor(id)) return true;
            if (IsFlower(id)) return false;
            int r = GetRank(id);
            return r == 1 || r == 9;
        }

        static readonly string[] HonorNames = { "東", "南", "西", "北", "中", "發", "白" };
        static readonly string[] FlowerNames = { "春", "夏", "秋", "冬", "梅", "蘭", "竹", "菊" };
        static readonly string[] SuitNames = { "萬", "筒", "條" };

        public static string Name(int id)
        {
            if (IsFlower(id)) return FlowerNames[id - FLOWER_BASE];
            if (IsHonor(id)) return HonorNames[id - 27];
            return GetRank(id) + SuitNames[id / 9];
        }

        public static string HandToString(int[] counts)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < KINDS; i++)
                for (int c = 0; c < counts[i]; c++)
                    sb.Append(Name(i)).Append(' ');
            return sb.ToString().TrimEnd();
        }
    }

    // ============================================================
    // 副露（吃碰槓）
    // ============================================================

    public enum MeldType { Chi, Pon, MinKan, AnKan }  // 吃、碰、明槓、暗槓

    public class Meld
    {
        public MeldType Type;
        public int BaseTile;      // Chi 為順子最小張；其餘為該牌 id
        public int FromPlayer;    // 來源玩家 index；暗槓為自己

        /// <summary>
        /// 從別人手上叫過來的那一張。暗槓與自己加槓沒有這張牌，為 -1。
        /// 畫面要把它擺在明顯的位置，讓人看得出這組是吃誰的、碰誰的。
        /// </summary>
        public int ClaimedTile = -1;

        public bool IsConcealed => Type == MeldType.AnKan;

        /// <summary>展開成實際牌張</summary>
        public int[] Tiles()
        {
            switch (Type)
            {
                case MeldType.Chi:
                    return new[] { BaseTile, BaseTile + 1, BaseTile + 2 };
                case MeldType.Pon:
                    return new[] { BaseTile, BaseTile, BaseTile };
                default:
                    return new[] { BaseTile, BaseTile, BaseTile, BaseTile };
            }
        }

        /// <summary>算牌型時，槓視為刻子</summary>
        public bool IsTriplet => Type != MeldType.Chi;

        /// <summary>深拷貝。GameState.Clone 用，避免 AI 模擬改動到真實局面。</summary>
        public Meld Clone()
            => new Meld
            {
                Type = Type,
                BaseTile = BaseTile,
                FromPlayer = FromPlayer,
                ClaimedTile = ClaimedTile
            };

        public override string ToString()
        {
            string t = Type == MeldType.Chi ? "吃" : Type == MeldType.Pon ? "碰"
                     : Type == MeldType.MinKan ? "明槓" : "暗槓";
            return t + TileDef.Name(BaseTile);
        }
    }

    // ============================================================
    // 胡牌判定
    // 台灣 16 張：5 組面子 + 1 對將
    // hand 為「手中未副露的牌」計數，meldCount 為已副露組數
    // ============================================================

    public static class WinChecker
    {
        /// <summary>
        /// 判斷是否成牌。hand 必須是 3n+2 張（n = 5 - meldCount）。
        /// 注意：此方法會暫時改動 hand，但結束時會還原。
        /// </summary>
        public static bool CanWin(int[] hand, int meldCount)
        {
            ValidateCountArray(hand, nameof(hand));

            int needSets = 5 - meldCount;
            if (needSets < 0) return false;

            int total = 0;
            for (int i = 0; i < TileDef.KINDS; i++) total += hand[i];
            if (total != needSets * 3 + 2) return false;

            for (int i = 0; i < TileDef.KINDS; i++)
            {
                if (hand[i] < 2) continue;
                hand[i] -= 2;
                bool ok = Decompose(hand, needSets);
                hand[i] += 2;
                if (ok) return true;
            }
            return false;
        }

        /// <summary>把剩餘牌拆成 need 組面子（順子或刻子）</summary>
        static bool Decompose(int[] hand, int need)
        {
            if (need == 0)
            {
                for (int i = 0; i < TileDef.KINDS; i++)
                    if (hand[i] != 0) return false;
                return true;
            }

            int t = -1;
            for (int i = 0; i < TileDef.KINDS; i++)
                if (hand[i] > 0) { t = i; break; }
            if (t < 0) return false;

            // 嘗試刻子
            if (hand[t] >= 3)
            {
                hand[t] -= 3;
                bool ok = Decompose(hand, need - 1);
                hand[t] += 3;
                if (ok) return true;
            }

            // 嘗試順子（字牌不可、rank 需 <= 7）
            if (!TileDef.IsHonor(t) && TileDef.GetRank(t) <= 7
                && hand[t + 1] > 0 && hand[t + 2] > 0)
            {
                hand[t]--; hand[t + 1]--; hand[t + 2]--;
                bool ok = Decompose(hand, need - 1);
                hand[t]++; hand[t + 1]++; hand[t + 2]++;
                if (ok) return true;
            }

            return false;
        }

        /// <summary>
        /// 取得所有拆解方式（算台數時需要，例如平胡、碰碰胡、獨聽都依賴拆解結果）
        /// </summary>
        public static List<HandPattern> AllPatterns(int[] hand, int meldCount)
        {
            ValidateCountArray(hand, nameof(hand));

            var results = new List<HandPattern>();
            int needSets = 5 - meldCount;
            if (needSets < 0) return results;

            // 與 CanWin 採相同的張數前提：3n+2。張數不符不是錯誤，只是不可能成牌。
            int total = 0;
            for (int i = 0; i < TileDef.KINDS; i++) total += hand[i];
            if (total != needSets * 3 + 2) return results;

            for (int i = 0; i < TileDef.KINDS; i++)
            {
                if (hand[i] < 2) continue;
                hand[i] -= 2;
                var sets = new List<SetInfo>();
                CollectSets(hand, needSets, sets, results, i);
                hand[i] += 2;
            }
            return results;
        }

        static void CollectSets(int[] hand, int need, List<SetInfo> current,
                                List<HandPattern> output, int pairTile)
        {
            if (need == 0)
            {
                for (int i = 0; i < TileDef.KINDS; i++)
                    if (hand[i] != 0) return;
                output.Add(new HandPattern
                {
                    PairTile = pairTile,
                    Sets = new List<SetInfo>(current)
                });
                return;
            }

            int t = -1;
            for (int i = 0; i < TileDef.KINDS; i++)
                if (hand[i] > 0) { t = i; break; }
            if (t < 0) return;

            if (hand[t] >= 3)
            {
                hand[t] -= 3;
                current.Add(new SetInfo { IsTriplet = true, BaseTile = t, Concealed = true });
                CollectSets(hand, need - 1, current, output, pairTile);
                current.RemoveAt(current.Count - 1);
                hand[t] += 3;
            }

            if (!TileDef.IsHonor(t) && TileDef.GetRank(t) <= 7
                && hand[t + 1] > 0 && hand[t + 2] > 0)
            {
                hand[t]--; hand[t + 1]--; hand[t + 2]--;
                current.Add(new SetInfo { IsTriplet = false, BaseTile = t, Concealed = true });
                CollectSets(hand, need - 1, current, output, pairTile);
                current.RemoveAt(current.Count - 1);
                hand[t]++; hand[t + 1]++; hand[t + 2]++;
            }
        }

        /// <summary>
        /// 聽牌計算：hand 為 3n+1 張，回傳所有可胡的牌 id
        /// </summary>
        public static List<int> GetWaits(int[] hand, int meldCount)
        {
            ValidateCountArray(hand, nameof(hand));

            var waits = new List<int>();
            for (int i = 0; i < TileDef.KINDS; i++)
            {
                if (hand[i] >= 4) continue;   // 四張都在自己手上，不可能再摸到
                hand[i]++;
                if (CanWin(hand, meldCount)) waits.Add(i);
                hand[i]--;
            }
            return waits;
        }

        /// <summary>
        /// 聽牌計算（扣除已知牌張版本）。
        /// 上面的多載只看手中張數，會把「世上已經沒有第 5 張」的牌也列為聽張，
        /// 例如自己槓掉四張九筒後仍聽九筒，那是死聽，永遠不會進張。
        /// 這個多載把自己的副露與場上可見牌（牌河、他家副露）一併扣掉。
        /// </summary>
        /// <param name="hand">手中未副露的牌，長度 34 的計數陣列</param>
        /// <param name="melds">自己的副露，null 視為門清</param>
        /// <param name="visibleCounts">場上已可見的牌張數，長度 34；可傳 null</param>
        public static List<int> GetWaits(int[] hand, List<Meld> melds, int[] visibleCounts = null)
        {
            ValidateCountArray(hand, nameof(hand));
            if (visibleCounts != null) ValidateCountArray(visibleCounts, nameof(visibleCounts));

            var usedInMelds = new int[TileDef.KINDS];
            if (melds != null)
                foreach (var meld in melds)
                    foreach (var tile in meld.Tiles())
                        if (tile < TileDef.KINDS) usedInMelds[tile]++;

            var reachableWaits = new List<int>();
            foreach (int candidate in GetWaits(hand, melds == null ? 0 : melds.Count))
            {
                int alreadyAccountedFor = hand[candidate] + usedInMelds[candidate]
                                        + (visibleCounts == null ? 0 : visibleCounts[candidate]);
                if (alreadyAccountedFor < TILES_PER_KIND) reachableWaits.Add(candidate);
            }
            return reachableWaits;
        }

        public static bool IsTenpai(int[] hand, int meldCount)
            => GetWaits(hand, meldCount).Count > 0;

        /// <summary>每種牌的張數（花牌除外）</summary>
        public const int TILES_PER_KIND = 4;

        static void ValidateCountArray(int[] counts, string paramName)
        {
            if (counts == null)
                throw new ArgumentNullException(paramName, "計數陣列不可為 null");
            if (counts.Length != TileDef.KINDS)
                throw new ArgumentException(
                    $"計數陣列長度必須為 {TileDef.KINDS}（不含花牌），實際為 {counts.Length}", paramName);
        }
    }

    public class SetInfo
    {
        public bool IsTriplet;   // true = 刻子/槓，false = 順子
        public int BaseTile;     // 順子取最小張
        public bool Concealed;   // 是否為暗（手中組成）
    }

    public class HandPattern
    {
        public int PairTile;
        public List<SetInfo> Sets;
    }

    // ============================================================
    // 牌山
    // ============================================================

    public class Wall
    {
        // 隨機開局時用來產生種子。WebGL 為單執行緒，此靜態狀態不會有競爭問題。
        static readonly Random SeedSource = new Random();

        readonly List<int> tiles = new List<int>();
        readonly Random rng;
        int drawIndex = 0;   // 下一張從牌頭摸的位置
        int tailIndex;       // 下一張從牌尾補的位置

        /// <summary>
        /// 本副牌實際使用的亂數種子。
        /// 即使建構時傳 0（隨機開局），這裡也會記下真正用到的種子，
        /// 之後把它傳回建構子即可完整重現同一副牌——除錯與向客戶重演牌局都靠它。
        /// </summary>
        public int Seed { get; private set; }

        /// <param name="seed">0 代表隨機開局；非 0 則固定牌山，可重現。</param>
        public Wall(int seed = 0, bool includeFlowers = true)
        {
            Seed = seed != 0 ? seed : SeedSource.Next(1, int.MaxValue);
            rng = new Random(Seed);

            for (int i = 0; i < TileDef.KINDS; i++)
                for (int c = 0; c < WinChecker.TILES_PER_KIND; c++)
                    tiles.Add(i);

            if (includeFlowers)
                for (int i = 0; i < TileDef.FLOWER_COUNT; i++)
                    tiles.Add(TileDef.FLOWER_BASE + i);

            Shuffle();
            tailIndex = tiles.Count - 1;
        }

        void Shuffle()
        {
            for (int i = tiles.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (tiles[i], tiles[j]) = (tiles[j], tiles[i]);
            }
        }

        public int Remaining => tailIndex - drawIndex + 1;
        public bool IsEmpty => Remaining <= 0;

        /// <summary>已經從牌頭摸走幾張（正常摸牌）</summary>
        public int DrawnFromHead => drawIndex;

        /// <summary>已經從牌尾補走幾張（補花與槓後補牌）</summary>
        public int DrawnFromTail => tiles.Count - 1 - tailIndex;

        /// <summary>整副牌山原本有幾張</summary>
        public int TotalTiles => tiles.Count;

        /// <summary>從牌頭正常摸牌</summary>
        public int Draw()
        {
            if (IsEmpty) throw new InvalidOperationException("牌山已空，無法摸牌");
            return tiles[drawIndex++];
        }

        /// <summary>
        /// 從牌尾補牌。台灣麻將的補花與槓後補牌都從牌山尾端取，
        /// 不能用 Draw()，否則摸牌順序會錯亂。
        /// </summary>
        public int DrawFromTail()
        {
            if (IsEmpty) throw new InvalidOperationException("牌山已空，無法補牌");
            return tiles[tailIndex--];
        }
    }
}
