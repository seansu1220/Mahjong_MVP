using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mahjong.View
{
    // ============================================================
    // 進入點
    //
    // 場景裡不需要放任何東西：這支程式會在進入 Play 時自己生出
    // Canvas、EventSystem 與所有畫面元件，也不需要到 Inspector 拖拉引用。
    //
    // 職責只有兩件事：
    //   1. 組出畫面
    //   2. 驅動 TurnEngine 的主迴圈，把玩家點擊與 AI 決策餵給它
    // 所有規則判定都在 Core，這裡一條規則都不寫。
    // ============================================================

    public class Bootstrap : MonoBehaviour
    {
        public const int HumanSeat = 0;

        static readonly Color TableColor = new Color(0.07f, 0.26f, 0.19f);
        static readonly Color TableRim = new Color(0.05f, 0.18f, 0.13f);
        static readonly Color HintColor = new Color(0.55f, 0.62f, 0.57f);
        static readonly Color StatusColor = new Color(0.86f, 0.90f, 0.84f);

        const float AiThinkDelay = 0.30f;
        const float DrawDelay = 0.14f;

        GameState state;
        TurnEngine engine;
        FanTable fanTable;
        AIPlayer[] opponents;

        TableView tableView;
        HandView handView;
        ActionButtons actionButtons;
        ResultView resultView;
        AnnouncementView announcement;
        DealAnimation dealAnimation;
        Text statusLabel;

        GameAction pendingHumanAction;
        List<GameAction> humanTurnOptions;
        int lastDrawnTile = TileView.NoTile;
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
            var canvas = CreateCanvas();
            BuildBackground(canvas.transform);

            tableView = TableView.Create(canvas.transform, HumanSeat);

            handView = HandView.Create(canvas.transform);
            handView.TileChosen += OnHumanTileChosen;

            actionButtons = ActionButtons.Create(canvas.transform);
            actionButtons.ActionChosen += OnHumanActionChosen;

            resultView = ResultView.Create(canvas.transform);
            announcement = AnnouncementView.Create(canvas.transform);
            dealAnimation = DealAnimation.Create(canvas.transform);

            BuildStatusLabels(canvas.transform);
        }

        void BuildBackground(Transform parent)
        {
            var backdrop = UIFactory.CreateImage("Backdrop", parent, TableRim, rounded: false);
            UIFactory.Stretch(backdrop.rectTransform);

            // 中央鋪一塊比較亮的桌布，讓四邊自然收深，看起來像張真的牌桌
            var felt = UIFactory.CreateImage("Felt", parent, TableColor);
            UIFactory.Anchor(felt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             Vector2.zero, new Vector2(1560f, 900f));
        }

        void BuildStatusLabels(Transform parent)
        {
            // 擺在上家資訊面板（y +310..+530）與上家牌河（y +40..+220）之間的空白帶
            statusLabel = UIFactory.CreateText("Status", parent, "", 28, StatusColor);
            UIFactory.Anchor(statusLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             new Vector2(0f, 265f), new Vector2(900f, 38f));

            var fontNotice = UIFactory.CreateText("FontNotice", parent,
                                                  "字型：" + UiFont.SourceDescription, 17, HintColor,
                                                  TextAnchor.UpperLeft);
            UIFactory.Anchor(fontNotice.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                             new Vector2(14f, -10f), new Vector2(760f, 24f));
        }

        static Canvas CreateCanvas()
        {
            var go = new GameObject("MahjongCanvas", typeof(Canvas), typeof(CanvasScaler),
                                    typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
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

            lastDrawnTile = TileView.NoTile;
            pendingHumanAction = null;
            humanTurnOptions = null;
            resultView.Hide();

            yield return PlayDealAnimation();

            RefreshAll();
            yield return PlayHand();
        }

        /// <summary>開局的洗牌發牌演出。牌局本身已經發好，這裡純粹是給玩家看的。</summary>
        IEnumerator PlayDealAnimation()
        {
            tableView.gameObject.SetActive(false);
            handView.gameObject.SetActive(false);
            actionButtons.Hide();

            SetStatus(UiFont.SupportsChinese ? "洗牌中…" : "Shuffling...");
            yield return dealAnimation.Play(TableView.SeatAnchors, state.Wall.Seed);

            tableView.gameObject.SetActive(true);
            handView.gameObject.SetActive(true);
            SetStatus("");

            string dealerText = UiFont.SupportsChinese
                ? (dealerIndex == HumanSeat ? "你坐莊" : "本局莊家：" + SeatName(dealerIndex))
                : (dealerIndex == HumanSeat ? "You are the dealer" : "Dealer: seat " + dealerIndex);
            yield return announcement.Play(UiFont.SupportsChinese ? "開局" : "Start",
                                           dealerText, AnnouncementView.NeutralColor);
        }

        void RefreshAll()
        {
            tableView.Refresh(state);
            handView.Refresh(state.Players[HumanSeat],
                             state.CurrentPlayer == HumanSeat ? lastDrawnTile : TileView.NoTile);
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
                    yield return new WaitForSeconds(DrawDelay);
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
                    ? SeatName(seat) + " 思考中…"
                    : "Seat " + seat + " thinking...");
                yield return new WaitForSeconds(AiThinkDelay);
                chosen = opponents[seat].ChooseTurnAction(state, engine.GetTurnActions(seat));
            }

            if (chosen == null) yield break;

            var result = engine.ApplyTurnAction(chosen);
            if (!result.Success)
            {
                SetStatus(result.Error);
                yield break;
            }

            // 槓完會補一張，補進來的那張要標示出來；打牌則清掉標示
            lastDrawnTile = result.DrawnTile != GameState.NoTile ? result.DrawnTile : TileView.NoTile;
            if (result.EndReason != GameEndReason.None) reportEnd(result);

            SetStatus("");
            RefreshAll();
            yield return AnnounceIfNeeded(chosen);
        }

        IEnumerator RunClaimPhase(System.Action<TurnResult> reportEnd)
        {
            var declarations = CollectAiDeclarations();

            var humanOptions = engine.GetClaimActions(HumanSeat);
            if (HasRealChoice(humanOptions))
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

            lastDrawnTile = result.DrawnTile != GameState.NoTile ? result.DrawnTile : TileView.NoTile;
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
        // 中央大字提示
        // ------------------------------------------------------------

        IEnumerator AnnounceIfNeeded(GameAction action)
        {
            if (action == null) yield break;

            string headline = HeadlineFor(action.Type, action.Tile == GameState.NoTile);
            if (headline == null) yield break;

            var color = action.Type == ActionType.Win
                ? AnnouncementView.WinColor
                : AnnouncementView.ClaimColor;

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
                default: return null;   // 出牌與過牌不需要大字
            }
        }

        // ------------------------------------------------------------
        // 等玩家操作
        // ------------------------------------------------------------

        IEnumerator WaitForHumanTurnAction()
        {
            pendingHumanAction = null;
            humanTurnOptions = engine.GetTurnActions(HumanSeat);

            SetStatus(UiFont.SupportsChinese ? "輪到你出牌，點兩下打出" : "Your turn - tap twice to discard");
            actionButtons.Show(humanTurnOptions);
            handView.SetInteractable(true);

            while (pendingHumanAction == null) yield return null;

            handView.SetInteractable(false);
            actionButtons.Hide();
            SetStatus("");
        }

        IEnumerator WaitForHumanClaim(List<GameAction> options)
        {
            pendingHumanAction = null;
            humanTurnOptions = null;

            SetStatus(UiFont.SupportsChinese ? "要不要叫牌？" : "Claim the discard?");
            actionButtons.Show(options);

            while (pendingHumanAction == null) yield return null;

            actionButtons.Hide();
            SetStatus("");
        }

        void OnHumanTileChosen(int tile)
        {
            if (humanTurnOptions == null) return;
            foreach (var option in humanTurnOptions)
                if (option.Type == ActionType.Discard && option.Tile == tile)
                {
                    pendingHumanAction = option;
                    return;
                }
        }

        void OnHumanActionChosen(GameAction action) => pendingHumanAction = action;

        // ------------------------------------------------------------
        // 結算
        // ------------------------------------------------------------

        IEnumerator ShowResult(TurnResult result)
        {
            handView.SetInteractable(false);
            actionButtons.Hide();
            SetStatus("");
            RefreshAll();

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
