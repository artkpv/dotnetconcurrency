using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace DPProblem
{
	public partial class DinningPhilosophers : IDisposable
	{
		class AgentsSolution
		{
			private enum EMessages
			{
				Hungry,
				TakeFork,
			}

			private BlockingCollection<EMessages>[] _inboxes;
			public AgentsSolution()
			{
				_inboxes = new BlockingCollection<EMessages>[PhilosophersAmount];
				for (int i = 0; i < PhilosophersAmount; i++)
					_inboxes[i] = new BlockingCollection<EMessages>(new ConcurrentQueue<EMessages>());
			}

			public void Run(int i, CancellationToken token)
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
		}
	}
}
