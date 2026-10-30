using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BattleArenaServer
{
    /// <summary>
    /// 배틀아레나 게임 서버
    ///
    /// ═══════════════════════════════════════════════
    /// [3주차 — TICKET-002]
    /// "클라팀이 접속 테스트를 원합니다"
    ///
    /// 오늘 하는 것:
    ///   - 서버 열기 (포트 7777)
    ///   - 클라이언트 접속 받기
    ///   - 로그인 처리 (닉네임 받고 환영 메시지 보내기)
    ///   - 입장/퇴장 알림 브로드캐스트
    ///
    /// 다음 주에 할 것:
    ///   - 100명 동시 접속 처리 (async 개선)
    /// ═══════════════════════════════════════════════
    /// </summary>
    public class GameServer
    {
        public static GameServer Instance { get; private set; } = null!;

        private const int PORT = 7777;

        private Socket? _listenSocket;
        private int _nextPlayerId = 0;

        private GameLoop? _gameLoop;
        private readonly System.Diagnostics.Stopwatch _uptime = new();

        // 접속 중인 클라이언트 목록
        // ConcurrentDictionary : 여러 쓰레드가 동시에 접근해도 안전한 사전
        private readonly ConcurrentDictionary<int, ClientSession> _sessions = new();

        public GameServer()
        {
            Instance = this;
        }

        // ═══════════════════════════════════════
        // 서버 시작
        // ═══════════════════════════════════════
        public async Task StartAsync()
        {
            PrintBanner();
            _uptime.Start();

            // ── 1단계 : 소켓 만들기 ──
            _listenSocket = new Socket(
                AddressFamily.InterNetwork,  // IPv4
                SocketType.Stream,           // TCP는 Stream
                ProtocolType.Tcp);

            // ── 2단계 : 주소에 묶기 (Bind) ──
            // 0.0.0.0 = 이 컴퓨터의 모든 네트워크로 들어오는 연결을 받겠다
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, PORT);
            _listenSocket.Bind(endPoint);

            // ── 3단계 : 대기 시작 (Listen) ──
            // 100 = 동시에 대기시킬 수 있는 접속 요청 수
            _listenSocket.Listen(100);

            Console.WriteLine($"[TCP] 포트 {PORT}번에서 접속을 기다립니다...");

            // ── 5주차 추가 : UDP 서버도 같이 켭니다 ──
            //
            //   TCP 포트 7777 — 로그인, 채팅 (반드시 도착해야 함)
            //   UDP 포트 7778 — 위치        (최신 값만 중요)
            //
            //   await를 붙이지 않습니다.
            //   붙이면 UDP 루프에서 안 빠져나와서
            //   아래 Accept로 못 갑니다.
            // ── 6주차 : UDP 입출력과 게임 루프를 따로 띄웁니다 ──
            //
            //   UdpServer  받아서 큐에 넣기만
            //   GameLoop   큐를 꺼내 판정하고 뿌리기
            //
            //   받는 쪽과 판정하는 쪽을 떼어놓는 것이
            //   오늘 배우는 구조입니다.
            var udpServer = new UdpServer();
            _ = udpServer.StartAsync();

            _gameLoop = new GameLoop(udpServer);
            _ = _gameLoop.RunAsync();

            _ = StatsLoopAsync();

            // ── 4단계 : 접속 받기 (Accept) 무한 반복 ──
            while (true)
            {
                // 클라이언트가 접속할 때까지 여기서 대기
                Socket clientSocket = await _listenSocket.AcceptAsync();

                // 플레이어 ID 발급
                int playerId = Interlocked.Increment(ref _nextPlayerId);

                // 세션 만들고 목록에 추가
                ClientSession session = new ClientSession(clientSocket, playerId);
                _sessions.TryAdd(playerId, session);

                Console.WriteLine($"[현재 접속자] {_sessions.Count}명");

                // 이 클라이언트 전용 처리를 시작 (기다리지 않고 바로 다음 접속 받으러 감)
                _ = session.RunAsync();
            }
        }

        // ═══════════════════════════════════════
        // 상태 출력 — 2초마다
        //
        //   거부   검증에 걸린 입력 (위조 · 과도한 거리 · 지난 번호)
        //   폐기   줄이 꽉 차서 버린 입력          ← 오늘의 새 지표
        //
        //   정상 클라이언트만 붙어 있으면 폐기는 0에 가깝습니다.
        //   속도핵(예제 G 6번)을 돌리면 폐기 숫자가 수십씩 오릅니다.
        //   "보내긴 보냈지만 서버가 안 써줬다" 는 뜻입니다.
        // ═══════════════════════════════════════
        private async Task StatsLoopAsync()
        {
            while (true)
            {
                await Task.Delay(2000);
                if (_gameLoop == null) continue;

                Console.WriteLine(
                    $"[통계] t={_uptime.Elapsed.TotalSeconds,5:F0}s | " +
                    $"접속 {_sessions.Count,3} | " +
                    $"틱 {_gameLoop.Tick,6} | " +
                    $"입력 {_gameLoop.InputCount,7} | " +
                    $"거부 {_gameLoop.RejectCount,6} | " +
                    $"폐기 {_gameLoop.DropCount,6} | " +
                    $"아이템 {_gameLoop.ItemCount,2}");
            }
        }

        // ═══════════════════════════════════════
        // 세션 관리
        // ═══════════════════════════════════════
        public void RemoveSession(int playerId)
        {
            _sessions.TryRemove(playerId, out _);
            Console.WriteLine($"[현재 접속자] {_sessions.Count}명");
        }

        // ═══════════════════════════════════════
        // [5주차 추가] UDP 서버가 쓰는 조회 함수
        // ═══════════════════════════════════════

        /// <summary>플레이어 ID로 세션 찾기</summary>
        public ClientSession? FindSession(int playerId)
        {
            _sessions.TryGetValue(playerId, out ClientSession? session);
            return session;
        }

        /// <summary>현재 접속 중인 세션 전체</summary>
        public System.Collections.Generic.List<ClientSession> GetSessions()
        {
            return new System.Collections.Generic.List<ClientSession>(_sessions.Values);
        }

        // ═══════════════════════════════════════
        // 브로드캐스트 — 모든 접속자에게 전송
        // ═══════════════════════════════════════
        public void Broadcast(object packet, int exceptPlayerId = -1)
        {
            foreach (var pair in _sessions)
            {
                if (pair.Key == exceptPlayerId) continue;
                _ = pair.Value.SendAsync(packet);
            }
        }

        // ═══════════════════════════════════════
        // 시작 화면
        // ═══════════════════════════════════════
        private void PrintBanner()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔════════════════════════════════════╗");
            Console.WriteLine("║      배틀아레나 게임 서버            ║");
            Console.WriteLine("║      동양게임즈 서버개발팀           ║");
            Console.WriteLine("╠════════════════════════════════════╣");
            Console.WriteLine("║  TICKET-021  서버가 심판이 된다      ║");
            Console.WriteLine("║  TCP 7777 (로그인) + UDP 7778 (조작) ║");
            Console.WriteLine("╚════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
        }

        // ═══════════════════════════════════════
        // 진입점
        // ═══════════════════════════════════════
        public static async Task Main()
        {
            GameServer server = new GameServer();

            try
            {
                await server.StartAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[서버 에러] {ex.Message}");
                Console.WriteLine("아무 키나 누르면 종료합니다.");
                Console.ReadKey();
            }
        }
    }
}
