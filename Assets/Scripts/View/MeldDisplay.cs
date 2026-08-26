using System.Collections.Generic;

namespace Mahjong.View
{
    // ============================================================
    // 副露的排列方式
    //
    // 實際打牌時，吃碰過來的那一張會擺在明顯的位置，
    // 讓所有人看得出這組是從誰手上叫來的、叫的是哪一張。
    // 這裡把它排到整組的正中間。
    //
    // 暗槓與加槓沒有「被叫走的牌」，維持原本順序。
    // ============================================================

    public struct MeldLayout
    {
        /// <summary>要依序畫出來的牌</summary>
        public int[] Tiles;

        /// <summary>哪一個位置是被叫走的那張；沒有則為 -1</summary>
        public int ClaimedIndex;
    }

    public static class MeldDisplay
    {
        public static MeldLayout Arrange(Meld meld)
        {
            int[] tiles = meld.Tiles();
            if (meld.ClaimedTile < 0)
                return new MeldLayout { Tiles = tiles, ClaimedIndex = -1 };

            var others = new List<int>();
            bool claimedRemoved = false;
            foreach (int tile in tiles)
            {
                if (!claimedRemoved && tile == meld.ClaimedTile)
                {
                    claimedRemoved = true;
                    continue;
                }
                others.Add(tile);
            }

            // 理論上一定找得到，找不到就照原順序畫，不要讓畫面出錯
            if (!claimedRemoved)
                return new MeldLayout { Tiles = tiles, ClaimedIndex = -1 };

            int middle = others.Count / 2;
            others.Insert(middle, meld.ClaimedTile);
            return new MeldLayout { Tiles = others.ToArray(), ClaimedIndex = middle };
        }

        /// <summary>暗槓要蓋住頭尾兩張，讓人看得出是暗的</summary>
        public static bool IsFaceDown(Meld meld, int index, int total)
            => meld.Type == MeldType.AnKan && (index == 0 || index == total - 1);
    }
}
