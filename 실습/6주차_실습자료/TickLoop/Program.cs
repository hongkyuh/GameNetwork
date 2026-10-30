using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace TickLoop
{
    /// <summary>
    /// ═══════════════════════════════════════════════
    ///  예제 F — 틱 루프
    ///
    ///  네트워크를 빼고 "게임 루프" 만 봅니다.
    ///  서버에 들어갈 구조가 그대로 들어 있습니다.
    ///
    ///      1. 입력 처리    쌓인 조작을 꺼내 검사하고 적용
    ///      2. 게임 로직    아이템 스폰 · 획득 판정
    ///      3. 그리기       화면에 뿌리기
    ///
    ///  ── 조작 ──
    ///      W A S D   이동
    ///      V         검증 켜기 / 끄기     ★ 이걸 눌러보세요
    ///      Q         종료
    ///
    ///  검증을 끄면 봇 하나가 순간이동을 시작합니다.
    /// ═══════════════════════════════════════════════
    /// </summary>
    public static class Program
    {
        public const int MAP_W = 20;
        public const int MAP_H = 12;

        private const int TICK_MS     = 33;   // 초당 약 30번
        private const int SPAWN_TICKS = 30;
        private const int MAX_ITEMS   = 8;

        private static readonly ConcurrentQueue<Input> _inputs = new ConcurrentQueue<Input>();
        private static readonly List<Player> _players = new List<Player>();
        private static readonly Dictionary<int, Item> _items = new Dictionary<int, Item>();

        private static readonly Random _rng = new Random();

        private static int  _nextItemId;
        private static long _tick;
        private static long _applied;
        private static long _rejected;

        /// <summary>★ 이게 오늘의 주인공입니다</summary>
        private static bool _validate = true;

        public static async Task Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.CursorVisible  = false;
            Console.Clear();

            _players.Add(new Player { Id = 1, Mark = '@', Name = "나",   X = 5,  Y = 5 });
            _players.Add(new Player { Id = 2, Mark = 'A', Name = "봇A",  X = 15, Y = 2 });
            _players.Add(new Player { Id = 3, Mark = 'B', Name = "봇B",  X = 18, Y = 9, Cheater = true });

            _ = BotLoopAsync();

            while (true)
            {
                // ── 키 입력 → 큐에 넣기만 ──
                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);

                    switch (key.Key)
                    {
                        case ConsoleKey.W: case ConsoleKey.UpArrow:    Push(1,  0,  1); break;
                        case ConsoleKey.S: case ConsoleKey.DownArrow:  Push(1,  0, -1); break;
                        case ConsoleKey.A: case ConsoleKey.LeftArrow:  Push(1, -1,  0); break;
                        case ConsoleKey.D: case ConsoleKey.RightArrow: Push(1,  1,  0); break;

                        case ConsoleKey.V: _validate = !_validate; break;

                        case ConsoleKey.Q:
                            Console.CursorVisible = true;
                            Console.Clear();
                            return;
                    }
                }

                // ── 틱 ──
                await Task.Delay(TICK_MS);
                _tick++;

                ProcessInputs();

                if (_tick % SPAWN_TICKS == 0) SpawnItem();
                CheckPickups();

                if (_tick % 3 == 0) Render();    // 그리기는 10fps 면 충분합니다
            }
        }

        private static void Push(int id, int dx, int dy)
        {
            _inputs.Enqueue(new Input { PlayerId = id, Dx = dx, Dy = dy });
        }

        // ═══════════════════════════════════════
        // 1. 입력 처리
        // ═══════════════════════════════════════
        private static void ProcessInputs()
        {
            while (_inputs.TryDequeue(out Input cmd))
            {
                Player? p = _players.Find(x => x.Id == cmd.PlayerId);
                if (p == null) continue;

                // ★ 한 틱에 갈 수 있는 거리인가
                //
                //   이 검사가 없으면 dx 에 99 를 적어 보내는 순간
                //   맵 반대편으로 날아갑니다.
                if (_validate && (Math.Abs(cmd.Dx) > 1 || Math.Abs(cmd.Dy) > 1))
                {
                    _rejected++;
                    continue;
                }

                p.X = Math.Clamp(p.X + cmd.Dx, 0, MAP_W - 1);
                p.Y = Math.Clamp(p.Y + cmd.Dy, 0, MAP_H - 1);

                _applied++;
            }
        }

        // ═══════════════════════════════════════
        // 2. 게임 로직
        // ═══════════════════════════════════════
        private static void SpawnItem()
        {
            if (_items.Count >= MAX_ITEMS) return;

            int id = ++_nextItemId;
            _items[id] = new Item { Id = id, X = _rng.Next(MAP_W), Y = _rng.Next(MAP_H) };
        }

        private static void CheckPickups()
        {
            // 틱 루프가 혼자 돌기 때문에 lock 이 필요 없습니다.
            // "동시에 두 명이 먹는" 일이 생길 수가 없습니다.
            foreach (var p in _players)
            {
                int? hit = null;

                foreach (var it in _items.Values)
                {
                    if (Math.Abs(it.X - p.X) > 1) continue;
                    if (Math.Abs(it.Y - p.Y) > 1) continue;

                    hit = it.Id;
                    break;
                }

                if (hit != null && _items.Remove(hit.Value))
                    p.Score++;
            }
        }

        // ═══════════════════════════════════════
        // 봇 — 조작을 만들어 큐에 넣습니다
        // ═══════════════════════════════════════
        private static async Task BotLoopAsync()
        {
            while (true)
            {
                await Task.Delay(120);

                foreach (var p in _players)
                {
                    if (p.Id == 1) continue;     // 사람은 건너뜁니다

                    if (p.Cheater && !_validate)
                    {
                        // 검증이 꺼져 있으면 아이템으로 순간이동합니다
                        foreach (var it in _items.Values)
                        {
                            Push(p.Id, it.X - p.X, it.Y - p.Y);
                            break;
                        }
                        continue;
                    }

                    Push(p.Id, _rng.Next(-1, 2), _rng.Next(-1, 2));
                }
            }
        }

        // ═══════════════════════════════════════
        // 3. 그리기
        // ═══════════════════════════════════════
        private static void Render()
        {
            var lines = new List<string>();

            lines.Add("  예제 F — 틱 루프");
            lines.Add($"  틱 {_tick,6}    적용 {_applied,6}    거부 {_rejected,6}    아이템 {_items.Count}");
            lines.Add("");
            lines.Add(_validate
                ? "  검증 : 켜짐     (V 를 누르면 꺼집니다)"
                : "  검증 : 꺼짐 ★   봇B 가 순간이동 중입니다");
            lines.Add("");

            lines.Add("  ┌" + new string('─', MAP_W) + "┐");

            for (int row = MAP_H - 1; row >= 0; row--)
            {
                var buf = new char[MAP_W];
                for (int i = 0; i < MAP_W; i++) buf[i] = '.';

                foreach (var it in _items.Values)
                    if (it.Y == row && it.X >= 0 && it.X < MAP_W) buf[it.X] = '*';

                foreach (var p in _players)
                    if (p.Y == row && p.X >= 0 && p.X < MAP_W) buf[p.X] = p.Mark;

                lines.Add("  │" + new string(buf) + "│");
            }

            lines.Add("  └" + new string('─', MAP_W) + "┘");
            lines.Add("");

            var sb = new StringBuilder("  점수   ");
            foreach (var p in _players) sb.Append($"{p.Mark} {p.Name} {p.Score}    ");
            lines.Add(sb.ToString());

            lines.Add("");
            lines.Add("  W A S D 이동    V 검증 켜기/끄기    Q 종료");

            try
            {
                int w = Math.Max(40, Console.WindowWidth - 1);
                Console.SetCursorPosition(0, 0);

                for (int i = 0; i < lines.Count; i++)
                {
                    string line = lines[i].PadRight(w);
                    if (line.Length > w) line = line.Substring(0, w);

                    if (i == lines.Count - 1) Console.Write(line);
                    else                      Console.WriteLine(line);
                }
            }
            catch { }
        }
    }

    public struct Input
    {
        public int PlayerId;
        public int Dx;
        public int Dy;
    }

    public class Player
    {
        public int    Id;
        public char   Mark;
        public string Name = "";
        public int    X;
        public int    Y;
        public int    Score;
        public bool   Cheater;
    }

    public class Item
    {
        public int Id;
        public int X;
        public int Y;
    }
}
