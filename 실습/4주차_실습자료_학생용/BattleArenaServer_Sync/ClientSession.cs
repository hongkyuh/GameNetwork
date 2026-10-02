using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BattleArenaServer
{
    /// <summary>
    /// 클라이언트 한 명과의 연결 — 비동기 버전
    ///
    /// ═══════════════════════════════════════════════
    /// [4주차 실습 대상]
    ///
    /// Run() 안의 while 루프가 통째로 동기입니다.
    /// Receive 에서 데이터를 기다리는 동안
    /// 이 쓰레드는 아무것도 못 합니다.
    ///
    /// 여러분이 할 일:
    /// Run() → RunAsync()
    /// Send() → await SendAsync()
    /// Receive() → await ReceiveAsync()
    ///
    /// ═══════════════════════════════════════════════
    /// </summary>
    public class ClientSession
    {
        public int PlayerId { get; private set; }
        public string Nickname { get; private set; } = "Unknown";

        private readonly Socket _socket;
        private bool _isConnected = true;

        public ClientSession(Socket socket, int playerId)
        {
            _socket = socket;
            PlayerId = playerId;
        }

        // ═══════════════════════════════════════
        // 수신 루프 (비동기)
        // ═══════════════════════════════════════
        public async Task RunAsync()
        {
            Console.WriteLine($"[접속] ID:{PlayerId}  {_socket.RemoteEndPoint}");

            try
            {
                while (_isConnected)
                {
                    string? json = await PacketHelper.ReceiveAsync(_socket);

                    if (json == null) break;

                    Console.WriteLine($"[수신] ID:{PlayerId}  {json}");
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
        // ═══════════════════════════════════════
        private async Task HandlePacketAsync(string json)
        {
            dynamic? packet = JsonConvert.DeserializeObject(json);
            if (packet == null) return;

            int type = (int)packet.Type;

            switch (type)
            {
                case PacketType.LOGIN:
                    await HandleLoginAsync(json);
                    break;

                default:
                    Console.WriteLine($"[경고] 모르는 패킷 타입: {type}");
                    break;
            }
        }

        private async Task HandleLoginAsync(string json)
        {
            LoginPacket? login = JsonConvert.DeserializeObject<LoginPacket>(json);
            if (login == null) return;

            Nickname = string.IsNullOrWhiteSpace(login.Nickname)
                ? $"Player{PlayerId}"
                : login.Nickname;

            Console.WriteLine($"[로그인] {Nickname} (ID:{PlayerId})");

            await SendAsync(new WelcomePacket
            {
                PlayerId = PlayerId,
                Message = $"{Nickname}님, 배틀아레나에 오신 것을 환영합니다!"
            });

            GameServer.Instance.Broadcast(new NoticePacket
            {
                Type = PacketType.PLAYER_IN,
                Message = $"{Nickname}님이 입장했습니다."
            }, exceptPlayerId: PlayerId);
        }

        // ═══════════════════════════════════════
        // 전송 (비동기)
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
        
        public void Send(object packet)
        {
            _ = SendAsync(packet);
        }

        // ═══════════════════════════════════════
        // 연결 종료
        // ═══════════════════════════════════════
        public void Disconnect()
        {
            if (!_isConnected) return;
            _isConnected = false;

            Console.WriteLine($"[퇴장] {Nickname} (ID:{PlayerId})");

            try
            {
                _socket.Shutdown(SocketShutdown.Both);
                _socket.Close();
            }
            catch { }

            GameServer.Instance.RemoveSession(PlayerId);

            GameServer.Instance.Broadcast(new NoticePacket
            {
                Type = PacketType.PLAYER_OUT,
                Message = $"{Nickname}님이 나갔습니다."
            });
        }
    }
}