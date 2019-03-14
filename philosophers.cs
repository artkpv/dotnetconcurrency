using System;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using System.Numerics;

namespace DPProblem
{
    public class DinningPhilosophers : IDisposable
    {
        private const int philosophersAmount = 2;

		// 0 - a fork is not taken, x - taken by x philosopher:
        private int[] forks = Enumerable.Repeat(0, philosophersAmount).ToArray();

        private static object lockObject = new object();

        private const int FirstBigInteger = 2;

        private long bigInteger = FirstBigInteger;

        private int[] eatenFood = new int[philosophersAmount];

        private int[] lastEatenFood = new int[philosophersAmount];

        private int[] thoughts = new int[philosophersAmount];

	    private Timer threadingTimer;

	    private DateTime startTime;

		[Pure] private static int Left(int i) => i;
		[Pure] private static int Right(int i) => (i + 1) % philosophersAmount;

	    private void Log(string message)
	    {
			Console.WriteLine($"{(DateTime.Now - startTime).TotalMilliseconds:000000.0}: {message}");
	    }

	    private void Think(int philosopher_inx)
        {
			// TODO: Make fraction of sum big prime number. Or find all primes within large interval (int.max)
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

		void TakeFork(int fork, int philosopher) =>
			SpinWait.SpinUntil(() => Interlocked.CompareExchange(ref forks[fork], philosopher, 0) == 0);

		void PutFork(int fork) => forks[fork] = 0; 

        private void RunDeadlock(int i, CancellationToken token)
        {
            Log($"P{i + 1} starting, thread {Thread.CurrentThread.ManagedThreadId}");
            while (true)
            {
				TakeFork(Left(i), i+1);
				TakeFork(Right(i), i+1);
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
			// Take left fork and wait t time for the right fork to be available.
			// If the right one is not available put down left and try again in t time.
	        TimeSpan waitTime = TimeSpan.FromMilliseconds(0);

	        bool TakeFork(int fork) => 
		        SpinWait.SpinUntil(() => Interlocked.CompareExchange(ref forks[fork], i+1, 0) == 0, waitTime);
			void PutFork(int fork) => forks[fork] = 0;

            Log($"P{i + 1} starting");
            while (true)
            {
	            bool hasForks = false;
	            if (TakeFork(Left(i)))
	            {
					// Log($"P{i+1} took left");
		            if (Interlocked.CompareExchange(ref forks[Right(i)], i+1, 0) == 0)
			            hasForks = true;
		            else
						PutFork(Left(i));
	            }
	            if (!hasForks)
	            {
					// Log($"P{i+1} T{Thread.CurrentThread.ManagedThreadId} w/o forks: forks {string.Join(' ', forks)}");

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
	            // token.ThrowIfCancellationRequested();
	            if (token.IsCancellationRequested)
		            break;
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

            Log($"P{i + 1} starting");
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

            Log($"P{i + 1} starting");
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
			// Log($"Forks {string.Join(' ', forks)}");
            for (int i = 0; i < philosophersAmount; i++)
            {
                if (lastEatenFood[i] == eatenFood[i])
                    Log($"P{i + 1} didn't eat: {lastEatenFood[i]}-{eatenFood[i]}, thoughts: {thoughts[i]}, forks: {string.Join(' ', forks)}.");
                lastEatenFood[i] = eatenFood[i];
            }
			Console.Out.Flush();
        }

        public void Run()
        {
            startTime = DateTime.Now;
            Log("Starting...");

            const int dueTime = 3000;
            const int checkPeriod = 1000;
	        threadingTimer = new Timer(Observe, null, dueTime, checkPeriod);
	        var philosophers1 = new Thread[philosophersAmount];

	        var cancelTokenSource = new CancellationTokenSource();

	        // Action<int> create = (i) => RunDeadlock(i, cancelTokenSource.Token);
	        Action<int> create = (i) => RunStarvation(i, cancelTokenSource.Token);
	        // Action<int> create = (i) => RunMonitor(i, cancelTokenSource.Token);
	        // Action<int> create = (i) => RunInterlocked(i, cancelTokenSource.Token);
            for (int i = 0; i < philosophersAmount; i++)
            {
	            int icopy = i;
				philosophers1[i] = 
					new Thread( () => RunStarvation(icopy, cancelTokenSource.Token))
					{
						IsBackground = true,
						Priority = i % 2 == 0 ? ThreadPriority.Highest : ThreadPriority.Lowest
					};
				philosophers1[i].Start();

                // use thread pool to start a philosopher:
	            // philosophers1[i] = Task.Run(() => create(icopy), cancelTokenSource.Token);
				// Thread.Sleep(200*i);
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();
			cancelTokenSource.Cancel();
            try
            {
	            const int timeToWaitMS = 3000;
	            foreach (Thread thread in philosophers1)
		            thread.Join(timeToWaitMS);
                // Task.WaitAll(philosophers1, timeToWaitMS);
            }
            catch (Exception e)
            { 
                Log("Exception: " + e.Message);
            }

            TimeSpan spentTime = DateTime.Now - startTime;
            for (int i = 0; i < philosophersAmount; i++)
            {
                Log($"P{i+1} {eatenFood[i]} eaten, {thoughts[i]} thoughts.");
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
