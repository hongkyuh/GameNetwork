using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncBasics
{
    /// <summary>
    /// ═══════════════════════════════════════════════════
    /// 예제 A — 동기와 비동기, 뭐가 다른가
    ///
    /// 아침 준비를 한다고 생각해봅시다.
    ///   커피 내리기  3초
    ///   토스트 굽기  3초
    ///   계란 삶기    3초
    ///
    /// 동기로 하면 9초, 비동기로 하면 3초입니다.
    /// 직접 실행해서 시간을 확인해보세요.
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
                Console.WriteLine("─────────────────────────────────────");
                Console.WriteLine(" 1. 동기 방식으로 아침 준비");
                Console.WriteLine(" 2. 비동기 방식으로 아침 준비");
                Console.WriteLine(" 3. 쓰레드 번호 확인하기");
                Console.WriteLine(" 4. 쓰레드를 막으면 어떻게 되나");
                Console.WriteLine(" 0. 종료");
                Console.WriteLine("─────────────────────────────────────");
                Console.Write(" 선택 > ");

                string? input = Console.ReadLine();
                Console.WriteLine();

                switch (input)
                {
                    case "1": RunSync();               break;
                    case "2": await RunAsync();        break;
                    case "3": await ShowThreadId();    break;
                    case "4": await BlockingDemo();    break;
                    case "0": return;
                    default:  Console.WriteLine("1~4 중에 골라주세요."); break;
                }
            }
        }

        // ═══════════════════════════════════════
        // 1. 동기 방식 — 하나씩 순서대로
        // ═══════════════════════════════════════
        static void RunSync()
        {
            Console.WriteLine("[동기 방식] 시작");
            Console.WriteLine();

            Stopwatch sw = Stopwatch.StartNew();

            MakeCoffee();   // 3초 걸림
            MakeToast();    // 3초 걸림
            BoilEgg();      // 3초 걸림

            sw.Stop();

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"총 걸린 시간 : {sw.Elapsed.TotalSeconds:F2}초");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("커피가 다 내려질 때까지 아무것도 못 했습니다.");
            Console.WriteLine("그 3초 동안 토스트를 구울 수도 있었는데요.");
        }

        static void MakeCoffee()
        {
            Console.WriteLine("  커피 내리는 중...");
            Thread.Sleep(3000);          // ← 쓰레드를 3초간 묶어둠
            Console.WriteLine("  커피 완성");
        }

        static void MakeToast()
        {
            Console.WriteLine("  토스트 굽는 중...");
            Thread.Sleep(3000);
            Console.WriteLine("  토스트 완성");
        }

        static void BoilEgg()
        {
            Console.WriteLine("  계란 삶는 중...");
            Thread.Sleep(3000);
            Console.WriteLine("  계란 완성");
        }

        // ═══════════════════════════════════════
        // 2. 비동기 방식 — 동시에 걸어두고 기다리기
        // ═══════════════════════════════════════
        static async Task RunAsync()
        {
            Console.WriteLine("[비동기 방식] 시작");
            Console.WriteLine();

            Stopwatch sw = Stopwatch.StartNew();

            // 세 가지를 동시에 시작 (아직 기다리지 않음)
            Task coffee = MakeCoffeeAsync();
            Task toast  = MakeToastAsync();
            Task egg    = BoilEggAsync();

            // 셋 다 끝날 때까지 기다림
            await Task.WhenAll(coffee, toast, egg);

            sw.Stop();

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"총 걸린 시간 : {sw.Elapsed.TotalSeconds:F2}초");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("세 가지를 동시에 진행했습니다.");
            Console.WriteLine("커피가 내려지는 동안 토스트도 굽고 계란도 삶았어요.");
        }

        static async Task MakeCoffeeAsync()
        {
            Console.WriteLine("  커피 내리는 중...");
            await Task.Delay(3000);      // ← 쓰레드를 반납하고 기다림
            Console.WriteLine("  커피 완성");
        }

        static async Task MakeToastAsync()
        {
            Console.WriteLine("  토스트 굽는 중...");
            await Task.Delay(3000);
            Console.WriteLine("  토스트 완성");
        }

        static async Task BoilEggAsync()
        {
            Console.WriteLine("  계란 삶는 중...");
            await Task.Delay(3000);
            Console.WriteLine("  계란 완성");
        }

        // ═══════════════════════════════════════
        // 3. 쓰레드 번호 확인
        //
        // await 앞뒤로 쓰레드가 바뀌는 걸 직접 봅니다.
        // ═══════════════════════════════════════
        static async Task ShowThreadId()
        {
            Console.WriteLine("[쓰레드 번호 확인]");
            Console.WriteLine();

            Console.WriteLine($"  await 전  : 쓰레드 #{Thread.CurrentThread.ManagedThreadId}");

            await Task.Delay(100);

            Console.WriteLine($"  await 후  : 쓰레드 #{Thread.CurrentThread.ManagedThreadId}");

            await Task.Delay(100);

            Console.WriteLine($"  또 await 후 : 쓰레드 #{Thread.CurrentThread.ManagedThreadId}");

            Console.WriteLine();
            Console.WriteLine("번호가 바뀌었죠?");
            Console.WriteLine("await를 만나면 쓰레드를 반납하고,");
            Console.WriteLine("결과가 오면 그때 남는 쓰레드를 다시 빌려옵니다.");
            Console.WriteLine();
            Console.WriteLine("같은 번호가 나올 수도 있습니다.");
            Console.WriteLine("마침 그 쓰레드가 놀고 있었으면 다시 쓰거든요.");
        }

        // ═══════════════════════════════════════
        // 4. 쓰레드를 막으면 어떻게 되나
        //
        // 비동기 메서드 안에서 Thread.Sleep을 쓰면
        // 비동기의 장점이 사라집니다.
        // ═══════════════════════════════════════
        static async Task BlockingDemo()
        {
            Console.WriteLine("[쓰레드를 막는 실수]");
            Console.WriteLine();

            Console.WriteLine("Task.Delay 를 쓴 경우");
            Stopwatch sw1 = Stopwatch.StartNew();
            await Task.WhenAll(
                GoodAsync(1), GoodAsync(2), GoodAsync(3));
            sw1.Stop();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  → {sw1.Elapsed.TotalSeconds:F2}초");
            Console.ResetColor();

            Console.WriteLine();
            Console.WriteLine("Thread.Sleep 을 쓴 경우 (잘못된 방식)");
            Stopwatch sw2 = Stopwatch.StartNew();
            await Task.WhenAll(
                BadAsync(1), BadAsync(2), BadAsync(3));
            sw2.Stop();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  → {sw2.Elapsed.TotalSeconds:F2}초");
            Console.ResetColor();

            Console.WriteLine();
            Console.WriteLine("async를 붙였다고 다 비동기가 아닙니다.");
            Console.WriteLine("안에서 쓰레드를 막으면 의미가 없어요.");
            Console.WriteLine("Thread.Sleep 대신 await Task.Delay 를 써야 합니다.");
        }

        static async Task GoodAsync(int n)
        {
            await Task.Delay(1000);      // 쓰레드 반납
            Console.WriteLine($"    작업 {n} 완료 (쓰레드 #{Thread.CurrentThread.ManagedThreadId})");
        }

        static async Task BadAsync(int n)
        {
            await Task.Yield();          // 비동기 흉내만 냄
            Thread.Sleep(1000);          // 쓰레드를 붙잡고 있음
            Console.WriteLine($"    작업 {n} 완료 (쓰레드 #{Thread.CurrentThread.ManagedThreadId})");
        }

        // ═══════════════════════════════════════
        static void PrintTitle()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔══════════════════════════════════════╗");
            Console.WriteLine("║   예제 A — 동기 vs 비동기            ║");
            Console.WriteLine("║   동양게임즈 서버개발팀 교육자료      ║");
            Console.WriteLine("╚══════════════════════════════════════╝");
            Console.ResetColor();
        }
    }
}
