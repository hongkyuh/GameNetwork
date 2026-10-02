using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BattleArena
{
    /// <summary>
    /// 서버 접속 담당
    ///
    /// ═══════════════════════════════════════════════
    /// [3주차]
    /// 서버에 접속해서 닉네임을 보내고,
    /// 환영 메시지와 입장/퇴장 알림을 받습니다.
    ///
    /// 패킷 구조는 서버와 동일합니다.
    ///   [ 길이(4바이트) ][ JSON 데이터 ]
    /// ═══════════════════════════════════════════════
    /// </summary>
    public class NetworkClient
    {
        // ── 연결 정보 ──
        private const string SERVER_IP   = "127.0.0.1";
        private const int    SERVER_PORT = 7777;

        private TcpClient _client;
        private NetworkStream _stream;
        private bool _isConnected;

        public bool IsConnected => _isConnected;

        // 수신한 메시지를 메인 쓰레드로 전달하는 큐
        // (Unity는 다른 쓰레드에서 UI를 건드릴 수 없기 때문)
        private readonly ConcurrentQueue<string> _receiveQueue = new ConcurrentQueue<string>();

        // ═══════════════════════════════════════
        // 접속
        // ═══════════════════════════════════════
        public async Task<bool> ConnectAsync(string nickname)
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(SERVER_IP, SERVER_PORT);
                _stream = _client.GetStream();
                _isConnected = true;

                Debug.Log($"[네트워크] 서버 접속 성공 ({SERVER_IP}:{SERVER_PORT})");

                // 수신 루프 시작 (백그라운드)
                _ = ReceiveLoopAsync();

                // 로그인 패킷 전송
                await SendAsync(new { Type = 1, Nickname = nickname });

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[네트워크] 접속 실패: {ex.Message}");
                _isConnected = false;
                return false;
            }
        }

        // ═══════════════════════════════════════
        // 전송
        // ═══════════════════════════════════════
        public async Task SendAsync(object packet)
        {
            if (!_isConnected || _stream == null) return;

            try
            {
                string json = JsonUtilityWrapper.ToJson(packet);

                // 1. 문자열 → 바이트
                byte[] body = Encoding.UTF8.GetBytes(json);

                // 2. 길이를 4바이트로
                byte[] header = BitConverter.GetBytes(body.Length);

                // 3. [길이][데이터] 합치기
                byte[] full = new byte[header.Length + body.Length];
                Buffer.BlockCopy(header, 0, full, 0, header.Length);
                Buffer.BlockCopy(body, 0, full, header.Length, body.Length);

                // 4. 전송
                await _stream.WriteAsync(full, 0, full.Length);

                Debug.Log($"[송신] {json}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[네트워크] 전송 실패: {ex.Message}");
                Disconnect();
            }
        }

        // ═══════════════════════════════════════
        // 수신 루프
        // ═══════════════════════════════════════
        private async Task ReceiveLoopAsync()
        {
            try
            {
                while (_isConnected)
                {
                    // 1. 길이 헤더 4바이트 읽기
                    byte[] header = await ReadExactAsync(4);
                    if (header == null) break;

                    int bodyLength = BitConverter.ToInt32(header, 0);
                    if (bodyLength <= 0 || bodyLength > 65535) break;

                    // 2. 본문 읽기
                    byte[] body = await ReadExactAsync(bodyLength);
                    if (body == null) break;

                    // 3. 문자열로 변환해서 큐에 넣기
                    string json = Encoding.UTF8.GetString(body);
                    _receiveQueue.Enqueue(json);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[네트워크] 수신 종료: {ex.Message}");
            }
            finally
            {
                Disconnect();
            }
        }

        /// <summary>정확히 count 바이트를 읽을 때까지 반복</summary>
        private async Task<byte[]> ReadExactAsync(int count)
        {
            byte[] buffer = new byte[count];
            int received = 0;

            while (received < count)
            {
                int n = await _stream.ReadAsync(buffer, received, count - received);
                if (n == 0) return null;   // 연결 끊김
                received += n;
            }

            return buffer;
        }

        // ═══════════════════════════════════════
        // 메인 쓰레드에서 호출 — 받은 메시지 꺼내기
        // ═══════════════════════════════════════
        public bool TryGetMessage(out string json)
        {
            return _receiveQueue.TryDequeue(out json);
        }

        // ═══════════════════════════════════════
        // 연결 종료
        // ═══════════════════════════════════════
        public void Disconnect()
        {
            if (!_isConnected) return;
            _isConnected = false;

            try
            {
                _stream?.Close();
                _client?.Close();
            }
            catch { }

            Debug.Log("[네트워크] 연결 종료");
        }
    }

    /// <summary>
    /// 익명 객체를 JSON으로 만들기 위한 도우미
    /// (Unity의 JsonUtility는 익명 객체를 지원하지 않아서 직접 작성)
    /// </summary>
    public static class JsonUtilityWrapper
    {
        public static string ToJson(object obj)
        {
            if (obj == null) return "{}";

            var type = obj.GetType();
            var props = type.GetProperties();
            var sb = new StringBuilder();

            sb.Append("{");
            bool first = true;

            foreach (var p in props)
            {
                if (!first) sb.Append(",");
                first = false;

                object value = p.GetValue(obj);
                sb.Append("\"").Append(p.Name).Append("\":");

                if (value is string s)
                    sb.Append("\"").Append(EscapeJson(s)).Append("\"");
                else if (value is bool b)
                    sb.Append(b ? "true" : "false");
                else if (value == null)
                    sb.Append("null");
                else
                    sb.Append(value.ToString());
            }

            sb.Append("}");
            return sb.ToString();
        }

        private static string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>JSON에서 특정 필드 값 꺼내기 (간단 파서)</summary>
        public static string GetString(string json, string key)
        {
            string pattern = "\"" + key + "\"";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return "";

            idx = json.IndexOf(':', idx);
            if (idx < 0) return "";
            idx++;

            // 공백 건너뛰기
            while (idx < json.Length && json[idx] == ' ') idx++;

            if (idx >= json.Length) return "";

            // 문자열이면 따옴표 사이
            if (json[idx] == '"')
            {
                idx++;
                int end = idx;
                while (end < json.Length && json[end] != '"')
                {
                    if (json[end] == '\\') end++;
                    end++;
                }
                return json.Substring(idx, end - idx);
            }
            else
            {
                // 숫자면 쉼표나 중괄호 전까지
                int end = idx;
                while (end < json.Length && json[end] != ',' && json[end] != '}') end++;
                return json.Substring(idx, end - idx).Trim();
            }
        }

        public static int GetInt(string json, string key)
        {
            string v = GetString(json, key);
            return int.TryParse(v, out int result) ? result : 0;
        }
    }
}
