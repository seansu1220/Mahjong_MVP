using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.View
{
    // ============================================================
    // 中央大字提示
    //
    // 有人吃碰槓胡時，在畫面正中央打出一個大字，
    // 由小放大再淡出，讓玩家清楚知道剛剛發生了什麼事。
    //
    // 只負責演出，不判斷任何規則——要顯示什麼字由 Bootstrap 決定。
    // ============================================================

    public class AnnouncementView : MonoBehaviour
    {
        public static readonly Color ClaimColor = new Color(1f, 0.86f, 0.35f);
        public static readonly Color WinColor = new Color(1f, 0.45f, 0.35f);
        public static readonly Color NeutralColor = new Color(0.92f, 0.94f, 0.90f);

        // 128px 的字實際行高會超過文字框，原本會壓到下方那行小字，
        // 所以字級縮到 104、面板加高，並把兩行拉開。
        const int HeadlineFontSize = 104;
        const int DetailFontSize = 30;

        const float PopInDuration = 0.14f;
        const float HoldDuration = 0.46f;
        const float FadeOutDuration = 0.24f;
        const float StartScale = 1.75f;
        const float EndScale = 1f;

        RectTransform panel;
        Image backdrop;
        Text label;
        Text subLabel;

        public static AnnouncementView Create(Transform parent)
        {
            var rect = UIFactory.CreateRect("AnnouncementView", parent);
            UIFactory.Stretch(rect);

            var view = rect.gameObject.AddComponent<AnnouncementView>();
            view.Build();
            view.SetVisible(false);
            return view;
        }

        void Build()
        {
            panel = UIFactory.CreateRect("Panel", transform);
            UIFactory.Anchor(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             new Vector2(0f, 0f), new Vector2(620f, 268f));

            backdrop = UIFactory.CreateImage("Backdrop", panel, new Color(0f, 0f, 0f, 0.55f));
            UIFactory.Stretch(backdrop.rectTransform);

            label = UIFactory.CreateText("Text", panel, "", HeadlineFontSize, ClaimColor);
            UIFactory.Anchor(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                             new Vector2(0f, -22f), new Vector2(600f, 140f));

            subLabel = UIFactory.CreateText("SubText", panel, "", DetailFontSize, NeutralColor);
            UIFactory.Anchor(subLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                             new Vector2(0f, 26f), new Vector2(600f, 44f));
        }

        // ------------------------------------------------------------

        /// <summary>播一次提示。呼叫端用 yield return 等它演完。</summary>
        public IEnumerator Play(string headline, string detail, Color color)
        {
            if (string.IsNullOrEmpty(headline)) yield break;

            label.text = headline;
            label.color = color;
            subLabel.text = detail ?? "";
            SetVisible(true);

            yield return Animate(StartScale, EndScale, PopInDuration, 0f, 1f);
            yield return new WaitForSeconds(HoldDuration);
            yield return Animate(EndScale, EndScale * 1.08f, FadeOutDuration, 1f, 0f);

            SetVisible(false);
        }

        IEnumerator Animate(float fromScale, float toScale, float duration, float fromAlpha, float toAlpha)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - (1f - t) * (1f - t);   // 先快後慢，收得比較俐落

                panel.localScale = Vector3.one * Mathf.Lerp(fromScale, toScale, eased);
                SetAlpha(Mathf.Lerp(fromAlpha, toAlpha, eased));
                yield return null;
            }
            panel.localScale = Vector3.one * toScale;
            SetAlpha(toAlpha);
        }

        void SetAlpha(float alpha)
        {
            var backdropColor = backdrop.color;
            backdropColor.a = 0.55f * alpha;
            backdrop.color = backdropColor;

            var labelColor = label.color;
            labelColor.a = alpha;
            label.color = labelColor;

            var subColor = subLabel.color;
            subColor.a = alpha;
            subLabel.color = subColor;
        }

        void SetVisible(bool visible)
        {
            panel.gameObject.SetActive(visible);
            if (visible) return;
            panel.localScale = Vector3.one;
        }
    }
}
