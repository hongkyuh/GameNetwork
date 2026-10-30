using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace BattleArena
{
    /// <summary>서버가 알려준 플레이어 한 명</summary>
    public class RemotePlayer
    {
        public int    PlayerId;
        public string Nickname;
        public int    X;
        public int    Y;
    }

    /// <summary>서버가 알려준 아이템 하나</summary>
    public class RemoteItem
    {
        public int ItemId;
        public int X;
        public int Y;
    }

    /// <summary>
    /// ═══════════════════════════════════════════════
    /// [6주차] UDP 조작 전송 · 상태 수신
    ///
    /// ★ 5주차와 달라진 점
    ///
    ///   5주차   SendPosition(x, y)    "나 (10,7)에 있어"
    ///   6주차   SendDirection(dx, dy) "오른쪽으로 갈래"
    ///
    ///   좌표를 보내면 서버가 받아 적을 수밖에 없습니다.
    ///   방향만 보내면 좌표는 서버가 계산합니다.
    ///
    /// ★ 그리고 두 가지가 더 생겼습니다
    ///
    ///   1. SendDirection 이 시퀀스 번호를 돌려줍니다
    ///        보낸 입력을 기억해두려면 번호가 필요합니다.
    ///
    ///   2. STATE 에서 AckSeq 를 꺼냅니다
    ///        "네 152번까지 처리했다" 는 영수증입니다.
    ///
    ///   이 두 가지가 있어야 클라이언트 예측을 할 수 있습니다.
    ///   예측은 "서버 답을 기다리지 않고 화면을 먼저 움직이는 것" 이고,
    ///   보정은 "서버 답이 오면 맞춰보는 것" 입니다.
    ///   맞춰보려면 무엇을 보냈는지 기억하고 있어야 하죠.
    ///
    /// ── 주고받는 것 ──
    ///
    ///   보내기 : MOVE|플레이어ID|시퀀스|DX|DY
    ///   받기   : STATE|AckSeq|id,닉네임,x,y|...
    ///            ITEMS|아이템id,x,y|...
    /// ═══════════════════════════════════════════════
    /// </summary>
    public class UdpNetworkClient
    {
        private const string SERVER_IP   = "127.0.0.1";
        private const int    SERVER_PORT = 7778;

        private Socket _socket;
        private IPEndPoint _serverEndPoint;
        private bool _isRunning;

        private int _playerId;
        private int _seq;

        private readonly ConcurrentQueue<string> _stateQueue = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _itemQueue  = new ConcurrentQueue<string>();

        public bool IsRunning => _isRunning;

        // ═══════════════════════════════════════
        // 시작
        // ═══════════════════════════════════════
        public void Start(int playerId)
        {
            _playerId = playerId;

            try
            {
                _socket = new Socket(AddressFamily.InterNetwork,
                                     SocketType.Dgram,
                                     ProtocolType.Udp);

                // 받으려면 Bind 가 필요합니다 (5주차 내용)
                _socket.Bind(new IPEndPoint(IPAddress.Any, 0));

                // 서버가 꺼졌을 때 Windows 가 돌려주는 ICMP 무시
                IgnoreServerDownError();

                _serverEndPoint = new IPEndPoint(IPAddress.Parse(SERVER_IP), SERVER_PORT);
                _isRunning = true;

                _ = ReceiveLoopAsync();

                Debug.Log($"[UDP] 시작 (PlayerId {playerId})");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UDP] 시작 실패 : {ex.Message}");
                _isRunning = false;
            }
        }

        private void IgnoreServerDownError()
        {
            if (_socket == null) return;

            try
            {
                const int SIO_UDP_CONNRESET = -1744830452;   // 0x9800000C
                _socket.IOControl((IOControlCode)SIO_UDP_CONNRESET,
                                  new byte[] { 0, 0, 0, 0 }, null);
            }
            catch
            {
                // Windows 외 플랫폼에는 없는 옵션입니다
            }
        }

        // ═══════════════════════════════════════
        // 조작 보내기 — 좌표가 아니라 방향입니다
        // ═══════════════════════════════════════
        /// <summary>
        /// 방향을 보내고, 이번에 쓴 시퀀스 번호를 돌려줍니다
        ///
        /// ★ 번호를 돌려주는 것이 중요합니다
        ///
        ///   클라이언트는 보낸 입력을 번호와 함께 적어둬야 합니다.
        ///   나중에 서버가 "152번까지 처리했다" 고 알려주면
        ///   153번 이후만 다시 계산해서 예측을 맞춥니다.
        ///
        ///   번호를 모르면 무엇을 다시 계산해야 할지 알 수 없고,
        ///   그러면 예측은 그냥 추측이 됩니다.
        /// </summary>
        public int SendDirection(int dx, int dy)
        {
            if (!_isRunning || _socket == null) return 0;

            _seq++;

            // 번호는 지금 돌려주고, 실제 전송은 뒤에서 처리합니다
            SendRawAsync($"MOVE|{_playerId}|{_seq}|{dx}|{dy}");

            return _seq;
        }

        private async void SendRawAsync(string msg)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(msg);

                await _socket.SendToAsync(new ArraySegment<byte>(data),
                                          SocketFlags.None, _serverEndPoint);
            }
            catch
            {
                // UDP 는 실패해도 넘어갑니다
            }
        }

        // ═══════════════════════════════════════
        // 수신
        // ═══════════════════════════════════════
        private async Task ReceiveLoopAsync()
        {
            byte[] buffer = new byte[4096];
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);

            while (_isRunning)
            {
                try
                {
                    var result = await _socket.ReceiveFromAsync(
                        new ArraySegment<byte>(buffer), SocketFlags.None, from);

                    string msg = Encoding.UTF8.GetString(buffer, 0, result.ReceivedBytes);

                    // Unity 는 다른 쓰레드에서 GameObject 를 못 건드립니다.
                    // 큐에 넣어두고 메인 쓰레드에서 꺼냅니다.
                    if (msg.StartsWith("STATE"))      _stateQueue.Enqueue(msg);
                    else if (msg.StartsWith("ITEMS")) _itemQueue.Enqueue(msg);
                }
                catch (SocketException se)
                    when (se.SocketErrorCode == SocketError.ConnectionReset)
                {
                    // 서버가 잠깐 꺼진 것일 뿐입니다. 여기서 break 하면 안 됩니다.
                    continue;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[UDP] 수신 중단 : {ex.Message}");
                    break;
                }
            }
        }

        // ═══════════════════════════════════════
        // 메인 쓰레드에서 호출 — 최신 것만 꺼내기
        // ═══════════════════════════════════════

        /// <summary>
        /// 서버가 보낸 최신 상태를 꺼냅니다
        ///
        /// ★ ackSeq 가 같이 나옵니다
        ///
        ///   STATE|152|1,홍길동,10,7|3,철수,4,2
        ///         ↑
        ///   "네가 보낸 152번까지 반영했고, 그 결과가 이 좌표다"
        ///
        ///   이 번호가 보정의 기준점입니다.
        /// </summary>
        public bool TryGetLatestState(out List<RemotePlayer> players, out int ackSeq)
        {
            players = null;
            ackSeq  = 0;

            string latest = null;
            while (_stateQueue.TryDequeue(out string msg)) latest = msg;

            if (latest == null) return false;

            // 머리글 바로 뒤가 AckSeq 입니다
            string[] head = latest.Split('|');
            if (head.Length >= 2) int.TryParse(head[1], out ackSeq);

            players = new List<RemotePlayer>();

            foreach (string[] f in Rows(latest, 2))
            {
                if (f.Length < 4) continue;
                if (!int.TryParse(f[0], out int id)) continue;
                if (!int.TryParse(f[2], out int x))  continue;
                if (!int.TryParse(f[3], out int y))  continue;

                players.Add(new RemotePlayer
                {
                    PlayerId = id,
                    Nickname = f[1],
                    X = x,
                    Y = y,
                });
            }

            return true;
        }

        public bool TryGetLatestItems(out List<RemoteItem> items)
        {
            items = null;

            string latest = null;
            while (_itemQueue.TryDequeue(out string msg)) latest = msg;

            if (latest == null) return false;

            items = new List<RemoteItem>();

            foreach (string[] f in Rows(latest))
            {
                if (f.Length < 3) continue;
                if (!int.TryParse(f[0], out int id)) continue;
                if (!int.TryParse(f[1], out int x))  continue;
                if (!int.TryParse(f[2], out int y))  continue;

                items.Add(new RemoteItem { ItemId = id, X = x, Y = y });
            }

            return true;
        }

        /// <summary>
        /// 머리글|행|행|행 형태를 잘라서 돌려줍니다
        ///
        ///   STATE|152|1,홍길동,10,7|3,철수,4,2     skip = 2  (AckSeq 를 건너뜀)
        ///   ITEMS|7,3,9|8,15,2                     skip = 1
        /// </summary>
        private static IEnumerable<string[]> Rows(string packet, int skip = 1)
        {
            string[] parts = packet.Split('|');
            for (int i = skip; i < parts.Length; i++)
                yield return parts[i].Split(',');
        }

        // ═══════════════════════════════════════
        // 종료
        // ═══════════════════════════════════════
        public void Stop()
        {
            _isRunning = false;

            try { _socket?.Close(); } catch { }
            _socket = null;

            Debug.Log("[UDP] 종료");
        }
    }
}
