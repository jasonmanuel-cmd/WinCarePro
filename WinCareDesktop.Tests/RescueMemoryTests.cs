using WinCareDesktop.Services;
using Xunit;
namespace WinCareDesktop.Tests;
public class RescueMemoryTests
{
    private static ProcessSample P(int pid=7, long start=100, double cpu=100) => new(pid,start,"Fixture",104857600,cpu);
    [Fact] public void Cpu_is_delta_normalized_by_elapsed_time_and_logical_processors()
    {
        var rows=MemorySampler.Compare(new[]{P()},new[]{P(cpu:300)},TimeSpan.FromSeconds(1),4);
        Assert.Equal(5,Assert.Single(rows).CpuPercent); Assert.Equal(100,rows[0].MemoryMb);
    }
    [Fact] public void Cpu_uses_each_process_observation_interval_not_enumeration_wall_time()
    {
        long hz=System.Diagnostics.Stopwatch.Frequency;
        var row=Assert.Single(MemorySampler.Compare(new[]{P() with {CapturedTicks=hz}},new[]{P(cpu:300) with {CapturedTicks=2*hz}},TimeSpan.FromSeconds(20),4));
        Assert.Equal(5,row.CpuPercent);
    }
    [Fact] public void Reused_pid_is_not_matched_to_previous_cpu()
    {
        var row=Assert.Single(MemorySampler.Compare(new[]{P()},new[]{P(start:200)},TimeSpan.FromSeconds(1),4));
        Assert.Null(row.CpuPercent);
    }
    [Fact] public void Negative_delta_is_unknown_and_results_are_bounded()
    {
        var rows=MemorySampler.Compare(new[]{P(cpu:300)},Enumerable.Range(1,40).Select(x=>P(pid:x)).ToArray(),TimeSpan.FromSeconds(1),4);
        Assert.True(rows.Count<=20); Assert.Null(rows.Single(x=>x.Pid==7).CpuPercent);
    }
}
