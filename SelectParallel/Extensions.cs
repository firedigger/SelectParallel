using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace SelectParallel;

public delegate IAsyncEnumerable<R> SelectParallelDelegate<T, R>(IReadOnlyList<T> source, Func<T, Task<R>> body, int maxDegreeOfParallelism);

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

    public static async IAsyncEnumerable<R> SelectParallelInterleaved<T, R>(this IReadOnlyList<T> source, Func<T, Task<R>> body, int maxDegreeOfParallelism)
    {
        var buckets = new TaskCompletionSource<R>[source.Count];
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
            if (index < source.Count)
                _ = body(source[index]).ContinueWith(completed =>
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

        for (var i = 0; i < Math.Min(maxDegreeOfParallelism, source.Count); i++)
            Trigger(i);

        foreach (var result in results)
        {
            yield return await result;
            Trigger(Interlocked.Increment(ref toLaunchTaskIndex));
        }
    }

    //Does not respect maxDegreeOfParallelism
    public static async IAsyncEnumerable<R> SelectParallelQuery<T, R>(this IEnumerable<T> source, Func<T, Task<R>> body, int maxDegreeOfParallelism)
    {
        foreach (var result in source.AsParallel().WithDegreeOfParallelism(maxDegreeOfParallelism).Select(async item => await body(item)))
            yield return await result;
    }

    //Does not return result
    public static Task SelectParallel<T>(this IEnumerable<T> source, Func<T, Task> body, int maxDegreeOfParallelism)
    {
        return Parallel.ForEachAsync(source, new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism }, async (item, _) => await body(item));
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
        await transformBlock.Completion;
    }
}