using UnityEngine;

namespace BattleArena
{
    /// <summary>
    /// 플레이어 한 명의 상태
    ///
    /// [2주차 안내]
    /// 지금은 클라이언트에서만 관리합니다.
    /// 다음 주부터 이 정보를 서버와 주고받게 됩니다.
    /// </summary>
    public class PlayerState
    {
        public int    PlayerId { get; set; }
        public string Nickname { get; set; } = "Player";
        public int    X        { get; set; }
        public int    Y        { get; set; }
        public int    Score    { get; set; }
        public bool   IsLocal  { get; set; }   // 내 캐릭터인가?

        /// <summary>격자 좌표를 월드 좌표로 변환</summary>
        public Vector3 WorldPosition
        {
            get { return GridToWorld(X, Y); }
        }

        /// <summary>격자 좌표 → 월드 좌표 (맵 중앙 정렬)</summary>
        public static Vector3 GridToWorld(int x, int y)
        {
            float offsetX = (GameConfig.MAP_WIDTH  - 1) * 0.5f;
            float offsetY = (GameConfig.MAP_HEIGHT - 1) * 0.5f;
            return new Vector3(x - offsetX, y - offsetY, 0f);
        }
    }
}
