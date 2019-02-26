using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DPProblem
{
    public static class Program
    {
        private const int philosophersAmount = 4;

        // use integer to be able to use Interlocked:
        private static int[] forks;

        private static int[] eatenFood = new int[philosophersAmount];

        private static int[] lastEatenFood = new int[philosophersAmount];

        private static int[] thoughts = new int[philosophersAmount];

        private static Timer threadingTimer;

        private static DateTime startTime;

        static Program()
        {
            const int dueTime = 3000;
            const int checkPeriod = 2000;
            threadingTimer = new Timer(Observe, null, dueTime, checkPeriod);

            forks = Enumerable.Repeat(1, philosophersAmount).ToArray();
        }

        public static void TakeFork(int fork_inx)
        {
            SpinWait.SpinUntil(()=>forks[fork_inx] == 1);
            forks[fork_inx] = 0;
        }

        public static void PutFork(int fork_inx)
        {
            forks[fork_inx] = 1;
        }

        private static void Think(int philosopher_inx, ref int number)
        {
            const int modulus = 0b1000000 - 1;
            int copy = number;
            for (int delimiter = 2; delimiter * delimiter <= number; delimiter++)
                while (copy % delimiter == 0)
                    copy /= delimiter;
            if (number % modulus == 0)
                thoughts[philosopher_inx]++;
            number = (number + 1) % modulus;
        }

        public static void DoPhilosopherDeadlock(int i)
        {
            int number = 1;
            Console.WriteLine($"Philosopher {i + 1} starting");
            while (true)
            {
                Think(i, ref number);
                TakeFork(i);
                TakeFork((i + 1) % philosophersAmount);
                eatenFood[i] = (eatenFood[i] + 1) % (int.MaxValue - 1);
                PutFork(i);
                PutFork((i + 1) % philosophersAmount);
            }
        }

        public static void DoPhilosopherSimplestLock(int i)
        {
            /*
            This gives thoughts speed: 54609.62 t/sec.
            Only one philosopher eats at a time. Additional time to go into kernel space:
            */
            int number = 1;
            Console.WriteLine($"Philosopher {i + 1} starting");
            while (true)
            {
                Think(i, ref number);
                lock(forks)
                {
                    TakeFork(i);
                    TakeFork((i + 1) % philosophersAmount);
                    eatenFood[i] = (eatenFood[i] + 1) % (int.MaxValue - 1);
                    PutFork(i);
                    PutFork((i + 1) % philosophersAmount);
                }
            }
        }

        public static void DoPhilosopherInterlocked(int i)
        {
            /*
            This gives thoughts speed:  101238.20 t/sec
            */
            void TakeForks()
            {
                // This takes two forks if available or none (atomical) and does not go into kernel mode
                SpinWait.SpinUntil(() =>
                {
                    // try to take left and right fork if they are awailable:
                    int left = Interlocked.CompareExchange(ref forks[i], 0, 1);
                    int right = Interlocked.CompareExchange(ref forks[(i + 1)% philosophersAmount], 0, 1);
                    bool wereBothFree = left == 1 && right == 1;
                    if (wereBothFree)  
                        return true;
                    // else put the taken fork back if any:
                    Interlocked.CompareExchange(ref forks[i], 1, 0);
                    Interlocked.CompareExchange(ref forks[(i + 1)% philosophersAmount], 1, 0);
                    return false;
                });
                // at this point this philosopher has taken two forks
            }
            void PutForks() 
            {
                forks[i] = 1;
                forks[(i+1)%philosophersAmount] = 1;
            }

            int number = 1;
            Console.WriteLine($"Philosopher {i + 1} starting");
            while (true)
            {
                Think(i, ref number);
                TakeForks();
                eatenFood[i] = (eatenFood[i] + 1) % (int.MaxValue - 1);
                PutForks();
            }
        }

        private static void Observe(object state)
        {
            for (int i = 0; i < philosophersAmount; i++)
            {
                if (lastEatenFood[i] == eatenFood[i])
                    Console.WriteLine($"Philosopher {i + 1} starvation: last {lastEatenFood[i]}, now {eatenFood[i]}.");
                lastEatenFood[i] = eatenFood[i];
            }
        }

        public static void Main(string[] args)
        {
            // Observer:
            Console.WriteLine("Starting...");
            startTime = DateTime.Now;
            var philosophers = new Task[philosophersAmount];
            for (int i = 0; i < philosophersAmount; i++)
            {
                int icopy = i;
                // philosophers[i] = Task.Run(() => DoPhilosopherDeadlock(icopy));
                // philosophers[i] = Task.Run(() => DoPhilosopherSimplestLock(icopy));
                philosophers[i] = Task.Run(() => DoPhilosopherInterlocked(icopy));
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();
            for (int i = 0; i < philosophersAmount; i++) {
                try { philosophers[0].Dispose(); } catch { };
            }
            TimeSpan spentTime = DateTime.Now - startTime;
            Console.WriteLine($"Time: {spentTime:g}");
            Console.WriteLine($"Thoughts speed: {thoughts.Sum() / (spentTime.TotalSeconds):.00} t/sec");
            for (int i = 0; i < philosophersAmount; i++)
            {
                Console.WriteLine($"P{i+1} {eatenFood[i]} eaten, {thoughts[i]} thoughts.");
            }

            Console.WriteLine("Exit");
        }
    }
}
