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
	public partial class DinningPhilosophers : IDisposable
	{
		public enum EMethods
		{
			Deadlock = 0,
			Starvation = 1,
			SpinLock = 2,
			Interlocked = 3,
			AutoResetEvent = 4,
			Monitor = 5,
			SemaphorSlim = 6,
			Agents = 7
		}
		private const int PhilosophersAmount = 50;

		// 0 - a fork is not taken, x - taken by x philosopher:
		private static int[] forks = Enumerable.Repeat(0, PhilosophersAmount).ToArray();

		private volatile static int[] eatenFood = new int[PhilosophersAmount];

		private volatile static int[] lastEatenFood = new int[PhilosophersAmount];

		private volatile static int[] thoughts = new int[PhilosophersAmount];

		private volatile static long[] _waitTime = Enumerable.Repeat(0L, PhilosophersAmount).ToArray();

		private Timer threadingTimer;

		private static Stopwatch watch;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static int Left(int i) => i;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static int LeftPhilosopher(int i) => (PhilosophersAmount + i - 1) % PhilosophersAmount;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static int Right(int i) => (i + 1) % PhilosophersAmount;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static int RightPhilosopher(int i) => (i + 1) % PhilosophersAmount;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void Log(string message) =>
			Console.WriteLine($"{watch.ElapsedMilliseconds}: {message}");

		/// <summary>
		/// A philosopher will find all primes not greater than 2^16-1 (~65ms)
		/// </summary>
		/// <param name="philosopherInx"></param>
		private static void Think(int philosopherInx)
		{
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
			var watch = new Stopwatch();
			while (true)
			{
				watch.Restart();
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
				_waitTime[i] += watch.ElapsedMilliseconds;

				Think(i);

				if (token.IsCancellationRequested)
					break;
			}
		}

		private void Observe(object state)
		{
			Console.WriteLine($"Wait time: {string.Join(' ', _waitTime)}");
			for (int i = 0; i < PhilosophersAmount; i++)
			{
				if (lastEatenFood[i] == eatenFood[i])
					Log($"P{i + 1} didn't eat: {lastEatenFood[i]}-{eatenFood[i]}. Forks {string.Join(' ', forks)}");
				lastEatenFood[i] = eatenFood[i];
			}
			Console.Out.Flush();
		}

		public void Run(EMethods method)
		{
			watch = Stopwatch.StartNew();
			Log("Starting...");

			const int dueTime = 1000;
			const int checkPeriod = 2000;
			// threadingTimer = new Timer(Observe, null, dueTime, checkPeriod);
			var philosophers = new Task[PhilosophersAmount];

			var cancelTokenSource = new CancellationTokenSource();
			var autoResetEvent = new AutoResetEventSolution();
			var monitorSolution = new MonitorSolution();
			var semaphorSlimSolution = new SemaphoreSlimSolution();
			var agentsSolution = new AgentsSolution();

			var runActions = new Func<int, Task>[]
			{
				(i) => Task.Run(() => RunDeadlock(i, cancelTokenSource.Token)),
				(i) => Task.Run(() => RunStarvation(i, cancelTokenSource.Token)),
				(i) => Task.Run(() => RunSpinLock(i, cancelTokenSource.Token)),
				(i) => Task.Run(() => RunInterlocked(i, cancelTokenSource.Token)),
				(i) => Task.Run(() => autoResetEvent.Run(i, cancelTokenSource.Token)),
				(i) => Task.Run(() => monitorSolution.Run(i, cancelTokenSource.Token)),
				(i) => Task.Run(() => semaphorSlimSolution.Run(i, cancelTokenSource.Token)),
				(i) => Task.Run(() => agentsSolution.Run(i, cancelTokenSource.Token)),
			};

			Log($"Method {method}");
			for (int i = 0; i < PhilosophersAmount; i++)
			{
				int icopy = i;
				philosophers[i] = runActions[(int)method](icopy);
			}

			Console.WriteLine("Press any key to exit...");
			Console.ReadLine();
			cancelTokenSource.Cancel();
			try
			{
				const int timeToWaitMS = 3000;
				Task.WaitAll(philosophers, timeToWaitMS);
				watch.Stop();
			}
			catch (Exception e)
			{
				Log("Exception: " + e.Message);
			}

			Log($"Food {string.Join(' ', eatenFood)}, thoughts {string.Join(' ', thoughts)}");
			Console.WriteLine($"Elapsed time: {watch.ElapsedMilliseconds}");
			Console.WriteLine($"Wait time: {string.Join(' ', _waitTime)}");
			if (_waitTime.Any(i => i == 0))
				Console.WriteLine("Failed to run some philosopher(s)");
			else
				Console.WriteLine(
					$"Total wait time: {_waitTime.Sum()},"
					+ $" total wait / elapsed: {(double)_waitTime.Sum() / watch.ElapsedMilliseconds}");
		}

		public void Dispose()
		{
			threadingTimer?.Dispose();
		}
	}

}
