using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Mahjong.View
{
    // ============================================================
    // 開局的洗牌與發牌演出
    //
    // 純粹是視覺效果：真正的洗牌與發牌在 GameState.CreateNewHand 就做完了，
    // 這裡只是把過程演給玩家看，一張真牌都沒動到。
    //
    // 三個階段：
    //   1. 疊牌   —— 牌從畫面外飛進中央堆成一疊
    //   2. 洗牌   —— 整疊抖動並來回錯位
    //   3. 發牌   —— 一輪一輪飛向四家座位，抵達後淡出
    // ============================================================

    public class DealAnimation : MonoBehaviour
    {
        static readonly Vector2 TileSize = new Vector2(44f, 60f);
        const int VisualTileCount = 24;      // 演出用的張數，不必等於真實牌數
        const int TilesPerRound = 4;         // 一輪發給四家各一張
        const float GatherDuration = 0.34f;
        const float ShuffleDuration = 0.85f;
        const float DealStepDuration = 0.085f;
        const float ShuffleRadius = 46f;
        const float FadeStartProgress = 0.75f;   // 飛到七成五才開始淡出

        readonly List<TileView> tiles = new List<TileView>();
        System.Random rng;

        public static DealAnimation Create(Transform parent)
        {
            var rect = UIFactory.CreateRect("DealAnimation", parent);
            UIFactory.Stretch(rect);
            return rect.gameObject.AddComponent<DealAnimation>();
        }

        /// <summary>
        /// 播放整段開局演出。
        /// </summary>
        /// <param name="seatTargets">四個座位在畫面上的位置，index 0 為玩家自己（下方）</param>
        /// <param name="seed">讓每一局的洗牌抖動不同，但同 seed 可重現</param>
        public IEnumerator Play(Vector2[] seatTargets, int seed)
        {
            rng = new System.Random(seed);
            BuildTiles();

            yield return GatherToCentre();
            yield return Shuffle();
            yield return DealToSeats(seatTargets);

            ClearTiles();
        }

        // ------------------------------------------------------------

        void BuildTiles()
        {
            ClearTiles();
            for (int i = 0; i < VisualTileCount; i++)
            {
                var tile = TileView.Create(transform, TileView.NoTile, TileSize, faceUp: false);
                tile.SetInteractable(false);
                UIFactory.Anchor(tile.Rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                 RandomOffscreenPosition(), TileSize);
                tiles.Add(tile);
            }
        }

        Vector2 RandomOffscreenPosition()
        {
            // 從畫面四邊之外隨機一點飛進來
            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 1400f;
        }

        IEnumerator GatherToCentre()
        {
            var starts = new Vector2[tiles.Count];
            var ends = new Vector2[tiles.Count];
            for (int i = 0; i < tiles.Count; i++)
            {
                starts[i] = tiles[i].Rect.anchoredPosition;
                // 中央堆成一小疊，每張稍微錯開才看得出是一疊牌
                ends[i] = new Vector2(RandomRange(-18f, 18f), RandomRange(-14f, 14f) + i * 0.8f);
            }

            yield return Tween(GatherDuration, progress =>
            {
                float eased = EaseOut(progress);
                for (int i = 0; i < tiles.Count; i++)
                    tiles[i].Rect.anchoredPosition = Vector2.Lerp(starts[i], ends[i], eased);
            });
        }

        IEnumerator Shuffle()
        {
            var phases = new float[tiles.Count];
            var radii = new float[tiles.Count];
            for (int i = 0; i < tiles.Count; i++)
            {
                phases[i] = (float)rng.NextDouble() * Mathf.PI * 2f;
                radii[i] = RandomRange(ShuffleRadius * 0.4f, ShuffleRadius);
            }

            yield return Tween(ShuffleDuration, progress =>
            {
                // 前段抖得大，後段收斂，看起來像洗完牌慢慢停下
                float damping = 1f - EaseOut(progress);
                float spin = progress * Mathf.PI * 6f;
                for (int i = 0; i < tiles.Count; i++)
                {
                    float angle = phases[i] + spin;
                    tiles[i].Rect.anchoredPosition = new Vector2(
                        Mathf.Cos(angle) * radii[i] * damping,
                        Mathf.Sin(angle * 1.3f) * radii[i] * 0.6f * damping);
                    tiles[i].Rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(angle) * 12f * damping);
                }
            });

            foreach (var tile in tiles) tile.Rect.localRotation = Quaternion.identity;
        }

        IEnumerator DealToSeats(Vector2[] seatTargets)
        {
            int rounds = tiles.Count / TilesPerRound;

            for (int round = 0; round < rounds; round++)
                for (int seat = 0; seat < TilesPerRound; seat++)
                {
                    int index = round * TilesPerRound + seat;
                    if (index >= tiles.Count) yield break;

                    var target = seatTargets[seat % seatTargets.Length];
                    StartCoroutine(FlyToSeat(tiles[index], target));
                    yield return new WaitForSeconds(DealStepDuration);
                }

            // 等最後幾張飛完
            yield return new WaitForSeconds(0.28f);
        }

        IEnumerator FlyToSeat(TileView tile, Vector2 target)
        {
            var start = tile.Rect.anchoredPosition;
            yield return Tween(0.30f, progress =>
            {
                if (tile == null) return;
                float eased = EaseOut(progress);
                tile.Rect.anchoredPosition = Vector2.Lerp(start, target, eased);
                tile.Rect.localScale = Vector3.one * Mathf.Lerp(1f, 0.72f, eased);
                tile.SetAlpha(1f - eased * eased);
            });

            if (tile != null) tile.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------

        /// <summary>
        /// 從指定位置飛一張牌到座位上，用來讓玩家看清楚這張是從牌山哪一端摸的。
        /// 純視覺，牌局狀態早就更新完了。
        /// </summary>
        public IEnumerator FlyTile(Vector2 from, Vector2 to, float duration)
        {
            var tile = TileView.Create(transform, TileView.NoTile, TileSize, faceUp: false);
            tile.SetInteractable(false);
            UIFactory.Anchor(tile.Rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), from, TileSize);

            yield return Tween(duration, progress =>
            {
                if (tile == null) return;
                float eased = EaseOut(progress);
                tile.Rect.anchoredPosition = Vector2.Lerp(from, to, eased);
                tile.Rect.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, eased);

                // 一路保持不透明，只在最後一小段收掉，中途才看得清楚
                float fade = Mathf.InverseLerp(FadeStartProgress, 1f, progress);
                tile.SetAlpha(1f - fade);
            });

            if (tile == null) yield break;
            tile.gameObject.SetActive(false);
            Destroy(tile.gameObject);
        }

        static IEnumerator Tween(float duration, System.Action<float> step)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                step(Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            step(1f);
        }

        static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        float RandomRange(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        void ClearTiles()
        {
            foreach (var tile in tiles)
            {
                if (tile == null) continue;
                tile.gameObject.SetActive(false);
                Destroy(tile.gameObject);
            }
            tiles.Clear();
        }
    }
}
