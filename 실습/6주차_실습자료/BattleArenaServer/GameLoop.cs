using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace BattleArenaServer
{
    /// <summary>맵에 떨어져 있는 아이템 하나</summary>
    public class Item
    {
        public int Id;
        public int X;
        public int Y;
    }

    /// <summary>
    /// ═══════════════════════════════════════════════
    ///  [6주차 — TICKET-021] 게임 루프
    ///
    ///  ── 5주차까지 서버가 한 일 ──
    ///      받은 좌표를 그대로 적고, 그대로 뿌림        (우체국)
    ///
    ///  ── 오늘부터 서버가 하는 일 ──
    ///      방향만 받아서 좌표를 직접 계산하고,
    ///      아이템을 뿌리고, 누가 먹었는지 판정함        (심판)
    ///
    ///  ── 그런데 "방향만 받는다" 로는 부족합니다 ──
    ///
    ///      MOVE|3|152|20|0     DX 가 20      → 거리 검사로 막힙니다
    ///
    ///      MOVE|3|152|1|0      DX 가 1
    ///      MOVE|3|153|1|0        "
    ///      MOVE|3|154|1|0      1초에 300번   → 검사를 전부 통과합니다
    ///
    ///    두 번째는 규칙을 하나도 안 어겼습니다.
    ///    거리가 아니라 "횟수" 로 속도를 올린 것이라
    ///    검사를 아무리 늘려도 막히지 않습니다.
    ///
    ///        좌표를 보냄    서버가 위치를 안 가짐   →  순간이동
    ///        명령을 보냄    서버가 시계를 안 가짐   →  속도핵
    ///        틱이 꺼내 씀   서버가 둘 다 가짐       →  막힘   ← 오늘 만드는 것
    ///
    ///  ── 틱 하나에서 일어나는 일 ──
    ///
    ///      1. 받기     쌓인 MOVE 를 검사해서 사람별 줄에 세움
    ///      2. 걸음     각 줄에서 "딱 하나" 꺼내 좌표를 계산       ★
    ///      3. 게임     아이템 스폰 · 획득 판정 · 점수
    ///      4. 알리기   STATE · ITEMS 를 전체에 전송
    ///
    ///    핵심은 2번입니다. 몇 개가 쌓여 있든 한 번에 하나만 씁니다.
    ///    패킷을 1000개 보내도 속도는 그대로입니다.
    ///
    ///  ★ 이 루프는 한 번에 하나만 돕니다.
    ///    그래서 lock 이 하나도 없습니다.
    ///    "동시에 두 명이 먹으면?" 이라는 문제가 애초에 안 생깁니다.
    ///
    ///  ── 덤으로 생기는 것 ──
    ///
    ///    꺼낸 입력의 번호를 AckSeq 에 적어 STATE 로 돌려줍니다.
    ///    "네 152번까지 반영했고 그 결과가 (10,7)이다" 라는 뜻입니다.
    ///    클라이언트는 이 번호로 자기 예측을 맞춰볼 수 있습니다.
    ///    → 예제 H · 오늘 후반부
    /// ═══════════════════════════════════════════════
    /// </summary>
    public class GameLoop
    {
        // ── 맵 ──
        public const int MAP_W = 20;
        public const int MAP_H = 12;

        /// <summary>틱 간격 (33ms ≒ 초당 30번)</summary>
        private const int TICK_MS = 33;

        /// <summary>몇 틱마다 아이템을 하나 뿌릴지 (60틱 ≒ 2초)</summary>
        private const int SPAWN_TICKS = 60;

        /// <summary>맵에 동시에 존재할 수 있는 아이템 수</summary>
        private const int MAX_ITEMS = 8;

        /// <summary>아이템을 주울 수 있는 거리 (0이면 같은 칸만)</summary>
        private const int PICK_RANGE = 1;

        /// <summary>
        /// 한 사람 줄에 쌓아둘 수 있는 입력 개수 (오늘의 핵심 상수 ①)
        ///
        /// ★ 이 상수는 속도핵을 막는 장치가 아닙니다.
        ///   속도는 STEP_TICKS(걸음 쿨타임)가 막습니다.
        ///   줄에 300개가 쌓여 있어도 132ms 에 한 칸 이상은 못 갑니다.
        ///
        /// 이 상수가 막는 것은 "밀린 입력이 한참 동안 재생되는 것" 입니다.
        ///
        ///   상한이 없으면
        ///     · 치터가 0.04초에 300개를 보내고 손을 떼도
        ///       캐릭터가 300 x 132ms = 약 40초 동안 혼자 걸어갑니다
        ///     · 그 사이 새로 누른 키는 300개 뒤에 줄을 서서
        ///       40초 뒤에야 반영됩니다
        ///     · 네트워크가 3초 끊겼던 정상 사용자도
        ///       3초 전 입력을 다 재생하느라 조작이 3초 밀립니다
        ///     · 줄이 끝없이 길어져 서버 메모리를 먹습니다
        ///
        ///   상한이 5면 밀려 봐야 0.66초 분량이고,
        ///   옛것부터 버리니 금방 최신 입력으로 돌아옵니다.
        ///
        /// 왜 0 이 아니라 5 인가 — 네트워크는 고르지 않습니다.
        /// 132ms 간격으로 보내도 가끔 두 개가 붙어서 옵니다.
        /// 조금 쌓아둬야 그 울렁임을 흡수해서 안 끊깁니다.
        /// </summary>
        private const int MAX_PENDING = 5;

        /// <summary>
        /// 한 걸음에 필요한 틱 수 (오늘의 핵심 상수 ②)
        ///
        /// 틱당 하나씩 꺼내 쓰기만 하면 최고 속도가 초당 30칸이 됩니다.
        /// 클라이언트는 0.132초마다 한 번 보내니 초당 7~8칸인데,
        /// 치터는 초당 30칸까지 낼 수 있다는 뜻입니다. 여전히 4배 빠릅니다.
        ///
        /// 그래서 걸음에도 쿨타임을 둡니다.
        /// 4틱 = 132ms = 클라이언트의 이동 쿨다운과 같은 값입니다.
        ///
        /// ★ 이 값과 GameConfig.MOVE_COOLDOWN 은 같아야 합니다.
        ///   서버가 더 느리면 입력이 쌓여서 조작이 밀리고,
        ///   서버가 더 빠르면 치터에게 여유를 주는 셈입니다.
        /// </summary>
        private const int STEP_TICKS = 4;

        private readonly UdpServer _udp;
        private readonly Random _rng = new Random();

        // 틱 루프 안에서만 건드리므로 일반 Dictionary 로 충분합니다
        private readonly Dictionary<int, Item> _items = new Dictionary<int, Item>();
        private int _nextItemId;

        /// <summary>
        /// 사람별 입력 줄
        ///
        /// UdpServer 의 큐는 전원이 섞인 한 줄입니다.
        /// 그러면 "이 사람이 몇 개 썼는지" 를 셀 수가 없습니다.
        /// 사람별로 나눠야 한 명당 하나씩 꺼내 쓸 수 있습니다.
        /// </summary>
        private readonly Dictionary<int, Queue<InputCommand>> _pending
            = new Dictionary<int, Queue<InputCommand>>();

        private long _tick;
        private long _inputCount;
        private long _rejectCount;
        private long _dropCount;

        public long Tick        => _tick;
        public long InputCount  => _inputCount;
        public long RejectCount => _rejectCount;
        public long DropCount   => _dropCount;
        public int  ItemCount   => _items.Count;

        public GameLoop(UdpServer udp)
        {
            _udp = udp;
        }

        // ═══════════════════════════════════════
        // 루프
        // ═══════════════════════════════════════
        public async Task RunAsync()
        {
            Console.WriteLine($"[게임] 틱 루프 시작 ({1000 / TICK_MS}fps · 맵 {MAP_W}x{MAP_H})");
            Console.WriteLine($"[게임] 입력 버퍼 {MAX_PENDING}개 · " +
                              $"걸음 쿨타임 {STEP_TICKS}틱({STEP_TICKS * TICK_MS}ms)");
            Console.WriteLine();

            while (true)
            {
                await Task.Delay(TICK_MS);
                _tick++;

                var sessions = GameServer.Instance.GetSessions();

                // ═══════════════════════════════════════════════
                //  TODO 2 — 틱 하나에서 할 일을 순서대로 부르세요   ★★
                //
                //    1. IntakeInputs()              받은 입력을 줄에 세운다
                //    2. StepPlayers(sessions)       각 줄에서 하나씩 꺼내 쓴다
                //    3. SPAWN_TICKS 마다 SpawnItem()
                //       await CheckPickupsAsync(sessions)
                //    4. await BroadcastAsync(sessions)
                //
                //    ★ 순서가 중요합니다.
                //      알리기를 먼저 하면 한 틱 전 상태를 뿌리게 됩니다.
                //      판정이 끝난 뒤에 알려야 합니다.
                //
                //    3번의 "몇 틱마다" 는 나머지 연산으로 씁니다.
                //        _tick % SPAWN_TICKS == 0
                //
                //    예제 F(TickLoop) 의 루프와 같은 구조입니다.
                // ═══════════════════════════════════════════════

            }
        }

        // ═══════════════════════════════════════
        // 1. 받기 — 검사해서 줄에 세우기만 합니다
        //
        //    여기서는 "움직이지" 않습니다.
        //    움직이는 것은 StepPlayers 가 합니다.
        // ═══════════════════════════════════════
        private void IntakeInputs()
        {
            while (_udp.Inputs.TryDequeue(out InputCommand cmd))
            {
                _inputCount++;

                // ── 검사 1 : 로그인한 사람인가 ──
                ClientSession? s = GameServer.Instance.FindSession(cmd.PlayerId);
                if (s == null)
                {
                    _rejectCount++;
                    continue;
                }

                // ═══════════════════════════════════════════════
                //  TODO 3 — 검사 세 가지를 넣으세요   ★★
                //
                //  (가) 정말 그 사람이 보낸 건가
                //        s.UdpEndPoint 가 null 이면 처음 보낸 것이므로
                //        cmd.From 을 등록합니다. (한 줄 출력해두면 좋습니다)
                //        이미 등록돼 있는데 cmd.From 과 다르면 남이 보낸 것입니다.
                //        → _rejectCount++ 하고 continue
                //
                //        패킷에 적힌 PlayerId 를 그대로 믿으면
                //        누구든 남의 ID 를 적어 보낼 수 있습니다. (예제 G 3번)
                //
                //  (나) 지난 번호인가
                //        cmd.Seq 가 s.LastSeq 보다 크지 않으면 옛날 패킷입니다.
                //        → _rejectCount++ 하고 continue
                //        통과하면 s.LastSeq 를 갱신하세요.
                //
                //        UDP 는 순서를 보장하지 않습니다.
                //        늦게 도착한 옛날 좌표를 쓰면 캐릭터가 뒤로 갑니다.
                //
                //  (다) 한 걸음 거리인가
                //        Dx, Dy 는 -1, 0, 1 만 허용합니다.
                //        20 을 보내면 순간이동이 됩니다. (예제 G 2번)
                //        → _rejectCount++ 하고 continue
                //
                //  ★ 이 세 가지로도 예제 G 6번(속도핵)은 못 막습니다.
                //    6번은 Dx 에 1 을 적고 횟수만 늘리기 때문입니다.
                //    그건 TODO 5 의 걸음 쿨타임이 막습니다.
                // ═══════════════════════════════════════════════


                // ═══════════════════════════════════════════════
                //  TODO 4 — 검사를 통과한 입력을 이 사람의 줄에 세우세요   ★★
                //
                //    (1) _pending 에 이 사람(cmd.PlayerId) 의 줄이 없으면
                //        새 Queue<InputCommand> 를 만들어 넣는다
                //
                //    (2) 그 줄에 이미 MAX_PENDING 개 이상 쌓여 있으면
                //        가장 오래된 것을 하나 Dequeue 해서 버리고 _dropCount++
                //
                //    (3) 방금 온 입력을 줄에 Enqueue 한다
                //
                //    ★ 새로 온 것을 버리면 안 됩니다.
                //      서버가 옛 입력만 처리하면 AckSeq 가 거기서 멈추고,
                //      클라는 버려진 입력을 "아직 확인 안 됨" 으로 계속 들고 있어서
                //      서버와 영영 어긋난 자리에 서 있게 됩니다.
                //
                //  ★ (2)번은 속도핵을 막는 장치가 아닙니다.
                //    속도는 TODO 5 의 걸음 쿨타임이 막습니다.
                //
                //    (2)번이 막는 건 "밀린 입력이 한참 재생되는 것" 입니다.
                //    상한이 없으면 300개를 보내고 손을 떼도 캐릭터가
                //    약 40초 동안 혼자 걸어가고, 그 사이 누른 키는
                //    300개 뒤에 줄을 섭니다.
                //
                //    정상 클라이언트는 여기 걸리지 않습니다.
                //    0.132초마다 한 번 보내고 서버도 그 속도로 꺼내니
                //    줄 길이가 0~1 사이에서 움직입니다.
                //
                //    왜 0 이 아니라 5 인가 — 네트워크는 고르지 않습니다.
                //    조금 쌓아둬야 울렁임을 흡수해서 안 끊깁니다.
                // ═══════════════════════════════════════════════

            }
        }

        // ═══════════════════════════════════════
        // 2. 걸음 — 서버가 시계를 갖는 지점
        //
        //    조건 두 개를 모두 만족해야 한 걸음입니다.
        //
        //      (1) 걸음 쿨타임이 끝났나   STEP_TICKS 마다 한 번
        //      (2) 줄에 입력이 있나        없으면 가만히 있는다
        //
        //    줄에 5개가 쌓여 있어도 쿨타임이 안 끝났으면 안 움직입니다.
        //    남은 것은 다음 차례로 넘어갑니다.
        //
        //    → 패킷을 아무리 빨리 보내도 속도가 안 빨라집니다
        // ═══════════════════════════════════════
        private void StepPlayers(List<ClientSession> sessions)
        {
            foreach (var s in sessions)
            {
                // ═══════════════════════════════════════════════
                //  TODO 5 — 한 걸음을 옮기세요   ★★★
                //
                //    (1) 아직 걸음 차례가 아니면 넘어간다
                //            지금 틱(_tick)이 s.NextStepTick 보다 작으면 continue
                //
                //    (2) _pending 에서 이 사람의 줄을 찾는다
                //            없거나 비어 있으면 continue (가만히 있는다)
                //
                //    (3) 줄에서 하나만 Dequeue 한다
                //            ★ while 로 다 꺼내면 안 됩니다. 그게 속도핵입니다.
                //
                //    (4) 다음 걸음 차례를 예약한다
                //            s.NextStepTick = _tick + STEP_TICKS;
                //
                //    (5) 좌표를 계산한다
                //            Math.Clamp 로 맵 밖으로 못 나가게 막으세요
                //
                //    (6) 어디까지 반영했는지 적어둔다
                //            s.AckSeq = 꺼낸 입력의 Seq
                //
                //  ★ (1)과 (3)이 속도핵을 막는 부분입니다.
                //
                //    (1)이 없으면 틱마다 한 걸음, 즉 초당 30칸까지 나갑니다.
                //    정상 클라는 초당 7~8칸이니 여전히 4배 빠릅니다.
                //
                //    (3)에서 while 을 쓰면 쌓인 걸 전부 써버립니다.
                //    300개가 쌓여 있으면 한 번에 300칸입니다.
                //
                //  ★ (6)이 없으면 클라이언트가 예측 보정을 못 합니다.
                //    화면에 "서버 확인 번호 0" 이 계속 찍히면 이게 빠진 것입니다.
                //
                //  예제 F(TickLoop) 와 예제 H(Prediction) 양쪽에
                //  같은 구조가 들어 있습니다.
                // ═══════════════════════════════════════════════

            }

            // 나간 사람의 줄 치우기
            if (_pending.Count > sessions.Count)
            {
                var live = new HashSet<int>();
                foreach (var s in sessions) live.Add(s.PlayerId);

                var gone = new List<int>();
                foreach (int id in _pending.Keys)
                    if (!live.Contains(id)) gone.Add(id);

                foreach (int id in gone) _pending.Remove(id);
            }
        }

        // ═══════════════════════════════════════
        // 3. 아이템
        // ═══════════════════════════════════════
        private void SpawnItem()
        {
            // ═══════════════════════════════════════════════
            //  TODO 6 — 아이템을 하나 뿌리세요   ★
            //
            //    (1) 맵에 이미 MAX_ITEMS 개가 있으면 아무것도 안 한다
            //
            //    (2) 새 번호를 하나 발급한다
            //            ++_nextItemId
            //            틱 루프 안에서만 증가시키므로
            //            Interlocked 가 필요 없습니다
            //
            //    (3) _items 에 Item 을 하나 넣는다
            //            위치는 _rng.Next(0, MAP_W) · _rng.Next(0, MAP_H)
            //
            //    ★ 어디에 생길지는 서버가 정합니다.
            //      클라이언트가 정하면 "내 발밑에 생겼다" 고
            //      주장할 수 있습니다.
            // ═══════════════════════════════════════════════

        }

        private async Task CheckPickupsAsync(List<ClientSession> sessions)
        {
            if (_items.Count == 0) return;

            var taken = new List<(ClientSession who, Item what)>();

            // ═══════════════════════════════════════════════
            //  TODO 7 — 누가 무엇을 주웠는지 찾으세요   ★★★
            //
            //    모든 사람 x 모든 아이템을 돌면서
            //    거리가 PICK_RANGE 이하면 taken 에 (사람, 아이템) 을 담습니다.
            //
            //    (가) s.UdpEndPoint 가 null 인 사람은 건너뜁니다
            //         아직 게임에 안 들어온 사람입니다.
            //         이걸 빠뜨리면 로그인만 한 사람이 (0,0) 에서
            //         아이템을 주워갑니다.
            //
            //    (나) 거리는 x 와 y 를 따로 봅니다
            //             Math.Abs(item.X - s.X) > PICK_RANGE  이면 아님
            //             Math.Abs(item.Y - s.Y) > PICK_RANGE  이면 아님
            //
            //    (다) 한 사람이 한 틱에 하나만 줍게 하세요
            //         아이템을 찾았으면 break 로 그 사람은 끝냅니다.
            //
            //    ★ 여기서 바로 점수를 올리면 안 됩니다.
            //      _items 를 돌면서 동시에 지우면 예외가 납니다.
            //      일단 taken 에 모아두고, 아래에서 처리합니다.
            //
            //      그리고 두 명이 같은 아이템에 닿았을 수 있습니다.
            //      아래 코드가 "먼저 찾은 사람이 가져간다" 로 해결합니다.
            // ═══════════════════════════════════════════════


            // ★ 먼저 찾은 사람이 가져갑니다.
            //   틱 루프가 혼자 돌기 때문에 순서가 확정됩니다.
            //   두 명이 "동시에" 먹는 일은 일어나지 않습니다.
            foreach (var (who, what) in taken)
            {
                if (!_items.Remove(what.Id)) continue;   // 이미 누가 가져감

                who.Score++;

                GameServer.Instance.Broadcast(new TakenPacket
                {
                    ItemId   = what.Id,
                    PlayerId = who.PlayerId,
                    Nickname = who.Nickname,
                });
            }

            if (taken.Count > 0)
                await BroadcastScoreAsync(sessions);
        }

        private Task BroadcastScoreAsync(List<ClientSession> sessions)
        {
            var sb = new StringBuilder();

            foreach (var s in sessions)
            {
                if (sb.Length > 0) sb.Append('|');
                sb.Append($"{s.PlayerId},{s.Nickname},{s.Score}");
            }

            GameServer.Instance.Broadcast(new ScorePacket { Board = sb.ToString() });
            return Task.CompletedTask;
        }

        // ═══════════════════════════════════════
        // 4. 알리기
        //
        //    ★ STATE 맨 앞에 AckSeq 가 붙습니다
        //
        //      STATE|152|id,닉,x,y|id,닉,x,y
        //            ↑
        //      받는 사람마다 다른 값
        //
        //    사람 목록은 모두에게 같지만 AckSeq 는 각자 다릅니다.
        //    그래서 본문은 한 번만 만들고 앞머리만 바꿔 붙입니다.
        // ═══════════════════════════════════════
        private async Task BroadcastAsync(List<ClientSession> sessions)
        {
            if (sessions.Count == 0) return;

            // 본문 : |id,닉네임,x,y|id,닉네임,x,y...
            var body = new StringBuilder();
            bool any = false;

            foreach (var s in sessions)
            {
                if (s.UdpEndPoint == null) continue;
                body.Append($"|{s.PlayerId},{s.Nickname},{s.X},{s.Y}");
                any = true;
            }

            if (!any) return;

            // ITEMS|아이템id,x,y|...
            var items = new StringBuilder(UdpPacket.ITEMS);
            foreach (var it in _items.Values)
                items.Append($"|{it.Id},{it.X},{it.Y}");

            string bodyMsg = body.ToString();
            string itemsMsg = items.ToString();

            foreach (var s in sessions)
            {
                if (s.UdpEndPoint == null) continue;

                string stateMsg = $"{UdpPacket.STATE}|{s.AckSeq}{bodyMsg}";

                await _udp.SendToAsync(s.UdpEndPoint, stateMsg);
                await _udp.SendToAsync(s.UdpEndPoint, itemsMsg);
            }
        }
    }
}
