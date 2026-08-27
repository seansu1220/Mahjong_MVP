using System.Collections.Generic;
using UnityEngine;

namespace Mahjong.View
{
    // ============================================================
    // 桌面中央的牌山
    //
    // 照實際打牌的擺法圍成一圈：144 張牌兩張一疊共 72 墩，
    // 沿著牌桌四邊排成方框，中間空出來的地方就是四家的牌河。
    //
    // 排列方向跟出牌順序一致，都是逆時針：
    //   下排 左→右 ／ 右排 下→上 ／ 上排 右→左 ／ 左排 上→下
    // 所以摸牌時牌會照著同一個方向一路消失，
    // 對家面前的牌就會從右邊往左邊減少，跟真的在牌桌上摸一樣。
    //
    // 兩端分別推進：
    //   正常摸牌 → 從牌頭那端往後推進
    //   補花、槓後補牌 → 從牌尾那端往回收
    //
    // 一墩兩張，只摸走一張時要看得出那一墩只剩下層那張。
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

        // 上層牌往右上錯開，露出下層的邊，看起來才像疊了兩張
        static readonly Vector2 UpperLayerOffset = new Vector2(3f, 7f);

        readonly List<GameObject> stacks = new List<GameObject>();
        Vector2[] stackPositions;
        bool[] stackRotated;

        int headStack;                              // 牌頭那端目前推進到第幾墩
        int tailStack = TotalStacks - 1;            // 牌尾那端目前收到第幾墩

        /// <summary>下一張正常摸牌會從哪裡拿</summary>
        public Vector2 HeadPosition => PositionOf(headStack);

        /// <summary>下一張補牌（補花、槓後補牌）會從哪裡拿</summary>
        public Vector2 TailPosition => PositionOf(tailStack);

        public static WallView Create(Transform parent)
        {
            var rect = UIFactory.CreateRect("WallView", parent);
            UIFactory.Stretch(rect);

            var view = rect.gameObject.AddComponent<WallView>();
            view.BuildPositions();
            return view;
        }

        Vector2 PositionOf(int stackIndex)
        {
            if (stackPositions == null || stackPositions.Length == 0) return Vector2.zero;
            return stackPositions[Mathf.Clamp(stackIndex, 0, stackPositions.Length - 1)];
        }

        /// <summary>
        /// 先把 72 個墩位依逆時針順序算好，之後只需依兩端推進到哪決定畫哪幾墩。
        /// 四邊的行進方向必須首尾相接，摸牌才會沿著同一個方向繞著消失。
        /// </summary>
        void BuildPositions()
        {
            var positions = new List<Vector2>();
            var rotated = new List<bool>();

            AddHorizontalSide(positions, rotated, -HorizontalSideY, leftToRight: true);   // 下：左→右
            AddVerticalSide(positions, rotated, VerticalSideX, bottomToTop: true);        // 右：下→上
            AddHorizontalSide(positions, rotated, HorizontalSideY, leftToRight: false);   // 上：右→左
            AddVerticalSide(positions, rotated, -VerticalSideX, bottomToTop: false);      // 左：上→下

            stackPositions = positions.ToArray();
            stackRotated = rotated.ToArray();
        }

        static void AddHorizontalSide(List<Vector2> positions, List<bool> rotated,
                                      float y, bool leftToRight)
        {
            float step = StackSize.x + StackGap;
            float extent = (StacksOnHorizontalSide - 1) * step * 0.5f;
            for (int i = 0; i < StacksOnHorizontalSide; i++)
            {
                float offset = -extent + i * step;
                positions.Add(new Vector2(leftToRight ? offset : -offset, y));
                rotated.Add(false);
            }
        }

        static void AddVerticalSide(List<Vector2> positions, List<bool> rotated,
                                    float x, bool bottomToTop)
        {
            // 側邊的牌是橫躺的，所以間距用牌的高度來算
            float step = StackSize.y + StackGap;
            float extent = (StacksOnVerticalSide - 1) * step * 0.5f;
            for (int i = 0; i < StacksOnVerticalSide; i++)
            {
                float offset = -extent + i * step;
                positions.Add(new Vector2(x, bottomToTop ? offset : -offset));
                rotated.Add(true);
            }
        }

        // ------------------------------------------------------------

        /// <summary>
        /// 依牌山兩端各摸走多少張重畫。
        /// </summary>
        /// <param name="drawnFromHead">已經從牌頭摸走的張數</param>
        /// <param name="drawnFromTail">已經從牌尾補走的張數</param>
        public void Refresh(int drawnFromHead, int drawnFromTail)
        {
            Clear();

            headStack = Mathf.Clamp(drawnFromHead / TilesPerStack, 0, TotalStacks);
            tailStack = Mathf.Clamp(TotalStacks - 1 - drawnFromTail / TilesPerStack, -1, TotalStacks - 1);

            // 摸走奇數張時，最前面那一墩的上層已經被拿走，只剩下層那張
            bool headHalfTaken = drawnFromHead % TilesPerStack != 0;
            bool tailHalfTaken = drawnFromTail % TilesPerStack != 0;

            for (int i = headStack; i <= tailStack && i < stackPositions.Length; i++)
            {
                if (i < 0) continue;

                int layers = TilesPerStack;
                if (i == headStack && headHalfTaken) layers--;
                if (i == tailStack && tailHalfTaken) layers--;
                if (layers <= 0) continue;

                stacks.Add(BuildStack(stackPositions[i], stackRotated[i], layers));
            }
        }

        /// <summary>
        /// 畫一墩。layers 為 2 時是完整的兩張；為 1 時只畫下層，
        /// 看起來就是上面那張已經被摸走了。
        /// </summary>
        GameObject BuildStack(Vector2 position, bool rotated, int layers)
        {
            var holder = UIFactory.CreateRect("Stack", transform);
            UIFactory.Anchor(holder, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             position, StackSize);
            if (rotated) holder.localRotation = Quaternion.Euler(0f, 0f, 90f);

            // 先畫下層再畫上層，上層才會蓋在前面
            for (int layer = 0; layer < layers; layer++)
            {
                var tile = TileView.Create(holder, TileView.NoTile, StackSize, faceUp: false);
                tile.SetInteractable(false);
                UIFactory.Anchor(tile.Rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                 UpperLayerOffset * layer, StackSize);
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
