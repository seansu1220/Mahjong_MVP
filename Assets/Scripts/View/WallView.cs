using System.Collections.Generic;
using UnityEngine;

namespace Mahjong.View
{
    // ============================================================
    // 桌面中央的牌山
    //
    // 照實際打牌的擺法圍成一圈：144 張牌兩張一疊共 72 墩，
    // 沿著牌桌四邊排成方形，中間空出來的地方就是四家的牌河。
    //
    // 每一墩畫成兩張錯開的牌背，看起來像疊起來的兩層。
    // 摸牌後整體墩數會跟著減少，玩家一眼就知道還剩多少牌。
    //
    // 螢幕是 16:9 而不是正方形，所以上下兩邊排得比左右兩邊多，
    // 這樣圍出來的方框才貼合畫面比例。
    // ============================================================

    public class WallView : MonoBehaviour
    {
        public const int TilesPerStack = 2;
        public const int TotalStacks = 72;          // 144 張 / 每墩 2 張

        const int StacksOnHorizontalSide = 22;      // 上下兩邊各排幾墩
        const int StacksOnVerticalSide = 14;        // 左右兩邊各排幾墩

        static readonly Vector2 StackSize = new Vector2(24f, 32f);
        const float StackGap = 1f;
        const float HorizontalSideY = 250f;         // 上下兩排離中心多遠
        const float VerticalSideX = 300f;           // 左右兩排離中心多遠
        const float StackDepthOffset = 3f;          // 上層牌往右上錯開，做出疊兩層的厚度

        readonly List<GameObject> stacks = new List<GameObject>();
        Vector2[] stackPositions;
        bool[] stackRotated;

        public static WallView Create(Transform parent)
        {
            var rect = UIFactory.CreateRect("WallView", parent);
            UIFactory.Stretch(rect);

            var view = rect.gameObject.AddComponent<WallView>();
            view.BuildPositions();
            return view;
        }

        /// <summary>先把 72 個墩位算好，之後只需依剩餘張數決定畫幾墩。</summary>
        void BuildPositions()
        {
            var positions = new List<Vector2>();
            var rotated = new List<bool>();

            AddHorizontalSide(positions, rotated, -HorizontalSideY);   // 下
            AddVerticalSide(positions, rotated, VerticalSideX);        // 右
            AddHorizontalSide(positions, rotated, HorizontalSideY);    // 上
            AddVerticalSide(positions, rotated, -VerticalSideX);       // 左

            stackPositions = positions.ToArray();
            stackRotated = rotated.ToArray();
        }

        static void AddHorizontalSide(List<Vector2> positions, List<bool> rotated, float y)
        {
            float step = StackSize.x + StackGap;
            float start = -(StacksOnHorizontalSide - 1) * step * 0.5f;
            for (int i = 0; i < StacksOnHorizontalSide; i++)
            {
                positions.Add(new Vector2(start + i * step, y));
                rotated.Add(false);
            }
        }

        static void AddVerticalSide(List<Vector2> positions, List<bool> rotated, float x)
        {
            // 側邊的牌是橫躺的，所以間距用牌的高度來算
            float step = StackSize.y + StackGap;
            float start = -(StacksOnVerticalSide - 1) * step * 0.5f;
            for (int i = 0; i < StacksOnVerticalSide; i++)
            {
                positions.Add(new Vector2(x, start + i * step));
                rotated.Add(true);
            }
        }

        // ------------------------------------------------------------

        /// <summary>依牌山剩餘張數重畫。摸掉的牌會從尾端開始消失。</summary>
        public void Refresh(int remainingTiles)
        {
            Clear();

            int remainingStacks = Mathf.Clamp(
                Mathf.CeilToInt(remainingTiles / (float)TilesPerStack), 0, TotalStacks);

            for (int i = 0; i < remainingStacks && i < stackPositions.Length; i++)
                stacks.Add(BuildStack(stackPositions[i], stackRotated[i]));
        }

        /// <summary>一墩 = 兩張錯開的牌背，看起來就是疊了兩層。</summary>
        GameObject BuildStack(Vector2 position, bool rotated)
        {
            var holder = UIFactory.CreateRect("Stack", transform);
            UIFactory.Anchor(holder, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             position, StackSize);
            if (rotated) holder.localRotation = Quaternion.Euler(0f, 0f, 90f);

            for (int layer = 0; layer < TilesPerStack; layer++)
            {
                var tile = TileView.Create(holder, TileView.NoTile, StackSize, faceUp: false);
                tile.SetInteractable(false);
                UIFactory.Anchor(tile.Rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                 new Vector2(layer * StackDepthOffset * 0.5f, layer * StackDepthOffset),
                                 StackSize);
            }
            return holder.gameObject;
        }

        void Clear()
        {
            // Destroy 要到影格結束才生效，先關掉才不會跟新畫的疊在一起
            foreach (var stack in stacks)
            {
                if (stack == null) continue;
                stack.SetActive(false);
                Destroy(stack);
            }
            stacks.Clear();
        }
    }
}
