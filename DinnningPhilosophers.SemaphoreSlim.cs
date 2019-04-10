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
		/// <summary>
		/// Semaphore per philosopher. GetAwaiter per philosopher so that no additional threads taken.
		/// </summary>
		class SemaphoreSlimSolution
		{
			private SemaphoreSlim[] _forkSemaphores;

			public SemaphoreSlimSolution()
			{
				_forkSemaphores = new SemaphoreSlim[philosophersAmount];
				for (int i = 0; i < philosophersAmount; i++)
					_forkSemaphores[i] = new SemaphoreSlim(1);
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
					await PutFork(Left(i), i + 1);
					await PutFork(Right(i), i + 1);
					_waitTime[i] += watch.ElapsedMilliseconds;
					Think(i);
					if (token.IsCancellationRequested) break;
				}
			}

			async Task<bool> TakeFork(int fork, int philosopher)
			{
				bool taken = false;
				await _forkSemaphores[fork].WaitAsync();
				if (forks[fork] == 0)
				{
					forks[fork] = philosopher;
					taken = true;
				}
				_forkSemaphores[fork].Release();
				return taken;
			}

			async Task TakeForks(int i)
			{
				// Even philosopher takes first left, then right, odd one otherwise.
				int first = i % 2 == 0 ? Left(i) : Right(i);
				int second = i % 2 == 0 ? Right(i) : Left(i);
				bool taken = false;
				while (!taken)
				{
					if (await TakeFork(first, i + 1))
					{
						if (await TakeFork(second, i + 1))
							taken = true;
						else
							await PutFork(first, i + 1);
					}
				}
			}

			async Task PutFork(int fork, int philosopher)
			{
				await _forkSemaphores[fork].WaitAsync();
				if (forks[fork] == philosopher)
					forks[fork] = 0;
				_forkSemaphores[fork].Release();
			}
		}
	}
}
