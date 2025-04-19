using System.Collections.Concurrent;
using System.Diagnostics;
using Xunit.Abstractions;

namespace ColaTask.Test;

public class CoTaskSchedulerTest(ITestOutputHelper output)
{
    [Fact]
    public void Should_Same_Instance()
    {
        var list = new ConcurrentBag<CoTaskScheduler>();
        var temp = CoTaskScheduler.Instance;
        Parallel.For(0, 20, _ =>
        {
            var instance = CoTaskScheduler.Instance;
            list.Add(instance);
        });
        //确保只有一个CoTaskScheduler实例
        Assert.All(list, instance => Assert.Same(temp, instance));
    }

    [Fact]
    public async Task Should_Single_Thread()
    {
        int mainThreadId = Thread.CurrentThread.ManagedThreadId;
        output.WriteLine($"main thread id {mainThreadId}");

        //获取单线程调度器
        var scheduler = CoTaskScheduler.Instance;
        //在单线程调度器上执行任务
        await Task.Factory.StartNew(async () =>
        {
            int taskThreadId = Thread.CurrentThread.ManagedThreadId;
            output.WriteLine($"task thread id {taskThreadId}");

            try
            {
                using var httpClient = new HttpClient();
                var response = httpClient.GetAsync("https://www.baidu.com");
                var timeout = Task.Delay(200);
                await Task.WhenAny(timeout, response);
                Assert.Equal(taskThreadId, Thread.CurrentThread.ManagedThreadId);

                if (response.IsCompleted)
                {
                    string _ = await response.Result.Content.ReadAsStringAsync();
                    Assert.Equal(taskThreadId, Thread.CurrentThread.ManagedThreadId);
                }
            }
            catch
            {
                // ignored
            }

            await Task.Delay(50);
            Assert.Equal(taskThreadId, Thread.CurrentThread.ManagedThreadId);

            await Task.Delay(100);
            Assert.Equal(taskThreadId, Thread.CurrentThread.ManagedThreadId);

            output.WriteLine("task thread completed");
        }, CancellationToken.None, TaskCreationOptions.None, scheduler).Unwrap();

        output.WriteLine("main thread completed");
    }

    [Fact]
    public async Task Should_Async()
    {
        //获取单线程调度器
        var scheduler = CoTaskScheduler.Instance;
        var sw = new Stopwatch();
        await Task.Factory.StartNew(async () =>
        {
            sw.Start();
            var task1 = Task.Delay(100);
            var task2 = Task.Delay(150);
            var task3 = Task.Delay(300);
            await Task.WhenAll(task1, task2, task3);
            sw.Stop();
        }, CancellationToken.None, TaskCreationOptions.None, scheduler).Unwrap();

        //如果是异步的，总花费时间应该为较长的300毫秒，
        //如果是同步的，总花费时间应该为(100+150+300)毫秒
        long ms = sw.ElapsedMilliseconds;
        output.WriteLine($"it takes {ms} milliseconds");
        //允许误差30毫秒
        Assert.True(ms - 300 < 30);
    }

    [Fact]
    public async Task Should_Sync()
    {
        //获取单线程调度器
        var scheduler = CoTaskScheduler.Instance;
        var sw = new Stopwatch();
        await Task.Factory.StartNew(async () =>
        {
            sw.Start();
            await Task.Delay(100);
            await Task.Delay(150);
            await Task.Delay(300);
            sw.Stop();
        }, CancellationToken.None, TaskCreationOptions.None, scheduler).Unwrap();

        //如果是异步的，总花费时间应该为较长的300毫秒上下，
        //如果是同步的，总花费时间应该为(100+150+300)550毫秒上下
        long ms = sw.ElapsedMilliseconds;
        output.WriteLine($"it takes {ms} milliseconds");
        //允许误差30毫秒
        Assert.True(ms - (100 + 150 + 300) < 30);
    }

    //测试死锁问题
    [Fact]
    public async Task Should_Dead_lock()
    {
        //获取单线程调度器
        var scheduler = CoTaskScheduler.Instance;
        await Task.Factory.StartNew(async () =>
        {
            using var httpClient = new HttpClient();
            var content = httpClient.GetStringAsync("https://www.baidu.com").Result;
            output.WriteLine(content);
        }, CancellationToken.None, TaskCreationOptions.None, scheduler).Unwrap();
    }
}
