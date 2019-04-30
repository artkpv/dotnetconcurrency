using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
			private enum EForkState { Dirty, Clean, Requested }

			public AgentsSolution()
			{
				_inboxes = new BufferBlock<(int, EForkState)>[PhilosophersAmount];
				for (int i = 0; i < PhilosophersAmount; i++)
					_inboxes[i] = new BufferBlock<(int, EForkState)>();
			}

			async Task<(EForkState?, EForkState?)> TakeForks(
				int i, 
				EForkState? leftFork,
				EForkState? rightFork,
				CancellationToken token)
			{
				if (leftFork == null)
					_inboxes[LeftPhilosopher(i)].Post((Left(i), EForkState.Requested));
				if (rightFork == null)
					_inboxes[RightPhilosopher(i)].Post((Right(i), EForkState.Requested));
				while (leftFork == null || rightFork == null)
				{
					(int fork, EForkState state) = await _inboxes[i].ReceiveAsync(token);
					(leftFork, rightFork) = ProcessMessage(i, leftFork, rightFork, fork, state);
				}
				Debug.Assert(rightFork != null && leftFork != null);
				return (leftFork, rightFork);
			}

			(EForkState?, EForkState?) ProcessMessage(
				int i, 
				EForkState? leftFork,
				EForkState? rightFork,
				int messageFork,
				EForkState state)
			{
				if (state == EForkState.Requested)
				{
					if (messageFork == Left(i))
					{
						Debug.Assert(leftFork != null);
						if (leftFork == EForkState.Dirty)
						{
							_inboxes[LeftPhilosopher(i)].Post((messageFork, EForkState.Clean));
							leftFork = null;
						}
						else
						{
							_inboxes[i].Post((messageFork, EForkState.Requested));
						}
					}
					else if (messageFork == Right(i))
					{
						Debug.Assert(rightFork != null);
						if (rightFork == EForkState.Dirty)
						{
							_inboxes[RightPhilosopher(i)].Post((messageFork, EForkState.Clean));
							rightFork = null;
						}
						else
						{
							_inboxes[i].Post((messageFork, EForkState.Requested));
						}
					}
					else 
						throw new ApplicationException($"no such fork: {messageFork}");
				}
				else
				{
					if (messageFork == Left(i))
					{
						Debug.Assert(leftFork == null);
						leftFork = state;
					}
					else if (messageFork == Right(i))
					{
						Debug.Assert(rightFork == null);
						rightFork = state;
					}
					else 
						throw new ApplicationException($"no such fork: {messageFork}");
				}
				return (leftFork, rightFork);
			}

			(EForkState?, EForkState?) GiveForks(int i, EForkState? leftFork, EForkState? rightFork, CancellationToken token)
			{
				var inbox = _inboxes[i];
				while (inbox.Count > 0)
				{
					(int fork, EForkState state) = inbox.Receive();
					(leftFork, rightFork) = ProcessMessage(i, leftFork, rightFork, fork, state);
				}
				return (leftFork, rightFork);
			}

			public async void Run(int i, CancellationToken token)
			{
				EForkState? leftFork = EForkState.Dirty;
				forks[Left(i)] = i+1;
				EForkState? rightFork = null;
				var watch = new Stopwatch();
				while (true)
				{
					watch.Restart();
					(leftFork, rightFork) = await TakeForks(i, leftFork, rightFork, token);
					forks[Left(i)] = i+1;
					forks[Right(i)] = i+1;
					_waitTime[i] += watch.ElapsedMilliseconds;

					eatenFood[i] = (eatenFood[i] + 1) % (int.MaxValue - 1);
					leftFork = EForkState.Dirty;
					rightFork = EForkState.Dirty;

					watch.Restart();
					(leftFork, rightFork) = GiveForks(i, leftFork, rightFork, token);
					_waitTime[i] += watch.ElapsedMilliseconds;

					Think(i);

					if (token.IsCancellationRequested) break;
				}
			}
		}
	}
}
