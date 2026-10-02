using System;

namespace BattleArenaServer
{
    /// <summary>
    /// 패킷 프로토콜 정의
    ///
    /// ═══════════════════════════════════════════════
    /// 클라이언트 → 서버
    ///   Type 1   로그인      { Type:1, Nickname:"홍길동" }
    ///
    /// 서버 → 클라이언트
    ///   Type 11  환영 메시지  { Type:11, PlayerId:1, Message:"환영합니다" }
    ///   Type 20  입장 알림    { Type:20, Message:"철수님이 입장했습니다" }
    ///   Type 21  퇴장 알림    { Type:21, Message:"철수님이 나갔습니다" }
    /// ═══════════════════════════════════════════════
    ///
    /// [패킷 구조]
    ///
    ///   [ 길이(4바이트) ][ JSON 데이터 ]
    ///
    ///   왜 길이가 필요한가?
    ///   TCP는 "스트림"이라서 메시지 경계가 없습니다.
    ///   "안녕" + "반가워"를 보내면 "안녕반가워"로 붙어서 올 수 있어요.
    ///   그래서 앞에 길이를 붙여서 "여기까지가 한 메시지다"를 표시합니다.
    /// </summary>
    public static class PacketType
    {
        // 클라이언트 → 서버
        public const int LOGIN = 1;

        // 서버 → 클라이언트
        public const int WELCOME    = 11;
        public const int PLAYER_IN  = 20;
        public const int PLAYER_OUT = 21;
    }

    // ─────────────────────────────────────────
    // 패킷 데이터 클래스
    // ─────────────────────────────────────────

    /// <summary>클라이언트가 보내는 로그인 요청</summary>
    public class LoginPacket
    {
        public int    Type     { get; set; } = PacketType.LOGIN;
        public string Nickname { get; set; } = "";
    }

    /// <summary>서버가 보내는 환영 메시지</summary>
    public class WelcomePacket
    {
        public int    Type     { get; set; } = PacketType.WELCOME;
        public int    PlayerId { get; set; }
        public string Message  { get; set; } = "";
    }

    /// <summary>입장/퇴장 알림</summary>
    public class NoticePacket
    {
        public int    Type    { get; set; }
        public string Message { get; set; } = "";
    }
}
