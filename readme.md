
Originally formulated in 1965 by Edsger Dijkstra 

# Intro
# Naive approach
- Naive, approach violating statement (takes fork that can not be taken etc.)
- Interlocked.CompareExchange and SpinWait to ensure rules -> deadlock
- Try take, put. Starvation guaranteed
- Try take, put, wait random time. Partial solution. Interlocked everything pattern.
# Arbitrage
- KMC
-- Only one eats. Semaphor (named / unnamed)
-- Only one eats. Mutex
- Hybrid
-- `SemaphorSlim`
-- `Monitor` (`lock`)
--- Condition Variable Pattern
-- `ReaderWriterLockSlim`. How? Ones computes, others checks results.
-- `Barrier`. Make all philosophers stop at %100 or smth.
# Based on messages
- Many semaphors
- BlockingCollection<T> (producer / consumer) and thread local storage
- Actors model (Orleans and Akka.Net)
# ? Comparing all solutions  


=======================================

# Сытые философы спустя 54 года или обзор синхронизации в .Net 

В этой статье описываются способы синхронизации потоков и процессов в .Net на примере проблемы обедающих философов. План простой -- от наивного решения, т.е. отсутствия синхронизации, до модели акторов. Статья может быть полезна, как введение в синхронизацию или для того чтобы освежить свои знания. Постараемся еще выделить те решения, которые можно использовать на разных платформах (.Net Core). 

Эдсгер Дейкстра задавал эту проблему своим ученикам еще в 1965. Устоявшаяся формулировка такая. Есть некоторое (обычно пять) количество философов и столько же вилок. Они сидят за круглым столом, вилки между ними. Философы могут есть из своих тарелок с бесконечной пищей, думать или ждать. Чтобы поесть философу, нужно взять две вилки (последний делит вилку с первым). Взять и положить вилку - два раздельных действия. Все философы безмолвные. Задача найти такой алгоритм, чтобы все они думали и были сыты спустя 54 года и больше.

# Задержанные и голодные философы

Для запуска потоков используем пул потоков через Task.Run:

		var cancelTokenSource = new CancellationTokenSource();
		Action<int> create = (i) => RunDeadlock(i, cancelTokenSource.Token);
		for (int i = 0; i < philosophersAmount; i++) 
		{
			int icopy = i;
			// Поместить задачу в очередь пула потоков. Метод RunDeadlock не запускаеться 
			// сразу, а ждет своего потока. Асинхронный запуск потоков.
			philosophers[i] = Task.Run(() => create(icopy), cancelTokenSource.Token);
		}

Пул потоков создан для оптимизации создания и удаления потоков. У этого пула есть очередь с задачами и CLR создает или удаляет потоки в зависимости от количества этих задач. Один пул на все AppDomain'ы. Настройки по умолчанию: минимальное количество потоков равняется количеству ядер, максимальное у меня 2^15-1. Этот пул стоит использовать почти всегда за исключением некоторых случаев (поменять приоритет потоку, для долгой операции, сделать поток не фоновым и др.)

CancelationTokenSource здесь нужен, чтобы поток мог сам завершится по сигналу вызывающего потока.

Хорошо, мы умеем создавать потоки, давайте попробуем отобедать:

        private int[] forks = Enumerable.Repeat(1, philosophersAmount).ToArray();
        private void RunDeadlock(int i, CancellationToken token)
        {
			// Пробовать взять вилку пока ее нет. Эквивалентно: 
			// while(true) 
			//     if forks[fork] == 0 
			//          forks[fork] = 1
			//          break
			//     Thread.Sleep() или Yield() или SpinWait()
			void TakeFork(int fork) =>
				SpinWait.SpinUntil(() => Interlocked.CompareExchange(ref forks[fork], 1, 0) == 0);
			void PutFork(int fork) => forks[fork] = 1; 

            while (true)
            {
				TakeFork(Left(i));
				TakeFork(Right(i));
				eatenFood[i] = (eatenFood[i] + 1) % (int.MaxValue - 1);
				PutFork(Left(i));
				PutFork(Right(i));
                Think(i);

				// Завершить работу по-хорошему:
	            token.ThrowIfCancellationRequested();
            }
        }

Здесь мы сначала пробуем взять левую, а потом правую вилки и если получилось, то едим и кладем их обратно. Взятие одной вилки атомарно, т.е. два потока не могут взять одну одновременно (неверно: первый читает, что вилка свободна, второй -- тоже, первый берет, второй берет). Для этого Interlocked.CompareExchange. Он должен быть реализован с помощью инструкции процессора (TSL, XCHG), которая блокирует участок памяти для атомарного последовательного чтения и, если условие выполняется, записи. А SpinWait эквивалентно конструкции while(true) только с небольшой "магией" -- поток занимает процессор (Thread.SpinWait), но иногда передает управление другому потоку (Thread.Yeild) или засыпает (Thread.Sleep).

Но это решение не работает, т.к. потоки скоро (у меня в течении 1 секунды) блокируются, все философы взяв 
