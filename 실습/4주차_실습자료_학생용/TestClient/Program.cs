using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestClient
{
    /// <summary>
    /// ═══════════════════════════════════════════════════
    /// 접속 테스트 클라이언트
    ///
    /// Unity를 여러 개 띄우기는 번거로우니
    /// 이 프로그램으로 여러 명이 동시에 접속하는 걸 흉내냅니다.    
    ///
    /// 사용법:
    ///   1. 서버를 먼저 켭니다
    ///   2. 이 프로그램을 실행합니다
    ///   3. 접속시킬 인원 수를 입력합니다
    /// ═══════════════════════════════════════════════════
    /// </summary>
    class Program
    {
        private const string SERVER_IP   = "127.0.0.1";
        private const int    SERVER_PORT = 7777;

        static async Task Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            PrintTitle();

            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("─────────────────────────────────────");
                Console.WriteLine(" 1. 1명 접속");
                Console.WriteLine(" 2. 2명 접속  ← 동기 서버는 여기서 막힘");
                Console.WriteLine(" 3. 10명 접속");
                Console.WriteLine(" 4. 100명 접속");
                Console.WriteLine(" 5. 직접 입력");
                Console.WriteLine(" 0. 종료");
                Console.WriteLine("─────────────────────────────────────");
                Console.Write(" 선택 > ");

                string? input = Console.ReadLine();
                Console.WriteLine();

                int count = input switch
                {
                    "1" => 1,
                    "2" => 2,
                    "3" => 10,
                    "4" => 100,
                    "5" => AskCount(),
                    "0" => -1,
                    _   => 0
                };

                if (count == -1) return;
                if (count == 0)
                {
                    Console.WriteLine("다시 선택해주세요.");
                    continue;
                }

                await RunTest(count);
            }
        }

        static int AskCount()
        {
            Console.Write(" 몇 명? > ");
            return int.TryParse(Console.ReadLine(), out int n) ? n : 0;
        }

        // ═══════════════════════════════════════
        // 접속 테스트 실행
        // ═══════════════════════════════════════
        static async Task RunTest(int count)
        {
            Console.WriteLine($"[테스트] {count}명 동시 접속 시도");
            Console.WriteLine();

            Stopwatch sw = Stopwatch.StartNew();

            int success = 0;
            int failed  = 0;
            object lockObj = new object();

            List<Task> tasks = new List<Task>();
            List<TcpClient> clients = new List<TcpClient>();

            for (int i = 1; i <= count; i++)
            {
                int id = i;
                tasks.Add(Task.Run(async () =>
                {
                    TcpClient? client = await ConnectOne(id);

                    lock (lockObj)
                    {
                        if (client != null)
                        {
                            success++;
                            clients.Add(client);
                        }
                        else
                        {
                            failed++;
                        }
                    }
                }));
            }

            // 최대 10초 대기
            Task all = Task.WhenAll(tasks);
            Task timeout = Task.Delay(10000);

            Task done = await Task.WhenAny(all, timeout);

            sw.Stop();

            Console.WriteLine();
            Console.WriteLine("─────────────────────────────────────");

            if (done == timeout)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(" 10초가 지나도 끝나지 않았습니다.");
                Console.WriteLine(" 서버가 처리를 못 하고 있습니다.");
                Console.ResetColor();
            }

            Console.WriteLine($" 성공 : {success}명");
            if (failed > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($" 실패 : {failed}명");
                Console.ResetColor();
            }
            Console.WriteLine($" 시간 : {sw.Elapsed.TotalSeconds:F2}초");
            Console.WriteLine("─────────────────────────────────────");

            // 판정
            Console.WriteLine();
            if (success == count)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" 전원 접속 성공");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($" {count - success}명이 접속하지 못했습니다.");
                Console.WriteLine(" 서버가 동기 방식이면 이렇게 됩니다.");
                Console.ResetColor();
            }

            Console.WriteLine();
            Console.Write("접속을 유지할까요? (y/n) > ");
            string? keep = Console.ReadLine();

            if (keep?.ToLower() != "y")
            {
                foreach (var c in clients)
                {
                    try { c.Close(); } catch { }
                }
                Console.WriteLine("모두 연결을 끊었습니다.");
            }
            else
            {
                Console.WriteLine("접속을 유지합니다. 서버 콘솔을 확인해보세요.");
                Console.WriteLine("(이 창을 닫으면 모두 끊깁니다)");
            }
        }

        // ═══════════════════════════════════════
        // 한 명 접속
        // ═══════════════════════════════════════
        static async Task<TcpClient?> ConnectOne(int id)
        {
            try
            {
                TcpClient client = new TcpClient();

                // 접속 시도 (3초 타임아웃)
                Task connect = client.ConnectAsync(SERVER_IP, SERVER_PORT);
                Task timeout = Task.Delay(3000);

                if (await Task.WhenAny(connect, timeout) == timeout)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  [{id:D3}] 접속 시간 초과");
                    Console.ResetColor();
                    client.Close();
                    return null;
                }

                NetworkStream stream = client.GetStream();

                // 로그인 패킷 전송
                string json = $"{{\"Type\":1,\"Nickname\":\"Test{id:D3}\"}}";
                byte[] body   = Encoding.UTF8.GetBytes(json);
                byte[] header = BitConverter.GetBytes(body.Length);

                await stream.WriteAsync(header, 0, header.Length);
                await stream.WriteAsync(body, 0, body.Length);

                // 응답 대기 (3초 타임아웃)
                byte[] respHeader = new byte[4];
                Task<int> read = stream.ReadAsync(respHeader, 0, 4);
                Task readTimeout = Task.Delay(3000);

                if (await Task.WhenAny(read, readTimeout) == readTimeout)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  [{id:D3}] 응답 없음 (서버가 멈춤)");
                    Console.ResetColor();
                    client.Close();
                    return null;
                }

                int n = await read;
                if (n == 0)
                {
                    Console.WriteLine($"  [{id:D3}] 연결 끊김");
                    client.Close();
                    return null;
                }

                int len = BitConverter.ToInt32(respHeader, 0);
                byte[] respBody = new byte[len];
                int received = 0;
                while (received < len)
                {
                    int r = await stream.ReadAsync(respBody, received, len - received);
                    if (r == 0) break;
                    received += r;
                }

                string resp = Encoding.UTF8.GetString(respBody);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  [{id:D3}] 접속 성공");
                Console.ResetColor();

                return client;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [{id:D3}] 실패 : {ex.Message}");
                Console.ResetColor();
                return null;
            }
        }

        // ═══════════════════════════════════════
        static void PrintTitle()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔══════════════════════════════════════╗");
            Console.WriteLine("║   접속 테스트 클라이언트              ║");
            Console.WriteLine("║   동양게임즈 QA팀                    ║");
            Console.WriteLine("╚══════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine(" 서버를 먼저 실행한 뒤 사용하세요.");
            Console.WriteLine($" 대상 : {SERVER_IP}:{SERVER_PORT}");
        }
    }
}
