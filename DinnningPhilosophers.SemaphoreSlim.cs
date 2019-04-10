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
		class SemaphoreSlimSolution
		{
			private SemaphoreSlim _tableSemaphore = new SemaphoreSlim(1);
			private SemaphoreSlim[] _philosopherSemaphores;

			public SemaphoreSlimSolution()
			{
				_philosopherSemaphores = new SemaphoreSlim[PhilosophersAmount];
				for (int i = 0; i < PhilosophersAmount; i++)
					_philosopherSemaphores[i] = new SemaphoreSlim(1);
			}

			public async Task Run(int i, CancellationToken token)
			{
				var watch = new Stopwatch();
				while (true)
				{
					watch.Restart();
					await TakeForks(i);
					_waitTime[i] += watch.ElapsedMilliseconds;

					eatenFood[i] = (eatenFood[i] + 1) % (int.MaxValue - 1);

					watch.Restart();
					await PutForks(i);
					_waitTime[i] += watch.ElapsedMilliseconds;

					Think(i);

					if (token.IsCancellationRequested) break;
				}
			}

			async Task TakeForks(int i)
			{
				bool hasForks = false;
				while (!hasForks)
				{
					await _tableSemaphore.WaitAsync();
					if (forks[Left(i)] == 0 && forks[Right(i)] == 0)
					{
						forks[Left(i)] = i+1;
						forks[Right(i)] = i+1;
						hasForks = true;
					}
					_tableSemaphore.Release();
					if (!hasForks)
						await _philosopherSemaphores[i].WaitAsync();
				}
			}

			async Task PutForks(int i)
			{
				await _tableSemaphore.WaitAsync();
				forks[Left(i)] = 0;
				_philosopherSemaphores[LeftPhilosopher(i)].Release();
				forks[Right(i)] = 0;
				_philosopherSemaphores[RightPhilosopher(i)].Release();
				_tableSemaphore.Release();
			}
		}
	}
}
