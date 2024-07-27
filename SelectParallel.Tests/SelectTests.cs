namespace SelectParallel.Tests;

public class SelectTests
{
    private readonly Random _random = new(1);
    const int delay = 100;
    const int range = 100;
    public delegate IAsyncEnumerable<R> SelectParallelDelegate<T, R>(IEnumerable<T> source, Func<T, Task<R>> body, int maxDegreeOfParallelism);

    public static IEnumerable<object[]> Implementations => [[new SelectParallelDelegate<int, int>(Extensions.SelectParallelSemaphoreWhenAny)],
            [new SelectParallelDelegate<int, int>(Extensions.SelectParallelInterleaved)],
            [new SelectParallelDelegate<int, int>(Extensions.SelectParallelDataFlow)],
            [new SelectParallelDelegate<int, int>(Extensions.SelectParallelQuery)]];


    [Theory]
    [MemberData(nameof(Implementations))]
    public void BasicTest(SelectParallelDelegate<int, int> implementation)
    {
        async Task<int> GetValueRandomlyAsync(int v)
        {
            await Task.Delay(_random.Next(delay));
            return v;
        }
        var values = implementation(Enumerable.Range(0, range), GetValueRandomlyAsync, 10).Where(v => v % 2 == 0).OrderBy(v => v).ToBlockingEnumerable();
        Assert.Equal(Enumerable.Range(0, range).Where(v => v % 2 == 0), values);
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public void MaxThreadsTest(SelectParallelDelegate<int, int> implementation)
    {
        const int maxThreads = 5;
        var threads = 0;
        async Task<int> GetValueRandomlyAsync(int v)
        {
            Interlocked.Increment(ref threads);
            if (threads > maxThreads)
                throw new InvalidOperationException("Too many threads");
            await Task.Delay(_random.Next(delay));
            Interlocked.Decrement(ref threads);
            return v;
        }
        var values = implementation(Enumerable.Range(0, range), GetValueRandomlyAsync, maxThreads).Where(v => v % 2 == 0).OrderBy(v => v).ToBlockingEnumerable();
        Assert.Equal(Enumerable.Range(0, range).Where(v => v % 2 == 0), values);
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public void MinThreadsTest(SelectParallelDelegate<int, int> implementation)
    {
        const int maxThreads = 3;
        var threads = 0;
        var flag = false;
        async Task<int> GetValueRandomlyAsync(int v)
        {
            Interlocked.Increment(ref threads);
            if (threads >= maxThreads)
                flag = true;
            await Task.Delay(_random.Next(delay));
            Interlocked.Decrement(ref threads);
            return v;
        }
        var values = implementation(Enumerable.Range(0, range), GetValueRandomlyAsync, maxThreads).Where(v => v % 2 == 0).OrderBy(v => v).ToBlockingEnumerable();
        Assert.Equal(Enumerable.Range(0, range).Where(v => v % 2 == 0), values);
        Assert.True(flag);
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task FastFirstResultTest(SelectParallelDelegate<int, int> implementation)
    {
        async Task<int> GetValueAsync(int v, int delay)
        {
            await Task.Delay(delay);
            return v;
        }
        const int range = 3;
        const int delay = 2000;
        var values = implementation(Enumerable.Range(0, range), v => GetValueAsync(v, (v + 1) * delay), range);
        var firstValueTask = values.FirstAsync().AsTask();
        var completedTask = await Task.WhenAny(Task.Delay(delay * 2 - 1), firstValueTask);
        Assert.Equal(0, await firstValueTask);
        Assert.Equal(firstValueTask, completedTask);
    }
}