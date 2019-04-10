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
		class MonitorSolution
		{
			private readonly object _lock = new object();
			private DateTime?[] _waitTimes = new DateTime?[philosophersAmount];

			public void Run(int i, CancellationToken token)
			{
				var watch = new Stopwatch();
				while (true)
				{
					watch.Restart();
					TakeForks(i);
					_waitTime[i] += watch.ElapsedMilliseconds;
					eatenFood[i] = (eatenFood[i] + 1) % (int.MaxValue - 1);
					watch.Restart();
					PutForks(i);
					_waitTime[i] += watch.ElapsedMilliseconds;
					Think(i);
					if (token.IsCancellationRequested) break;
				}
			}

			bool CanIEat(int i)
			{
				if (forks[Left(i)] != 0 && forks[Right(i)] != 0)
					return false;
				var now = DateTime.Now;
				foreach (var p in new int[] { LeftPhilosopher(i), RightPhilosopher(i) })
					if (_waitTimes[p] != null && now - _waitTimes[p] > now - _waitTimes[i])
						return false;
				return true;
			}

			void TakeForks(int i)
			{
				bool lockTaken = false;
				Monitor.Enter(_lock, ref lockTaken);
				try
				{
					_waitTimes[i] = DateTime.Now;
					while (!CanIEat(i))
						Monitor.Wait(_lock);
					forks[Left(i)] = i + 1;
					forks[Right(i)] = i + 1;
					_waitTimes[i] = null;
				}
				finally
				{
					if (lockTaken) Monitor.Exit(_lock);
				}
			}

			void PutForks(int i)
			{
				bool lockTaken = false;
				Monitor.Enter(_lock, ref lockTaken);
				try
				{
					forks[Left(i)] = 0;
					forks[Right(i)] = 0;
					Monitor.PulseAll(_lock);
				}
				finally
				{
					if (lockTaken) Monitor.Exit(_lock);
				}
			}
		}
	}
}
