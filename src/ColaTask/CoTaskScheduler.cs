using System.Collections.Concurrent;

namespace ColaTask;

public class CoTaskScheduler : TaskScheduler
{
    private static readonly Lazy<CoTaskScheduler> _instance = new Lazy<CoTaskScheduler>(() => new CoTaskScheduler(), true);

    //用于存放任务的容器
    private readonly BlockingCollection<Task> _tasks = new BlockingCollection<Task>();

    //用于执行任务的线程
    private readonly Thread _worker;

    private CoTaskScheduler()
    {
        _worker = new Thread(Work)
        {
            IsBackground = true,
            Name = "CoTaskScheduler Executor"
        };
        _worker.UnsafeStart();
    }

    public static CoTaskScheduler Instance => _instance.Value;

    private void Work()
    {
        foreach (var task in _tasks.GetConsumingEnumerable())
        {
            TryExecuteTask(task);
        }
    }

    protected override IEnumerable<Task> GetScheduledTasks() => _tasks.ToArray();

    protected override void QueueTask(Task task) => _tasks.Add(task);

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
    {
        // // 在单线程Scheduler中，要确保当前线程是本Scheduler中的线程，才执行任务
        // bool isCurrentThread = Thread.CurrentThread == _worker;
        // if (!isCurrentThread)
        // {
        //     //表示，当前Scheduler不处理该任务，请将任务加入队列中
        //     return false;
        //     // throw new InvalidOperationException($"尝试在错误线程({Thread.CurrentThread.ManagedThreadId})上执行任务，应该在线程{_worker.ManagedThreadId}上执行");
        // }
        //
        // //内联执行任务
        // return TryExecuteTask(task);
        return Thread.CurrentThread == _worker && TryExecuteTask(task);
    }
}
