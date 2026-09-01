using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mahjong.View
{
    // ============================================================
    // 吃碰槓聽胡的按鈕列
    //
    // **有得做的時候整排才出現**：這一手一個動作都做不到就整排不顯示，
    // 免得畫面上永遠掛著一排灰按鈕。
    // 出現之後六個位置固定不動：吃、碰、槓、聽、胡、過，
    // 做得到的才點得下去，做不到的變灰留在原位——
    // 按鈕不會跳來跳去，玩家不必每次重新找。
    //
    // 吃可能有好幾種組法（手上 1245 條要吃 3 條，可組 123／234／345）。
    // 這種時候按下「吃」不會直接成立，而是把這一排換成各種組法讓玩家挑，
    // 每個按鈕標出實際的順子，旁邊再放一個「返回」。
    //
    // 按鈕內容完全由 TurnEngine 給的合法動作清單決定，
    // View 層不自己判斷什麼時候能碰、什麼時候能吃。
    // ============================================================

    public class ActionButtons : MonoBehaviour
    {
        static readonly Vector2 ButtonSize = new Vector2(150f, 76f);
        const float ButtonGap = 14f;

        static readonly Color WinColor = new Color(0.80f, 0.24f, 0.22f);
        static readonly Color ClaimColor = new Color(0.22f, 0.45f, 0.72f);
        static readonly Color ReadyColor = new Color(0.76f, 0.48f, 0.12f);
        static readonly Color PassColor = new Color(0.38f, 0.38f, 0.38f);
        static readonly Color DisabledColor = new Color(0.26f, 0.28f, 0.27f, 0.85f);
        static readonly Color LabelColor = new Color(0.97f, 0.97f, 0.95f);
        static readonly Color DisabledLabelColor = new Color(0.55f, 0.57f, 0.55f);

        readonly List<GameObject> buttons = new List<GameObject>();

        /// <summary>玩家選了某個動作</summary>
        public event Action<GameAction> ActionChosen;

        /// <summary>滑鼠移到某個動作上（移開時傳 null），用來標出會用掉哪幾張牌</summary>
        public event Action<GameAction> ActionHovered;

        /// <summary>玩家按下「聽」</summary>
        public event Action ReadyDeclared;

        IReadOnlyList<GameAction> currentOptions;
        bool readyAvailable;

        // ------------------------------------------------------------

        public static ActionButtons Create(Transform parent)
        {
            // 釘在畫面底部、自己手牌的正上方
            var rect = UIFactory.CreateRect("ActionButtons", parent);
            UIFactory.Anchor(rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                             new Vector2(0f, 234f), new Vector2(1120f, ButtonSize.y));

            var view = rect.gameObject.AddComponent<ActionButtons>();
            view.Hide();
            return view;
        }

        /// <summary>
        /// 顯示這一手能做什麼。出牌動作會被略過——那由手牌點擊處理。
        /// </summary>
        /// <param name="canDeclareReady">現在能不能宣告聽牌</param>
        public void Show(IReadOnlyList<GameAction> options, bool canDeclareReady)
        {
            currentOptions = options;
            readyAvailable = canDeclareReady;

            if (!HasAnythingToDo())
            {
                Hide();
                return;
            }
            ShowMainRow();
        }

        /// <summary>
        /// 這一手有沒有任何可以按的東西。
        /// 「過」單獨存在不算——輪到自己出牌時只會有出牌可選，
        /// 那是點手牌處理的，不需要整排按鈕跑出來。
        /// </summary>
        bool HasAnythingToDo()
        {
            if (readyAvailable) return true;
            if (currentOptions == null) return false;

            foreach (var option in currentOptions)
                if (option.Type != ActionType.Discard && option.Type != ActionType.Pass)
                    return true;
            return false;
        }

        public void Hide()
        {
            currentOptions = null;
            readyAvailable = false;
            Clear();
            RaiseHover(null);
        }

        // ------------------------------------------------------------
        // 固定的六格
        // ------------------------------------------------------------

        void ShowMainRow()
        {
            Clear();
            bool chinese = UiFont.SupportsChinese;

            var slots = new List<Slot>
            {
                new Slot(chinese ? "吃" : "Chi", ClaimColor, Find(ActionType.Chi)),
                new Slot(chinese ? "碰" : "Pon", ClaimColor, Find(ActionType.Pon)),
                new Slot(chinese ? "槓" : "Kan",
                         ClaimColor, Find(ActionType.MinKan, ActionType.AnKan, ActionType.AddKan)),
                new Slot(chinese ? "聽" : "Ready", ReadyColor, null) { ReadyOnly = true },
                new Slot(chinese ? "胡" : "Win", WinColor, Find(ActionType.Win)),
                new Slot(chinese ? "過" : "Pass", PassColor, Find(ActionType.Pass))
            };

            LayoutSlots(slots);
        }

        void LayoutSlots(List<Slot> slots)
        {
            float width = slots.Count * ButtonSize.x + (slots.Count - 1) * ButtonGap;
            float cursor = -width * 0.5f;

            foreach (var slot in slots)
            {
                CreateSlotButton(slot, cursor);
                cursor += ButtonSize.x + ButtonGap;
            }
        }

        void CreateSlotButton(Slot slot, float x)
        {
            bool enabled = slot.ReadyOnly ? readyAvailable : slot.Options.Count > 0;
            var background = enabled ? slot.Color : DisabledColor;

            var button = UIFactory.CreateButton("Action", transform, slot.Label, ButtonSize,
                                                background,
                                                enabled ? LabelColor : DisabledLabelColor,
                                                enabled ? BuildHandler(slot) : null);
            button.interactable = enabled;

            UIFactory.Anchor((RectTransform)button.transform, new Vector2(0.5f, 0.5f),
                             new Vector2(0f, 0.5f), new Vector2(x, 0f), ButtonSize);

            // 只有唯一一種做法時才標出會用掉哪幾張；有好幾種組法時要先挑，標了反而混淆
            var hoverTarget = enabled && !slot.ReadyOnly && slot.Options.Count == 1
                ? slot.Options[0] : null;
            AddHoverEvents(button.gameObject, hoverTarget);

            buttons.Add(button.gameObject);
        }

        Action BuildHandler(Slot slot)
        {
            if (slot.ReadyOnly) return () => { if (ReadyDeclared != null) ReadyDeclared(); };
            if (slot.Options.Count == 1) return () => Choose(slot.Options[0]);

            var options = slot.Options;
            return () => ShowVariantRow(options);
        }

        // ------------------------------------------------------------
        // 吃有好幾種組法時，展開讓玩家挑
        // ------------------------------------------------------------

        void ShowVariantRow(List<GameAction> options)
        {
            Clear();
            RaiseHover(null);

            int count = options.Count + 1;   // 加上「返回」
            float width = count * ButtonSize.x + (count - 1) * ButtonGap;
            float cursor = -width * 0.5f;

            foreach (var option in options)
            {
                var captured = option;
                var button = UIFactory.CreateButton("Variant", transform, VariantLabel(captured),
                                                    ButtonSize, ClaimColor, LabelColor,
                                                    () => Choose(captured));
                UIFactory.Anchor((RectTransform)button.transform, new Vector2(0.5f, 0.5f),
                                 new Vector2(0f, 0.5f), new Vector2(cursor, 0f), ButtonSize);
                AddHoverEvents(button.gameObject, captured);
                buttons.Add(button.gameObject);
                cursor += ButtonSize.x + ButtonGap;
            }

            var back = UIFactory.CreateButton("Back", transform,
                                              UiFont.SupportsChinese ? "返回" : "Back",
                                              ButtonSize, PassColor, LabelColor, ShowMainRow);
            UIFactory.Anchor((RectTransform)back.transform, new Vector2(0.5f, 0.5f),
                             new Vector2(0f, 0.5f), new Vector2(cursor, 0f), ButtonSize);
            AddHoverEvents(back.gameObject, null);
            buttons.Add(back.gameObject);
        }

        /// <summary>吃的組法標出實際的順子，例如「吃 234」</summary>
        static string VariantLabel(GameAction action)
        {
            if (action.Type != ActionType.Chi) return action.Type.ToString();

            int baseTile = action.ChiBaseTile;
            string combination = string.Format("{0}{1}{2}",
                TileDef.GetRank(baseTile),
                TileDef.GetRank(baseTile + 1),
                TileDef.GetRank(baseTile + 2));
            return (UiFont.SupportsChinese ? "吃 " : "Chi ") + combination;
        }

        // ------------------------------------------------------------

        void Choose(GameAction action)
        {
            RaiseHover(null);
            if (ActionChosen != null) ActionChosen(action);
        }

        List<GameAction> Find(params ActionType[] types)
        {
            var found = new List<GameAction>();
            if (currentOptions == null) return found;

            foreach (var option in currentOptions)
                foreach (var type in types)
                    if (option.Type == type) { found.Add(option); break; }
            return found;
        }

        void AddHoverEvents(GameObject target, GameAction action)
        {
            var trigger = target.AddComponent<EventTrigger>();
            trigger.triggers.Add(BuildEntry(EventTriggerType.PointerEnter, action));
            trigger.triggers.Add(BuildEntry(EventTriggerType.PointerExit, null));
        }

        EventTrigger.Entry BuildEntry(EventTriggerType eventType, GameAction action)
        {
            var entry = new EventTrigger.Entry { eventID = eventType };
            entry.callback.AddListener(_ => RaiseHover(action));
            return entry;
        }

        void RaiseHover(GameAction action)
        {
            if (ActionHovered != null) ActionHovered(action);
        }

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

        class Slot
        {
            public readonly string Label;
            public readonly Color Color;
            public readonly List<GameAction> Options;

            /// <summary>「聽」不是 TurnEngine 的動作，能不能按由 Bootstrap 另外判斷</summary>
            public bool ReadyOnly;

            public Slot(string label, Color color, List<GameAction> options)
            {
                Label = label;
                Color = color;
                Options = options ?? new List<GameAction>();
            }
        }
    }
}
