using System;
using System.Collections.Generic;
using System.Linq;

namespace Mahjong
{
    // ============================================================
    // 台數計算
    //
    // 設計重點：台數表是「資料」不是「程式碼」。
    // 客戶各地規則差異極大（是否算莊、連莊拉莊、獨聽算不算…），
    // 全部寫死在 if-else 裡，改一次規則就要改一次程式。
    // 這裡用 FanRule 清單驅動，客戶給的台數表可直接對應設定。
    // ============================================================

    public enum FanId
    {
        Menqing,        // 門清
        Zimo,           // 自摸
        MenqingZimo,    // 門清自摸（額外加）
        Pinghu,         // 平胡
        Pengpenghu,     // 碰碰胡
        Hunyise,        // 混一色
        Qingyise,       // 清一色
        Ziyise,         // 字一色
        Xiaosanyuan,    // 小三元
        Dasanyuan,      // 大三元
        Xiaosixi,       // 小四喜
        Dasixi,         // 大四喜
        WuAnke,         // 五暗刻
        Dudiao,         // 獨聽
        MenFeng,        // 門風
        QuanFeng,       // 圈風
        SanYuanPai,     // 三元牌（中發白刻子，每組）
        Flower,         // 花牌（每張）
        ZhengHua,       // 正花
        GangShangKaiHua,// 槓上開花
        HaiDiLaoYue,    // 海底撈月
        Zhuang,         // 莊家
        LianZhuang      // 連莊（每連一拉一）
    }

    public class FanRule
    {
        public FanId Id;
        public string Name;
        public int Value;
        public bool Enabled = true;
    }

    /// <summary>台數表。預設為常見台灣 16 張規則，實際專案應由客戶提供並逐條確認。</summary>
    public class FanTable
    {
        public int BaseDi = 100;   // 底
        public int PerTai = 100;   // 每台

        public readonly Dictionary<FanId, FanRule> Rules = new Dictionary<FanId, FanRule>();

        public static FanTable Default()
        {
            var t = new FanTable();
            void Add(FanId id, string name, int v) =>
                t.Rules[id] = new FanRule { Id = id, Name = name, Value = v };

            Add(FanId.Menqing, "門清", 1);
            Add(FanId.Zimo, "自摸", 1);
            Add(FanId.MenqingZimo, "門清自摸加", 1);
            Add(FanId.Pinghu, "平胡", 2);
            Add(FanId.Pengpenghu, "碰碰胡", 4);
            Add(FanId.Hunyise, "混一色", 4);
            Add(FanId.Qingyise, "清一色", 8);
            Add(FanId.Ziyise, "字一色", 16);
            Add(FanId.Xiaosanyuan, "小三元", 4);
            Add(FanId.Dasanyuan, "大三元", 8);
            Add(FanId.Xiaosixi, "小四喜", 8);
            Add(FanId.Dasixi, "大四喜", 16);
            Add(FanId.WuAnke, "五暗刻", 8);
            Add(FanId.Dudiao, "獨聽", 1);
            Add(FanId.MenFeng, "門風", 1);
            Add(FanId.QuanFeng, "圈風", 1);
            Add(FanId.SanYuanPai, "三元牌", 1);
            Add(FanId.Flower, "花牌", 1);
            Add(FanId.ZhengHua, "正花", 1);
            Add(FanId.GangShangKaiHua, "槓上開花", 1);
            Add(FanId.HaiDiLaoYue, "海底撈月", 1);
            Add(FanId.Zhuang, "莊家", 1);
            Add(FanId.LianZhuang, "連莊拉莊", 2);
            return t;
        }

        public int ValueOf(FanId id)
            => Rules.TryGetValue(id, out var r) && r.Enabled ? r.Value : 0;

        public string NameOf(FanId id)
            => Rules.TryGetValue(id, out var r) ? r.Name : id.ToString();
    }

    /// <summary>胡牌當下的情境</summary>
    public class WinContext
    {
        public int[] ConcealedHand;        // 手中牌（不含副露、不含胡的那張）
        public List<Meld> Melds = new List<Meld>();
        public List<int> Flowers = new List<int>();
        public int WinningTile;
        public bool IsSelfDraw;            // 自摸
        public bool IsDealer;              // 是否莊家
        public int DealerStreak;           // 連莊數
        public int SeatWind = TileDef.EAST;// 門風
        public int RoundWind = TileDef.EAST;// 圈風
        public bool AfterKan;              // 槓上開花
        public bool LastTile;              // 海底撈月
        public int SeatIndex;              // 座位 0=東 1=南 2=西 3=北
    }

    public class ScoreResult
    {
        public int TotalTai;
        public int Points;
        public List<(string Name, int Tai)> Items = new List<(string, int)>();

        public override string ToString()
            => string.Join("、", Items.Select(i => $"{i.Name}{i.Tai}台"))
               + $"　共 {TotalTai} 台，{Points} 點";
    }

    public static class ScoreCalculator
    {
        public static ScoreResult Calculate(WinContext ctx, FanTable table)
        {
            var result = new ScoreResult();

            // 組出完整 17 張的計數陣列
            var full = (int[])ctx.ConcealedHand.Clone();
            full[ctx.WinningTile]++;

            int meldCount = ctx.Melds.Count;
            var patterns = WinChecker.AllPatterns(full, meldCount);
            if (patterns.Count == 0) return result;   // 未成牌

            // 多種拆法時取台數最高者（實務常見算法）
            ScoreResult best = null;
            foreach (var p in patterns)
            {
                var r = ScorePattern(p, ctx, table, full);
                if (best == null || r.TotalTai > best.TotalTai) best = r;
            }
            return best;
        }

        static ScoreResult ScorePattern(HandPattern p, WinContext ctx, FanTable t, int[] full)
        {
            var res = new ScoreResult();
            void Add(FanId id, int times = 1)
            {
                int v = t.ValueOf(id) * times;
                if (v > 0) res.Items.Add((t.NameOf(id), v));
                res.TotalTai += v;
            }

            bool concealed = ctx.Melds.All(m => m.IsConcealed);

            // 所有面子（手中 + 副露）
            var allSets = new List<SetInfo>(p.Sets);
            foreach (var m in ctx.Melds)
                allSets.Add(new SetInfo
                {
                    IsTriplet = m.IsTriplet,
                    BaseTile = m.BaseTile,
                    Concealed = m.IsConcealed
                });

            bool allTriplets = allSets.All(s => s.IsTriplet);

            // ---- 基本 ----
            if (concealed) Add(FanId.Menqing);
            if (ctx.IsSelfDraw) Add(FanId.Zimo);
            if (concealed && ctx.IsSelfDraw) Add(FanId.MenqingZimo);
            if (ctx.IsDealer) Add(FanId.Zhuang);
            if (ctx.DealerStreak > 0) Add(FanId.LianZhuang, ctx.DealerStreak * 2 - 1);

            // ---- 牌型 ----
            if (allTriplets) Add(FanId.Pengpenghu);

            // 平胡：全順子、將非字牌、非自摸、兩面聽（各地差異大，此為常見版本）
            if (!allTriplets && allSets.All(s => !s.IsTriplet)
                && !TileDef.IsHonor(p.PairTile) && !ctx.IsSelfDraw)
                Add(FanId.Pinghu);

            // 花色
            var suits = new HashSet<Suit>();
            bool hasHonor = false;
            for (int i = 0; i < TileDef.KINDS; i++)
            {
                if (full[i] == 0) continue;
                if (TileDef.IsHonor(i)) hasHonor = true;
                else suits.Add(TileDef.GetSuit(i));
            }
            foreach (var m in ctx.Melds)
            {
                if (TileDef.IsHonor(m.BaseTile)) hasHonor = true;
                else suits.Add(TileDef.GetSuit(m.BaseTile));
            }

            if (suits.Count == 0 && hasHonor) Add(FanId.Ziyise);
            else if (suits.Count == 1 && !hasHonor) Add(FanId.Qingyise);
            else if (suits.Count == 1 && hasHonor) Add(FanId.Hunyise);

            // ---- 字牌 ----
            int dragonTriplets = allSets.Count(s => s.IsTriplet && TileDef.IsDragon(s.BaseTile));
            bool dragonPair = TileDef.IsDragon(p.PairTile);
            if (dragonTriplets == 3) Add(FanId.Dasanyuan);
            else if (dragonTriplets == 2 && dragonPair) Add(FanId.Xiaosanyuan);
            else if (dragonTriplets > 0) Add(FanId.SanYuanPai, dragonTriplets);

            int windTriplets = allSets.Count(s => s.IsTriplet && TileDef.IsWind(s.BaseTile));
            bool windPair = TileDef.IsWind(p.PairTile);
            if (windTriplets == 4) Add(FanId.Dasixi);
            else if (windTriplets == 3 && windPair) Add(FanId.Xiaosixi);
            else
            {
                if (allSets.Any(s => s.IsTriplet && s.BaseTile == ctx.SeatWind)) Add(FanId.MenFeng);
                if (allSets.Any(s => s.IsTriplet && s.BaseTile == ctx.RoundWind)) Add(FanId.QuanFeng);
            }

            // ---- 暗刻 ----
            int concealedTriplets = allSets.Count(s => s.IsTriplet && s.Concealed);
            if (concealedTriplets >= 5) Add(FanId.WuAnke);

            // ---- 情境 ----
            if (ctx.AfterKan) Add(FanId.GangShangKaiHua);
            if (ctx.LastTile) Add(FanId.HaiDiLaoYue);

            // 獨聽：胡牌前僅聽一張
            var beforeWin = (int[])full.Clone();
            beforeWin[ctx.WinningTile]--;
            if (WinChecker.GetWaits(beforeWin, ctx.Melds.Count).Count == 1) Add(FanId.Dudiao);

            // ---- 花牌 ----
            if (ctx.Flowers.Count > 0)
            {
                Add(FanId.Flower, ctx.Flowers.Count);
                // 正花：花牌序號對應座位（春夏秋冬 / 梅蘭竹菊 各對應東南西北）
                int zheng = ctx.Flowers.Count(f =>
                    (f - TileDef.FLOWER_BASE) % 4 == ctx.SeatIndex);
                if (zheng > 0) Add(FanId.ZhengHua, zheng);
            }

            res.Points = t.BaseDi + res.TotalTai * t.PerTai;
            return res;
        }
    }
}
