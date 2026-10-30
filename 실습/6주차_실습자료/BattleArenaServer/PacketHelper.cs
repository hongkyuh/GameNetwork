using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BattleArenaServer
{
    /// <summary>
    /// 패킷 송수신 도우미
    ///
    /// TCP로 메시지를 주고받을 때 "어디까지가 한 메시지인지"를
    /// 알려주기 위해 앞에 길이를 붙여서 보냅니다.
    ///
    /// ┌──────────────┬─────────────────────┐
    /// │ 길이 (4바이트) │  JSON 데이터          │
    /// └──────────────┴─────────────────────┘
    ///
    /// 예) {"Type":1,"Nickname":"홍길동"}  (30바이트)
    ///     → [30][{"Type":1,"Nickname":"홍길동"}]
    /// </summary>
    public static class PacketHelper
    {
        // ═══════════════════════════════════════
        // 보내기
        // ═══════════════════════════════════════

        /// <summary>객체를 JSON으로 변환해서 전송</summary>
        public static async Task SendAsync(Socket socket, object packet)
        {
            string json = JsonConvert.SerializeObject(packet);
            await SendAsync(socket, json);
        }

        /// <summary>JSON 문자열을 전송</summary>
        public static async Task SendAsync(Socket socket, string json)
        {
            // 1. 문자열을 바이트 배열로 변환 (UTF-8)
            byte[] body = Encoding.UTF8.GetBytes(json);

            // 2. 길이를 4바이트로 변환
            byte[] header = BitConverter.GetBytes(body.Length);

            // 3. [길이][데이터] 형태로 합치기
            byte[] packet = new byte[header.Length + body.Length];
            Array.Copy(header, 0, packet, 0, header.Length);
            Array.Copy(body, 0, packet, header.Length, body.Length);

            // 4. 전송
            await socket.SendAsync(packet, SocketFlags.None);
        }

        // ═══════════════════════════════════════
        // 받기
        // ═══════════════════════════════════════

        /// <summary>
        /// 패킷 하나를 받아서 JSON 문자열로 반환
        /// 연결이 끊기면 null 반환
        /// </summary>
        public static async Task<string?> ReceiveAsync(Socket socket)
        {
            // 1. 길이 헤더 4바이트 먼저 읽기
            byte[]? headerBytes = await ReceiveExactAsync(socket, 4);
            if (headerBytes == null) return null;

            int bodyLength = BitConverter.ToInt32(headerBytes, 0);

            // 비정상 길이 방어
            if (bodyLength <= 0 || bodyLength > 65535)
            {
                Console.WriteLine($"[경고] 비정상 패킷 길이: {bodyLength}");
                return null;
            }

            // 2. 그 길이만큼 본문 읽기
            byte[]? bodyBytes = await ReceiveExactAsync(socket, bodyLength);
            if (bodyBytes == null) return null;

            // 3. UTF-8 문자열로 변환
            return Encoding.UTF8.GetString(bodyBytes);
        }

        /// <summary>
        /// 정확히 count 바이트를 읽을 때까지 반복
        ///
        /// TCP는 한 번에 요청한 만큼 다 안 올 수 있습니다.
        /// 100바이트를 요청해도 30바이트만 올 수 있어요.
        /// 그래서 다 받을 때까지 반복합니다.
        /// </summary>
        private static async Task<byte[]?> ReceiveExactAsync(Socket socket, int count)
        {
            byte[] buffer = new byte[count];
            int received = 0;

            while (received < count)
            {
                int n = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer, received, count - received),
                    SocketFlags.None);

                // 0이 오면 상대가 연결을 끊은 것
                if (n == 0) return null;

                received += n;
            }

            return buffer;
        }
    }
}
