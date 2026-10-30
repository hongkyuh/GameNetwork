using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Newtonsoft.Json;

namespace BattleArena
{
    /// <summary>
    /// ═══════════════════════════════════════════════
    ///  배틀아레나 게임 서버  (외주 납품본 v0.9)
    ///
    ///  TCP  로그인 · 채팅
    ///  UDP  위치 동기화
    /// ═══════════════════════════════════════════════
    /// </summary>
    public class GameServer
    {
        public static GameServer Instance { get; private set; } = null!;

        private Socket? _listenSocket;
        private int _nextPlayerId = 0;

        private readonly ConcurrentDictionary<int, ClientSession> _sessions = new();
        private readonly Stopwatch _uptime = new();

        private UdpServer? _udpServer;

        private readonly ConcurrentDictionary<int, string> _pendingJoinNotices = new();
        private const int JOIN_NOTICE_INTERVAL_MS = 100;
        private const int MAX_JOIN_NOTICE_BODY = 8192;

        public GameServer()
        {
            Instance = this;
        }

        // ═══════════════════════════════════════
        // 서버 시작
        // ═══════════════════════════════════════
        public async Task StartAsync()
        {
            _uptime.Start();

            _listenSocket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp);

            _listenSocket.Bind(new IPEndPoint(IPAddress.Any, StudentConfig.TcpPort));
            _listenSocket.Listen(512);

            Console.WriteLine($"[TCP] 포트 {StudentConfig.TcpPort}번에서 접속을 기다립니다");

            // UDP 서버를 같이 켭니다 (await를 붙이면 아래 Accept로 못 갑니다)
            _udpServer = new UdpServer();
            _ = _udpServer.StartAsync();

            // 2초마다 상태를 출력합니다 (측정 과제에서 씁니다)
            _ = StatsLoopAsync();
            _ = JoinNoticeLoopAsync();

            while (true)
            {
                Socket clientSocket = await _listenSocket.AcceptAsync();

                clientSocket.NoDelay = true;

                int playerId = Interlocked.Increment(ref _nextPlayerId);

                ClientSession session = new ClientSession(clientSocket, playerId);
                _sessions.TryAdd(playerId, session);

                _ = session.RunAsync();
            }
        }

        // ═══════════════════════════════════════
        // 상태 출력 — 측정 과제용
        //
        // 이 줄을 캡처해서 제출물에 넣으세요.
        // 학번과 포트가 같이 찍힙니다.
        // ═══════════════════════════════════════
        private async Task StatsLoopAsync()
        {
            while (true)
            {
                await Task.Delay(2000);

                ThreadPool.GetAvailableThreads(out int worker, out int io);

                int applied = _udpServer?.AppliedCount ?? 0;
                int dropped = _udpServer?.DroppedCount ?? 0;

                Console.WriteLine(
                    $"[통계 {StudentConfig.STUDENT_ID}] " +
                    $"t={_uptime.Elapsed.TotalSeconds,5:F0}s | " +
                    $"접속 {_sessions.Count,4} | " +
                    $"스레드 {ThreadPool.ThreadCount,3} | " +
                    $"가용워커 {worker,5} | " +
                    $"UDP적용 {applied,6} | 버림 {dropped,5}");
            }
        }

        // ═══════════════════════════════════════
        // 세션 관리
        // ═══════════════════════════════════════
        public void RemoveSession(int playerId)
        {
            _sessions.TryRemove(playerId, out _);
        }

        public ClientSession? FindSession(int playerId)
        {
            _sessions.TryGetValue(playerId, out ClientSession? session);
            return session;
        }

        public List<ClientSession> GetSessions()
        {
            return new List<ClientSession>(_sessions.Values);
        }

        // ═══════════════════════════════════════
        // 브로드캐스트
        // ═══════════════════════════════════════
        public void Broadcast(object packet, int exceptPlayerId = -1)
        {
            foreach (var pair in _sessions)
            {
                if (pair.Key == exceptPlayerId) continue;
                if (!pair.Value.IsLoggedIn) continue;

                _ = pair.Value.SendAsync(packet);
            }
        }

        public void RegisterJoinNotice(int playerId, string nickname)
        {
            _pendingJoinNotices.TryAdd(playerId, nickname);
        }

        private async Task JoinNoticeLoopAsync()
        {
            while (true)
            {
                await Task.Delay(JOIN_NOTICE_INTERVAL_MS);

                try
                {
                    var pending = _pendingJoinNotices.ToArray();

                    if (pending.Length == 0) continue;

                    var batch = new List<KeyValuePair<int, string>>();

                    foreach (var entry in pending)
                    {
                        if (!_pendingJoinNotices.TryRemove(
                                entry.Key, out string? nickname))
                        {
                            continue;
                        }

                        var player =
                            new KeyValuePair<int, string>(entry.Key, nickname);

                        batch.Add(player);

                        if (GetJoinNoticeBodyBytes(batch) <= MAX_JOIN_NOTICE_BODY)
                            continue;

                        batch.RemoveAt(batch.Count - 1);

                        await SendJoinNoticeBatchAsync(batch);

                        batch.Clear();
                        batch.Add(player);

                        if (GetJoinNoticeBodyBytes(batch) > MAX_JOIN_NOTICE_BODY)
                        {
                            batch[0] = new KeyValuePair<int, string>(
                                player.Key, $"Player{player.Key}");
                        }
                    }

                    await SendJoinNoticeBatchAsync(batch);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[입장 알림 오류] {ex.Message}");
                }
            }
        }

        private static NoticePacket BuildJoinNotice(
            List<KeyValuePair<int, string>> batch,
            int exceptPlayerId = -1)
        {
            var names = new List<string>();

            foreach (var player in batch)
            {
                if (player.Key == exceptPlayerId) continue;

                names.Add(player.Value);
            }

            return new NoticePacket
            {
                Type = PacketType.PLAYER_IN,
                Message = $"{string.Join(", ", names)}님이 입장했습니다."
            };
        }

        private static int GetJoinNoticeBodyBytes(
            List<KeyValuePair<int, string>> batch)
        {
            string json =
                JsonConvert.SerializeObject(BuildJoinNotice(batch));

            return Encoding.UTF8.GetByteCount(json);
        }

        private async Task SendJoinNoticeBatchAsync(
            List<KeyValuePair<int, string>> batch)
        {
            if (batch.Count == 0) return;

            var sends = new List<Task>();

            foreach (var session in _sessions.Values)
            {
                if (!session.IsLoggedIn) continue;

                if (batch.Count == 1 &&
                    batch[0].Key == session.PlayerId)
                {
                    continue;
                }

                NoticePacket notice = BuildJoinNotice(batch, session.PlayerId);

                sends.Add(session.SendAsync(notice));
            }

            await Task.WhenAll(sends);

            Console.WriteLine($"[입장 알림] 묶음 {batch.Count}명 / 전송 대상 {sends.Count}명");
        }
    }
}
