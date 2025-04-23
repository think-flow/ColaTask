# CoTaskScheduler - 单线程任务调度器

## 概述

CoTaskScheduler 是一个自定义的 `TaskScheduler` 实现，它在一个专用的后台线程上顺序执行所有任务，类似javascript的事件循环。这种设计适用于需要线程亲和性或任务顺序执行的场景。

## 特性

- **单线程执行**：所有任务都在同一个后台线程上顺序执行，无数据竞争的风险
- **线程安全的任务队列**：使用 `BlockingCollection` 实现线程安全的任务管理
- **单例模式**：通过 `CoTaskScheduler.Instance` 提供全局访问
- **简单API**：与标准 `Task` API 无缝集成
- **轻量级**：相比默认线程池调度器开销更小

## 使用示例

### 基础用法

```csharp
// 创建将在CoTaskScheduler上运行的任务
Task.Factory.StartNew(() => 
{
    Console.WriteLine($"当前线程ID: {Thread.CurrentThread.ManagedThreadId}");
}, 
CancellationToken.None, 
TaskCreationOptions.None, 
CoTaskScheduler.Instance);
```


### 异步/等待示例
```csharp
static async Task Main(string[] args)
{
    await Task.Factory.StartNew(async () =>
    {
        Console.WriteLine($"线程: {Thread.CurrentThread.ManagedThreadId}");
        await Task.Delay(100);
        Console.WriteLine($"线程: {Thread.CurrentThread.ManagedThreadId}");
        await Worker();
    }, CancellationToken.None, TaskCreationOptions.None, CoTaskScheduler.Instance).Unwrap();
    
    
    static async Task Worker()
    {
        Console.WriteLine($"Worker线程: {Thread.CurrentThread.ManagedThreadId}");
        await Task.Yield();
        Console.WriteLine($"Worker执行线程: {Thread.CurrentThread.ManagedThreadId}");
    }
}
```

## 适用场景

本调度器特别适用于：
- 单线程异步，避免数据竞争
- 严格顺序执行任务的场景
- 线程亲和性的情况
- 确定性任务执行顺序的测试环境

## 性能注意事项

虽然此调度器提供了顺序执行保证，但它不具备默认线程池调度器的可扩展性。它最适合特定用例，而非通用任务调度。

## 实现原理

1. 使用单例模式确保全局唯一调度器实例
2. 内部维护一个 `BlockingCollection<Task>` 作为任务队列
3. 专用工作线程通过 `GetConsumingEnumerable` 消费任务
4. 通过 `TryExecuteTaskInline` 方法控制内联执行

## 许可证

MIT 许可证 - 可自由用于开源和商业项目。

## 贡献指南

欢迎贡献代码！如有任何改进或错误修复，请提交issue或pull request。