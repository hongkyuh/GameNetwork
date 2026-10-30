using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BattleArenaServer
{
    /// <summary>
    /// 클라이언트 한 명과의 연결을 담당
    ///
    /// 클라이언트가 접속할 때마다 이 객체가 하나씩 만들어집니다.
    /// 100명이 접속하면 ClientSession 객체도 100개가 됩니다.
    /// </summary>
    public class ClientSession
    {
        public int    PlayerId { get; private set; }
        public string Nickname { get; private set; } = "Unknown";

        // ── 5주차 추가 : 위치 정보 ──
        public int X { get; set; }
        public int Y { get; set; }

        /// <summary>
        /// 이 플레이어의 UDP 주소
        ///
        /// TCP 소켓과 UDP 주소는 별개입니다.
        /// 클라이언트가 UDP로 첫 MOVE 패킷을 보내면 그때 채워집니다.
        /// </summary>
        public System.Net.EndPoint? UdpEndPoint { get; set; }

        /// <summary>
        /// 마지막으로 받은 시퀀스 번호
        ///
        /// UDP는 순서를 보장하지 않아서
        /// 이것보다 작은 번호가 오면 오래된 패킷이므로 버립니다.
        /// </summary>
        public int LastSeq { get; set; }

        /// <summary>
        /// 마지막으로 "실제로 반영한" 시퀀스 번호 (6주차 추가)
        ///
        /// LastSeq 와 헷갈리기 쉬운데 역할이 다릅니다.
        ///
        ///   LastSeq   받아본 중 가장 큰 번호   — 중복·역순 패킷을 걸러내는 용도
        ///   AckSeq    걸음에 써먹은 번호       — 클라이언트에게 돌려주는 영수증
        ///
        /// 입력은 줄에 쌓여 있다가 틱마다 하나씩 쓰이므로
        /// 보통 AckSeq 가 LastSeq 보다 몇 개 뒤처져 있습니다.
        ///
        /// 이 값을 STATE 로 보내주면 클라이언트가
        /// "서버는 내 152번까지 처리했다" 를 알고
        /// 그 이후 입력만 다시 적용해 자기 예측을 맞춥니다.
        /// </summary>
        public int AckSeq { get; set; }

        /// <summary>
        /// 다음 걸음이 가능해지는 틱 (6주차 추가)
        ///
        /// 걸음을 옮길 때마다 "지금 틱 + STEP_TICKS" 로 밀어둡니다.
        /// 그 틱이 되기 전에는 입력이 쌓여 있어도 움직이지 않습니다.
        ///
        /// 이 한 줄이 속도핵을 막습니다.
        /// 클라이언트가 초당 300번 보내도 걸음 수는 서버가 셉니다.
        /// </summary>
        public long NextStepTick { get; set; }

        /// <summary>
        /// 모은 아이템 개수 (6주차 추가)
        ///
        /// 이 값도 서버만 바꿉니다.
        /// 클라이언트는 결과를 받아서 보여주기만 합니다.
        /// </summary>
        public int Score { get; set; }

        private readonly Socket _socket;
        private bool _isConnected = true;

        public ClientSession(Socket socket, int playerId)
        {
            _socket   = socket;
            PlayerId  = playerId;
        }

        // ═══════════════════════════════════════
        // 수신 루프 — 클라이언트가 보낸 패킷을 계속 받음
        // ═══════════════════════════════════════
        public async Task RunAsync()
        {
            Console.WriteLine($"[접속] ID:{PlayerId}  {_socket.RemoteEndPoint}");

            try
            {
                while (_isConnected)
                {
                    // 패킷 하나 받기
                    string? json = await PacketHelper.ReceiveAsync(_socket);

                    // null이면 연결이 끊긴 것
                    if (json == null) break;

                    Console.WriteLine($"[수신] ID:{PlayerId}  {json}");

                    // 패킷 처리
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
            // JSON에서 Type만 먼저 꺼내기
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

        /// <summary>로그인 처리</summary>
        private async Task HandleLoginAsync(string json)
        {
            LoginPacket? login = JsonConvert.DeserializeObject<LoginPacket>(json);
            if (login == null) return;

            Nickname = string.IsNullOrWhiteSpace(login.Nickname)
                ? $"Player{PlayerId}"
                : login.Nickname;

            Console.WriteLine($"[로그인] {Nickname} (ID:{PlayerId})");

            // 1. 본인에게 환영 메시지
            await SendAsync(new WelcomePacket
            {
                PlayerId = PlayerId,
                Message  = $"{Nickname}님, 배틀아레나에 오신 것을 환영합니다!"
            });

            // 2. 다른 사람들에게 입장 알림
            GameServer.Instance.Broadcast(new NoticePacket
            {
                Type    = PacketType.PLAYER_IN,
                Message = $"{Nickname}님이 입장했습니다."
            }, exceptPlayerId: PlayerId);
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

            // 서버에서 제거
            GameServer.Instance.RemoveSession(PlayerId);

            // 다른 사람들에게 퇴장 알림
            GameServer.Instance.Broadcast(new NoticePacket
            {
                Type    = PacketType.PLAYER_OUT,
                Message = $"{Nickname}님이 나갔습니다."
            });
        }
    }
}
