using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.View
{
    // ============================================================
    // 自己的手牌（2D）
    //
    // 牌桌是 3D 的，但自己的手牌改用 2D 介面畫在畫面下緣。
    // 原因是攝影機俯視牌桌，立在桌上的牌只看得到上緣，
    // 把牌後仰又會變得很怪；其他麻將遊戲也都是這樣處理的。
    //
    // 每一張牌就是「桌上那張 3D 牌拍下來的照片」（TileAssets.TileSprite），
    // 頂面、側面與光影都是真的，不是用色塊在 UI 裡拼出來的假立體。
    //
    // 吃碰槓出來的副露接在手牌右邊，同一列顯示。
    // 它們原本畫在 3D 桌上自己那一側，但桌子近端剛好落在攝影機視野邊緣，
    // 玩家根本看不到自己吃了什麼；放進這一列就永遠看得見。
    // 副露畫得比手牌小一號，也不能點——一眼就知道那幾張已經定了。
    //
    // 互動：點一下選取（牌會抬起來），再點同一張才打出。
    // 選取記的是「哪一張」而不是「哪一種」——手上有兩張五萬時，
    // 點左邊那張只有左邊那張會抬起來。
    // ============================================================

    public class HandStrip : MonoBehaviour
    {
        static readonly Vector2 TileSize = new Vector2(94f, 128f);
        const float TileGap = 6f;
        const float DrawnTileGap = 34f;    // 剛摸進來的那張跟其他牌隔開
        const float SelectedLift = 26f;
        const float ClaimLift = 13f;

        /// <summary>副露的牌比手牌小一號，看得出來是已經定下來的</summary>
        const float MeldScale = 0.86f;

        static readonly Vector2 MeldTileSize = TileSize * MeldScale;
        const float HandToMeldGap = 36f;   // 手牌與副露之間
        const float MeldGroupGap = 18f;    // 副露每一組之間

        // 牌本身已經是拍好的圖，這裡只是替它上色。
        // 底色是白的，乘上去才不會把牌面染掉。
        static readonly Color BodyColor = Color.white;
        static readonly Color SelectedColor = new Color(1f, 0.92f, 0.72f);
        static readonly Color ClaimColor = new Color(0.78f, 0.90f, 1f);
        static readonly Color DimColor = new Color(0.78f, 0.78f, 0.76f);
        static readonly Color MeldColor = new Color(0.90f, 0.90f, 0.88f);

        RectTransform row;
        readonly List<Entry> entries = new List<Entry>();
        readonly List<GameObject> meldTiles = new List<GameObject>();
        readonly HashSet<int> claimHighlightSlots = new HashSet<int>();

        int selectedSlot = -1;
        bool interactable;

        /// <summary>玩家決定打出這張牌</summary>
        public event Action<int> TileChosen;

        class Entry
        {
            public int Tile;
            public RectTransform Rect;
            public Image Body;
        }

        // ------------------------------------------------------------

        public static HandStrip Create(Transform parent)
        {
            var rect = UIFactory.CreateRect("HandStrip", parent);
            UIFactory.Anchor(rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                             new Vector2(0f, 10f), new Vector2(1840f, TileSize.y + SelectedLift));

            var view = rect.gameObject.AddComponent<HandStrip>();
            view.row = rect;
            return view;
        }

        // ------------------------------------------------------------

        /// <summary>依目前手牌重畫。justDrawnTile 會排到最右邊並隔開。</summary>
        public void Refresh(PlayerState player, int justDrawnTile)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));

            selectedSlot = -1;
            claimHighlightSlots.Clear();
            Clear();

            var ordered = BuildOrderedHand(player, justDrawnTile);
            LayoutRow(player, ordered, justDrawnTile);
            ApplyVisuals();
        }

        /// <summary>手牌由小到大排序；剛摸的那張抽出來放最後。</summary>
        static List<int> BuildOrderedHand(PlayerState player, int justDrawnTile)
        {
            var ordered = new List<int>();
            for (int tile = 0; tile < TileDef.KINDS; tile++)
                for (int count = 0; count < player.ConcealedCounts[tile]; count++)
                    ordered.Add(tile);

            if (justDrawnTile >= 0 && ordered.Remove(justDrawnTile)) ordered.Add(justDrawnTile);
            return ordered;
        }

        /// <summary>手牌在左、副露接在右邊，整列置中。</summary>
        void LayoutRow(PlayerState player, List<int> ordered, int justDrawnTile)
        {
            bool hasDrawnTile = justDrawnTile >= 0 && ordered.Count > 0;

            float handWidth = ordered.Count * TileSize.x + Mathf.Max(0, ordered.Count - 1) * TileGap
                            + (hasDrawnTile ? DrawnTileGap : 0f);
            float meldsWidth = MeasureMelds(player.Melds);
            float total = handWidth + (meldsWidth > 0f ? HandToMeldGap + meldsWidth : 0f);

            float cursor = -total * 0.5f;
            LayoutHand(ordered, hasDrawnTile, ref cursor);

            if (meldsWidth > 0f)
            {
                cursor += HandToMeldGap;
                LayoutMelds(player.Melds, cursor);
            }
        }

        void LayoutHand(List<int> ordered, bool hasDrawnTile, ref float cursor)
        {
            int lastConcealed = hasDrawnTile ? ordered.Count - 1 : ordered.Count;

            for (int i = 0; i < ordered.Count; i++)
            {
                if (hasDrawnTile && i == lastConcealed) cursor += DrawnTileGap;
                entries.Add(CreateTile(ordered[i], cursor, entries.Count));
                cursor += TileSize.x + TileGap;
            }
        }

        /// <summary>量寬度與實際排列必須用同一套累加，不然整列會偏掉</summary>
        static float MeasureMelds(List<Meld> melds)
        {
            if (melds == null || melds.Count == 0) return 0f;

            float width = 0f;
            foreach (var meld in melds)
                width += meld.Tiles().Length * (MeldTileSize.x + TileGap) + MeldGroupGap;
            return width - TileGap - MeldGroupGap;
        }

        void LayoutMelds(List<Meld> melds, float cursor)
        {
            foreach (var meld in melds)
            {
                // 被吃碰走的那張排在整組中間，跟桌上的副露畫法一致
                var layout = MeldDisplay.Arrange(meld);
                foreach (int tileId in layout.Tiles)
                {
                    CreateMeldTile(tileId, cursor);
                    cursor += MeldTileSize.x + TileGap;
                }
                cursor += MeldGroupGap;
            }
        }

        void CreateMeldTile(int tile, float x)
        {
            var body = UIFactory.CreateImage("MeldTile", row, MeldColor, rounded: false);
            body.sprite = Board.TileAssets.TileSprite(tile);
            body.preserveAspect = true;
            UIFactory.Anchor(body.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 0f),
                             new Vector2(x, 0f), MeldTileSize);

            meldTiles.Add(body.gameObject);
        }

        Entry CreateTile(int tile, float x, int slot)
        {
            var body = UIFactory.CreateImage("Tile", row, BodyColor, rounded: false, raycast: true);
            body.sprite = Board.TileAssets.TileSprite(tile);
            body.preserveAspect = true;
            UIFactory.Anchor(body.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 0f),
                             new Vector2(x, 0f), TileSize);

            var button = body.gameObject.AddComponent<Button>();
            button.targetGraphic = body;
            button.transition = Selectable.Transition.None;

            int captured = slot;
            button.onClick.AddListener(() => OnSlotClicked(captured));

            return new Entry { Tile = tile, Rect = body.rectTransform, Body = body };
        }

        // ------------------------------------------------------------
        // 選取與出牌
        // ------------------------------------------------------------

        void OnSlotClicked(int slot)
        {
            if (!interactable || slot < 0 || slot >= entries.Count) return;

            // 點同一張第二次才真的打出
            if (selectedSlot == slot)
            {
                int chosen = entries[slot].Tile;
                selectedSlot = -1;
                if (TileChosen != null) TileChosen(chosen);
                return;
            }

            selectedSlot = slot;
            ApplyVisuals();
        }

        /// <summary>
        /// 三種狀態會互相覆蓋，套用順序固定為：可否點擊 → 叫牌提示 → 已選取。
        /// 叫牌提示要蓋過變灰，因為考慮要不要碰的時候手牌本來就不能點。
        /// </summary>
        void ApplyVisuals()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                bool isSelected = i == selectedSlot;
                bool isClaimTile = claimHighlightSlots.Contains(i);

                var color = interactable ? BodyColor : DimColor;
                if (isClaimTile) color = ClaimColor;
                if (isSelected) color = SelectedColor;
                entries[i].Body.color = color;

                float lift = isSelected ? SelectedLift : (isClaimTile ? ClaimLift : 0f);
                var position = entries[i].Rect.anchoredPosition;
                position.y = lift;
                entries[i].Rect.anchoredPosition = position;
            }
        }

        // ------------------------------------------------------------

        public void SetInteractable(bool value)
        {
            interactable = value;
            if (!value) selectedSlot = -1;
            ApplyVisuals();
        }

        /// <summary>標出手上會被拿去湊成那一組的牌。傳 null 表示清掉提示。</summary>
        public void SetClaimHighlight(int[] tileCounts)
        {
            claimHighlightSlots.Clear();

            if (tileCounts != null)
            {
                var remaining = (int[])tileCounts.Clone();
                for (int i = 0; i < entries.Count; i++)
                {
                    int tile = entries[i].Tile;
                    if (tile < 0 || tile >= TileDef.KINDS || remaining[tile] <= 0) continue;
                    remaining[tile]--;
                    claimHighlightSlots.Add(i);
                }
            }
            ApplyVisuals();
        }

        public void ClearClaimHighlight() => SetClaimHighlight(null);

        void Clear()
        {
            // Destroy 要到影格結束才生效，先關掉才不會跟新畫的牌疊在一起
            foreach (var entry in entries)
            {
                if (entry.Rect == null) continue;
                entry.Rect.gameObject.SetActive(false);
                Destroy(entry.Rect.gameObject);
            }
            entries.Clear();

            foreach (var meldTile in meldTiles)
            {
                if (meldTile == null) continue;
                meldTile.SetActive(false);
                Destroy(meldTile);
            }
            meldTiles.Clear();
        }
    }
}
