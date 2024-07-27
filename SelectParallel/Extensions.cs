using System.Threading.Tasks.Dataflow;
using System.Linq;
using System.Collections.Generic;

namespace SelectParallel;

public delegate IAsyncEnumerable<R> SelectParallelDelegate<T, R>(IEnumerable<T> source, Func<T, Task<R>> body, int maxDegreeOfParallelism);

public static class Extensions
{
    public static async IAsyncEnumerable<R> SelectParallelSemaphoreWhenAny<T, R>(this IEnumerable<T> source, Func<T, Task<R>> body, int maxDegreeOfParallelism)
    {
        using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);
        var tasks = source.Select(async item =>
        {
            await semaphore.WaitAsync();
            try
            {
                return await body(item);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToHashSet();
        while (tasks.Count > 0)
        {
            var completed = await Task.WhenAny(tasks);
            tasks.Remove(completed);
            yield return await completed;
        }
    }

    public static async IAsyncEnumerable<R> SelectParallelInterleaved<T, R>(this IEnumerable<T> source, Func<T, Task<R>> body, int maxDegreeOfParallelism)
    {
        var sourceArray = source.ToList();
        var buckets = new TaskCompletionSource<R>[sourceArray.Count];
        var results = new Task<R>[buckets.Length];
        for (var i = 0; i < buckets.Length; i++)
        {
            buckets[i] = new TaskCompletionSource<R>();
            results[i] = buckets[i].Task;
        }

        var readyTaskIndex = -1;
        var toLaunchTaskIndex = maxDegreeOfParallelism - 1;

        void Trigger(int index)
        {
            if (index < sourceArray.Count)
                _ = body(sourceArray[index]).ContinueWith(completed =>
                {
                    var bucket = buckets[Interlocked.Increment(ref readyTaskIndex)];
                    if (completed.IsCompleted)
                        bucket.TrySetResult(completed.Result);
                    else if (completed.IsFaulted)
                        bucket.TrySetException(completed.Exception!.InnerExceptions);
                    else if (completed.IsCanceled)
                        bucket.TrySetCanceled();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        for (var i = 0; i < Math.Min(maxDegreeOfParallelism, sourceArray.Count); i++)
            Trigger(i);

        foreach (var result in results)
        {
            yield return await result;
            Trigger(Interlocked.Increment(ref toLaunchTaskIndex));
        }
    }

    public static async IAsyncEnumerable<R> SelectParallelDataFlow<T, R>(this IEnumerable<T> source, Func<T, Task<R>> body, int maxDegreeOfParallelism)
    {
        var transformBlock = new TransformBlock<T, R>(
            async item => await body(item),
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism
            });
        foreach (var item in source)
        {
            await transformBlock.SendAsync(item);
        }
        transformBlock.Complete();
        while (await transformBlock.OutputAvailableAsync())
        {
            while (transformBlock.TryReceive(out var result))
            {
                yield return result;
            }
        }
    }

    public static async IAsyncEnumerable<R> SelectParallelQuery<T, R>(this IEnumerable<T> source, Func<T, Task<R>> body, int degreeOfParallelism)
    {
        foreach (var result in source.AsParallel().WithDegreeOfParallelism(degreeOfParallelism).Select(item => body(item).GetAwaiter().GetResult()))
            yield return result;
    }

    //Does not return result
    public static Task SelectParallelForEach<T>(this IEnumerable<T> source, Func<T, Task> body, int maxDegreeOfParallelism)
    {
        return Parallel.ForEachAsync(source, new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism }, async (item, _) => await body(item));
    }
}