using System;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Numerics;

namespace DPProblem
{
    public class DinningPhilosophers : IDisposable
    {
        private const int philosophersAmount = 2;

        private int[] forks = Enumerable.Repeat(1, philosophersAmount).ToArray();

        private static object lockObject = new object();

        private const int FirstBigInteger = 2;

        private long bigInteger = FirstBigInteger;

        private int[] eatenFood = new int[philosophersAmount];

        private int[] lastEatenFood = new int[philosophersAmount];

        private int[] thoughts = new int[philosophersAmount];

	    private Timer threadingTimer;

	    private Task[] philosophers;
	    private DateTime startTime;

		[Pure] private static int Left(int i) => i;
		[Pure] private static int Right(int i) => (i + 1) % philosophersAmount;

	    private void Think(int philosopher_inx)
        {
            long original = bigInteger;
            long number = original;
            // find all fractions:
            for (long delimiter = 2; delimiter * delimiter <= number; delimiter++)
                while (number % delimiter == 0)
                    number /= delimiter;
            // take next or start over:
            long next = original == long.MaxValue ? FirstBigInteger : original + 1;
            // to make others work on the next number:
            if (Interlocked.CompareExchange(ref bigInteger, next, original) == original) 
            {
                // this philosopher was first to do it:
                thoughts[philosopher_inx]++;
            }
        }

        private void RunDeadlock(int i, CancellationToken token)
        {
			void TakeFork(int fork)
			{
				// Here a philosopher eventually get a deadlock
				SpinWait.SpinUntil(() => Interlocked.CompareExchange(ref forks[fork], 1, 0) == 0);
			}

			void PutFork(int fork) { forks[fork] = 1; }

            Console.WriteLine($"P{i + 1} starting");
            while (true)
            {
				TakeFork(Left(i));
				TakeFork(Right(i));
				eatenFood[i] = (eatenFood[i] + 1) % (int.MaxValue - 1);
				PutFork(Left(i));
				PutFork(Right(i));
                Think(i);

				// stop when client requests:
	            token.ThrowIfCancellationRequested();
            }
        }

        private void RunStarvation(int i, CancellationToken token)
        {
			// TODO: 
			// Take left fork and wait t time for the right fork to be available.
			// If the right one is not available put down left and try again in t time.
	        var waitTime = TimeSpan.FromMilliseconds(1000*i);

	        bool TakeFork(int fork) => Interlocked.CompareExchange(ref forks[fork], 1, 0) == 0;
	        bool WaitFork(int fork) => SpinWait.SpinUntil(() => TakeFork(fork), waitTime);
			void PutFork(int fork) => forks[fork] = 1;

            Console.WriteLine($"P{i + 1} starting");
            while (true)
            {
	            bool hasForks = false;
	            if (WaitFork(Left(i)))
	            {
					Console.WriteLine($"P{i+1} took left");
		            if (TakeFork(Right(i)))
			            hasForks = true;
		            else
						PutFork(Left(i));
	            }
	            if (!hasForks)
	            {
					Console.WriteLine($"P{i+1} doesn't have forks");
					// here we can have starvation eventually as all philosophers take left
					// fork and their right one is not available and repeat.
					Thread.Sleep(waitTime);
					continue;
	            }
				eatenFood[i] = (eatenFood[i] + 1) % (int.MaxValue - 1);
				PutFork(Left(i));
				PutFork(Right(i));
                Think(i);

				// stop when client requests:
	            token.ThrowIfCancellationRequested();
            }
        }

        public void RunMonitor(int i, CancellationToken token)
        {
			void TakeFork(int fork_inx)
			{
				SpinWait.SpinUntil(()=>forks[fork_inx] == 1);
				forks[fork_inx] = 0;
			}

			void PutFork(int fork_inx) { forks[fork_inx] = 1; }

            Console.WriteLine($"P{i + 1} starting");
            while (true)
            {
                lock(forks)
                {
					TakeFork(Left(i));
					TakeFork(Right(i));
					eatenFood[i] = (eatenFood[i] + 1) % (int.MaxValue - 1);
					PutFork(Left(i));
					PutFork(Right(i));
                }
                Think(i);
				// stop when client requests:
	            token.ThrowIfCancellationRequested();
            }
        }

        public void RunInterlocked(int i, CancellationToken token)
        {
            void TakeForks()
            {
                // This takes two forks if available or none (atomical) and does not go into kernel mode
                SpinWait.SpinUntil(() =>
                {
                    // try to take left and right fork if they are awailable:
                    int left = Interlocked.CompareExchange(ref forks[Left(i)], 0, 1);
                    int right = Interlocked.CompareExchange(ref forks[Right(i)], 0, 1);
                    bool wereBothFree = left == 1 && right == 1;
                    if (wereBothFree)  
                        return true;
                    // else put the taken fork back if any:
                    Interlocked.CompareExchange(ref forks[Left(i)], 1, 0);
                    Interlocked.CompareExchange(ref forks[Right(i)], 1, 0);
                    return false;
                });
                // at this point this philosopher has taken two forks
            }
            void PutForks() 
            {
                forks[Left(i)] = 1;
                forks[Right(i)] = 1;
            }

            Console.WriteLine($"P{i + 1} starting");
            while (true)
            {
                TakeForks();
                eatenFood[i] = (eatenFood[i] + 1) % (int.MaxValue - 1);
                PutForks();
                Think(i);
				token.ThrowIfCancellationRequested();
            }
        }

        private void Observe(object state) 
        {
            for (int i = 0; i < philosophersAmount; i++)
            {
                if (lastEatenFood[i] == eatenFood[i])
                    Console.WriteLine($"P{i + 1} didn't eat: {lastEatenFood[i]}-{eatenFood[i]}, thoughts: {thoughts[i]}, forks: {string.Join(' ', forks)}.");
                lastEatenFood[i] = eatenFood[i];
            }
        }

        public void Run()
        {
            Console.WriteLine("Starting...");

            startTime = DateTime.Now;
            const int dueTime = 3000;
            const int checkPeriod = 2000;
	        threadingTimer = new Timer(Observe, null, dueTime, checkPeriod);
	        philosophers = new Task[philosophersAmount];

            var cancelTokenSource = new CancellationTokenSource();

	        // Action<int> create = (i) => RunDeadlock(i, cancelTokenSource.Token);
	        Action<int> create = (i) => RunStarvation(i, cancelTokenSource.Token);
	        // Action<int> create = (i) => RunMonitor(i, cancelTokenSource.Token);
	        // Action<int> create = (i) => RunInterlocked(i, cancelTokenSource.Token);
            for (int i = 0; i < philosophersAmount; i++)
            {
                int icopy = i;
                // use thread pool to start a philosopher:
                philosophers[i] = Task.Run(() => create(icopy), cancelTokenSource.Token);
				Thread.Sleep(200*i);
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();
            cancelTokenSource.Cancel();
            try
            {
	            const int timeToWaitMS = 3000;
                Task.WaitAll(philosophers, timeToWaitMS);
            }
            catch (Exception e)
            { 
                Console.WriteLine("Exception: " + e.Message);
            }
            TimeSpan spentTime = DateTime.Now - startTime;
            for (int i = 0; i < philosophersAmount; i++)
            {
                Console.WriteLine($"P{i+1} {eatenFood[i]} eaten, {thoughts[i]} thoughts.");
            }
            Console.WriteLine($"Time: {spentTime:g}");
            Console.WriteLine($"Thoughts speed: {thoughts.Sum() / (spentTime.TotalSeconds):.00} t/sec");
        }

	    public void Dispose()
	    {
		    threadingTimer?.Dispose();
	    }
    }

    public static class Program
    {
        public static void Main(string[] args)
        {
	        using (var dinner = new DinningPhilosophers())
	        {
				dinner.Run();
	        }
        }
    }
}
