using System;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using System.Numerics;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices; 

namespace DPProblem
{
	public class DinningPhilosophers : IDisposable
	{
		private const int philosophersAmount = 5;

		// 0 - a fork is not taken, x - taken by x philosopher:
		private static int[] forks = Enumerable.Repeat(0, philosophersAmount).ToArray();

		private static object lockObject = new object();

		private static int[] eatenFood = new int[philosophersAmount];

		private static int[] lastEatenFood = new int[philosophersAmount];

		private static int[] thoughts = new int[philosophersAmount];

		private Timer threadingTimer;

		private static Stopwatch watch;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static int Left(int i) => i;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static int LeftPhilosopher(int i) => (philosophersAmount + i - 1) % philosophersAmount;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static int Right(int i) => (i + 1) % philosophersAmount;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static int RightPhilosopher(int i) => (i + 1) % philosophersAmount;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void Log(string message) =>
			Console.WriteLine($"{watch.ElapsedMilliseconds}: {message}");

		private static void Think(int philosopherInx)
		{
			// A philosopher will find all primes not greater than 2^16-1 (~65ms)
			const int primesLimit = 0x1_0000 - 1;
			bool isPrime(long number) =>
				Enumerable.Range(2, (int)Math.Sqrt(number) - 1).All(i => number % i != 0);
			int primes = 1; // for 2 is prime
			const int rangeFirst = 3;
			primes += Enumerable.Range(rangeFirst, primesLimit - rangeFirst + 1).Count(i => isPrime(i));
			if (primes > 0) // so that compiler won't optimize this out
				thoughts[philosopherInx]++;
		}

		bool TakeFork(int fork, int philosopher, TimeSpan? waitTime = null)
		{
			return SpinWait.SpinUntil(() => Interlocked.CompareExchange(ref forks[fork], philosopher, 0) == 0,
									  waitTime ?? TimeSpan.FromMilliseconds(-1));
		}

		void PutFork(int fork) => Debug.Assert(Interlocked.Exchange(ref forks[fork], 0) != 0);


		private void RunDeadlock(int i, CancellationToken token)
		{
			Log($"P{i + 1} starting, thread {Thread.CurrentThread.ManagedThreadId}");
			while (true)
			{
				TakeFork(Left(i), i + 1);
				TakeFork(Right(i), i + 1);
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
			Log($"P{i + 1} starting, {Thread.CurrentThread.Name} {Thread.CurrentThread.ManagedThreadId}");
			while (true)
			{
				bool hasTwoForks = false;
				var waitTime = TimeSpan.FromMilliseconds(50);
				bool hasLeft = forks[Left(i)] == i + 1;
				if (hasLeft || TakeFork(Left(i), i + 1, waitTime))
				{
					if (forks[Right(i)] == i + 1 || TakeFork(Right(i), i + 1, TimeSpan.Zero))
						hasTwoForks = true;
					else
						PutFork(Left(i));
				}
				if (!hasTwoForks)
				{
					if (token.IsCancellationRequested) break;
					continue;
				}
				eatenFood[i] = (eatenFood[i] + 1) % (int.MaxValue - 1);
				bool goodPhilosopher = i % 2 == 1;
				if (goodPhilosopher)
				{
					PutFork(Left(i));
					PutFork(Right(i));
				}

				Think(i);

				if (token.IsCancellationRequested)
					break;
			}
		}

		public void RunInterlocked(int i, CancellationToken token)
		{
			while (true)
			{
				// This takes two forks if available or none (atomical) and does not go into kernel mod.
				// Can lead to deadlock (all take their left forks repeatedly).
				SpinWait.SpinUntil(() =>
				{
					// try to take left and right fork if they are awailable:
					int left = Interlocked.CompareExchange(ref forks[Left(i)], i + 1, 0);
					int right = Interlocked.CompareExchange(ref forks[Right(i)], i + 1, 0);
					bool wereBothFree = left == 0 && right == 0;
					if (wereBothFree)
						return true;
					// else put the taken fork back if any:
					Interlocked.CompareExchange(ref forks[Left(i)], 0, i + 1);
					Interlocked.CompareExchange(ref forks[Right(i)], 0, i + 1);
					return false;
				});
				// at this point this philosopher has taken two forks

				eatenFood[i] = (eatenFood[i] + 1) % (int.MaxValue - 1);
				PutFork(Left(i));
				PutFork(Right(i));
				Think(i);
				if (token.IsCancellationRequested) break;
			}
		}

		private static SpinLock spinLock = new SpinLock();
		private void RunSpinLock(int i, CancellationToken token)
		{
			while (true)
			{
				bool hasLock = false;
				try
				{
					spinLock.Enter(ref hasLock);
					forks[Left(i)] = i + 1;
					forks[Right(i)] = i + 1;
					eatenFood[i] = (eatenFood[i] + 1) % (int.MaxValue - 1);
					forks[Left(i)] = 0;
					forks[Right(i)] = 0;
				}
				finally
				{
					if (hasLock)
						spinLock.Exit();
				}

				Think(i);

				if (token.IsCancellationRequested)
					break;
			}
		}


		class AutoResetEventSample
		{
			// Для блокирования каждого философа:
			private AutoResetEvent[] philosopherEvents;
			// Для доступа к вилкам:
			private AutoResetEvent tableEvent = new AutoResetEvent(true);

			public AutoResetEventSample()
			{
				philosopherEvents = new AutoResetEvent[philosophersAmount];
				for(int i = 0; i < philosophersAmount; i++)
					philosopherEvents[i] = new AutoResetEvent(true);
			}

			public void Run(int i, CancellationToken token)
			{
				while (true)
				{
					TakeForks(i);
					eatenFood[i] = (eatenFood[i] + 1) % (int.MaxValue - 1);
					PutForks(i);
					Think(i);
					if (token.IsCancellationRequested) break;
				}
			}

			void TakeForks(int i)
			{
				bool hasForks = false;
				while (!hasForks)
				{
					tableEvent.WaitOne();
					if (forks[Left(i)] == 0 && forks[Right(i)] == 0)
						forks[Left(i)] = forks[Right(i)] = i + 1;
					hasForks = forks[Left(i)] == i + 1 && forks[Right(i)] == i + 1;
					if (hasForks)
						philosopherEvents[i].Set();
					tableEvent.Set();
					philosopherEvents[i].WaitOne();
				}
			}

			void PutForks(int i)
			{
				tableEvent.WaitOne();
				forks[Left(i)] = 0;
				philosopherEvents[LeftPhilosopher(i)].Set();
				forks[Right(i)] = 0;
				philosopherEvents[RightPhilosopher(i)].Set();
				tableEvent.Set();
			}
		}

		public void RunMonitor(int i, CancellationToken token)
		{
			Log($"P{i + 1} starting");
			while (true)
			{
				try
				{
					Monitor.Enter(lockObject);
					TakeFork(Left(i), i + 1);
					TakeFork(Right(i), i + 1);
					eatenFood[i] = (eatenFood[i] + 1) % (int.MaxValue - 1);
					PutFork(Left(i));
					PutFork(Right(i));
				}
				finally
				{
					Monitor.Exit(lockObject);
				}
				Think(i);

				if (token.IsCancellationRequested)
					break;
			}
		}

		private void Observe(object state)
		{
			Log($"Food {string.Join(' ', eatenFood)}, thoughts {string.Join(' ', thoughts)}");
			for (int i = 0; i < philosophersAmount; i++)
			{
				if (lastEatenFood[i] == eatenFood[i])
					Log($"P{i + 1} didn't eat: {lastEatenFood[i]}-{eatenFood[i]}. Forks {string.Join(' ', forks)}");
				lastEatenFood[i] = eatenFood[i];
			}
			Console.Out.Flush();
		}

		public void Run()
		{
			watch = Stopwatch.StartNew();
			Log("Starting...");

			const int dueTime = 1000;
			const int checkPeriod = 1000;
			threadingTimer = new Timer(Observe, null, dueTime, checkPeriod);
			var philosophers = new Task[philosophersAmount];

			var cancelTokenSource = new CancellationTokenSource();
			var autoResetEventSample = new AutoResetEventSample();

			for (int i = 0; i < philosophersAmount; i++)
			{
				int icopy = i;
				philosophers[i] =
					// Task.Run((otheri) => RunDeadlock(icopy, cancelTokenSource.Token))
					//Task.Run(() => RunStarvation(icopy, cancelTokenSource.Token))
					// Task.Run(() => RunSpinLock(icopy, cancelTokenSource.Token))
					// Task.Run(() => RunInterlocked(icopy, cancelTokenSource.Token))
					Task.Run(() => autoResetEventSample.Run(icopy, cancelTokenSource.Token))
					//Task.Run(() => RunMonitor(icopy, cancelTokenSource.Token))
					;
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
				Log("Exception: " + e.Message);
			}

			for (int i = 0; i < philosophersAmount; i++)
			{
				Log($"P{i + 1} {eatenFood[i]} eaten, {thoughts[i]} thoughts.");
			}
			Console.WriteLine($"Time: {watch.ElapsedMilliseconds}");
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
			AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
			{
				Console.WriteLine("unhandled exception");

			};
			using (var dinner = new DinningPhilosophers())
			{
				dinner.Run();
			}
		}
	}
}
