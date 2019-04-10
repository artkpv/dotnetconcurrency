using System;

namespace DPProblem
{
	public static class Program
	{
		public static void Main(string[] args)
		{
			AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
			{
				Console.WriteLine("unhandled exception");
			};
			using (var dinner = new DinningPhilosophers())
			{
				if (args.Length == 0 || !Enum.TryParse<DinningPhilosophers.EMethods>(args[0], out DinningPhilosophers.EMethods method))
					method = DinningPhilosophers.EMethods.Deadlock;
				dinner.Run(method);
			}
		}
	}
}
