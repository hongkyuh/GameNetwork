using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace BattleArenaServer
{
    /// <summary>
    /// 클라이언트가 보낸 조작 하나
    ///
    /// 좌표가 아니라 방향입니다.
    /// 좌표는 서버가 계산합니다. (GameLoop.ProcessInputs)
    /// </summary>
    public struct InputCommand
    {
        public int      PlayerId;
        public int      Seq;
        public int      Dx;
        public int      Dy;
        public EndPoint From;      // 실제로 패킷이 날아온 주소
    }

    /// <summary>
    /// ═══════════════════════════════════════════════
    ///  UDP 입출력 담당
    ///
    ///  ★ 5주차와 달라진 점
    ///
    ///    5주차 : 받자마자 그 자리에서 위치를 바꿨습니다
    ///    6주차 : 받으면 큐에 넣기만 합니다
    ///
    ///    판정은 전부 GameLoop 의 틱 안에서 합니다.
    ///    받는 쪽과 판정하는 쪽을 떼어놓으면
    ///    "여러 패킷이 동시에 들어와서 꼬이는" 문제가 없어집니다.
    ///
    ///  ★ 속도핵을 막는 코드가 여기 없다는 점을 봐두세요
    ///
    ///    받는 쪽에서 "너무 빨리 보내네" 를 세는 방법도 있습니다.
    ///    그런데 네트워크가 잠깐 끊겼다 돌아온 사람은
    ///    그동안의 입력이 한꺼번에 도착합니다. 정상인데 차단됩니다.
    ///
    ///    그래서 받는 쪽을 조이지 않습니다.
    ///    쓰는 쪽이 자기 속도로 꺼내 씁니다.
    ///    → GameLoop.StepPlayers()
    /// ═══════════════════════════════════════════════
    /// </summary>
    public class UdpServer
    {
        public const int UDP_PORT = 7778;

        private Socket _socket = null!;

        /// <summary>수신 루프가 넣고, 틱 루프가 꺼내 갑니다</summary>
        public readonly ConcurrentQueue<InputCommand> Inputs = new ConcurrentQueue<InputCommand>();

        public Task StartAsync()
        {
            _socket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp);

            _socket.Bind(new IPEndPoint(IPAddress.Any, UDP_PORT));

            // 클라이언트가 꺼졌을 때 Windows 가 돌려주는 ICMP 를 무시합니다
            try
            {
                _socket.IOControl((IOControlCode)(-1744830452),
                                  new byte[] { 0, 0, 0, 0 }, null);
            }
            catch { }

            Console.WriteLine($"[UDP] 포트 {UDP_PORT}번에서 조작을 기다립니다");

            _ = ReceiveLoopAsync();
            return Task.CompletedTask;
        }

        // ═══════════════════════════════════════
        // 수신 — 큐에 넣기만 합니다
        // ═══════════════════════════════════════
        private async Task ReceiveLoopAsync()
        {
            byte[] buffer = new byte[1024];
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
                try
                {
                    var r = await _socket.ReceiveFromAsync(
                        new ArraySegment<byte>(buffer), SocketFlags.None, from);

                    string msg = Encoding.UTF8.GetString(buffer, 0, r.ReceivedBytes);

                    Enqueue(msg, r.RemoteEndPoint);
                }
                catch (SocketException se)
                    when (se.SocketErrorCode == SocketError.ConnectionReset)
                {
                    // 클라이언트 하나가 꺼진 것일 뿐입니다
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UDP 수신 에러] {ex.Message}");
                    await Task.Delay(100);
                }
            }
        }

        /// <summary>MOVE|플레이어ID|시퀀스|DX|DY</summary>
        private void Enqueue(string msg, EndPoint from)
        {
            string[] parts = msg.Split('|');
            if (parts.Length < 5) return;
            if (parts[0] != UdpPacket.MOVE) return;

            if (!int.TryParse(parts[1], out int playerId)) return;
            if (!int.TryParse(parts[2], out int seq))      return;
            if (!int.TryParse(parts[3], out int dx))       return;
            if (!int.TryParse(parts[4], out int dy))       return;

            // ═══════════════════════════════════════════════
            //  TODO 1 — 받은 조작을 큐에 넣으세요   ★
            //
            //    Inputs 큐에 InputCommand 를 하나 넣으면 됩니다.
            //    채울 것은 PlayerId, Seq, Dx, Dy, From 다섯 개입니다.
            //
            //    ★ 여기서는 아무것도 판단하지 않습니다.
            //      "이 사람 맞나" "너무 멀리 가는 거 아닌가" 같은 판단은
            //      전부 GameLoop 이 합니다.
            //
            //      받는 일과 판단하는 일을 섞으면,
            //      여러 패킷이 동시에 들어올 때 꼬입니다.
            //
            //    From 을 빠뜨리기 쉽습니다. 이게 없으면
            //    나중에 "정말 그 사람이 보냈나" 를 확인할 수 없습니다.
            // ═══════════════════════════════════════════════

        }

        // ═══════════════════════════════════════
        // 송신
        // ═══════════════════════════════════════
        public async Task SendToAsync(EndPoint to, string msg)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(msg);
                await _socket.SendToAsync(new ArraySegment<byte>(data), SocketFlags.None, to);
            }
            catch
            {
                // UDP 는 실패해도 넘어갑니다. 다음 틱에 또 보냅니다.
            }
        }
    }
}
