using System;
using System.Collections.Generic;

namespace Mahjong
{
    // ============================================================
    // 牌局現況
    //
    // 這一層只負責「記住現在的局面」與「純查詢」，不負責流程推進。
    // 誰該摸牌、誰能吃碰槓胡、優先權怎麼比，全部是 TurnEngine 的事。
    //
    // 座位以 index 表示，順序為逆時針：0 → 1 → 2 → 3 → 0。
    // ============================================================

    public enum GamePhase
    {
        NotStarted,     // 尚未發牌
        WaitingDraw,    // 輪到某家摸牌
        WaitingDiscard, // 某家摸完牌，等他打出一張
        WaitingClaim,   // 有人打出牌，等其他三家決定吃碰槓胡或過
        Ended           // 有人胡牌或流局
    }

    /// <summary>牌局如何結束</summary>
    public enum GameEndReason
    {
        None,
        Win,        // 有人胡牌
        Exhausted   // 牌山抽乾，流局
    }

    // ============================================================
    // 單一玩家的局面
    // ============================================================

    public class PlayerState
    {
        public int SeatIndex;   // 0~3
        public int SeatWind;    // 門風，TileDef.EAST ~ TileDef.NORTH

        /// <summary>手中未副露的牌，長度 34 的計數陣列（花牌不放這裡）</summary>
        public readonly int[] ConcealedCounts = new int[TileDef.KINDS];

        public readonly List<Meld> Melds = new List<Meld>();
        public readonly List<int> Flowers = new List<int>();
        public readonly List<int> Discards = new List<int>();

        /// <summary>手中張數（不含副露與花牌）</summary>
        public int ConcealedTileCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < TileDef.KINDS; i++) total += ConcealedCounts[i];
                return total;
            }
        }

        /// <summary>門清：沒有露出過任何一組（暗槓不破門清）</summary>
        public bool IsConcealedHand => Melds.TrueForAll(m => m.IsConcealed);

        public void AddTile(int tile)
        {
            ValidateTile(tile);
            ConcealedCounts[tile]++;
        }

        public void RemoveTile(int tile)
        {
            ValidateTile(tile);
            if (ConcealedCounts[tile] <= 0)
                throw new InvalidOperationException(
                    $"座位 {SeatIndex} 手中沒有 {TileDef.Name(tile)}，無法移除");
            ConcealedCounts[tile]--;
        }

        static void ValidateTile(int tile)
        {
            if (tile < 0 || tile >= TileDef.KINDS)
                throw new ArgumentOutOfRangeException(nameof(tile),
                    $"手牌計數陣列只放 0~{TileDef.KINDS - 1} 的牌（花牌請放 Flowers），實際為 {tile}");
        }

        public PlayerState Clone()
        {
            var copy = new PlayerState { SeatIndex = SeatIndex, SeatWind = SeatWind };
            Array.Copy(ConcealedCounts, copy.ConcealedCounts, TileDef.KINDS);
            foreach (var meld in Melds) copy.Melds.Add(meld.Clone());
            copy.Flowers.AddRange(Flowers);
            copy.Discards.AddRange(Discards);
            return copy;
        }
    }

    // ============================================================
    // 整桌局面
    // ============================================================

    public class GameState
    {
        public const int PlayerCount = 4;
        public const int DealerHandSize = 17;   // 莊家 17 張
        public const int PlayerHandSize = 16;   // 閒家 16 張
        public const int NoTile = -1;           // 牌山已空時的回傳值

        public readonly PlayerState[] Players = new PlayerState[PlayerCount];
        public Wall Wall;

        public int DealerIndex;      // 莊家座位
        public int CurrentPlayer;    // 現在輪到誰
        public int RoundWind = TileDef.EAST;
        public int DealerStreak;     // 連莊次數

        public GamePhase Phase = GamePhase.NotStarted;
        public GameEndReason EndReason = GameEndReason.None;

        /// <summary>最後打出的那張牌，以及是誰打的。沒有時為 NoTile。</summary>
        public int LastDiscardTile = NoTile;
        public int LastDiscardFrom = -1;

        /// <summary>是否處於槓後補牌狀態（供槓上開花判定）</summary>
        public bool AwaitingKanReplacement;

        /// <summary>
        /// 現在這張牌是不是剛摸進來的。
        /// 自摸只能在剛摸完牌時宣告——吃碰完雖然手牌也是 3n+2 張，但那不是自摸。
        /// </summary>
        public bool HasDrawnThisTurn;

        // ---------- 開局 ----------

        /// <summary>
        /// 發一副新牌：莊家 17 張、閒家 16 張，接著依序補花。
        /// </summary>
        /// <param name="dealerIndex">莊家座位 0~3</param>
        /// <param name="roundWind">圈風，TileDef.EAST ~ TileDef.NORTH</param>
        /// <param name="dealerStreak">連莊次數</param>
        /// <param name="seed">牌山種子，0 為隨機；可用回傳物件的 Wall.Seed 重現</param>
        public static GameState CreateNewHand(int dealerIndex, int roundWind = TileDef.EAST,
                                              int dealerStreak = 0, int seed = 0)
        {
            if (dealerIndex < 0 || dealerIndex >= PlayerCount)
                throw new ArgumentOutOfRangeException(nameof(dealerIndex),
                    $"莊家座位必須是 0~{PlayerCount - 1}，實際為 {dealerIndex}");
            if (roundWind < TileDef.EAST || roundWind > TileDef.NORTH)
                throw new ArgumentOutOfRangeException(nameof(roundWind),
                    "圈風必須是 TileDef.EAST ~ TileDef.NORTH");

            var state = new GameState
            {
                Wall = new Wall(seed),
                DealerIndex = dealerIndex,
                CurrentPlayer = dealerIndex,
                RoundWind = roundWind,
                DealerStreak = dealerStreak
            };

            for (int offset = 0; offset < PlayerCount; offset++)
            {
                int seat = (dealerIndex + offset) % PlayerCount;
                state.Players[seat] = new PlayerState
                {
                    SeatIndex = seat,
                    SeatWind = TileDef.EAST + offset   // 莊家為東，之後依序南西北
                };
            }

            state.DealInitialTiles();
            state.ReplaceAllFlowers();
            state.Phase = GamePhase.WaitingDiscard;   // 莊家已有 17 張，接著要打出一張
            state.HasDrawnThisTurn = true;            // 莊家的第 17 張視同摸進來的，可宣告天胡以外的自摸
            return state;
        }

        /// <summary>從牌頭發牌。莊家發滿 17 張，其餘各 16 張。</summary>
        void DealInitialTiles()
        {
            for (int offset = 0; offset < PlayerCount; offset++)
            {
                int seat = (DealerIndex + offset) % PlayerCount;
                int handSize = ExpectedHandSize(seat);
                for (int i = 0; i < handSize; i++)
                    PlaceDrawnTile(seat, Wall.Draw());
            }
        }

        /// <summary>
        /// 依莊家起算的順序，把每家手上的花牌換成正常牌，直到全部沒有花為止。
        /// 補進來的可能又是花，所以要重複補。
        /// </summary>
        void ReplaceAllFlowers()
        {
            for (int offset = 0; offset < PlayerCount; offset++)
            {
                int seat = (DealerIndex + offset) % PlayerCount;
                while (Players[seat].ConcealedTileCount < ExpectedHandSize(seat))
                    if (DrawReplacementTile(seat) == NoTile) return;   // 牌山抽乾，交給 TurnEngine 判流局
            }
        }

        /// <summary>該座位發完牌後手上應有的張數</summary>
        public int ExpectedHandSize(int seat)
            => seat == DealerIndex ? DealerHandSize : PlayerHandSize;

        // ---------- 摸牌與補牌 ----------

        /// <summary>
        /// 從牌頭摸一張。摸到花會自動從牌尾補，補到不是花為止。
        /// 回傳真正進手的那張牌；牌山抽乾則回傳 NoTile。
        /// </summary>
        public int DrawTile(int seat)
        {
            if (Wall == null || Wall.IsEmpty) return NoTile;
            int tile = Wall.Draw();
            PlaceDrawnTile(seat, tile);
            return TileDef.IsFlower(tile) ? DrawReplacementTile(seat) : tile;
        }

        /// <summary>
        /// 從牌尾補一張。補花與槓後補牌都走這裡（台灣麻將規則）。
        /// 補到花會繼續補，回傳真正進手的那張牌；牌山抽乾則回傳 NoTile。
        /// </summary>
        public int DrawReplacementTile(int seat)
        {
            while (Wall != null && !Wall.IsEmpty)
            {
                int tile = Wall.DrawFromTail();
                PlaceDrawnTile(seat, tile);
                if (!TileDef.IsFlower(tile)) return tile;
            }
            return NoTile;
        }

        /// <summary>花牌進花區，一般牌進手牌</summary>
        void PlaceDrawnTile(int seat, int tile)
        {
            if (TileDef.IsFlower(tile)) Players[seat].Flowers.Add(tile);
            else Players[seat].AddTile(tile);
        }

        // ---------- 純查詢 ----------

        /// <summary>下一家（逆時針）</summary>
        public static int NextSeat(int seat) => (seat + 1) % PlayerCount;

        /// <summary>從 fromSeat 逆時針走幾步會到 toSeat。用於比較胡牌優先權。</summary>
        public static int SeatDistance(int fromSeat, int toSeat)
            => (toSeat - fromSeat + PlayerCount) % PlayerCount;

        /// <summary>claimer 是不是 discarder 的下家（吃只能吃下家打出的牌）</summary>
        public static bool IsNextSeatOf(int claimer, int discarder)
            => SeatDistance(discarder, claimer) == 1;

        /// <summary>
        /// 場上已可見的牌張數：所有牌河 + 所有副露。
        /// 供 AI 與聽牌計算扣除已經摸不到的牌。
        /// </summary>
        /// <param name="excludeSeat">要排除副露的座位。傳自己的座位，
        /// 因為 WinChecker.GetWaits 會另外扣自己的副露，避免重複扣。</param>
        public int[] BuildVisibleCounts(int excludeSeat = -1)
        {
            var visible = new int[TileDef.KINDS];
            for (int seat = 0; seat < PlayerCount; seat++)
            {
                if (Players[seat] == null) continue;

                foreach (int tile in Players[seat].Discards)
                    if (tile < TileDef.KINDS) visible[tile]++;

                if (seat == excludeSeat) continue;
                foreach (var meld in Players[seat].Melds)
                    foreach (int tile in meld.Tiles())
                        if (tile < TileDef.KINDS) visible[tile]++;
            }
            return visible;
        }

        /// <summary>牌山是否已抽乾（流局條件）</summary>
        public bool IsWallExhausted => Wall == null || Wall.IsEmpty;

        /// <summary>
        /// 深拷貝。AI 模擬一律用這個，不得直接改動真實局面。
        /// 注意：Wall 不複製（模擬不該偷看牌山），複製後 Wall 為 null。
        /// </summary>
        public GameState Clone()
        {
            var copy = new GameState
            {
                Wall = null,
                DealerIndex = DealerIndex,
                CurrentPlayer = CurrentPlayer,
                RoundWind = RoundWind,
                DealerStreak = DealerStreak,
                Phase = Phase,
                EndReason = EndReason,
                LastDiscardTile = LastDiscardTile,
                LastDiscardFrom = LastDiscardFrom,
                AwaitingKanReplacement = AwaitingKanReplacement,
                HasDrawnThisTurn = HasDrawnThisTurn
            };
            for (int seat = 0; seat < PlayerCount; seat++)
                copy.Players[seat] = Players[seat] == null ? null : Players[seat].Clone();
            return copy;
        }
    }
}
