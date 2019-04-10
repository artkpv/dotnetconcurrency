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
		class AutoResetEventSolution
		{
			private AutoResetEvent[] philosopherEvents;
			private AutoResetEvent tableEvent = new AutoResetEvent(true);

			public AutoResetEventSolution()
			{
				philosopherEvents = new AutoResetEvent[philosophersAmount];
				for (int i = 0; i < philosophersAmount; i++)
					philosopherEvents[i] = new AutoResetEvent(true);
			}

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
	}
}
