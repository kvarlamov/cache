// See https://aka.ms/new-console-template for more information

using BenchmarkDotNet.Running;
using BenchmarkLocalMemoryCache;

BenchmarkRunner.Run<LocalMemoryCacheBenchmarkRunner>();