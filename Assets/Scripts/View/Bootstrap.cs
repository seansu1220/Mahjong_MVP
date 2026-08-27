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
        static readonly Color ReadyColor = new Color(1f, 0.78f, 0.30f);
        static readonly Color ReadyButtonColor = new Color(0.72f, 0.45f, 0.10f);

        const float AiThinkDelay = 0.30f;
        const float DrawFlightDuration = 0.5f;
        const float RevealPause = 1.1f;      // 攤牌後先讓玩家看一眼再蓋上結算視窗

        WinningHandView winningHand;

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

        Button readyButton;
        bool humanDeclaredReady;
        bool readyAnnounced;
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
            actionButtons.ActionHovered += OnHumanActionHovered;

            winningHand = WinningHandView.Create(canvas.transform);
            resultView = ResultView.Create(canvas.transform);
            announcement = AnnouncementView.Create(canvas.transform);
            dealAnimation = DealAnimation.Create(canvas.transform);

            BuildStatusLabels(canvas.transform);
            BuildReadyButton(canvas.transform);
        }

        /// <summary>
        /// 宣告聽牌用的按鈕。放在動作按鈕列右側僅有的那段空位，
        /// 只有「打掉剛摸的那張之後仍然聽牌」時才會出現。
        /// </summary>
        void BuildReadyButton(Transform parent)
        {
            readyButton = UIFactory.CreateButton("Ready", parent,
                                                 UiFont.SupportsChinese ? "聽" : "Ready",
                                                 new Vector2(100f, 60f),
                                                 ReadyButtonColor, Color.white,
                                                 DeclareReady);
            UIFactory.Anchor((RectTransform)readyButton.transform,
                             new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                             new Vector2(555f, 206f), new Vector2(100f, 60f));
            readyButton.gameObject.SetActive(false);
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
            // 原本擺在畫面中央上方，正好壓在牌山上排（y +234..+266）。
            // 改釘到左下角、自己手牌的正上方——提示是給玩家看的，離手邊最近最好讀，
            // 而且跟右下角的名牌、中間的動作按鈕左右分開，三者互不遮擋。
            statusLabel = UIFactory.CreateText("Status", parent, "", 24, StatusColor,
                                               TextAnchor.MiddleLeft);
            UIFactory.Anchor(statusLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                             new Vector2(24f, 212f), new Vector2(400f, 34f));

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

            // 鎖定高度：畫布的邏輯高度永遠是 1080，只有寬度隨畫面比例變化。
            // 先前設 0.5 會同時參考寬高，畫面比 16:9 寬時邏輯高度會縮到 1080 以下，
            // 釘在上方的對家名牌就被擠出可視範圍了。
            scaler.matchWidthOrHeight = 1f;
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
            humanDeclaredReady = false;
            readyAnnounced = false;
            readyButton.gameObject.SetActive(false);
            tableView.RevealHands = false;
            tableView.WinnerSeat = -1;
            winningHand.Hide();
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
                    // 位置要在摸之前先記下來，摸完那一墩就從牌山上消失了
                    var wallPosition = tableView.NextDrawPosition;
                    int drawingSeat = state.CurrentPlayer;

                    var result = engine.DrawForCurrentPlayer();
                    lastDrawnTile = result.DrawnTile;
                    if (result.EndReason != GameEndReason.None) finalResult = result;
                    RefreshAll();

                    yield return AnimateTileToSeat(wallPosition, drawingSeat);
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

            // 槓完要從牌尾補牌，先記下牌尾位置才能演出「從另一端補牌」
            bool isKan = chosen.Type == ActionType.AnKan || chosen.Type == ActionType.AddKan;
            var replacementPosition = tableView.NextReplacementPosition;

            bool announceReady = seat == HumanSeat && humanDeclaredReady && !readyAnnounced;

            var result = engine.ApplyTurnAction(chosen);
            if (!result.Success)
            {
                SetStatus(result.Error);
                yield break;
            }

            if (announceReady)
            {
                readyAnnounced = true;
                yield return announcement.Play(UiFont.SupportsChinese ? "聽" : "READY",
                                               UiFont.SupportsChinese ? "牌型已固定" : "Hand locked",
                                               ReadyColor);
            }

            // 槓完會補一張，補進來的那張要標示出來；打牌則清掉標示
            lastDrawnTile = result.DrawnTile != GameState.NoTile ? result.DrawnTile : TileView.NoTile;
            if (result.EndReason != GameEndReason.None) reportEnd(result);

            SetStatus("");
            RefreshAll();
            yield return AnnounceIfNeeded(chosen);

            if (isKan && result.DrawnTile != GameState.NoTile)
                yield return AnimateTileToSeat(replacementPosition, seat);
        }

        /// <summary>演出「牌從牌山某一端飛到某家手上」，讓玩家看得出是正常摸還是補牌。</summary>
        IEnumerator AnimateTileToSeat(Vector2 from, int seat)
        {
            var target = TableView.SeatAnchors[tableView.DisplayIndexOf(seat)];
            yield return dealAnimation.FlyTile(from, target, DrawFlightDuration);
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

            // 宣告聽牌之後牌型就固定了，摸到什麼打什麼，只有能胡才停下來
            if (humanDeclaredReady)
            {
                pendingHumanAction = AutoPlayAction();
                if (pendingHumanAction != null)
                {
                    SetStatus(UiFont.SupportsChinese ? "已聽牌，自動打出" : "Ready - auto discard");
                    yield return new WaitForSeconds(AiThinkDelay);
                    yield break;
                }
                // 理論上不會發生；真的找不到動作就退回手動，不要卡住牌局
                humanDeclaredReady = false;
            }

            SetStatus(UiFont.SupportsChinese ? "輪到你出牌（點兩下打出）" : "Your turn: tap twice");
            actionButtons.Show(humanTurnOptions);
            readyButton.gameObject.SetActive(CanDeclareReadyNow());
            handView.SetInteractable(true);

            while (pendingHumanAction == null) yield return null;

            handView.SetInteractable(false);
            actionButtons.Hide();
            readyButton.gameObject.SetActive(false);
            SetStatus("");
        }

        /// <summary>剛摸的那張打掉之後仍然聽牌，才讓玩家宣告</summary>
        bool CanDeclareReadyNow()
            => !humanDeclaredReady
               && lastDrawnTile != TileView.NoTile
               && engine.IsReadyAfterDiscarding(HumanSeat, lastDrawnTile);

        void DeclareReady()
        {
            if (!CanDeclareReadyNow()) return;
            humanDeclaredReady = true;
            pendingHumanAction = FindDiscardAction(lastDrawnTile);
        }

        /// <summary>宣告聽牌後的自動決策：能胡就胡，否則打掉剛摸的那張。</summary>
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

        IEnumerator WaitForHumanClaim(List<GameAction> options)
        {
            pendingHumanAction = null;
            humanTurnOptions = null;

            SetStatus(UiFont.SupportsChinese ? "要不要叫牌？滑按鈕看用掉哪幾張" : "Claim it?");
            actionButtons.Show(options);

            while (pendingHumanAction == null) yield return null;

            actionButtons.Hide();
            handView.ClearClaimHighlight();
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
        // 叫牌提示：標出手上會被拿去湊的牌
        // ------------------------------------------------------------

        /// <summary>
        /// 只有滑鼠移到某個叫牌按鈕上時才標出那一組會用掉的牌，移開就清掉。
        /// 吃可能有好幾種組法（例如手上 1245 條要吃 3 條，可組 123／234／345），
        /// 一次全標會把四張都標亮反而看不出差別，所以一次只標一組。
        /// </summary>
        void OnHumanActionHovered(GameAction action)
            => handView.SetClaimHighlight(TilesUsedBy(action));

        /// <summary>某個叫牌動作會從手上拿走哪幾張。胡牌用到整副手牌，不特別標。</summary>
        static int[] TilesUsedBy(GameAction action)
        {
            if (action == null) return null;
            var used = new int[TileDef.KINDS];

            switch (action.Type)
            {
                case ActionType.Pon:
                    used[action.Tile] = 2;
                    return used;

                case ActionType.MinKan:
                    used[action.Tile] = 3;
                    return used;

                case ActionType.Chi:
                    for (int offset = 0; offset < 3; offset++)
                    {
                        int tile = action.ChiBaseTile + offset;
                        if (tile != action.Tile) used[tile]++;
                    }
                    return used;

                default: return null;   // 胡、槓（自己回合）與過牌不需要標
            }
        }

        // ------------------------------------------------------------
        // 結算
        // ------------------------------------------------------------

        IEnumerator ShowResult(TurnResult result)
        {
            handView.SetInteractable(false);
            actionButtons.Hide();
            readyButton.gameObject.SetActive(false);
            handView.ClearClaimHighlight();
            SetStatus("");

            // 三家的手牌翻開，才看得到贏家到底做了什麼牌
            tableView.RevealHands = true;
            tableView.WinnerSeat = result == null ? -1 : result.WinnerSeat;
            RefreshAll();

            SetStatus(UiFont.SupportsChinese ? "全部攤牌" : "Hands revealed");

            if (result != null && result.EndReason == GameEndReason.Win)
                winningHand.Show(state.Players[result.WinnerSeat], result.WinningTile);

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
