// See https://aka.ms/new-console-template for more information
using BenchmarkDotNet.Running;
using RinkuLib.Tests.Benchmark;
//await BaseBenchmark.DbSetup();
BenchmarkRunner.Run<BaseBenchmark>();
//await BaseBenchmark._fixture.DisposeAsync();
/*var b = new BaseBenchmark();
await b.Setup();*/
