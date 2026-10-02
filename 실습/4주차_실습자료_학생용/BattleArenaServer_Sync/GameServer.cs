using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BattleArenaServer
{
    /// <summary>
    /// 배틀아레나 게임 서버 — 동기 버전
    ///
    /// ═══════════════════════════════════════════════
    /// [4주차 — TICKET-007]
    // "유저 100명이 동시에 못 붙습니다"
    ///
    /// 이 서버는 한 번에 한 명만 처리할 수 있습니다.
    ///
    /// 왜 그럴까요?
    /// 1. Accept 로 손님을 받고
    /// 2. session.Run() 을 호출하면
    /// 3. 그 손님이 나갈 때까지 Run() 이 안 끝납니다
    /// 4. 그래서 다음 Accept 로 못 갑니다
    ///
    /// 직접 확인해보세요:
    /// 클라이언트 1개 → 잘 붙음
    /// 클라이언트 2개 → 두 번째가 안 붙음
    ///
    /// 여러분이 할 일:
    /// 이 코드를 비동기로 바꿔서 여러 명이 붙게 만들기
    ///
    /// ═══════════════════════════════════════════════
    /// </summary>
    public class GameServer
    {
        public static GameServer Instance { get; private set; } = null!;

        private const int PORT = 7777;

        private Socket? _listenSocket;
        private int _nextPlayerId = 0;

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

            // 1. 소켓 만들기
            _listenSocket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp);

            // 2. Bind
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, PORT);
            _listenSocket.Bind(endPoint);

            // 3. Listen
            _listenSocket.Listen(100);

            Console.WriteLine($"[서버] 포트 {PORT}번에서 접속을 기다립니다...");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("※ 이 서버는 비동기 버전입니다.");
            Console.WriteLine("   여러 명의 클라이언트가 동시에 접속할 수 있습니다.");
            Console.ResetColor();
            Console.WriteLine();

            // 4. Accept 반복
            while (true)
            {
                Socket clientSocket = await _listenSocket.AcceptAsync();

                int playerId = Interlocked.Increment(ref _nextPlayerId);

                ClientSession session = new ClientSession(clientSocket, playerId);
                _sessions.TryAdd(playerId, session);

                Console.WriteLine($"[현재 접속자] {_sessions.Count}명");

                _ = session.RunAsync();
                Console.WriteLine($"[세션 종료] ID:{playerId}");
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
        // 브로드캐스트
        // ═══════════════════════════════════════
        public void Broadcast(object packet, int exceptPlayerId = -1)
        {
            foreach (var pair in _sessions)
            {
                if (pair.Key == exceptPlayerId) continue;
                pair.Value.Send(packet);
            }
        }

        // ═══════════════════════════════════════
        private void PrintBanner()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("╔════════════════════════════════════╗");
            Console.WriteLine("║   배틀아레나 서버 [비동기 버전]    ║");
            Console.WriteLine("║   TICKET-007  해결 완료            ║");
            Console.WriteLine("╚════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
        }

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