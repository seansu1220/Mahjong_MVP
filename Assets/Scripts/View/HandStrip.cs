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
    // 牌面直接用 TileAssets 烘焙好的那份貼圖，
    // 所以 2D 手牌跟桌上的 3D 牌長得一模一樣，不是另外畫一套。
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
        const float FaceInset = 7f;

        /// <summary>
        /// 牌下緣的厚度。實體牌看得到底下那一條側面，
        /// 少了它就會很像一張貼紙。
        ///
        /// 之前是在「上緣」加一條亮藍色橫帶，但那條太寬又太藍，
        /// 看起來像牌上面壓了一條色塊，反而更假。
        /// 改成放在下緣、用比牌身深一階的同色系，才像厚度。
        /// </summary>
        const float BottomEdgeRatio = 0.10f;

        static readonly Color BottomEdgeColor = new Color(0.80f, 0.78f, 0.73f);

        static readonly Color BodyColor = new Color(0.955f, 0.945f, 0.905f);
        static readonly Color SelectedColor = new Color(1f, 0.90f, 0.62f);
        static readonly Color ClaimColor = new Color(0.70f, 0.88f, 1f);
        static readonly Color DimColor = new Color(0.72f, 0.71f, 0.68f);

        RectTransform row;
        readonly List<Entry> entries = new List<Entry>();
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
            LayoutTiles(ordered, justDrawnTile);
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

        void LayoutTiles(List<int> ordered, int justDrawnTile)
        {
            bool hasDrawnTile = justDrawnTile >= 0 && ordered.Count > 0;
            int lastConcealed = hasDrawnTile ? ordered.Count - 1 : ordered.Count;

            float width = ordered.Count * TileSize.x + Mathf.Max(0, ordered.Count - 1) * TileGap
                        + (hasDrawnTile ? DrawnTileGap : 0f);
            float cursor = -width * 0.5f;

            for (int i = 0; i < ordered.Count; i++)
            {
                if (hasDrawnTile && i == lastConcealed) cursor += DrawnTileGap;
                entries.Add(CreateTile(ordered[i], cursor, entries.Count));
                cursor += TileSize.x + TileGap;
            }
        }

        Entry CreateTile(int tile, float x, int slot)
        {
            var body = UIFactory.CreateImage("Tile", row, BodyColor, rounded: true, raycast: true);
            UIFactory.Anchor(body.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 0f),
                             new Vector2(x, 0f), TileSize);

            AddBottomEdge(body.transform);

            var face = UIFactory.CreateImage("Face", body.transform, Color.white, rounded: false);
            face.sprite = Board.TileAssets.FaceSprite(tile);
            face.preserveAspect = true;
            StretchAboveBottomEdge(face.rectTransform);

            var button = body.gameObject.AddComponent<Button>();
            button.targetGraphic = body;
            button.transition = Selectable.Transition.None;

            int captured = slot;
            button.onClick.AddListener(() => OnSlotClicked(captured));

            return new Entry { Tile = tile, Rect = body.rectTransform, Body = body };
        }

        /// <summary>牌下緣的厚度：一條比牌身深一階的橫帶</summary>
        static void AddBottomEdge(Transform parent)
        {
            var edge = UIFactory.CreateImage("BottomEdge", parent, BottomEdgeColor, rounded: false);
            UIFactory.Anchor(edge.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                             new Vector2(0f, 3f),
                             new Vector2(TileSize.x - 8f, TileSize.y * BottomEdgeRatio));
        }

        /// <summary>牌面要讓出下緣那一條，不然會蓋住</summary>
        static void StretchAboveBottomEdge(RectTransform rect)
        {
            float bottomInset = TileSize.y * BottomEdgeRatio + 5f;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(FaceInset, bottomInset);
            rect.offsetMax = new Vector2(-FaceInset, -FaceInset);
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
        }
    }
}
