using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BattleArena
{
    /// <summary>
    /// 클라이언트 한 명과의 연결 담당
    ///
    /// 접속자 1명당 이 객체가 1개 만들어집니다.
    /// </summary>
    public class ClientSession
    {
        public int    PlayerId { get; private set; }
        public string Nickname { get; private set; } = "Unknown";
        private volatile bool _isLoggedIn = false;
        public bool IsLoggedIn => _isLoggedIn;

        // ── 위치 정보 (UDP로 갱신됨) ──
        public int X { get; set; }
        public int Y { get; set; }

        /// <summary>이 플레이어의 UDP 주소. 첫 MOVE 패킷을 받을 때 채워집니다.</summary>
        public EndPoint? UdpEndPoint { get; set; }

        /// <summary>마지막으로 적용한 시퀀스 번호</summary>
        public int LastSeq { get; set; }

        private readonly Socket _socket;
        private bool _isConnected = true;

        public ClientSession(Socket socket, int playerId)
        {
            _socket  = socket;
            PlayerId = playerId;
        }

        // ═══════════════════════════════════════
        // 수신 루프
        // ═══════════════════════════════════════
        public async Task RunAsync()
        {
            try
            {
                while (_isConnected)
                {
                    string? json = await PacketHelper.ReceiveAsync(_socket);
                    if (json == null) break;

                    await HandlePacketAsync(json);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[에러] ID:{PlayerId}  {ex.Message}");
            }
            finally
            {
                Disconnect();
            }
        }

        // ═══════════════════════════════════════
        // 패킷 처리
        //
        // 수신 루프를 막지 않기 위해 비동기로 처리합니다.
        // ═══════════════════════════════════════
        private async Task HandlePacketAsync(string json)
        {
            try
            {
                JObject packet = JObject.Parse(json);
                JToken? typeToken = packet["Type"];

                if (typeToken == null ||
                    typeToken.Type != JTokenType.Integer ||
                    !int.TryParse(typeToken.ToString(), out int type))
                {
                    Console.WriteLine(
                        $"[경고] ID:{PlayerId} Type이 없거나 올바른 정수가 아닙니다.");
                    return;
                }

                switch (type)
                {
                    case PacketType.LOGIN:
                        await HandleLoginAsync(json);
                        break;

                    case PacketType.CHAT:
                        await HandleChatAsync(json);
                        break;

                    default:
                        Console.WriteLine(
                            $"[경고] ID:{PlayerId} 모르는 패킷 타입: {type}");
                        break;
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine(
                    $"[경고] ID:{PlayerId} 잘못된 JSON 패킷: {ex.Message}");
            }
        }

        private async Task HandleLoginAsync(string json)
        {
            if (_isLoggedIn) return;

            LoginPacket? login =
                JsonConvert.DeserializeObject<LoginPacket>(json);

            if (login == null) return;

            Nickname = string.IsNullOrWhiteSpace(login.Nickname)
                ? $"Player{PlayerId}"
                : login.Nickname;

            await SendAsync(new WelcomePacket
            {
                PlayerId = PlayerId,
                Message = $"{Nickname}님, 배틀아레나에 오신 것을 환영합니다!"
            });

            if (!_isConnected) return;

            _isLoggedIn = true;

            GameServer.Instance.RegisterJoinNotice(PlayerId, Nickname);
        }

        private async Task HandleChatAsync(string json)
        {
            ChatPacket? chat = JsonConvert.DeserializeObject<ChatPacket>(json);
            if (chat == null) return;

            // 받은 그대로 전체에게 중계합니다
            GameServer.Instance.Broadcast(new ChatEchoPacket
            {
                Nickname = Nickname,
                Message  = chat.Message
            });

            await Task.CompletedTask;
        }

        // ═══════════════════════════════════════
        // 전송
        // ═══════════════════════════════════════
        public async Task SendAsync(object packet)
        {
            if (!_isConnected) return;

            try
            {
                await PacketHelper.SendAsync(_socket, packet);
            }
            catch
            {
                Disconnect();
            }
        }

        // ═══════════════════════════════════════
        // 종료
        // ═══════════════════════════════════════
        public void Disconnect()
        {
            if (!_isConnected) return;
            _isConnected = false;

            try
            {
                _socket.Shutdown(SocketShutdown.Both);
                _socket.Close();
            }
            catch { }

            GameServer.Instance.RemoveSession(PlayerId);
        }
    }
}
