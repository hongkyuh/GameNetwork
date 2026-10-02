using UnityEngine;

namespace BattleArena
{
    public enum ItemType
    {
        Coin = 0,   // 1점
        Gem  = 1,   // 5점
    }

    /// <summary>
    /// 맵에 놓인 아이템 하나
    ///
    /// [2주차 안내]
    /// 지금은 클라이언트가 아이템을 만들고 획득 판정도 합니다.
    /// 나중에는 서버가 만들고, 서버가 판정합니다.
    /// (클라이언트가 판정하면 치팅이 가능하기 때문)
    /// </summary>
    public class ItemState
    {
        public int      ItemId { get; set; }
        public int      X      { get; set; }
        public int      Y      { get; set; }
        public ItemType Type   { get; set; }

        public int Score
        {
            get { return Type == ItemType.Gem ? GameConfig.SCORE_GEM : GameConfig.SCORE_COIN; }
        }

        public Color Color
        {
            get { return Type == ItemType.Gem ? GameConfig.ColorGem : GameConfig.ColorCoin; }
        }

        public float Size
        {
            get { return Type == ItemType.Gem ? 0.55f : 0.4f; }
        }

        public Vector3 WorldPosition
        {
            get { return PlayerState.GridToWorld(X, Y); }
        }
    }
}
