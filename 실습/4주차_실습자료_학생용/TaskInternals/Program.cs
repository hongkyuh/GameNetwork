using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace TaskInternals
{
    /// <summary>
    /// ═══════════════════════════════════════════════════
    /// 예제 B — Task가 도대체 뭔가
    ///
    /// async/await는 문법 설탕(syntactic sugar)입니다.
    /// 컴파일러가 이걸 Task와 상태 머신으로 바꿔줍니다.
    ///
    /// 이 예제에서는 await 없이 Task를 직접 다뤄보면서
    /// 안에서 무슨 일이 일어나는지 확인합니다.
    /// ═══════════════════════════════════════════════════
    /// </summary>
    class Program
    {
        static async Task Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            PrintTitle();

            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("─────────────────────────────────────────");
                Console.WriteLine(" 1. Task는 '약속'이다");
                Console.WriteLine(" 2. Task 상태 변화 관찰하기");
                Console.WriteLine(" 3. await 없이 짜보기 (ContinueWith)");
                Console.WriteLine(" 4. await는 이걸 예쁘게 써준 것");
                Console.WriteLine(" 5. Task.Run vs async 메서드");
                Console.WriteLine(" 6. 던져놓기 패턴 (_ = )");
                Console.WriteLine(" 7. await 빼먹으면 생기는 일");
                Console.WriteLine(" 8. 예외는 어디로 가나");
                Console.WriteLine(" 0. 종료");
                Console.WriteLine("─────────────────────────────────────────");
                Console.Write(" 선택 > ");

                string? input = Console.ReadLine();
                Console.WriteLine();

                switch (input)
                {
                    case "1": await PromiseDemo();        break;
                    case "2": await StatusDemo();         break;
                    case "3": ContinueWithDemo();         break;
                    case "4": await CompareDemo();        break;
                    case "5": await TaskRunDemo();        break;
                    case "6": await FireAndForgetDemo();  break;
                    case "7": await MissingAwaitDemo();   break;
                    case "8": await ExceptionDemo();      break;
                    case "0": return;
                    default:  Console.WriteLine("1~8 중에 골라주세요."); break;
                }
            }
        }

        // ═══════════════════════════════════════
        // 1. Task는 '약속'이다
        // ═══════════════════════════════════════
        static async Task PromiseDemo()
        {
            Console.WriteLine("[Task는 약속이다]");
            Console.WriteLine();

            Console.WriteLine("  세탁소에 옷을 맡기면 '접수증'을 받죠?");
            Console.WriteLine("  접수증은 옷이 아니지만, 나중에 옷으로 바꿀 수 있습니다.");
            Console.WriteLine("  Task가 그 접수증입니다.");
            Console.WriteLine();

            // Task를 만들면 '접수증'을 받는 것
            Task<string> laundry = WashClothesAsync();

            Console.WriteLine("  접수증을 받았습니다. 아직 옷은 없어요.");
            Console.WriteLine($"  접수증 상태 : {laundry.Status}");
            Console.WriteLine();
            Console.WriteLine("  그동안 다른 일을 할 수 있습니다.");
            Console.WriteLine("  (장보기, 커피 마시기...)");
            Console.WriteLine();

            // 이제 결과가 필요하면 await
            string result = await laundry;

            Console.WriteLine($"  옷을 받았습니다 : {result}");
            Console.WriteLine($"  접수증 상태 : {laundry.Status}");
        }

        static async Task<string> WashClothesAsync()
        {
            await Task.Delay(2000);
            return "깨끗한 셔츠";
        }

        // ═══════════════════════════════════════
        // 2. Task 상태 변화 관찰
        // ═══════════════════════════════════════
        static async Task StatusDemo()
        {
            Console.WriteLine("[Task 상태 변화]");
            Console.WriteLine();

            Task<int> task = Task.Run(() =>
            {
                Thread.Sleep(2000);
                return 42;
            });

            // 상태를 계속 찍어보기
            for (int i = 0; i < 5; i++)
            {
                Console.WriteLine($"  {i * 0.5:F1}초 : {task.Status}");
                await Task.Delay(500);
            }

            int result = await task;

            Console.WriteLine($"  완료   : {task.Status}");
            Console.WriteLine($"  결과   : {result}");
            Console.WriteLine();
            Console.WriteLine("  상태가 이렇게 흘러갑니다:");
            Console.WriteLine("    WaitingToRun → Running → RanToCompletion");
            Console.WriteLine();
            Console.WriteLine("  Task는 '지금 어느 단계인지'를 스스로 알고 있습니다.");
        }

        // ═══════════════════════════════════════
        // 3. await 없이 짜보기
        // ═══════════════════════════════════════
        static void ContinueWithDemo()
        {
            Console.WriteLine("[await 없이 Task만으로]");
            Console.WriteLine();
            Console.WriteLine("  async/await가 없던 시절에는 이렇게 짰습니다.");
            Console.WriteLine();

            Task.Run(() =>
            {
                Console.WriteLine("  1단계 : 데이터 읽기");
                Thread.Sleep(500);
                return "원본데이터";
            })
            .ContinueWith(t =>
            {
                Console.WriteLine($"  2단계 : {t.Result} 가공하기");
                Thread.Sleep(500);
                return t.Result + " → 가공됨";
            })
            .ContinueWith(t =>
            {
                Console.WriteLine($"  3단계 : {t.Result} 저장하기");
                Thread.Sleep(500);
                Console.WriteLine("  완료");
                Console.WriteLine();
                Console.WriteLine("  읽기 힘들죠? 이게 '콜백 지옥'입니다.");
                Console.WriteLine("  그래서 async/await가 나왔습니다.");
            })
            .Wait();
        }

        // ═══════════════════════════════════════
        // 4. 같은 일을 await로
        // ═══════════════════════════════════════
        static async Task CompareDemo()
        {
            Console.WriteLine("[같은 일을 await로 써보면]");
            Console.WriteLine();

            string data = await ReadAsync();
            string processed = await ProcessAsync(data);
            await SaveAsync(processed);

            Console.WriteLine();
            Console.WriteLine("  코드가 위에서 아래로 쭉 읽히죠?");
            Console.WriteLine("  동기 코드처럼 보이지만 실제로는 비동기입니다.");
            Console.WriteLine();
            Console.WriteLine("  컴파일러가 이 코드를 3번 예제처럼 바꿔줍니다.");
            Console.WriteLine("  await 하나당 ContinueWith 하나라고 보면 됩니다.");
        }

        static async Task<string> ReadAsync()
        {
            Console.WriteLine("  1단계 : 데이터 읽기");
            await Task.Delay(500);
            return "원본데이터";
        }

        static async Task<string> ProcessAsync(string data)
        {
            Console.WriteLine($"  2단계 : {data} 가공하기");
            await Task.Delay(500);
            return data + " → 가공됨";
        }

        static async Task SaveAsync(string data)
        {
            Console.WriteLine($"  3단계 : {data} 저장하기");
            await Task.Delay(500);
            Console.WriteLine("  완료");
        }

        // ═══════════════════════════════════════
        // 5. Task.Run vs async 메서드
        // ═══════════════════════════════════════
        static async Task TaskRunDemo()
        {
            Console.WriteLine("[Task.Run 과 async 메서드의 차이]");
            Console.WriteLine();

            Console.WriteLine($"  메인 쓰레드 : #{Thread.CurrentThread.ManagedThreadId}");
            Console.WriteLine();

            // Task.Run — 쓰레드풀에서 새 쓰레드를 빌려 실행
            Console.WriteLine("  Task.Run 으로 실행");
            await Task.Run(() =>
            {
                Console.WriteLine($"    안쪽 쓰레드 : #{Thread.CurrentThread.ManagedThreadId}");
                Thread.Sleep(500);   // CPU 작업 흉내
            });

            Console.WriteLine();

            // async 메서드 — 쓰레드를 새로 만들지 않음
            Console.WriteLine("  async 메서드로 실행");
            await IoWorkAsync();

            Console.WriteLine();
            Console.WriteLine("  Task.Run    : CPU 작업용. 새 쓰레드를 빌려옴");
            Console.WriteLine("  async 메서드 : I/O 대기용. 쓰레드를 안 씀");
            Console.WriteLine();
            Console.WriteLine("  네트워크 대기는 CPU를 안 쓰니까");
            Console.WriteLine("  Task.Run 으로 감싸면 오히려 손해입니다.");
        }

        static async Task IoWorkAsync()
        {
            Console.WriteLine($"    시작 쓰레드 : #{Thread.CurrentThread.ManagedThreadId}");
            await Task.Delay(500);      // I/O 대기 흉내
            Console.WriteLine($"    복귀 쓰레드 : #{Thread.CurrentThread.ManagedThreadId}");
        }

        // ═══════════════════════════════════════
        // 6. 던져놓기 패턴
        // ═══════════════════════════════════════
        static async Task FireAndForgetDemo()
        {
            Console.WriteLine("[던져놓기 패턴 —  _ = 의 의미]");
            Console.WriteLine();

            Console.WriteLine("  손님을 받는 카페를 생각해봅시다.");
            Console.WriteLine();

            // 잘못된 방식 — 한 명씩 끝까지 처리
            Console.WriteLine("  [await 를 붙인 경우]");
            Stopwatch sw1 = Stopwatch.StartNew();
            for (int i = 1; i <= 3; i++)
            {
                await ServeCustomerAsync(i);   // 한 명 끝나야 다음 손님
            }
            sw1.Stop();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"    → {sw1.Elapsed.TotalSeconds:F2}초");
            Console.ResetColor();

            Console.WriteLine();

            // 올바른 방식 — 주문 받고 바로 다음 손님
            Console.WriteLine("  [ _ = 로 던져놓은 경우]");
            Stopwatch sw2 = Stopwatch.StartNew();
            Task[] jobs = new Task[3];
            for (int i = 1; i <= 3; i++)
            {
                jobs[i - 1] = ServeCustomerAsync(i);   // 시작만 시키고 넘어감
            }
            await Task.WhenAll(jobs);
            sw2.Stop();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"    → {sw2.Elapsed.TotalSeconds:F2}초");
            Console.ResetColor();

            Console.WriteLine();
            Console.WriteLine("  우리 서버 코드에서도 똑같이 합니다:");
            Console.WriteLine();
            Console.WriteLine("    Socket client = await listenSocket.AcceptAsync();");
            Console.WriteLine("    _ = session.RunAsync();   // ← 기다리지 않음");
            Console.WriteLine();
            Console.WriteLine("  여기에 await를 붙이면 한 명만 접속되는 서버가 됩니다.");
        }

        static async Task ServeCustomerAsync(int n)
        {
            Console.WriteLine($"    손님 {n} 주문 접수");
            await Task.Delay(1000);
            Console.WriteLine($"    손님 {n} 음료 전달");
        }

        // ═══════════════════════════════════════
        // 7. await 빼먹으면
        // ═══════════════════════════════════════
        static async Task MissingAwaitDemo()
        {
            Console.WriteLine("[await 를 빼먹으면 생기는 일]");
            Console.WriteLine();

            Console.WriteLine("  메서드를 호출했는데 결과를 안 기다리면?");
            Console.WriteLine();

            Console.WriteLine("  [await 있음]");
            await SlowWorkAsync("A");
            Console.WriteLine("  → 작업이 끝난 뒤에 이 줄이 나옵니다");

            Console.WriteLine();

            Console.WriteLine("  [await 없음]");
            _ = SlowWorkAsync("B");
            Console.WriteLine("  → 작업이 끝나기 전에 이 줄이 먼저 나옵니다");

            Console.WriteLine();
            Console.WriteLine("  2초 기다려볼게요...");
            await Task.Delay(2500);

            Console.WriteLine();
            Console.WriteLine("  의도한 거면 괜찮습니다. (서버 세션처럼)");
            Console.WriteLine("  실수로 빠뜨린 거면 버그가 됩니다.");
            Console.WriteLine();
            Console.WriteLine("  특히 이런 루프는 위험합니다:");
            Console.WriteLine();
            Console.WriteLine("    while (true)");
            Console.WriteLine("    {");
            Console.WriteLine("        listenSocket.AcceptAsync();   // await 없음");
            Console.WriteLine("    }");
            Console.WriteLine();
            Console.WriteLine("  루프에 브레이크가 없으니 초당 수백만 번 돕니다.");
            Console.WriteLine("  몇 초 만에 메모리가 터집니다.");
        }

        static async Task SlowWorkAsync(string name)
        {
            await Task.Delay(2000);
            Console.WriteLine($"    작업 {name} 완료");
        }

        // ═══════════════════════════════════════
        // 8. 예외는 어디로 가나
        // ═══════════════════════════════════════
        static async Task ExceptionDemo()
        {
            Console.WriteLine("[비동기에서 예외 처리]");
            Console.WriteLine();

            // await 하면 예외를 잡을 수 있음
            Console.WriteLine("  [await 있음 — 예외가 잡힘]");
            try
            {
                await ThrowAsync();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"    잡았습니다 : {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine();

            // await 안 하면 예외가 조용히 사라짐
            Console.WriteLine("  [await 없음 — 예외가 사라짐]");
            try
            {
                _ = ThrowAsync();        // 예외가 Task 안에 갇힘
                await Task.Delay(500);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("    아무 일도 안 일어났습니다");
                Console.ResetColor();
            }
            catch (Exception)
            {
                Console.WriteLine("    (여기로 안 옵니다)");
            }

            Console.WriteLine();
            Console.WriteLine("  던져놓기를 할 때는 안에서 직접 try-catch 해야 합니다.");
            Console.WriteLine("  우리 서버의 ClientSession.RunAsync 를 보세요.");
            Console.WriteLine("  메서드 전체가 try-catch 로 감싸져 있습니다.");
        }

        static async Task ThrowAsync()
        {
            await Task.Delay(100);
            throw new InvalidOperationException("일부러 낸 에러입니다");
        }

        // ═══════════════════════════════════════
        static void PrintTitle()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔══════════════════════════════════════╗");
            Console.WriteLine("║   예제 B — Task 안을 들여다보기      ║");
            Console.WriteLine("║   동양게임즈 서버개발팀 교육자료      ║");
            Console.WriteLine("╚══════════════════════════════════════╝");
            Console.ResetColor();
        }
    }
}
