using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.View
{
    // ============================================================
    // 結算畫面
    //
    // 顯示誰胡牌、胡了哪些台、共幾台幾點，或流局。
    // 台數項目直接來自 ScoreCalculator 的結果，這裡不重算任何東西。
    // ============================================================

    public class ResultView : MonoBehaviour
    {
        static readonly Color OverlayColor = new Color(0f, 0f, 0f, 0.72f);
        static readonly Color PanelColor = new Color(0.12f, 0.16f, 0.14f, 0.98f);
        static readonly Color TitleColor = new Color(1f, 0.86f, 0.38f);
        static readonly Color BodyColor = new Color(0.92f, 0.93f, 0.90f);
        static readonly Color ButtonColor = new Color(0.20f, 0.50f, 0.34f);

        Image overlay;
        Text title;
        Text body;
        Button nextButton;
        Action onNextHand;

        public static ResultView Create(Transform parent)
        {
            var view = new GameObject("ResultView", typeof(RectTransform))
                       .AddComponent<ResultView>();
            var rect = (RectTransform)view.transform;
            rect.SetParent(parent, worldPositionStays: false);
            UIFactory.Stretch(rect);
            view.Build();
            view.Hide();
            return view;
        }

        void Build()
        {
            overlay = UIFactory.CreateImage("Overlay", transform, OverlayColor, rounded: false, raycast: true);
            UIFactory.Stretch(overlay.rectTransform);

            var panel = UIFactory.CreateImage("Panel", overlay.transform, PanelColor);
            UIFactory.Anchor(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             Vector2.zero, new Vector2(760f, 520f));

            title = UIFactory.CreateText("Title", panel.transform, "", 46, TitleColor);
            UIFactory.Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                             new Vector2(0f, -40f), new Vector2(700f, 60f));

            body = UIFactory.CreateText("Body", panel.transform, "", 28, BodyColor, TextAnchor.UpperCenter);
            UIFactory.Anchor(body.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                             new Vector2(0f, -120f), new Vector2(700f, 290f));

            nextButton = UIFactory.CreateButton("NextHand", panel.transform,
                                                UiFont.SupportsChinese ? "再來一局" : "Next Hand",
                                                new Vector2(240f, 68f), ButtonColor, BodyColor,
                                                () => { if (onNextHand != null) onNextHand(); });
            UIFactory.Anchor((RectTransform)nextButton.transform, new Vector2(0.5f, 0f),
                             new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(240f, 68f));
        }

        // ------------------------------------------------------------

        public void ShowWin(GameState state, TurnResult result, int humanSeat, Action onNext)
        {
            onNextHand = onNext;
            bool chinese = UiFont.SupportsChinese;

            bool humanWon = result.WinnerSeat == humanSeat;
            bool selfDraw = result.Applied != null && result.Applied.Tile == GameState.NoTile;

            title.text = humanWon
                ? (chinese ? "你胡了！" : "You win!")
                : (chinese ? SeatLabel(state, result.WinnerSeat) + " 胡牌" : "Seat " + result.WinnerSeat + " wins");

            body.text = BuildScoreText(state, result, selfDraw, chinese);
            gameObject.SetActive(true);
        }

        public void ShowDraw(Action onNext)
        {
            onNextHand = onNext;
            bool chinese = UiFont.SupportsChinese;
            title.text = chinese ? "流局" : "Exhaustive draw";
            body.text = chinese ? "牌山抽乾，本局不計分。" : "The wall ran out. No score this hand.";
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);

        // ------------------------------------------------------------

        static string BuildScoreText(GameState state, TurnResult result, bool selfDraw, bool chinese)
        {
            if (result.Score == null) return "";

            var builder = new StringBuilder();
            builder.AppendLine(selfDraw
                ? (chinese ? "自摸" : "Self draw")
                : (chinese ? "放槍：" + SeatLabel(state, state.LastDiscardFrom) : "Discarded by seat " + state.LastDiscardFrom));
            builder.AppendLine();

            foreach (var item in result.Score.Items)
                builder.AppendLine(string.Format(chinese ? "{0}　{1} 台" : "{0}  {1}", item.Name, item.Tai));

            builder.AppendLine();
            builder.Append(chinese
                ? string.Format("共 {0} 台，{1} 點", result.Score.TotalTai, result.Score.Points)
                : string.Format("{0} tai, {1} points", result.Score.TotalTai, result.Score.Points));
            return builder.ToString();
        }

        static string SeatLabel(GameState state, int seat)
        {
            if (seat < 0 || seat >= GameState.PlayerCount) return "?";
            var player = state.Players[seat];
            if (player == null) return "?";
            return UiFont.SupportsChinese
                ? TileDef.Name(player.SeatWind) + "家"
                : "Seat " + seat;
        }
    }
}
