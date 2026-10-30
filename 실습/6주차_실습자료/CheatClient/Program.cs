using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CheatClient
{
    /// <summary>
    /// ═══════════════════════════════════════════════
    ///  예제 G — 치트 클라이언트
    ///
    ///  여러분이 만든 서버를 공격하는 도구입니다.
    ///  고치지 말고 그대로 쓰세요.
    ///
    ///  봇 두 명을 접속시킵니다.
    ///      피해자   가만히 있는 사람
    ///      공격자   치트를 시도하는 사람
    ///
    ///  검증을 제대로 넣었다면 전부 "차단" 이 나와야 합니다.
    ///
    ///  ── 6번을 특히 눈여겨보세요 ──
    ///
    ///    1~4번은 검사를 추가하면 막힙니다.
    ///    6번(속도핵)은 검사로는 막히지 않습니다.
    ///
    ///        2번   DX=20 을 한 번      규칙을 어긴다   → 거리 검사로 막힘
    ///        6번   DX=1 을 300번       규칙을 지킨다   → 검사를 다 통과
    ///
    ///    "패킷 하나 = 한 칸" 이라는 구조 자체가 문제입니다.
    ///    → GameLoop.StepPlayers() 가 그래서 필요합니다
    /// ═══════════════════════════════════════════════
    /// </summary>
    public static class Program
    {
        private static Bot _victim  = null!;
        private static Bot _attacker = null!;

        public static async Task Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            Console.WriteLine("╔══════════════════════════════════════════╗");
            Console.WriteLine("║   예제 G — 치트 클라이언트                 ║");
            Console.WriteLine("║   내가 만든 서버를 내가 공격해 봅니다       ║");
            Console.WriteLine("╚══════════════════════════════════════════╝");
            Console.WriteLine();

            try
            {
                Console.WriteLine("  봇 두 명을 접속시킵니다...");
                _victim   = await Bot.ConnectAsync("피해자");
                _attacker = await Bot.ConnectAsync("공격자");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  접속 실패 : {ex.Message}");
                Console.WriteLine("  서버가 떠 있는지 확인하세요.");
                Console.ReadKey();
                return;
            }

            if (_victim.PlayerId == 0 || _attacker.PlayerId == 0)
            {
                Console.WriteLine("  로그인 실패 (WELCOME 을 못 받았습니다)");
                Console.ReadKey();
                return;
            }

            Console.WriteLine($"  피해자 PlayerId = {_victim.PlayerId}");
            Console.WriteLine($"  공격자 PlayerId = {_attacker.PlayerId}");
            Console.WriteLine();

            // 서버에 등록되도록 양쪽 다 한 번씩 정상 조작을 보냅니다
            await _victim.MoveAsync(0, 0);
            await _attacker.MoveAsync(0, 0);
            await Task.Delay(500);

            while (true)
            {
                Console.WriteLine("────────────────────────────────────────────");
                Console.WriteLine("  1. 정상 이동        (비교용 — 이건 되어야 합니다)");
                Console.WriteLine("  2. 순간이동 시도");
                Console.WriteLine("  3. 남의 ID 로 이동 시도");
                Console.WriteLine("  4. 아이템 순간 획득 시도");
                Console.WriteLine("  6. 속도핵 시도        ★ 검사로는 막히지 않습니다");
                Console.WriteLine("  5. 전부 한 번에");
                Console.WriteLine("  0. 종료");
                Console.WriteLine("────────────────────────────────────────────");
                Console.Write("  선택 : ");

                string? sel = Console.ReadLine();
                Console.WriteLine();

                try
                {
                    switch (sel)
                    {
                        case "1": await NormalMoveAsync();  break;
                        case "2": await TeleportAsync();    break;
                        case "3": await ImpersonateAsync(); break;
                        case "4": await ItemGrabAsync();    break;
                        case "6": await SpeedHackAsync();   break;
                        case "5":
                            await NormalMoveAsync();
                            await TeleportAsync();
                            await ImpersonateAsync();
                            await ItemGrabAsync();
                            await SpeedHackAsync();
                            break;
                        case "0":
                            _victim.Close();
                            _attacker.Close();
                            return;
                        default:
                            Console.WriteLine("  없는 번호입니다.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [에러] {ex.GetType().Name} : {ex.Message}");
                }

                Console.WriteLine();
            }
        }

        // ═══════════════════════════════════════
        // 1. 정상 이동
        // ═══════════════════════════════════════
        private static async Task NormalMoveAsync()
        {
            Console.WriteLine("  [1] 정상 이동 — 오른쪽으로 5칸");

            var before = await _attacker.WhereAmIAsync();

            for (int i = 0; i < 5; i++)
            {
                await _attacker.MoveAsync(1, 0);
                await Task.Delay(140);   // 서버 걸음 쿨타임(132ms)에 맞춘 간격
            }

            await Task.Delay(400);
            var after = await _attacker.WhereAmIAsync();

            Console.WriteLine($"      이동 전 {Pos(before)}  →  이동 후 {Pos(after)}");

            if (after != null && before != null && after.Value.x > before.Value.x)
                Ok("정상 이동은 됩니다. 여기까지는 맞습니다.");
            else
                Warn("정상 이동이 안 됩니다. 검증이 너무 빡빡하거나 구현이 덜 됐습니다.");
        }

        // ═══════════════════════════════════════
        // 2. 순간이동
        // ═══════════════════════════════════════
        private static async Task TeleportAsync()
        {
            Console.WriteLine("  [2] 순간이동 — MOVE 에 DX 를 20 으로 적어 보냅니다");

            var before = await _attacker.WhereAmIAsync();

            await _attacker.MoveAsync(20, 0);
            await Task.Delay(500);

            var after = await _attacker.WhereAmIAsync();

            Console.WriteLine($"      이동 전 {Pos(before)}  →  이동 후 {Pos(after)}");

            if (after != null && before != null && Math.Abs(after.Value.x - before.Value.x) > 1)
                Bad("뚫렸습니다. 한 틱 이동 거리를 검사하지 않고 있습니다.");
            else
                Ok("차단됨. 한 틱에 갈 수 있는 거리만 허용하고 있습니다.");
        }

        // ═══════════════════════════════════════
        // 3. 남의 ID 로 이동
        // ═══════════════════════════════════════
        private static async Task ImpersonateAsync()
        {
            Console.WriteLine($"  [3] 공격자가 피해자(PlayerId {_victim.PlayerId}) 의 ID 를 적어 보냅니다");

            var before = await _victim.WhereAmIAsync();

            // 공격자의 소켓으로, 피해자의 ID 를 적어서 보냅니다
            for (int i = 0; i < 5; i++)
            {
                await _attacker.MoveAsAsync(_victim.PlayerId, 1, 0);
                await Task.Delay(60);
            }

            await Task.Delay(500);
            var after = await _victim.WhereAmIAsync();

            Console.WriteLine($"      피해자 위치 {Pos(before)}  →  {Pos(after)}");

            if (after != null && before != null && after.Value.x != before.Value.x)
                Bad("뚫렸습니다. 패킷에 적힌 PlayerId 를 그대로 믿고 있습니다.");
            else
                Ok("차단됨. 등록된 주소에서 온 패킷만 받고 있습니다.");
        }

        // ═══════════════════════════════════════
        // 4. 아이템 순간 획득
        // ═══════════════════════════════════════
        private static async Task ItemGrabAsync()
        {
            Console.WriteLine("  [4] 아이템 위치로 한 번에 뛰어서 먹기");

            var item = await _attacker.FirstItemAsync();

            if (item == null)
            {
                Console.WriteLine("      맵에 아이템이 없습니다. 잠깐 기다렸다 다시 해보세요.");
                return;
            }

            var me = await _attacker.WhereAmIAsync();
            if (me == null)
            {
                Console.WriteLine("      내 위치를 모르겠습니다. STATE 를 못 받고 있습니다.");
                return;
            }

            int dx = item.Value.x - me.Value.x;
            int dy = item.Value.y - me.Value.y;

            Console.WriteLine($"      내 위치 {Pos(me)} → 아이템 ({item.Value.x},{item.Value.y})");
            Console.WriteLine($"      MOVE 에 DX={dx} DY={dy} 를 적어 보냅니다");

            await _attacker.MoveAsync(dx, dy);
            await Task.Delay(500);

            var after = await _attacker.WhereAmIAsync();
            Console.WriteLine($"      결과 위치 {Pos(after)}");

            bool jumped = after != null &&
                          Math.Abs(after.Value.x - me.Value.x) > 1;

            if (jumped) Bad("뚫렸습니다. 거리 검사 없이 좌표를 받아 적고 있습니다.");
            else        Ok("차단됨. 한 칸씩 걸어가야 먹을 수 있습니다.");
        }

        // ═══════════════════════════════════════
        // 6. 속도핵   ★ 오늘의 핵심
        //
        //   2번(순간이동)과 뭐가 다른가 —
        //
        //     2번은 DX 에 20 을 적습니다.  규칙을 어깁니다.
        //     6번은 DX 에 1 을 적습니다.   규칙은 지킵니다.
        //          대신 1초에 300번 보냅니다.
        //
        //   그래서 "한 틱 거리 검사" 로는 6번을 막을 수 없습니다.
        //   검사를 추가해서 막는 게 아니라,
        //   서버가 자기 속도로 꺼내 쓰게 만들어야 막힙니다.
        // ═══════════════════════════════════════
        private const int MAP_W      = 20;
        private const int WAIT_MS    = 600;
        private const int HONEST_MS  = 132;   // 정상 클라의 한 걸음 간격

        private static async Task SpeedHackAsync()
        {
            Console.WriteLine("  [6] 속도핵 — DX=1 을 쉬지 않고 쏟아붓습니다");
            Console.WriteLine("      거리를 어기는 게 아니라 보내는 '횟수' 로 속도를 올립니다.");
            Console.WriteLine("      그래서 2번을 막은 검사로는 이게 막히지 않습니다.");
            Console.WriteLine();

            // 출발선을 맞춥니다 — 정상 속도로 왼쪽 끝까지
            Console.Write($"      먼저 왼쪽 끝으로 정상 속도로 걸어갑니다 (약 {MAP_W * HONEST_MS / 1000}초) ");
            for (int i = 0; i < MAP_W + 2; i++)
            {
                await _attacker.MoveAsync(-1, 0);
                await Task.Delay(HONEST_MS + 8);   // 서버 걸음 쿨타임보다 살짝 느리게
                if (i % 5 == 0) Console.Write(".");
            }
            await Task.Delay(500);
            Console.WriteLine(" 도착");

            var before = await _attacker.WhereAmIAsync();
            if (before == null)
            {
                Console.WriteLine("      위치를 못 읽었습니다. STATE 가 안 오고 있습니다.");
                return;
            }

            // 쏟아붓기 — 측정하는 동안 쉬지 않고 계속 보냅니다
            //
            //   한 번에 300개를 쏟고 끝내면 서버의 줄 상한(5개)에 걸려
            //   총 5걸음밖에 못 갑니다. 그러면 걸음 쿨타임이 빠져 있어도
            //   "차단" 으로 나와서 속도를 잴 수가 없습니다.
            //   계속 보내야 "서버가 얼마나 빨리 꺼내 쓰는가" 가 드러납니다.
            int sent = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < WAIT_MS)
            {
                for (int k = 0; k < 4; k++)
                {
                    await _attacker.MoveAsync(1, 0);
                    sent++;
                }
                await Task.Delay(10);
            }

            var after = await _attacker.WhereAmIAsync();

            // 정상 클라이언트가 이 시간에 갈 수 있는 거리
            int honest = WAIT_MS / HONEST_MS;

            Console.WriteLine($"      {WAIT_MS}ms 동안 MOVE {sent}개를 쉬지 않고 전송 " +
                              $"(정상 클라는 이 시간에 약 {honest}개)");
            Console.WriteLine($"      위치 {Pos(before)} → {Pos(after)}");
            Console.WriteLine($"      정상 속도라면 이 시간에 갈 수 있는 거리는 약 {honest}칸");

            if (after == null)
            {
                Console.WriteLine("      위치를 못 읽었습니다.");
                return;
            }

            int moved = after.Value.x - before.Value.x;
            Console.WriteLine($"      실제로 간 거리 {moved}칸");
            Console.WriteLine();

            if (moved > honest + 4)
                Bad("뚫렸습니다. 걸음 쿨타임(NextStepTick) 검사가 빠졌거나, " +
                    "while 로 줄을 한 번에 다 꺼내고 있습니다. (TODO 5)");
            else
                Ok("차단됨. 몇 개를 보내든 틱당 한 걸음입니다. " +
                   "서버 콘솔의 '폐기' 숫자가 올라갔는지 확인하세요.");
        }

        // ═══════════════════════════════════════
        // 출력 도우미
        // ═══════════════════════════════════════
        private static string Pos((int x, int y)? p) => p == null ? "(모름)" : $"({p.Value.x},{p.Value.y})";

        private static void Ok(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"      → {msg}");
            Console.ResetColor();
        }

        private static void Bad(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"      → {msg}");
            Console.ResetColor();
        }

        private static void Warn(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"      → {msg}");
            Console.ResetColor();
        }
    }

    // ═══════════════════════════════════════════
    // 봇 하나 (TCP 로그인 + UDP 조작)
    // ═══════════════════════════════════════════
    public class Bot
    {
        private const string SERVER_IP = "127.0.0.1";
        private const int    TCP_PORT  = 7777;
        private const int    UDP_PORT  = 7778;

        private Socket _tcp = null!;
        private Socket _udp = null!;
        private IPEndPoint _server = null!;

        private int _seq;

        public int    PlayerId { get; private set; }
        public string Nickname { get; private set; } = "";

        public static async Task<Bot> ConnectAsync(string nickname)
        {
            var bot = new Bot { Nickname = nickname };

            bot._tcp = new Socket(AddressFamily.InterNetwork,
                                  SocketType.Stream, ProtocolType.Tcp);
            await bot._tcp.ConnectAsync(IPAddress.Parse(SERVER_IP), TCP_PORT);

            await bot.SendJsonAsync(new { Type = 1, Nickname = nickname });

            string? welcome = await bot.ReceiveJsonAsync(5000);
            if (welcome != null && welcome.Contains("\"Type\":11"))
            {
                try
                {
                    dynamic? p = Newtonsoft.Json.JsonConvert.DeserializeObject(welcome);
                    if (p != null) bot.PlayerId = (int)p.PlayerId;
                }
                catch { }
            }

            bot._udp = new Socket(AddressFamily.InterNetwork,
                                  SocketType.Dgram, ProtocolType.Udp);
            bot._udp.Bind(new IPEndPoint(IPAddress.Any, 0));
            bot._udp.ReceiveTimeout = 1500;

            bot._server = new IPEndPoint(IPAddress.Parse(SERVER_IP), UDP_PORT);

            return bot;
        }

        /// <summary>내 ID 로 방향을 보냅니다</summary>
        public Task MoveAsync(int dx, int dy) => MoveAsAsync(PlayerId, dx, dy);

        /// <summary>지정한 ID 로 방향을 보냅니다 (치트용)</summary>
        public async Task MoveAsAsync(int playerId, int dx, int dy)
        {
            _seq++;
            byte[] d = Encoding.UTF8.GetBytes($"MOVE|{playerId}|{_seq}|{dx}|{dy}");
            await _udp.SendToAsync(new ArraySegment<byte>(d), SocketFlags.None, _server);
        }

        /// <summary>STATE 를 받아서 내 좌표를 꺼냅니다</summary>
        public async Task<(int x, int y)?> WhereAmIAsync()
        {
            string? state = await ReadUdpAsync("STATE");
            if (state == null) return null;

            foreach (var row in Rows(state, 2))
            {
                if (row.Length < 4) continue;
                if (!int.TryParse(row[0], out int id) || id != PlayerId) continue;
                if (!int.TryParse(row[2], out int x)) continue;
                if (!int.TryParse(row[3], out int y)) continue;
                return (x, y);
            }

            return null;
        }

        /// <summary>맵에 있는 아이템 하나를 찾습니다</summary>
        public async Task<(int x, int y)?> FirstItemAsync()
        {
            string? items = await ReadUdpAsync("ITEMS");
            if (items == null) return null;

            foreach (var row in Rows(items))
            {
                if (row.Length < 3) continue;
                if (!int.TryParse(row[1], out int x)) continue;
                if (!int.TryParse(row[2], out int y)) continue;
                return (x, y);
            }

            return null;
        }

        /// <summary>
        /// 머리글|행|행|행 형태를 잘라서 돌려줍니다
        ///
        /// ★ STATE 는 머리글 뒤에 AckSeq 가 하나 더 붙습니다
        ///
        ///     STATE|152|1,홍길동,10,7|3,철수,4,2      skip = 2
        ///     ITEMS|7,3,9|8,15,2                      skip = 1
        /// </summary>
        private static IEnumerable<string[]> Rows(string packet, int skip = 1)
        {
            string[] parts = packet.Split('|');
            for (int i = skip; i < parts.Length; i++)
                yield return parts[i].Split(',');
        }

        /// <summary>원하는 머리글의 UDP 패킷이 올 때까지 읽습니다</summary>
        private Task<string?> ReadUdpAsync(string head)
        {
            return Task.Run(() =>
            {
                byte[] buf = new byte[4096];
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                string? latest = null;

                var sw = System.Diagnostics.Stopwatch.StartNew();

                // ═══════════════════════════════════════════════
                //  ★ 쌓인 것을 전부 꺼내고 "가장 마지막" 것을 씁니다
                //
                //    서버는 초당 30번 STATE 를 보냅니다.
                //    읽지 않고 0.5초를 보내면 큐에 15개가 쌓입니다.
                //
                //    ReceiveFrom 을 한 번만 부르면 그 중
                //    "가장 먼저 온 것" = 0.5초 전 상태가 나옵니다.
                //
                //    그 값으로 치트 성공 여부를 판정하면
                //    실제로는 뚫렸는데 "차단됨" 으로 나옵니다.
                //
                //    Unity 클라이언트의 TryGetLatestState 도 같은 이유로
                //    큐를 끝까지 비우고 마지막 것만 씁니다.
                // ═══════════════════════════════════════════════
                while (sw.ElapsedMilliseconds < 1200)
                {
                    if (_udp.Available == 0)
                    {
                        if (latest != null) break;      // 더 안 옵니다
                        Thread.Sleep(10);
                        continue;
                    }

                    try
                    {
                        int n = _udp.ReceiveFrom(buf, ref from);

                        string msg = Encoding.UTF8.GetString(buf, 0, n);
                        if (msg.StartsWith(head)) latest = msg;
                    }
                    catch
                    {
                        break;
                    }
                }

                return latest;
            });
        }

        // ── TCP 길이 프리픽스 ──
        private async Task SendJsonAsync(object packet)
        {
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(packet);
            byte[] body = Encoding.UTF8.GetBytes(json);

            byte[] full = new byte[4 + body.Length];
            BitConverter.GetBytes(body.Length).CopyTo(full, 0);
            body.CopyTo(full, 4);

            int sent = 0;
            while (sent < full.Length)
            {
                sent += await _tcp.SendAsync(
                    new ArraySegment<byte>(full, sent, full.Length - sent), SocketFlags.None);
            }
        }

        private async Task<string?> ReceiveJsonAsync(int timeoutMs)
        {
            byte[] header = new byte[4];
            if (!await ReadExactAsync(header, 4, timeoutMs)) return null;

            int len = BitConverter.ToInt32(header, 0);
            if (len <= 0 || len > 65536) return null;

            byte[] body = new byte[len];
            if (!await ReadExactAsync(body, len, timeoutMs)) return null;

            return Encoding.UTF8.GetString(body, 0, len);
        }

        private async Task<bool> ReadExactAsync(byte[] buf, int size, int timeoutMs)
        {
            int read = 0;

            while (read < size)
            {
                Task<int> recv = _tcp.ReceiveAsync(
                    new ArraySegment<byte>(buf, read, size - read), SocketFlags.None);

                Task done = await Task.WhenAny(recv, Task.Delay(timeoutMs));
                if (done != (Task)recv) return false;

                int n = await recv;
                if (n == 0) return false;
                read += n;
            }

            return true;
        }

        public void Close()
        {
            try { _tcp.Shutdown(SocketShutdown.Both); } catch { }
            try { _tcp.Close(); } catch { }
            try { _udp.Close(); } catch { }
        }
    }
}
