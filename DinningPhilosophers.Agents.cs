using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks.Dataflow;

namespace DPProblem
{
	public partial class DinningPhilosophers : IDisposable
	{
		/// <summary>
		/// Chandy / Mistra solution. Solution based on agents (actors). 
		/// </summary>
		class AgentsSolution
		{
			private BufferBlock<(int, EForkState)>[] _inboxes;
			private enum EForkState { Dirty, Clean, Unknown }

			public AgentsSolution()
			{
				_inboxes = new BufferBlock<(int, EForkState)>[PhilosophersAmount];
				for (int i = 0; i < PhilosophersAmount; i++)
					_inboxes[i] = new BufferBlock<(int, EForkState)>();
			}

			public async void Run(int i, CancellationToken token)
			{
				/*
				EForkState? leftFork = EForkState.Dirty;
				EForkState? rightFork = null;
				var watch = new Stopwatch();
				async Task TakeForks()
				{
					watch.Restart();
					if (leftFork == null)
						_inboxes[LeftPhilosopher(i)].Post((Left(i), EForkState.Unknown));
					if (rightFork == null)
						_inboxes[RightPhilosopher(i)].Post((Right(i), EForkState.Unknown));
					while (leftFork == null || rightFork == null)
					{
						(int someFork, EForkState state) = await _inboxes[i].ReceiveAsync(token);
						Console.WriteLine($"Received {someFork}, {state}");
						if (someFork == Left(i))
						{
							leftFork = state;
						}
						if (someFork == Right(i))
						{
							rightFork = state;
						}
					}
					Debug.Assert(rightFork != null && leftFork != null);
					_waitTime[i] += watch.ElapsedMilliseconds;
				}

				void GiveForks()
				{
					watch.Restart();
					while (_inboxes[i].TryReceive(out (int fork, EForkState state) message))
					{
						if (message.fork == Left(i))
						{
							_inboxes[LeftPhilosopher(i)].Post((Left(i), EForkState.Clean));
							leftFork = null;
						}
						else if (message.fork == Right(i))
						{
							_inboxes[RightPhilosopher(i)].Post((Right(i), EForkState.Clean));
							rightFork = null;
						}
						else 
							throw new ApplicationException($"no such fork: {message.fork}");
					}
					_waitTime[i] += watch.ElapsedMilliseconds;
				}

				while (true)
				{
					await TakeForks();

					eatenFood[i] = (eatenFood[i] + 1) % (int.MaxValue - 1);
					leftFork = EForkState.Dirty;
					rightFork = EForkState.Dirty;

					GiveForks();

					Think(i);

					if (token.IsCancellationRequested) break;
				}
				*/
			}
		}
	}
}
