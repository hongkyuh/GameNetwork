using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TestClient
{
    /// <summary>
    /// ═══════════════════════════════════════════════
    ///  콘솔 테스트 클라이언트
    ///
    ///  Unity 없이 서버를 확인할 때 씁니다.
    ///  완성본이니 고치지 않아도 됩니다.
    ///
    ///  ★ 6주차에 달라진 것
    ///
    ///     좌표가 아니라 방향을 보냅니다.
    ///     내 캐릭터 위치도 서버가 정한 값을 따릅니다.
    ///
    ///     @ 는 예측한 내 자리, + 는 서버가 아는 내 자리입니다.
    ///     지연이 있으면 둘이 떨어져 보입니다.
    ///
    ///     P 로 예측을 끄면 @ 가 + 자리로 돌아가고,
    ///     키를 눌러도 왕복 시간만큼 늦게 움직입니다.
    ///
    ///  조작
    ///    W A S D   이동 (방향만 전송)
    ///    P         예측 끄기 / 켜기
    ///    C         채팅 보내기
    ///    R         연결 끊고 다시 접속
    ///    Q         종료
    /// ═══════════════════════════════════════════════
    /// </summary>
    public static class Program
    {
        private const string SERVER_IP = "127.0.0.1";
        private const int    TCP_PORT   = 7777;
        private const int    UDP_PORT   = 7778;

        private static Socket? _tcp;
        private static Socket? _udp;
        private static IPEndPoint? _udpServer;

        private static int _myId;
        private static int _seq;

        // ═══════════════════════════════════════════════
        // 클라이언트 예측
        //
        //   서버 좌표   왕복 시간만큼 과거입니다.      ( + 로 표시 )
        //   예측 좌표   내가 계산한 "지금" 자리입니다. ( @ 로 표시 )
        //
        //   두 표시가 떨어져 있으면 그만큼 지연이 있다는 뜻입니다.
        //   로컬에서는 거의 겹쳐 보입니다.
        // ═══════════════════════════════════════════════

        /// <summary>서버가 알려준 내 좌표 (과거)</summary>
        private static int _serverX = 5;
        private static int _serverY = 5;

        /// <summary>예측한 내 좌표 (화면에 그리는 값)</summary>
        private static int _predX = 5;
        private static int _predY = 5;

        /// <summary>서버가 마지막으로 반영했다고 알려준 입력 번호</summary>
        private static int _serverAck;
        private static int _lastAck = -1;

        private static bool _predictOn = true;
        private static int  _corrections;

        /// <summary>아직 서버 확인을 못 받은 내 입력들</summary>
        private static readonly List<(int seq, int dx, int dy)> _pending = new();

        private static string _nick      = "";
        private static string _lastState = "";
        private static string _lastItems = "";
        private static string _udpError  = "";
        private static string _tcpStatus = "미접속";

        /// <summary>채팅 입력 중에는 화면을 다시 그리지 않습니다</summary>
        private static bool _typing;

        /// <summary>움직일 수 있는 범위 (화면 지도 크기와 같습니다)</summary>
        private const int MAP_W = 20;
        private const int MAP_H = 12;

        private static readonly ConcurrentQueue<string> _chatLog = new();

        public static async Task Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "배틀아레나 테스트 클라이언트";

            Console.WriteLine("╔══════════════════════════════════════════╗");
            Console.WriteLine("║   배틀아레나 콘솔 테스트 클라이언트         ║");
            Console.WriteLine("║   TICKET-021  서버가 심판이 된다           ║");
            Console.WriteLine("╚══════════════════════════════════════════╝");
            Console.WriteLine();

            Console.Write("  닉네임 : ");
            _nick = Console.ReadLine() ?? "";
            if (string.IsNullOrWhiteSpace(_nick))
                _nick = "Tester" + new Random().Next(100, 999);

            await ConnectAsync();

            Console.CursorVisible = false;
            Console.Clear();

            // ── 메인 루프 ──
            //   키를 "기다리지" 않고 "있으면 꺼내는" 방식입니다.
            //   ReadKey 로 막아두면 화면을 다시 그릴 수가 없습니다.
            while (true)
            {
                int dx = 0, dy = 0;

                while (Console.KeyAvailable)
                {
                    ConsoleKeyInfo key = Console.ReadKey(intercept: true);

                    // ★ KeyChar 가 아니라 Key 로 판정합니다.
                    //   한글 입력 상태에서는 KeyChar 에 'W' 가 아니라
                    //   'ㅈ' 이 들어와서 WASD 가 안 먹습니다.
                    switch (key.Key)
                    {
                        case ConsoleKey.W:
                        case ConsoleKey.UpArrow:    dy =  1; break;

                        case ConsoleKey.S:
                        case ConsoleKey.DownArrow:  dy = -1; break;

                        case ConsoleKey.A:
                        case ConsoleKey.LeftArrow:  dx = -1; break;

                        case ConsoleKey.D:
                        case ConsoleKey.RightArrow: dx =  1; break;

                        case ConsoleKey.C: await SendChatAsync();  break;
                        case ConsoleKey.R: await ReconnectAsync(); break;

                        case ConsoleKey.P:
                            _predictOn   = !_predictOn;
                            _corrections = 0;
                            if (!_predictOn) { _predX = _serverX; _predY = _serverY; }
                            break;

                        case ConsoleKey.Q:
                            Console.CursorVisible = true;
                            Cleanup();
                            return;
                    }
                }

                // ★ 보내고, 적어두고, 화면을 먼저 움직입니다
                if (dx != 0 || dy != 0)
                {
                    await SendDirectionAsync(dx, dy);

                    // 서버 확인을 못 받은 입력으로 적어둡니다
                    _pending.Add((_seq, dx, dy));

                    // 예측 — 서버와 같은 계산을 먼저 해둡니다
                    if (_predictOn) StepGrid(ref _predX, ref _predY, dx, dy);
                }

                Render();
                await Task.Delay(50);
            }
        }

        // ═══════════════════════════════════════
        // 접속
        // ═══════════════════════════════════════
        private static async Task ConnectAsync()
        {
            try
            {
                _tcp = new Socket(AddressFamily.InterNetwork,
                                  SocketType.Stream, ProtocolType.Tcp);

                await _tcp.ConnectAsync(IPAddress.Parse(SERVER_IP), TCP_PORT);

                _tcpStatus = "접속됨";
                _ = TcpReceiveLoopAsync();

                await SendPacketAsync(new { Type = 1, Nickname = _nick });
            }
            catch (Exception ex)
            {
                _tcpStatus = "접속 실패 : " + ex.Message;
            }
        }

        private static void StartUdp()
        {
            try
            {
                _udp = new Socket(AddressFamily.InterNetwork,
                                  SocketType.Dgram, ProtocolType.Udp);

                // 받으려면 Bind 가 필요합니다 (5주차 내용)
                _udp.Bind(new IPEndPoint(IPAddress.Any, 0));

                _udpServer = new IPEndPoint(IPAddress.Parse(SERVER_IP), UDP_PORT);

                _seq = 0;
                _udpError = "";

                // 새 연결이면 번호가 1부터 다시 시작합니다.
                //         옛 입력 기록을 남겨두면 보정이 엉뚱한 좌표를 냅니다.
                _pending.Clear();
                _corrections = 0;
                _serverAck   = 0;
                _lastAck     = -1;

                _ = UdpReceiveLoopAsync();
            }
            catch (Exception ex)
            {
                _udpError = "UDP 시작 실패 : " + ex.Message;
            }
        }

        private static async Task ReconnectAsync()
        {
            _chatLog.Enqueue("── 재접속 ──");

            try { _tcp?.Close(); } catch { }
            try { _udp?.Close(); } catch { }

            _tcp = null;
            _udp = null;
            _myId = 0;
            _tcpStatus = "재접속 중...";

            await Task.Delay(300);
            await ConnectAsync();
        }

        private static void Cleanup()
        {
            try { _tcp?.Close(); } catch { }
            try { _udp?.Close(); } catch { }
            Console.Clear();
            Console.WriteLine("종료했습니다.");
        }

        // ═══════════════════════════════════════
        // TCP
        // ═══════════════════════════════════════
        private static async Task SendPacketAsync(object packet)
        {
            if (_tcp == null) return;

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(packet);
            byte[] body = Encoding.UTF8.GetBytes(json);

            byte[] full = new byte[4 + body.Length];
            BitConverter.GetBytes(body.Length).CopyTo(full, 0);
            body.CopyTo(full, 4);

            int sent = 0;
            while (sent < full.Length)
            {
                sent += await _tcp.SendAsync(
                    new ArraySegment<byte>(full, sent, full.Length - sent),
                    SocketFlags.None);
            }
        }

        private static async Task SendChatAsync()
        {
            // 입력 중에는 화면을 다시 그리지 않습니다.
            // 안 그러면 타이핑하는 글자가 계속 지워집니다.
            _typing = true;

            try
            {
                Console.CursorVisible = true;
                Console.Clear();
                Console.SetCursorPosition(0, 0);
                Console.WriteLine();
                Console.WriteLine("  보낼 메시지를 입력하고 엔터 (그냥 엔터면 취소)");
                Console.WriteLine();
                Console.Write("  > ");

                string msg = Console.ReadLine() ?? "";

                if (!string.IsNullOrWhiteSpace(msg))
                    await SendPacketAsync(new { Type = 2, Message = msg });
            }
            finally
            {
                Console.CursorVisible = false;
                Console.Clear();
                _typing = false;
            }
        }

        private static async Task TcpReceiveLoopAsync()
        {
            byte[] header = new byte[4];

            while (_tcp != null)
            {
                try
                {
                    // 헤더 4바이트를 끝까지 읽습니다
                    if (!await ReadExactAsync(_tcp, header, 4)) break;

                    int len = BitConverter.ToInt32(header, 0);
                    if (len <= 0 || len > 8192) break;

                    byte[] body = new byte[len];
                    if (!await ReadExactAsync(_tcp, body, len)) break;

                    string json = Encoding.UTF8.GetString(body, 0, len);
                    HandleTcpPacket(json);
                }
                catch
                {
                    break;
                }
            }

            _tcpStatus = "연결 끊김";
        }

        /// <summary>요청한 바이트 수를 다 받을 때까지 반복해서 읽습니다</summary>
        private static async Task<bool> ReadExactAsync(Socket socket, byte[] buffer, int size)
        {
            int read = 0;
            while (read < size)
            {
                int n = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer, read, size - read), SocketFlags.None);

                if (n == 0) return false;
                read += n;
            }
            return true;
        }

        private static void HandleTcpPacket(string json)
        {
            dynamic? p = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
            if (p == null) return;

            int type = (int)p.Type;

            if (type == 11)                       // WELCOME
            {
                _myId = (int)p.PlayerId;
                _chatLog.Enqueue((string)p.Message);

                // 내 ID를 받은 뒤에 UDP를 시작합니다
                StartUdp();
            }
            else if (type == 12)                  // CHAT_ECHO
            {
                _chatLog.Enqueue($"{(string)p.Nickname} : {(string)p.Message}");
            }
            else if (type == 20 || type == 21)    // 입장 / 퇴장
            {
                _chatLog.Enqueue((string)p.Message);
            }

            while (_chatLog.Count > 6) _chatLog.TryDequeue(out _);
        }

        // ═══════════════════════════════════════
        // UDP
        // ═══════════════════════════════════════
        private static async Task SendDirectionAsync(int dx, int dy)
        {
            if (_udp == null || _udpServer == null || _myId == 0) return;

            try
            {
                _seq++;
                string msg = $"MOVE|{_myId}|{_seq}|{dx}|{dy}";
                byte[] data = Encoding.UTF8.GetBytes(msg);

                await _udp.SendToAsync(new ArraySegment<byte>(data),
                                       SocketFlags.None, _udpServer);
            }
            catch (Exception ex)
            {
                _udpError = "전송 실패 : " + ex.Message;
            }
        }

        private static async Task UdpReceiveLoopAsync()
        {
            byte[] buffer = new byte[2048];
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);

            while (_udp != null)
            {
                try
                {
                    var r = await _udp.ReceiveFromAsync(
                        new ArraySegment<byte>(buffer), SocketFlags.None, from);

                    string msg = Encoding.UTF8.GetString(buffer, 0, r.ReceivedBytes);

                    if (msg.StartsWith("STATE"))      _lastState = msg;
                    else if (msg.StartsWith("ITEMS")) _lastItems = msg;
                }
                catch (Exception ex)
                {
                    _udpError = $"수신 중단 : {ex.GetType().Name} {ex.Message}";
                    break;
                }
            }
        }

        // ═══════════════════════════════════════
        // 화면
        //
        //   줄 수를 항상 똑같이 그립니다.
        //   줄마다 창 너비까지 공백을 채워서
        //   이전에 쓴 글자가 남지 않게 합니다.
        // ═══════════════════════════════════════
        private static void Render()
        {
            if (_typing) return;

            var lines = new List<string>();

            lines.Add($"  TCP {TCP_PORT}  ·  UDP {UDP_PORT}");
            lines.Add($"  나 : {_nick} (ID {_myId})    " +
                      $"@ 예측 ({_predX},{_predY})    " +
                      $"+ 서버 ({_serverX},{_serverY})");
            lines.Add($"  TCP : {_tcpStatus}");
            lines.Add("");

            // ── 지도 ──
            //
            //   나는 @ , 다른 사람은 A B C ... 로 표시합니다.
            //   닉네임 첫 글자를 쓰면 한글이 두 칸을 차지해서
            //   칸이 밀립니다.
            var players = ParseState();

            // 서버가 알려준 내 좌표를 받아두고, 바뀌었으면 보정합니다
            foreach (var p in players)
            {
                if (p.id != _myId) continue;
                _serverX = p.x;
                _serverY = p.y;
                break;
            }

            // 서버가 새 입력을 반영했을 때만 보정합니다.
            // 서버는 입력을 쓸 때만 움직이므로 AckSeq 가 바뀌면 움직인 것입니다.
            if (_serverAck != _lastAck)
            {
                _lastAck = _serverAck;
                Reconcile();
            }

            // 플레이어마다 기호 하나씩 배정
            var mark = new Dictionary<int, char>();
            char next = 'A';
            foreach (var p in players)
            {
                if (p.id == _myId) { mark[p.id] = '@'; continue; }
                mark[p.id] = next;
                if (next < 'Z') next++;
            }

            lines.Add("  ┌" + new string('─', MAP_W) + "┐");

            for (int row = MAP_H - 1; row >= 0; row--)   // 위가 y 큰 쪽
            {
                var buf = new char[MAP_W];
                for (int i = 0; i < MAP_W; i++) buf[i] = '.';

                // 아이템 먼저 (플레이어가 위에 덮이도록)
                foreach (var it in ParseItems())
                {
                    if (it.y != row) continue;
                    if (it.x < 0 || it.x >= MAP_W) continue;
                    buf[it.x] = '*';
                }

                // 서버가 아는 내 자리 — 예측과 다를 때만 + 로 따로 보여줍니다
                if ((_serverX != _predX || _serverY != _predY) &&
                    _serverY == row && _serverX >= 0 && _serverX < MAP_W)
                    buf[_serverX] = '+';

                foreach (var p in players)
                {
                    // 내 캐릭터는 예측 좌표에 그립니다 (화면이 바로 반응하는 이유)
                    int px = p.x, py = p.y;
                    if (p.id == _myId) { px = _predX; py = _predY; }

                    if (py != row) continue;
                    if (px < 0 || px >= MAP_W) continue;

                    buf[px] = mark.TryGetValue(p.id, out char m) ? m : 'o';
                }

                lines.Add("  │" + new string(buf) + "│");
            }

            lines.Add("  └" + new string('─', MAP_W) + "┘");

            // ── 누가 보이나 ──
            if (players.Count == 0)
            {
                lines.Add("  보이는 사람 : 없음    (@ 가 나, * 가 아이템입니다)");
            }
            else
            {
                var sb = new StringBuilder("  ");
                foreach (var p in players)
                {
                    char m = mark.TryGetValue(p.id, out char c) ? c : 'o';
                    string name = (p.id == _myId) ? "나" : p.nick;
                    int px = (p.id == _myId) ? _predX : p.x;
                    int py = (p.id == _myId) ? _predY : p.y;
                    sb.Append($"{m} {name}({px},{py})   ");
                }
                lines.Add(sb.ToString());
            }

            // ── 예측 상태 ──
            lines.Add(_predictOn
                ? $"  예측 ON    확인 안 된 입력 {_pending.Count}개    " +
                  $"보정 {_corrections}회    서버 확인 번호 {_serverAck}"
                : "  예측 OFF   서버 답을 기다립니다 (조작이 무거워집니다)");

            lines.Add(string.IsNullOrEmpty(_udpError) ? "" : $"  [UDP 문제] {_udpError}");

            // ── 채팅 4줄 고정 ──
            lines.Add("  ── 채팅 ──");

            var chat = new List<string>(_chatLog);
            int start = Math.Max(0, chat.Count - 3);
            for (int i = 0; i < 3; i++)
            {
                int idx = start + i;
                lines.Add(idx < chat.Count ? "  " + chat[idx] : "");
            }

            lines.Add("  WASD 이동   P 예측 끄기/켜기   C 채팅   R 재접속   Q 종료");

            // ── 출력 ──
            try
            {
                int w = Math.Max(40, Console.WindowWidth - 1);

                Console.SetCursorPosition(0, 0);

                for (int i = 0; i < lines.Count; i++)
                {
                    // 마지막 줄에 WriteLine 을 쓰면 화면이 한 줄 밀려서
                    // 다음 프레임부터 레이아웃이 어긋납니다
                    if (i == lines.Count - 1) Console.Write(Fit(lines[i], w));
                    else                      Console.WriteLine(Fit(lines[i], w));
                }
            }
            catch
            {
                // 창이 너무 작으면 이번 프레임은 건너뜁니다
            }
        }

        /// <summary>STATE 문자열을 플레이어 목록으로</summary>
        private static List<(int id, string nick, int x, int y)> ParseState()
        {
            var list = new List<(int, string, int, int)>();

            string s = _lastState;
            if (string.IsNullOrEmpty(s) || !s.StartsWith("STATE")) return list;

            string[] parts = s.Split('|');

            // ★ 머리글 바로 뒤가 AckSeq 입니다
            //
            //     STATE|152|1,홍길동,10,7|3,철수,4,2
            //           ↑           ↑
            //        AckSeq      여기부터 사람 목록 (i = 2)
            if (parts.Length >= 2) int.TryParse(parts[1], out _serverAck);

            for (int i = 2; i < parts.Length; i++)
            {
                string[] f = parts[i].Split(',');
                if (f.Length < 4) continue;

                if (!int.TryParse(f[0], out int id)) continue;
                if (!int.TryParse(f[2], out int x))  continue;
                if (!int.TryParse(f[3], out int y))  continue;

                list.Add((id, f[1], x, y));
            }

            return list;
        }

        /// <summary>
        /// 서버와 똑같은 이동 계산
        ///
        /// 서버 : s.X = Math.Clamp(s.X + cmd.Dx, 0, MAP_W - 1);
        ///
        /// 이 계산이 서버와 달라지면 예측이 틀립니다.
        /// </summary>
        private static void StepGrid(ref int x, ref int y, int dx, int dy)
        {
            x = Math.Clamp(x + dx, 0, MAP_W - 1);
            y = Math.Clamp(y + dy, 0, MAP_H - 1);
        }

        /// <summary>
        /// 보정 — 서버 답이 왔을 때 예측을 맞춰봅니다
        ///
        ///   1. 서버가 처리했다는 입력은 목록에서 지운다
        ///   2. 서버 좌표에서 출발한다        ← 서버가 진실
        ///   3. 확인 안 된 입력을 다시 적용한다
        /// </summary>
        private static void Reconcile()
        {
            _pending.RemoveAll(p => p.seq <= _serverAck);

            if (!_predictOn)
            {
                _predX = _serverX;
                _predY = _serverY;
                return;
            }

            int beforeX = _predX, beforeY = _predY;

            _predX = _serverX;
            _predY = _serverY;

            foreach (var p in _pending)
                StepGrid(ref _predX, ref _predY, p.dx, p.dy);

            if (_predX != beforeX || _predY != beforeY)
                _corrections++;
        }

        /// <summary>ITEMS 문자열을 아이템 목록으로</summary>
        private static List<(int id, int x, int y)> ParseItems()
        {
            var list = new List<(int, int, int)>();

            if (string.IsNullOrEmpty(_lastItems) || !_lastItems.StartsWith("ITEMS"))
                return list;

            string[] parts = _lastItems.Split('|');
            for (int i = 1; i < parts.Length; i++)
            {
                string[] f = parts[i].Split(',');
                if (f.Length < 3) continue;

                if (!int.TryParse(f[0], out int id)) continue;
                if (!int.TryParse(f[1], out int x))  continue;
                if (!int.TryParse(f[2], out int y))  continue;

                list.Add((id, x, y));
            }

            return list;
        }

        /// <summary>창 너비에 맞춰 자르거나 공백을 채웁니다</summary>
        private static string Fit(string s, int width)
        {
            s = (s ?? "").Replace("\r", "").Replace("\n", " ");

            // 한글은 두 칸을 차지하므로 길이를 따로 셉니다
            int visual = 0;
            var sb = new StringBuilder();

            foreach (char c in s)
            {
                int cw = (c >= 0x1100 && c <= 0xD7A3) || (c >= 0xFF01 && c <= 0xFF60) ? 2 : 1;
                if (visual + cw > width) break;

                sb.Append(c);
                visual += cw;
            }

            return sb.ToString() + new string(' ', Math.Max(0, width - visual));
        }

    }
}
