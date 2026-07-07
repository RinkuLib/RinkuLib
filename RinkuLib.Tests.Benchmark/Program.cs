// See https://aka.ms/new-console-template for more information
using BenchmarkDotNet.Running;
using RinkuLib.Tests.Benchmark;
//await BaseBenchmark.DebugBreakpointsAsync();
//await new BaseBenchmark().Setup();
BenchmarkRunner.Run<BaseBenchmark>();
//await BaseBenchmark._fixture.DisposeAsync();
