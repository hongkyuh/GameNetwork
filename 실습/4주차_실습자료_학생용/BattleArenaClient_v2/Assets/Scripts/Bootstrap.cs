using UnityEngine;

namespace BattleArena
{
    /// <summary>
    /// 자동 부팅 스크립트
    ///
    /// 씬에 아무것도 없어도 게임이 시작되도록 합니다.
    /// 덕분에 압축을 풀고 Unity에서 Play만 누르면 실행됩니다.
    ///
    /// [동작 원리]
    /// RuntimeInitializeOnLoadMethod 속성이 붙은 메서드는
    /// 씬이 로드된 직후 Unity가 자동으로 호출해 줍니다.
    /// </summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            // 이미 GameManager가 씬에 있으면 중복 생성하지 않음
            GameManager existing = Object.FindFirstObjectByType<GameManager>();
            if (existing != null) return;

            GameObject go = new GameObject("GameManager");
            go.AddComponent<GameManager>();

            Debug.Log("[BattleArena] 게임을 시작합니다. (서버 미연결 · 2주차 데모)");
        }
    }
}
