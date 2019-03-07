# Dinning philosophers problem - overview of synchronization constructs in .Net


# Problem
- Naive (deadlock)
# UMC
- `Interlocked` solution (wrong and correct). Valid implementation?
# KMC
- Semaphor (named / unnamed)
- Mutex
# Hybrid
- `SemaphorSlim`
- `Monitor` (`lock`)
-- Condition Variable Pattern
- `ReaderWriterLockSlim`. How? Ones computes, others checks results.
- `Barrier`. Make all philosophers stop at %100 or smth.
- Concurrent collections? Take two 
# MESSAGES (RUST recommends only on messages)
# Comparing all solutions


Сначала пример кода, которые приведет к deadlock.

СЛЕД:
1. Завершение потоков в дедлоке. Завершить пример с дедлоком. Потом сделай с локом пример. В статью только существенный код. Дублируй в коде самом чтобы быстрее и не в объекты обертывать и пр. Цель - самому разобраться с механизмами многопоточности и описать это понятно. Не тратить много времени. Проще.
2. Они берут вилку и если не получается то кладут обратно.


# Дедлоки

Сначала посмотрим на пример взаимной блокировки потоков.  

        private void RunDeadlock(int i, CancellationToken token)
        {
            Console.WriteLine($"P{i + 1} starting");
            while (true)
            {
                Think(i);
				TakeFork(i);
				TakeFork((i + 1) % philosophersAmount);
				eatenFood[i] = (eatenFood[i] + 1) % (int.MaxValue - 1);
				PutFork(i);
				PutFork((i + 1) % philosophersAmount);

				// stop when client requests:
	            token.ThrowIfCancellationRequested();
            }
        }

