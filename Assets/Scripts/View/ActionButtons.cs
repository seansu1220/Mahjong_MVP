using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.View
{
    // ============================================================
    // 吃碰槓胡的按鈕列
    //
    // 按鈕內容完全由 TurnEngine 給的合法動作清單決定，
    // View 層不自己判斷什麼時候能碰、什麼時候能吃。
    // ============================================================

    public class ActionButtons : MonoBehaviour
    {
        static readonly Vector2 ButtonSize = new Vector2(132f, 62f);
        const float ButtonGap = 12f;

        static readonly Color WinColor = new Color(0.78f, 0.22f, 0.20f);
        static readonly Color ClaimColor = new Color(0.20f, 0.42f, 0.68f);
        static readonly Color PassColor = new Color(0.35f, 0.35f, 0.35f);
        static readonly Color LabelColor = new Color(0.97f, 0.97f, 0.95f);

        readonly List<GameObject> buttons = new List<GameObject>();

        /// <summary>玩家選了某個動作</summary>
        public event Action<GameAction> ActionChosen;

        public static ActionButtons Create(Transform parent)
        {
            var rect = UIFactory.CreateRect("ActionButtons", parent);
            UIFactory.Anchor(rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                             new Vector2(0f, 250f), new Vector2(1200f, ButtonSize.y));

            var view = rect.gameObject.AddComponent<ActionButtons>();
            view.Hide();
            return view;
        }

        /// <summary>顯示可選動作。清單裡的出牌動作會被略過，那由手牌點擊處理。</summary>
        public void Show(IReadOnlyList<GameAction> options)
        {
            Clear();
            if (options == null) return;

            var displayable = new List<GameAction>();
            foreach (var option in options)
                if (option.Type != ActionType.Discard) displayable.Add(option);

            if (displayable.Count <= 1) return;   // 只剩「過」就不必顯示

            float width = displayable.Count * ButtonSize.x + (displayable.Count - 1) * ButtonGap;
            float cursor = -width * 0.5f;

            foreach (var option in displayable)
            {
                var captured = option;
                var button = UIFactory.CreateButton("Action", transform, LabelFor(captured),
                                                    ButtonSize, ColorFor(captured.Type), LabelColor,
                                                    () => { if (ActionChosen != null) ActionChosen(captured); });
                UIFactory.Anchor((RectTransform)button.transform, new Vector2(0.5f, 0.5f),
                                 new Vector2(0f, 0.5f), new Vector2(cursor, 0f), ButtonSize);
                buttons.Add(button.gameObject);
                cursor += ButtonSize.x + ButtonGap;
            }
        }

        public void Hide() => Clear();

        void Clear()
        {
            foreach (var button in buttons)
            {
                if (button == null) continue;
                button.SetActive(false);
                Destroy(button);
            }
            buttons.Clear();
        }

        // ------------------------------------------------------------

        static Color ColorFor(ActionType type)
        {
            if (type == ActionType.Win) return WinColor;
            if (type == ActionType.Pass) return PassColor;
            return ClaimColor;
        }

        static string LabelFor(GameAction action)
        {
            bool chinese = UiFont.SupportsChinese;
            switch (action.Type)
            {
                case ActionType.Win:
                    // 自摸的動作沒有指定牌，放槍胡才有
                    bool selfDraw = action.Tile == GameState.NoTile;
                    if (chinese) return selfDraw ? "自摸" : "胡";
                    return selfDraw ? "Self Draw" : "Win";

                case ActionType.Pon: return chinese ? "碰" : "Pon";
                case ActionType.MinKan:
                case ActionType.AnKan:
                case ActionType.AddKan: return chinese ? "槓" : "Kan";
                case ActionType.Chi: return ChiLabel(action, chinese);
                case ActionType.Pass: return chinese ? "過" : "Pass";
                default: return action.Type.ToString();
            }
        }

        /// <summary>吃可能有好幾種組法，按鈕上要標出是哪一組順子</summary>
        static string ChiLabel(GameAction action, bool chinese)
        {
            int baseTile = action.ChiBaseTile;
            string combination = string.Format("{0}{1}{2}",
                TileDef.GetRank(baseTile),
                TileDef.GetRank(baseTile + 1),
                TileDef.GetRank(baseTile + 2));
            return (chinese ? "吃 " : "Chi ") + combination;
        }
    }
}
