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

        static readonly Color TableColor = new Color(0.09f, 0.30f, 0.22f);
        static readonly Color HintColor = new Color(0.55f, 0.62f, 0.57f);

        const float AiThinkDelay = 0.32f;
        const float DrawDelay = 0.16f;

        GameState state;
        TurnEngine engine;
        FanTable fanTable;
        AIPlayer[] opponents;

        TableView tableView;
        HandView handView;
        ActionButtons actionButtons;
        ResultView resultView;
        Text fontNotice;

        GameAction pendingHumanAction;
        List<GameAction> humanTurnOptions;
        int lastDrawnTile = TileView.NoTile;
        int dealerIndex;
        int dealerStreak;
        int handSeed;

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
            StartNewHand(rotateDealer: false);
        }

        // ------------------------------------------------------------
        // 組畫面
        // ------------------------------------------------------------

        void BuildScene()
        {
            EnsureEventSystem();
            var canvas = CreateCanvas();

            var background = UIFactory.CreateImage("Background", canvas.transform, TableColor,
                                                   rounded: false, raycast: false);
            UIFactory.Stretch(background.rectTransform);

            tableView = TableView.Create(canvas.transform, HumanSeat);

            handView = HandView.Create(canvas.transform);
            handView.TileChosen += OnHumanTileChosen;

            actionButtons = ActionButtons.Create(canvas.transform);
            actionButtons.ActionChosen += OnHumanActionChosen;

            resultView = ResultView.Create(canvas.transform);

            fontNotice = UIFactory.CreateText("FontNotice", canvas.transform,
                                              "字型：" + UiFont.SourceDescription, 18, HintColor,
                                              TextAnchor.LowerLeft);
            UIFactory.Anchor(fontNotice.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                             new Vector2(12f, 8f), new Vector2(900f, 24f));
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

        void StartNewHand(bool rotateDealer)
        {
            if (rotateDealer) dealerIndex = GameState.NextSeat(dealerIndex);

            handSeed++;
            state = GameState.CreateNewHand(dealerIndex, TileDef.EAST, dealerStreak, seed: 0);
            engine = new TurnEngine(state, fanTable);

            opponents = new AIPlayer[GameState.PlayerCount];
            for (int seat = 0; seat < GameState.PlayerCount; seat++)
                if (seat != HumanSeat) opponents[seat] = new AIPlayer(seat, state.Wall.Seed + seat);

            lastDrawnTile = TileView.NoTile;
            resultView.Hide();
            RefreshAll();

            StopAllCoroutines();
            StartCoroutine(PlayHand());
        }

        void RefreshAll()
        {
            tableView.Refresh(state);
            handView.Refresh(state.Players[HumanSeat],
                             state.CurrentPlayer == HumanSeat ? lastDrawnTile : TileView.NoTile);
        }

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

            ShowResult(finalResult);
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
                tableView.SetMessage(DescribeTurn(seat));
                yield return new WaitForSeconds(AiThinkDelay);
                chosen = opponents[seat].ChooseTurnAction(state, engine.GetTurnActions(seat));
            }

            if (chosen == null) yield break;

            var result = engine.ApplyTurnAction(chosen);
            if (!result.Success)
            {
                tableView.SetMessage(result.Error);
                yield break;
            }

            // 槓完會補一張，補進來的那張要標示出來；打牌則清掉標示
            lastDrawnTile = result.DrawnTile != GameState.NoTile ? result.DrawnTile : TileView.NoTile;
            if (result.EndReason != GameEndReason.None) reportEnd(result);
            RefreshAll();
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
                tableView.SetMessage(result.Error);
                yield break;
            }

            lastDrawnTile = result.DrawnTile != GameState.NoTile ? result.DrawnTile : TileView.NoTile;
            if (result.EndReason != GameEndReason.None) reportEnd(result);
            tableView.SetMessage(DescribeClaim(result));
            RefreshAll();
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
        // 等玩家操作
        // ------------------------------------------------------------

        IEnumerator WaitForHumanTurnAction()
        {
            pendingHumanAction = null;
            humanTurnOptions = engine.GetTurnActions(HumanSeat);

            tableView.SetMessage(UiFont.SupportsChinese ? "輪到你出牌，點兩下打出" : "Your turn - tap twice to discard");
            actionButtons.Show(humanTurnOptions);
            handView.SetInteractable(true);

            while (pendingHumanAction == null) yield return null;

            handView.SetInteractable(false);
            actionButtons.Hide();
            tableView.SetMessage("");
        }

        IEnumerator WaitForHumanClaim(List<GameAction> options)
        {
            pendingHumanAction = null;
            humanTurnOptions = null;

            tableView.SetMessage(UiFont.SupportsChinese ? "要不要叫牌？" : "Claim the discard?");
            actionButtons.Show(options);

            while (pendingHumanAction == null) yield return null;

            actionButtons.Hide();
            tableView.SetMessage("");
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

        void ShowResult(TurnResult result)
        {
            handView.SetInteractable(false);
            actionButtons.Hide();
            tableView.SetMessage("");
            RefreshAll();

            bool dealerKeepsSeat;
            if (result != null && result.EndReason == GameEndReason.Win)
            {
                dealerKeepsSeat = result.WinnerSeat == dealerIndex;
                resultView.ShowWin(state, result, HumanSeat, () => NextHand(dealerKeepsSeat));
            }
            else
            {
                dealerKeepsSeat = true;   // 流局連莊
                resultView.ShowDraw(() => NextHand(true));
            }
        }

        void NextHand(bool dealerKeepsSeat)
        {
            dealerStreak = dealerKeepsSeat ? dealerStreak + 1 : 0;
            StartNewHand(rotateDealer: !dealerKeepsSeat);
        }

        // ------------------------------------------------------------

        string DescribeTurn(int seat)
        {
            string wind = UiFont.SupportsChinese
                ? TileDef.Name(state.Players[seat].SeatWind) + "家思考中…"
                : "Seat " + seat + " thinking...";
            return wind;
        }

        string DescribeClaim(TurnResult result)
        {
            if (result.Applied == null) return "";
            if (!UiFont.SupportsChinese) return result.Applied.Type.ToString();

            string wind = TileDef.Name(state.Players[result.Applied.SeatIndex].SeatWind) + "家";
            switch (result.Applied.Type)
            {
                case ActionType.Chi: return wind + " 吃";
                case ActionType.Pon: return wind + " 碰";
                case ActionType.MinKan: return wind + " 槓";
                case ActionType.Win: return wind + " 胡牌";
                default: return "";
            }
        }
    }
}
