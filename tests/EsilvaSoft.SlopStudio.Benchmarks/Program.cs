using BenchmarkDotNet.Running;
using EsilvaSoft.SlopStudio.Benchmarks;

// dotnet run -c Release --project tests/EsilvaSoft.SlopStudio.Benchmarks -- --filter "*"   (BenchmarkDotNet)
// dotnet run -c Release --project tests/EsilvaSoft.SlopStudio.Benchmarks -- memory         (catalog memory scenario)
if (args is ["memory"])
{
    await MemoryScenario.RunAsync(Console.Out);
    return;
}
BenchmarkSwitcher.FromAssembly(typeof(SyntheticWorkload).Assembly).Run(args);
