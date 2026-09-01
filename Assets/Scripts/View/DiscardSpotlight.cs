using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.View
{
    // ============================================================
    // 剛打出那張牌的提示（2D）
    //
    // 在剛打出的那張牌上面疊一個紫色圓框，框裡放同一張牌的牌面放大顯示。
    // 桌上的牌因為透視會變小變斜，光靠桌上那張很難一眼看出誰打了什麼；
    // 疊一個正面朝向玩家的 UI 就清楚多了。
    //
    // 位置是把那張牌在世界座標的位置投影到螢幕上算出來的，
    // 所以框會跟著實際的牌走，而不是固定在畫面某處。
    // ============================================================

    public class DiscardSpotlight : MonoBehaviour
    {
        static readonly Vector2 RingSize = new Vector2(132f, 132f);
        static readonly Vector2 TileSize = new Vector2(62f, 84f);

        static readonly Color RingColor = new Color(0.72f, 0.45f, 1f);

        RectTransform canvasRect;
        RectTransform holder;
        Image tileFace;

        public static DiscardSpotlight Create(RectTransform canvasRect)
        {
            var rect = UIFactory.CreateRect("DiscardSpotlight", canvasRect);
            UIFactory.Stretch(rect);

            var view = rect.gameObject.AddComponent<DiscardSpotlight>();
            view.canvasRect = canvasRect;
            view.Build();
            view.Hide();
            return view;
        }

        void Build()
        {
            holder = UIFactory.CreateRect("Holder", transform);
            UIFactory.Anchor(holder, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             Vector2.zero, RingSize);

            var ring = UIFactory.CreateImage("Ring", holder, RingColor, rounded: false);
            ring.sprite = Board.TileAssets.RingSprite;
            UIFactory.Stretch(ring.rectTransform);

            tileFace = UIFactory.CreateImage("Tile", holder, Color.white, rounded: false);
            tileFace.preserveAspect = true;
            UIFactory.Anchor(tileFace.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             Vector2.zero, TileSize);
        }

        // ------------------------------------------------------------

        /// <summary>
        /// 把提示移到那張牌在畫面上的位置。
        /// </summary>
        /// <param name="tile">要顯示的牌</param>
        /// <param name="worldPosition">那張牌在牌桌上的位置</param>
        /// <param name="camera">拍牌桌的攝影機</param>
        public void Show(int tile, Vector3 worldPosition, Camera camera)
        {
            if (camera == null || tile < 0)
            {
                Hide();
                return;
            }

            var screenPoint = camera.WorldToScreenPoint(worldPosition);
            if (screenPoint.z <= 0f)   // 在攝影機後面就不顯示
            {
                Hide();
                return;
            }

            Vector2 local;
            // 畫布是 Screen Space Overlay，換算時攝影機要傳 null
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, screenPoint, null, out local))
            {
                Hide();
                return;
            }

            holder.anchoredPosition = local;
            tileFace.sprite = Board.TileAssets.TileSprite(tile);
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
