using Caching.PerformanceTests;

internal class Program
{
    private const int DefaultNumberOfThreads = 1000;
    
    public static async Task Main(string[] args)
    {
        int numberOfTHeads = args.Length > 0 && int.TryParse(args[0], out var n) ? n : DefaultNumberOfThreads;
        
        Console.WriteLine("*Start Runner*\n");
        PerformanceTestRunner runner = new PerformanceTestRunner(numberOfTHeads);
        await runner.GetAllByKeysTest();
        await runner.GetAllByNamesTest();
        await runner.GetAllByIdsTest();
        await runner.GetAndSet();
        await runner.GetAllActualTest();
        Console.WriteLine("\n*Finish Runner*");
        Console.WriteLine("Press any key to close");
        Console.ReadKey();
    }
}