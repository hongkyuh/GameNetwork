using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Prediction
{
    /// <summary>
    /// ═══════════════════════════════════════════════
    ///  예제 H — 클라이언트 예측과 보정
    ///
    ///  네트워크를 쓰지 않습니다.
    ///  한 프로그램 안에 클라이언트와 서버가 같이 들어 있고,
    ///  그 사이에 "일부러 지연시키는 큐" 를 놓았습니다.
    ///
    ///      클라 ──[보낸 큐 : 지연]──> 서버
    ///      클라 <─[받은 큐 : 지연]── 서버
    ///
    ///  그래서 지연을 0부터 400ms 까지 마음대로 바꿀 수 있습니다.
    ///  진짜 네트워크로는 이런 실험을 할 수 없습니다.
    ///
    ///  ── 오늘 보려는 것 세 가지 ──
    ///
    ///  1. 예측을 끄면 (P)
    ///       키를 눌러도 지연만큼 늦게 움직입니다.
    ///       150ms 는 생각보다 많이 답답합니다.
    ///
    ///  2. 예측을 켜면
    ///       키를 누른 순간 화면이 반응합니다.
    ///       서버의 확인은 나중에 오지만 화면은 이미 움직였습니다.
    ///
    ///  3. 예측이 틀리면
    ///       맵 가운데에 벽이 있는데 클라이언트는 이걸 모릅니다.
    ///       예측은 벽을 통과하고, 서버의 답이 도착하면 끌려옵니다.
    ///       이게 게임에서 보이는 "고무줄 현상" 입니다.
    ///
    ///       R 을 눌러 클라이언트도 벽을 알게 하면
    ///       보정이 멈춥니다.
    ///
    ///       → 예측은 서버와 같은 규칙으로 계산해야 맞습니다.
    ///         규칙이 다르면 예측이 아니라 그냥 추측입니다.
    ///
    ///  ── 조작 ──
    ///
    ///      A / D       왼쪽 · 오른쪽 한 걸음
    ///      G           오른쪽으로 2초간 자동 질주 (손으로 누르기 귀찮을 때)
    ///      P           예측 켜기 / 끄기
    ///      R           클라이언트도 벽을 알게 하기 / 모르게 하기
    ///      + / -       지연 조절 (0 ~ 400ms)
    ///      Q           종료
    /// ═══════════════════════════════════════════════
    /// </summary>
    public static class Program
    {
        // ── 공통 규칙 ──
        private const int MAP_W   = 30;
        private const int WALL_X  = 15;   // 서버는 항상 안다. 클라는 R 로 켠다.
        private const int TICK_MS = 33;

        /// <summary>서버가 한 사람 몫으로 쌓아두는 입력 개수 (GameLoop 과 같은 값)</summary>
        private const int MAX_PENDING = 5;

        // ── 클라이언트가 보낸 조작 하나 ──
        private struct Input
        {
            public int Seq;
            public int Dx;
        }

        // ── 서버가 돌려주는 상태 ──
        private struct Snapshot
        {
            public int X;
            public int AckSeq;
        }

        // ── 지연 큐 ──
        private static readonly Queue<(long at, Input v)>    _toServer = new Queue<(long, Input)>();
        private static readonly Queue<(long at, Snapshot v)> _toClient = new Queue<(long, Snapshot)>();

        // ── 설정 ──
        private static int  _lagMs          = 150;   // 왕복 지연
        private static bool _predictOn      = true;
        private static bool _clientKnowsWall = false;

        // ── 서버 상태 ──
        private static int _serverX = 5;
        private static int _serverAck;
        private static readonly Queue<Input> _pending = new Queue<Input>();
        private static long _serverUsed;
        private static long _serverDropped;

        // ── 클라이언트 상태 ──
        private static int _seq;
        private static int _predX = 5;                       // 화면에 그리는 위치
        private static int _lastServerX = 5;                 // 서버가 알려준 마지막 위치
        private static int _lastServerAck;
        private static readonly List<Input> _history = new List<Input>();
        private static long _sent;
        private static long _corrections;
        private static string _lastCorrection = "-";

        private static int _autoTicks;

        private static readonly Stopwatch _sw = new Stopwatch();
        private static long Now => _sw.ElapsedMilliseconds;

        public static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.CursorVisible  = false;
            Console.Clear();

            _sw.Start();

            long nextClientTick = 0;
            long nextServerTick = 0;
            long nextRender     = 0;

            while (true)
            {
                if (!PollKeys()) break;

                long now = Now;

                if (now >= nextClientTick) { ClientTick(); nextClientTick = now + TICK_MS; }
                if (now >= nextServerTick) { ServerTick(); nextServerTick = now + TICK_MS; }

                DeliverToClient();

                if (now >= nextRender) { Render(); nextRender = now + 50; }

                Thread.Sleep(1);
            }

            Console.CursorVisible = true;
            Console.Clear();
            Console.WriteLine("예제 H 를 종료합니다.");
        }

        // ═══════════════════════════════════════
        // 입력
        // ═══════════════════════════════════════
        private static bool PollKeys()
        {
            while (Console.KeyAvailable)
            {
                // KeyChar 가 아니라 Key 를 씁니다.
                // 한글 입력 상태에서는 KeyChar 에 'ㅈ' 같은 글자가 들어옵니다.
                ConsoleKey k = Console.ReadKey(true).Key;

                switch (k)
                {
                    case ConsoleKey.Q:          return false;

                    case ConsoleKey.A:
                    case ConsoleKey.LeftArrow:  PressMove(-1); break;

                    case ConsoleKey.D:
                    case ConsoleKey.RightArrow: PressMove(+1); break;

                    case ConsoleKey.G:          _autoTicks = 60; break;

                    case ConsoleKey.P:
                        _predictOn = !_predictOn;
                        // 예측을 끄면 화면은 서버가 알려준 위치만 믿습니다
                        if (!_predictOn) _predX = _lastServerX;
                        break;

                    case ConsoleKey.R:
                        _clientKnowsWall = !_clientKnowsWall;
                        break;

                    case ConsoleKey.Add:
                    case ConsoleKey.OemPlus:
                        _lagMs = Math.Min(400, _lagMs + 50); break;

                    case ConsoleKey.Subtract:
                    case ConsoleKey.OemMinus:
                        _lagMs = Math.Max(0, _lagMs - 50); break;
                }
            }

            return true;
        }

        private static int _queuedDx;

        /// <summary>키를 눌렀을 때 — 다음 클라 틱에 보냅니다</summary>
        private static void PressMove(int dx) => _queuedDx = dx;

        // ═══════════════════════════════════════
        // 클라이언트 — 초당 30번
        // ═══════════════════════════════════════
        private static void ClientTick()
        {
            int dx = _queuedDx;
            _queuedDx = 0;

            if (_autoTicks > 0) { dx = +1; _autoTicks--; }

            if (dx == 0) return;

            _seq++;
            var inp = new Input { Seq = _seq, Dx = dx };

            // ── 1. 기록해 둡니다 ──
            //   서버 답이 올 때까지는 "아직 확인 안 된 입력" 입니다.
            //   이 목록이 있어야 나중에 다시 계산할 수 있습니다.
            _history.Add(inp);

            // ── 2. 예측 : 서버를 기다리지 않고 화면을 먼저 움직입니다 ──
            if (_predictOn)
                _predX = Move(_predX, dx, _clientKnowsWall);

            // ── 3. 보냅니다 (지연 큐에 넣음) ──
            _toServer.Enqueue((Now + _lagMs / 2, inp));
            _sent++;
        }

        // ═══════════════════════════════════════
        // 서버 — 초당 30번
        //
        //   받기 : 도착한 것을 줄에 세운다 (상한 있음)
        //   걸음 : 줄에서 딱 하나만 꺼낸다      ★
        // ═══════════════════════════════════════
        private static void ServerTick()
        {
            // 받기
            while (_toServer.Count > 0 && _toServer.Peek().at <= Now)
            {
                Input inp = _toServer.Dequeue().v;

                // 꽉 찼으면 가장 오래된 것을 버리고 방금 온 것을 넣습니다
                if (_pending.Count >= MAX_PENDING) { _pending.Dequeue(); _serverDropped++; }
                _pending.Enqueue(inp);
            }

            // 걸음 — 쌓인 게 10개여도 이번 틱에는 하나만
            if (_pending.Count > 0)
            {
                Input inp = _pending.Dequeue();

                _serverX   = Move(_serverX, inp.Dx, true);   // 서버는 벽을 안다
                _serverAck = inp.Seq;
                _serverUsed++;
            }

            // 알리기
            _toClient.Enqueue((Now + _lagMs / 2,
                               new Snapshot { X = _serverX, AckSeq = _serverAck }));
        }

        // ═══════════════════════════════════════
        // 클라이언트 — 서버 답을 받았을 때 (보정)
        // ═══════════════════════════════════════
        private static void DeliverToClient()
        {
            while (_toClient.Count > 0 && _toClient.Peek().at <= Now)
            {
                Snapshot snap = _toClient.Dequeue().v;

                _lastServerX   = snap.X;
                _lastServerAck = snap.AckSeq;

                // 서버가 처리했다고 한 입력은 목록에서 지웁니다.
                // 예측을 쓰든 안 쓰든 지워야 합니다. 안 지우면 끝없이 쌓입니다.
                _history.RemoveAll(h => h.Seq <= snap.AckSeq);

                if (!_predictOn)
                {
                    // 예측을 안 하면 서버가 보내준 위치가 곧 화면입니다.
                    // 그래서 지연만큼 늦게 움직입니다.
                    _predX = snap.X;
                    continue;
                }

                // ── 보정 (Reconciliation) ──
                //
                //  1. 서버가 알려준 위치를 출발점으로 삼는다   ← 서버가 진실
                //  2. 아직 확인 안 된 입력을 다시 적용한다
                //
                //  예측이 맞았으면 결과가 지금 화면과 같습니다.
                //  틀렸으면 위치가 바뀝니다. 그게 고무줄 현상입니다.

                int before = _predX;

                int x = snap.X;
                foreach (Input h in _history)
                    x = Move(x, h.Dx, _clientKnowsWall);

                _predX = x;

                if (_predX != before)
                {
                    _corrections++;
                    _lastCorrection = $"x {before} -> {_predX}";
                }
            }
        }

        /// <summary>
        /// 이동 규칙 — 서버와 클라이언트가 같은 함수를 씁니다
        ///
        /// 예측이 맞으려면 이 계산이 양쪽에서 똑같아야 합니다.
        /// knowsWall 을 false 로 주면 벽을 모르는 계산이 되고,
        /// 그 순간 예측이 서버와 어긋나기 시작합니다.
        /// </summary>
        private static int Move(int x, int dx, bool knowsWall)
        {
            int nx = Math.Clamp(x + dx, 0, MAP_W - 1);

            if (knowsWall && nx == WALL_X) return x;   // 벽은 통과할 수 없다

            return nx;
        }

        // ═══════════════════════════════════════
        // 화면
        // ═══════════════════════════════════════
        private const int LINE_W = 70;

        /// <summary>
        /// 한글은 콘솔에서 두 칸을 차지합니다.
        /// 글자 수로 맞추면 줄이 어긋나므로 "차지하는 칸 수" 로 셉니다.
        /// (TestClient 에서 쓰는 것과 같은 방법입니다)
        /// </summary>
        private static int Width(string s)
        {
            int w = 0;
            foreach (char ch in s) w += ch >= 0x1100 ? 2 : 1;
            return w;
        }

        private static string Pad(string s)
        {
            int w = Width(s);
            if (w >= LINE_W) return s;
            return s + new string(' ', LINE_W - w);
        }

        private static void Render()
        {
            var lines = new List<string>();

            string bar = "+" + new string('-', LINE_W - 2) + "+";

            lines.Add(bar);
            lines.Add(ClipLine("|  예제 H   클라이언트 예측과 보정"));
            lines.Add(bar);
            lines.Add("");

            lines.Add($"  왕복 지연 {_lagMs,3}ms      " +
                      $"예측 {(_predictOn ? "ON " : "OFF")}      " +
                      $"클라도 벽을 안다 {(_clientKnowsWall ? "예" : "아니오")}");
            lines.Add("");

            // 눈금
            var ruler = new System.Text.StringBuilder("           ");
            for (int i = 0; i < MAP_W; i++)
                ruler.Append(i % 10 == 0 ? (i / 10).ToString() : ".");
            lines.Add(ruler.ToString());

            lines.Add($"  서버     {Row(_serverX)}   x = {_serverX,2}");
            lines.Add($"  화면     {Row(_predX)}   x = {_predX,2}");

            // 설명 한 줄
            if (_predictOn && !_clientKnowsWall && _predX > WALL_X)
                lines.Add("           " + new string(' ', Math.Min(_predX, MAP_W - 1))
                          + "^ 예측이 벽을 통과");
            else if (!_predictOn)
                lines.Add("           예측 OFF — 화면이 지연만큼 늦게 따라옵니다");
            else
                lines.Add("");

            lines.Add("");
            lines.Add($"  보낸 입력 {_sent,5}   서버가 쓴 입력 {_serverUsed,5}   " +
                      $"대기중 {_pending.Count,2}   폐기 {_serverDropped,4}");
            lines.Add($"  보정 발생 {_corrections,5}회   마지막 보정 {_lastCorrection}");
            lines.Add($"  확인 안 된 입력 {_history.Count,3}개   서버가 본 마지막 번호 {_lastServerAck,5}");
            lines.Add("");
            lines.Add("  A / D  이동      G  2초 자동 질주      P  예측 켜기/끄기");
            lines.Add("  R  클라에 벽 알려주기      + / -  지연 조절      Q  종료");

            var sb = new System.Text.StringBuilder();
            foreach (string l in lines) sb.AppendLine(Pad(l));

            Console.SetCursorPosition(0, 0);
            Console.Write(sb.ToString());
        }

        /// <summary>박스 안쪽 제목 줄 — 오른쪽 테두리까지 맞춥니다</summary>
        private static string ClipLine(string s)
        {
            int w = Width(s);
            if (w > LINE_W - 1) return s;
            return s + new string(' ', LINE_W - 1 - w) + "|";
        }

        /// <summary>맵 한 줄을 문자열로</summary>
        private static string Row(int playerX)
        {
            var c = new char[MAP_W];

            for (int i = 0; i < MAP_W; i++) c[i] = '-';

            c[WALL_X] = '#';

            if (playerX >= 0 && playerX < MAP_W)
                c[playerX] = '@';

            return new string(c);
        }
    }
}
