namespace BattleArenaServer
{
    /// <summary>TCP 패킷 종류</summary>
    public static class PacketType
    {
        // 클라이언트 → 서버
        public const int LOGIN = 1;
        public const int CHAT  = 2;

        // 서버 → 클라이언트
        public const int WELCOME    = 11;
        public const int CHAT_ECHO  = 12;
        public const int PLAYER_IN  = 20;
        public const int PLAYER_OUT = 21;

        // ── 6주차 추가 ──
        //   놓치면 점수가 틀어지는 것들이라 TCP로 보냅니다
        public const int TAKEN = 30;   // 누가 어떤 아이템을 먹었다
        public const int SCORE = 31;   // 전체 점수판
    }

    /// <summary>
    /// UDP 패킷 종류 (문자열 기반)
    ///
    /// ★ 오늘 MOVE 의 의미가 바뀝니다
    ///
    ///   5주차  MOVE|id|시퀀스|X|Y        "나 여기 있어"   (좌표)
    ///   6주차  MOVE|id|시퀀스|DX|DY      "이쪽으로 갈래"  (방향)
    ///
    /// 좌표를 받으면 서버는 받아 적을 수밖에 없습니다.
    /// 방향만 받으면 좌표는 서버가 계산합니다.
    ///
    /// ★ STATE 도 같이 바뀝니다
    ///
    ///   5주차  STATE|id,닉,x,y|...
    ///   6주차  STATE|AckSeq|id,닉,x,y|...
    ///                 ↑
    ///          "네 입력 몇 번까지 썼다" 는 영수증
    ///
    /// 받는 사람마다 AckSeq 값이 다릅니다.
    /// 이 번호가 있어야 클라이언트가 자기 예측을 맞춰볼 수 있습니다.
    /// 없으면 예측은 그냥 추측이 됩니다.
    /// </summary>
    public static class UdpPacket
    {
        public const string MOVE  = "MOVE";
        public const string STATE = "STATE";
        public const string ITEMS = "ITEMS";
    }

    public class LoginPacket
    {
        public int    Type     { get; set; } = PacketType.LOGIN;
        public string Nickname { get; set; } = "";
    }

    public class ChatPacket
    {
        public int    Type    { get; set; } = PacketType.CHAT;
        public string Message { get; set; } = "";
    }

    public class WelcomePacket
    {
        public int    Type     { get; set; } = PacketType.WELCOME;
        public int    PlayerId { get; set; }
        public string Message  { get; set; } = "";
    }

    public class ChatEchoPacket
    {
        public int    Type     { get; set; } = PacketType.CHAT_ECHO;
        public string Nickname { get; set; } = "";
        public string Message  { get; set; } = "";
    }

    public class NoticePacket
    {
        public int    Type    { get; set; }
        public string Message { get; set; } = "";
    }

    // ═══════════════════════════════════════
    // 6주차 추가
    // ═══════════════════════════════════════

    /// <summary>아이템 획득 알림</summary>
    public class TakenPacket
    {
        public int    Type     { get; set; } = PacketType.TAKEN;
        public int    ItemId   { get; set; }
        public int    PlayerId { get; set; }
        public string Nickname { get; set; } = "";
    }

    /// <summary>
    /// 점수판
    ///
    /// Board 형식 : "1,홍길동,5|3,철수,2"
    /// </summary>
    public class ScorePacket
    {
        public int    Type  { get; set; } = PacketType.SCORE;
        public string Board { get; set; } = "";
    }
}
