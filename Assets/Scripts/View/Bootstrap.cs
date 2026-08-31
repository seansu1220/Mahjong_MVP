using System.Collections;
using System.Collections.Generic;
using Mahjong.View.Board;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mahjong.View
{
    // ============================================================
    // 進入點
    //
    // 場景裡不需要放任何東西：進入 Play 時自己生出 3D 牌桌、攝影機、
    // 燈光，以及疊在上面的 2D 介面，也不需要到 Inspector 拖拉引用。
    //
    // 畫面分兩層：
    //   3D  牌桌本身——牌山、四家手牌、副露、牌河（TableBoard）
    //   2D  疊在上面的介面——動作按鈕、名牌、狀態、大字、結算視窗
    //
    // 職責只有兩件事：組出畫面、驅動 TurnEngine 的主迴圈。
    // 所有規則判定都在 Core，這裡一條規則都不寫。
    // ============================================================

    public class Bootstrap : MonoBehaviour
    {
        public const int HumanSeat = 0;

        static readonly Color HintColor = new Color(0.55f, 0.62f, 0.57f);
        static readonly Color StatusColor = new Color(0.86f, 0.90f, 0.84f);
        static readonly Color SeatColor = new Color(0.88f, 0.91f, 0.86f);
        static readonly Color ActiveSeatColor = new Color(1f, 0.85f, 0.35f);
        static readonly Color WinnerSeatColor = new Color(1f, 0.55f, 0.40f);
        static readonly Color ReadyColor = new Color(1f, 0.78f, 0.30f);
        static readonly Color ReadyButtonColor = new Color(0.72f, 0.45f, 0.10f);

        const float AiThinkDelay = 0.32f;
        const float DrawPause = 0.24f;
        const float RevealPause = 1.2f;

        GameState state;
        TurnEngine engine;
        FanTable fanTable;
        AIPlayer[] opponents;

        TableBoard board;
        ActionButtons actionButtons;
        ResultView resultView;
        AnnouncementView announcement;
        Text statusLabel;
        Text centreInfo;
        Text[] seatLabels;
        Button readyButton;

        GameAction pendingHumanAction;
        List<GameAction> humanTurnOptions;
        bool humanDeclaredReady;
        bool readyAnnounced;
        int lastDrawnTile = TileObject.NoTile;
        int dealerIndex;
        int dealerStreak;

        // ------------------------------------------------------------

        /// <summary>進入 Play 時自動啟動，不必在場景裡手動掛任何物件。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (FindObjectOfType<Bootstrap>() != null) return;
            new GameObject("Mahjong Bootstrap").AddComponent<Bootstrap>();
        }

        void Start()
        {
            fanTable = FanTable.Default();
            BuildScene();
            StartCoroutine(StartNewHand(rotateDealer: false));
        }

        // ------------------------------------------------------------
        // 組畫面
        // ------------------------------------------------------------

        void BuildScene()
        {
            EnsureEventSystem();

            board = TableBoard.Create(HumanSeat);
            board.TileChosen += OnHumanTileChosen;

            var canvas = CreateOverlayCanvas();
            BuildSeatLabels(canvas.transform);
            BuildStatusLabels(canvas.transform);

            actionButtons = ActionButtons.Create(canvas.transform);
            actionButtons.ActionChosen += OnHumanActionChosen;
            actionButtons.ActionHovered += OnHumanActionHovered;

            BuildReadyButton(canvas.transform);
            resultView = ResultView.Create(canvas.transform);
            announcement = AnnouncementView.Create(canvas.transform);
        }

        /// <summary>2D 介面疊在 3D 牌桌上方，用螢幕座標，不受攝影機角度影響。</summary>
        static Canvas CreateOverlayCanvas()
        {
            var go = new GameObject("OverlayCanvas", typeof(Canvas), typeof(CanvasScaler),
                                    typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

            // 鎖定高度，畫面比 16:9 寬時上下的元件才不會被擠出可視範圍
            scaler.matchWidthOrHeight = 1f;
            return canvas;
        }

        static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        /// <summary>四家的名牌釘在畫面對應的那一邊，順序為自己、右、上、左。</summary>
        void BuildSeatLabels(Transform parent)
        {
            seatLabels = new Text[BoardLayout.SeatCount];
            seatLabels[0] = CreateSeatLabel(parent, "SeatBottom", new Vector2(1f, 0f),
                                            new Vector2(-24f, 212f), TextAnchor.MiddleRight);
            seatLabels[1] = CreateSeatLabel(parent, "SeatRight", new Vector2(1f, 0.5f),
                                            new Vector2(-24f, 300f), TextAnchor.MiddleRight);
            seatLabels[2] = CreateSeatLabel(parent, "SeatTop", new Vector2(0.5f, 1f),
                                            new Vector2(0f, -28f), TextAnchor.MiddleCenter);
            seatLabels[3] = CreateSeatLabel(parent, "SeatLeft", new Vector2(0f, 0.5f),
                                            new Vector2(24f, 300f), TextAnchor.MiddleLeft);
        }

        static Text CreateSeatLabel(Transform parent, string name, Vector2 anchor,
                                    Vector2 offset, TextAnchor alignment)
        {
            var label = UIFactory.CreateText(name, parent, "", 26, SeatColor, alignment);
            UIFactory.Anchor(label.rectTransform, anchor, anchor, offset, new Vector2(360f, 34f));
            return label;
        }

        void BuildStatusLabels(Transform parent)
        {
            statusLabel = UIFactory.CreateText("Status", parent, "", 24, StatusColor,
                                               TextAnchor.MiddleLeft);
            UIFactory.Anchor(statusLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                             new Vector2(24f, 212f), new Vector2(400f, 34f));

            centreInfo = UIFactory.CreateText("CentreInfo", parent, "", 22, StatusColor);
            UIFactory.Anchor(centreInfo.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                             new Vector2(24f, -46f), new Vector2(360f, 84f));
            centreInfo.alignment = TextAnchor.UpperLeft;

            var fontNotice = UIFactory.CreateText("FontNotice", parent,
                                                  "字型：" + UiFont.SourceDescription, 17, HintColor,
                                                  TextAnchor.UpperLeft);
            UIFactory.Anchor(fontNotice.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                             new Vector2(24f, -14f), new Vector2(760f, 24f));
        }

        /// <summary>宣告聽牌的按鈕，擺在動作按鈕列右側僅有的那段空位</summary>
        void BuildReadyButton(Transform parent)
        {
            readyButton = UIFactory.CreateButton("Ready", parent,
                                                 UiFont.SupportsChinese ? "聽" : "Ready",
                                                 new Vector2(100f, 60f),
                                                 ReadyButtonColor, Color.white, DeclareReady);
            UIFactory.Anchor((RectTransform)readyButton.transform,
                             new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                             new Vector2(555f, 206f), new Vector2(100f, 60f));
            readyButton.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------
        // 開新的一局
        // ------------------------------------------------------------

        IEnumerator StartNewHand(bool rotateDealer)
        {
            if (rotateDealer) dealerIndex = GameState.NextSeat(dealerIndex);

            state = GameState.CreateNewHand(dealerIndex, TileDef.EAST, dealerStreak, seed: 0);
            engine = new TurnEngine(state, fanTable);

            opponents = new AIPlayer[GameState.PlayerCount];
            for (int seat = 0; seat < GameState.PlayerCount; seat++)
                if (seat != HumanSeat) opponents[seat] = new AIPlayer(seat, state.Wall.Seed + seat);

            lastDrawnTile = TileObject.NoTile;
            pendingHumanAction = null;
            humanTurnOptions = null;
            humanDeclaredReady = false;
            readyAnnounced = false;
            readyButton.gameObject.SetActive(false);

            board.RevealHands = false;
            board.WinnerSeat = -1;
            board.HideWinningHand();
            resultView.Hide();
            RefreshAll();

            string dealerText = UiFont.SupportsChinese
                ? (dealerIndex == HumanSeat ? "你坐莊" : "本局莊家：" + SeatName(dealerIndex))
                : (dealerIndex == HumanSeat ? "You are the dealer" : "Dealer: seat " + dealerIndex);
            yield return announcement.Play(UiFont.SupportsChinese ? "開局" : "Start",
                                           dealerText, AnnouncementView.NeutralColor);

            yield return PlayHand();
        }

        void RefreshAll()
        {
            board.Refresh(state);
            RefreshLabels();
        }

        void RefreshLabels()
        {
            bool chinese = UiFont.SupportsChinese;

            for (int offset = 0; offset < BoardLayout.SeatCount; offset++)
            {
                int seat = (HumanSeat + offset) % BoardLayout.SeatCount;
                var player = state.Players[seat];
                bool isTurn = state.CurrentPlayer == seat && state.Phase != GamePhase.Ended;

                var label = seatLabels[offset];
                label.text = BuildSeatText(player, seat, chinese);
                label.color = seat == board.WinnerSeat ? WinnerSeatColor
                            : (isTurn ? ActiveSeatColor : SeatColor);
                label.fontStyle = (isTurn || seat == board.WinnerSeat)
                    ? FontStyle.Bold : FontStyle.Normal;
            }

            int remaining = state.Wall == null ? 0 : state.Wall.Remaining;
            centreInfo.text = chinese
                ? string.Format("{0}風圈\n剩 {1} 張{2}",
                    TileDef.Name(state.RoundWind), remaining,
                    state.DealerStreak > 0 ? "\n莊家連 " + state.DealerStreak : "")
                : string.Format("{0} round\n{1} tiles left", state.RoundWind, remaining);
        }

        string BuildSeatText(PlayerState player, int seat, bool chinese)
        {
            string wind = chinese ? TileDef.Name(player.SeatWind) + "家" : "Seat " + seat;
            string dealer = seat == dealerIndex ? (chinese ? "・莊" : " (D)") : "";
            string you = seat == HumanSeat ? (chinese ? "・你" : " YOU") : "";
            string flowers = player.Flowers.Count > 0
                ? (chinese ? "・花" : " F") + player.Flowers.Count : "";
            string winner = seat == board.WinnerSeat ? (chinese ? "　胡牌！" : "  WIN!") : "";
            return wind + dealer + you + flowers + winner;
        }

        void SetStatus(string message) => statusLabel.text = message ?? "";

        // ------------------------------------------------------------
        // 主迴圈
        // ------------------------------------------------------------

        IEnumerator PlayHand()
        {
            TurnResult finalResult = null;

            while (state.Phase != GamePhase.Ended)
            {
                if (state.Phase == GamePhase.WaitingDraw)
                {
                    var result = engine.DrawForCurrentPlayer();
                    lastDrawnTile = result.DrawnTile;
                    if (result.EndReason != GameEndReason.None) finalResult = result;
                    RefreshAll();
                    yield return new WaitForSeconds(DrawPause);
                }
                else if (state.Phase == GamePhase.WaitingDiscard)
                {
                    yield return RunDiscardPhase(r => finalResult = r);
                }
                else if (state.Phase == GamePhase.WaitingClaim)
                {
                    yield return RunClaimPhase(r => finalResult = r);
                }
                else
                {
                    break;
                }
            }

            yield return ShowResult(finalResult);
        }

        IEnumerator RunDiscardPhase(System.Action<TurnResult> reportEnd)
        {
            int seat = state.CurrentPlayer;
            GameAction chosen;

            if (seat == HumanSeat)
            {
                yield return WaitForHumanTurnAction();
                chosen = pendingHumanAction;
            }
            else
            {
                SetStatus(UiFont.SupportsChinese
                    ? SeatName(seat) + " 思考中…" : "Seat " + seat + " thinking...");
                yield return new WaitForSeconds(AiThinkDelay);
                chosen = opponents[seat].ChooseTurnAction(state, engine.GetTurnActions(seat));
            }

            if (chosen == null) yield break;

            bool announceReady = seat == HumanSeat && humanDeclaredReady && !readyAnnounced;
            var result = engine.ApplyTurnAction(chosen);
            if (!result.Success)
            {
                SetStatus(result.Error);
                yield break;
            }

            lastDrawnTile = result.DrawnTile != GameState.NoTile
                ? result.DrawnTile : TileObject.NoTile;
            if (result.EndReason != GameEndReason.None) reportEnd(result);

            SetStatus("");
            RefreshAll();

            if (announceReady)
            {
                readyAnnounced = true;
                yield return announcement.Play(UiFont.SupportsChinese ? "聽" : "READY",
                                               UiFont.SupportsChinese ? "牌型已固定" : "Hand locked",
                                               ReadyColor);
            }
            yield return AnnounceIfNeeded(chosen);
        }

        IEnumerator RunClaimPhase(System.Action<TurnResult> reportEnd)
        {
            var declarations = CollectAiDeclarations();
            var humanOptions = engine.GetClaimActions(HumanSeat);

            // 宣告聽牌之後不再吃碰槓，只有能胡才出手
            if (humanDeclaredReady)
            {
                foreach (var option in humanOptions)
                    if (option.Type == ActionType.Win) declarations.Add(option);
            }
            else if (HasRealChoice(humanOptions))
            {
                yield return WaitForHumanClaim(humanOptions);
                if (pendingHumanAction != null && pendingHumanAction.Type != ActionType.Pass)
                    declarations.Add(pendingHumanAction);
            }
            else
            {
                yield return new WaitForSeconds(AiThinkDelay * 0.5f);
            }

            var result = engine.ResolveClaims(declarations);
            if (!result.Success)
            {
                SetStatus(result.Error);
                yield break;
            }

            lastDrawnTile = result.DrawnTile != GameState.NoTile
                ? result.DrawnTile : TileObject.NoTile;
            if (result.EndReason != GameEndReason.None) reportEnd(result);

            RefreshAll();
            yield return AnnounceIfNeeded(result.Applied);
        }

        List<GameAction> CollectAiDeclarations()
        {
            var declarations = new List<GameAction>();
            for (int seat = 0; seat < GameState.PlayerCount; seat++)
            {
                if (seat == HumanSeat || seat == state.LastDiscardFrom) continue;
                var choice = opponents[seat].ChooseClaimAction(state, engine.GetClaimActions(seat));
                if (choice != null && choice.Type != ActionType.Pass) declarations.Add(choice);
            }
            return declarations;
        }

        static bool HasRealChoice(List<GameAction> options)
        {
            foreach (var option in options)
                if (option.Type != ActionType.Pass) return true;
            return false;
        }

        // ------------------------------------------------------------
        // 中央大字
        // ------------------------------------------------------------

        IEnumerator AnnounceIfNeeded(GameAction action)
        {
            if (action == null) yield break;

            string headline = HeadlineFor(action.Type, action.Tile == GameState.NoTile);
            if (headline == null) yield break;

            var color = action.Type == ActionType.Win
                ? AnnouncementView.WinColor : AnnouncementView.ClaimColor;
            yield return announcement.Play(headline, SeatName(action.SeatIndex), color);
        }

        static string HeadlineFor(ActionType type, bool selfDraw)
        {
            bool chinese = UiFont.SupportsChinese;
            switch (type)
            {
                case ActionType.Chi: return chinese ? "吃" : "CHI";
                case ActionType.Pon: return chinese ? "碰" : "PON";
                case ActionType.MinKan:
                case ActionType.AnKan:
                case ActionType.AddKan: return chinese ? "槓" : "KAN";
                case ActionType.Win:
                    if (chinese) return selfDraw ? "自摸" : "胡";
                    return selfDraw ? "TSUMO" : "WIN";
                default: return null;
            }
        }

        // ------------------------------------------------------------
        // 等玩家操作
        // ------------------------------------------------------------

        IEnumerator WaitForHumanTurnAction()
        {
            pendingHumanAction = null;
            humanTurnOptions = engine.GetTurnActions(HumanSeat);

            // 宣告聽牌之後牌型固定，摸到什麼打什麼，只有能胡才停下來
            if (humanDeclaredReady)
            {
                pendingHumanAction = AutoPlayAction();
                if (pendingHumanAction != null)
                {
                    SetStatus(UiFont.SupportsChinese ? "已聽牌，自動打出" : "Ready - auto discard");
                    yield return new WaitForSeconds(AiThinkDelay);
                    yield break;
                }
                humanDeclaredReady = false;   // 理論上不會發生，退回手動免得卡住
            }

            SetStatus(UiFont.SupportsChinese ? "輪到你出牌（點兩下打出）" : "Your turn: tap twice");
            actionButtons.Show(humanTurnOptions);
            readyButton.gameObject.SetActive(CanDeclareReadyNow());
            board.SetHandInteractable(true);

            while (pendingHumanAction == null) yield return null;

            board.SetHandInteractable(false);
            actionButtons.Hide();
            readyButton.gameObject.SetActive(false);
            SetStatus("");
        }

        IEnumerator WaitForHumanClaim(List<GameAction> options)
        {
            pendingHumanAction = null;
            humanTurnOptions = null;

            SetStatus(UiFont.SupportsChinese ? "要不要叫牌？滑按鈕看用掉哪幾張" : "Claim it?");
            actionButtons.Show(options);

            while (pendingHumanAction == null) yield return null;

            actionButtons.Hide();
            board.ClearClaimHighlight();
            SetStatus("");
        }

        bool CanDeclareReadyNow()
            => !humanDeclaredReady
               && lastDrawnTile != TileObject.NoTile
               && engine.IsReadyAfterDiscarding(HumanSeat, lastDrawnTile);

        void DeclareReady()
        {
            if (!CanDeclareReadyNow()) return;
            humanDeclaredReady = true;
            pendingHumanAction = FindDiscardAction(lastDrawnTile);
        }

        GameAction AutoPlayAction()
        {
            foreach (var option in humanTurnOptions)
                if (option.Type == ActionType.Win) return option;

            var drawn = FindDiscardAction(lastDrawnTile);
            if (drawn != null) return drawn;

            foreach (var option in humanTurnOptions)
                if (option.Type == ActionType.Discard) return option;
            return null;
        }

        GameAction FindDiscardAction(int tile)
        {
            if (humanTurnOptions == null) return null;
            foreach (var option in humanTurnOptions)
                if (option.Type == ActionType.Discard && option.Tile == tile) return option;
            return null;
        }

        void OnHumanTileChosen(int tile)
        {
            var action = FindDiscardAction(tile);
            if (action != null) pendingHumanAction = action;
        }

        void OnHumanActionChosen(GameAction action) => pendingHumanAction = action;

        /// <summary>只有滑鼠移到某個叫牌按鈕上時才標出那一組會用掉的牌，移開就清掉。</summary>
        void OnHumanActionHovered(GameAction action)
            => board.SetClaimHighlight(TilesUsedBy(action));

        static int[] TilesUsedBy(GameAction action)
        {
            if (action == null) return null;
            var used = new int[TileDef.KINDS];

            switch (action.Type)
            {
                case ActionType.Pon: used[action.Tile] = 2; return used;
                case ActionType.MinKan: used[action.Tile] = 3; return used;
                case ActionType.Chi:
                    for (int offset = 0; offset < 3; offset++)
                    {
                        int tile = action.ChiBaseTile + offset;
                        if (tile != action.Tile) used[tile]++;
                    }
                    return used;
                default: return null;
            }
        }

        // ------------------------------------------------------------
        // 結算
        // ------------------------------------------------------------

        IEnumerator ShowResult(TurnResult result)
        {
            board.SetHandInteractable(false);
            board.ClearClaimHighlight();
            actionButtons.Hide();
            readyButton.gameObject.SetActive(false);

            board.RevealHands = true;
            board.WinnerSeat = result == null ? -1 : result.WinnerSeat;
            if (result != null && result.EndReason == GameEndReason.Win)
                board.ShowWinningHand(state.Players[result.WinnerSeat], result.WinningTile);
            RefreshAll();

            SetStatus(UiFont.SupportsChinese ? "全部攤牌" : "Hands revealed");
            yield return new WaitForSeconds(RevealPause);
            SetStatus("");

            if (result != null && result.EndReason == GameEndReason.Win)
            {
                bool dealerKeepsSeat = result.WinnerSeat == dealerIndex;
                resultView.ShowWin(state, result, HumanSeat, () => NextHand(dealerKeepsSeat));
                yield break;
            }

            yield return announcement.Play(UiFont.SupportsChinese ? "流局" : "DRAW",
                                           UiFont.SupportsChinese ? "牌山抽乾" : "Wall exhausted",
                                           AnnouncementView.NeutralColor);
            resultView.ShowDraw(() => NextHand(true));   // 流局連莊
        }

        void NextHand(bool dealerKeepsSeat)
        {
            dealerStreak = dealerKeepsSeat ? dealerStreak + 1 : 0;
            StopAllCoroutines();
            StartCoroutine(StartNewHand(rotateDealer: !dealerKeepsSeat));
        }

        string SeatName(int seat)
        {
            if (seat < 0 || seat >= GameState.PlayerCount || state.Players[seat] == null) return "";
            if (!UiFont.SupportsChinese) return "Seat " + seat;
            string wind = TileDef.Name(state.Players[seat].SeatWind) + "家";
            return seat == HumanSeat ? "你（" + wind + "）" : wind;
        }
    }
}
