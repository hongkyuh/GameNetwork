using UnityEngine;

namespace BattleArena
{
    /// <summary>
    /// 게임 전역 설정값
    /// 서버와 클라이언트가 동일한 값을 사용해야 합니다.
    /// </summary>
    public static class GameConfig
    {
        // ── 맵 크기 (격자 단위) ──
        public const int MAP_WIDTH  = 20;
        public const int MAP_HEIGHT = 14;

        // ── 게임 규칙 ──
        public const float ROUND_TIME     = 60f;   // 한 판 시간(초)
        public const int   MAX_PLAYERS    = 4;     // 방 최대 인원
        public const int   ITEM_MAX_COUNT = 8;     // 맵에 동시에 존재하는 아이템 수
        public const float ITEM_SPAWN_SEC = 1.5f;  // 아이템 생성 주기(초)

        // ── 이동 ──
        public const float MOVE_COOLDOWN = 0.12f;  // 이동 쿨다운(초)

        // ── 아이템 점수 ──
        public const int SCORE_COIN = 1;
        public const int SCORE_GEM  = 5;

        // ── 색상 (플레이어 4명) ──
        public static readonly Color[] PlayerColors = {
            new Color(0.18f, 0.75f, 0.44f),  // 초록 (나)
            new Color(0.98f, 0.38f, 0.40f),  // 빨강
            new Color(0.31f, 0.60f, 0.85f),  // 파랑
            new Color(0.96f, 0.62f, 0.14f),  // 주황
        };

        // ── 색상 (기타) ──
        public static readonly Color ColorBackground = new Color(0.10f, 0.12f, 0.23f);
        public static readonly Color ColorGrid       = new Color(0.16f, 0.19f, 0.32f);
        public static readonly Color ColorCoin       = new Color(0.98f, 0.91f, 0.58f);
        public static readonly Color ColorGem        = new Color(0.55f, 0.85f, 0.98f);

        /// <summary>격자 좌표가 맵 안에 있는지 검사</summary>
        public static bool IsInside(int x, int y)
        {
            return x >= 0 && x < MAP_WIDTH && y >= 0 && y < MAP_HEIGHT;
        }
    }
}
