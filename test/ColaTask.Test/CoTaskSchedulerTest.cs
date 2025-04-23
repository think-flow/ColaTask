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

    /// <summary>
    /// 通过 thread id 判断是不是单线程的scheduler
    /// </summary>
    [Fact]
    public async Task Should_Single_Thread1()
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
                Assert.Equal(taskThreadId, await GetAsyncMethodInnerThreadIdAsync());
                
                if (response.IsCompleted)
                {
                    string _ = await response.Result.Content.ReadAsStringAsync();
                    Assert.Equal(taskThreadId, Thread.CurrentThread.ManagedThreadId);
                    Assert.Equal(taskThreadId, await GetAsyncMethodInnerThreadIdAsync());
                }
            }
            catch
            {
                // ignored
            }

            await Task.Delay(50);
            Assert.Equal(taskThreadId, Thread.CurrentThread.ManagedThreadId);
            Assert.Equal(taskThreadId, await GetAsyncMethodInnerThreadIdAsync());

            await Task.Delay(100);
            Assert.Equal(taskThreadId, Thread.CurrentThread.ManagedThreadId);
            Assert.Equal(taskThreadId, await GetAsyncMethodInnerThreadIdAsync());

            output.WriteLine("task thread completed");
        }, CancellationToken.None, TaskCreationOptions.None, scheduler).Unwrap();

        output.WriteLine("main thread completed");

        async Task<int> GetAsyncMethodInnerThreadIdAsync()
        {
            int threadId = Thread.CurrentThread.ManagedThreadId;
            await Task.Yield();
            return threadId;
        }
    }

    /// <summary>
    /// 通过 同时等待阻塞和异步调用，判断是不是单线程的scheduler
    /// </summary>
    [Fact]
    public void Should_Single_Thread2()
    {
        //获取单线程调度器
        var singleScheduler = CoTaskScheduler.Instance;
        var sw = Stopwatch.StartNew();

        var task1 = ThreadSleep300(singleScheduler);
        var task2 = TaskDelay100(singleScheduler);

        //假如是单线程的
        //如果我们在 task1 中阻塞线程300毫秒，而 task2 中我们异步等待100毫秒
        //100毫秒后 由于任务线程还在task1中阻塞着, 所以task2的完成时间，取决于task1的阻塞结束时间
        //当我们进行WaitAny等待时，首先返回的应该是task1，而不是等待时间少的task2
        //如果我们测量总耗时的话，应该为300毫秒左右
        Task.WaitAny(task1, task2);
        sw.Stop();
        long ms = sw.ElapsedMilliseconds;
        output.WriteLine($"CoTaskScheduler it takes {ms} milliseconds");
        //允许误差30毫秒
        Assert.True(ms - 300 <= 30);

        //如果以上的验证成功
        //我们用默认的多线程 ThreadPoolTaskScheduler 进行同样的步骤，那么总耗时，应该为100毫秒
        sw.Restart();
        task1 = ThreadSleep300(TaskScheduler.Default);
        task2 = TaskDelay100(TaskScheduler.Default);

        Task.WaitAny(task1, task2);
        sw.Stop();
        ms = sw.ElapsedMilliseconds;
        output.WriteLine($"ThreadPoolTaskScheduler it takes {ms} milliseconds");
        //允许误差20毫秒
        Assert.True(ms - 100 <= 20);

        static Task ThreadSleep300(TaskScheduler scheduler)
        {
            return Task.Factory.StartNew(() =>
            {
                //让任务线程阻塞300毫秒
                Thread.Sleep(300);
            }, CancellationToken.None, TaskCreationOptions.None, scheduler);
        }

        static Task TaskDelay100(TaskScheduler scheduler)
        {
            return Task.Factory.StartNew(async () =>
            {
                await Task.Delay(100);
            }, CancellationToken.None, TaskCreationOptions.None, scheduler).Unwrap();
        }
    }

    /// <summary>
    /// 通过 数据竞争，判断是不是单线程的scheduler
    /// </summary>
    [Fact]
    public void Should_Single_Thread3()
    {
        int num = 5;
        for (int i = 0; i < num; i++)
        {
            //获取单线程调度器
            var singleScheduler = CoTaskScheduler.Instance;
            int shareCount = 0;

            Task[] tasks = new Task[100];
            for (int j = 0; j < 100; j++)
            {
                var task = Task.Factory.StartNew(async () =>
                {
                    await Task.Delay(100);
                    shareCount += 1;
                }, CancellationToken.None, TaskCreationOptions.None, singleScheduler);

                tasks[j] = task.Unwrap();
            }

            Task.WaitAll(tasks);

            //如果是单线程环境下，那么不可能出现数据竞争，所以shareCount 100次自增，值肯定为100
            Assert.Equal(100, shareCount);
        }

        // #####取消以下注释代码，可以验证多线程环境下的数据竞争

        /*
        //使用默认ThreadPoolTaskScheduler
        for (int i = 0; i < num; i++)
        {
            int shareCount1 = 0;

            Task[] tasks1 = new Task[100];
            for (int j = 0; j < 100; j++)
            {
                var task = Task.Factory.StartNew(async () =>
                {
                    await Task.Delay(100);
                    shareCount1 += 1;
                }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);

                tasks1[j] = task.Unwrap();
            }

            Task.WaitAll(tasks1);

            //如果是多线程环境下，则可能出现数据竞争，所以shareCount1 100次自增，值不一定为100
            output.WriteLine($"ThreadPoolTaskScheduler shareCount1: {shareCount1}");
        }
        */
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
        Assert.True(ms - 300 <= 30);
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
        //允许误差50毫秒
        Assert.True(ms - (100 + 150 + 300) <= 50);
    }
}
