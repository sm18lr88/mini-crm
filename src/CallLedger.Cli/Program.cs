namespace CallLedger.Cli;

internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;
        return CliApplication.RunAsync(
            args,
            Console.In,
            Console.Out,
            Console.Error,
            Path.Combine(AppContext.BaseDirectory, "Data"),
            () => DateTimeOffset.Now);
    }
}
