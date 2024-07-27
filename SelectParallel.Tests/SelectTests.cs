namespace SelectParallel.Tests;

public class SelectTests
{
    private readonly Random _random = new();
    const int delay = 100;
    const int range = 100;
    public delegate IAsyncEnumerable<R> SelectParallelDelegate<T, R>(IReadOnlyList<T> source, Func<T, Task<R>> body, int maxDegreeOfParallelism);

    public static IEnumerable<object[]> GetImplementations()
    {
        return [[new SelectParallelDelegate<int, int>(Extensions.SelectParallelSemaphoreWhenAny)],
            [new SelectParallelDelegate<int, int>(Extensions.SelectParallelInterleaved)],
            [new SelectParallelDelegate<int, int>(Extensions.SelectParallelDataFlow)]];
    }

    [Theory]
    [MemberData(nameof(GetImplementations))]
    public void BasicTest(SelectParallelDelegate<int, int> implementation)
    {
        async Task<int> GetValueRandomlyAsync(int v)
        {
            await Task.Delay(_random.Next(delay));
            return v;
        }
        var values = implementation(Enumerable.Range(0, range).ToArray(), GetValueRandomlyAsync, 10).Where(v => v % 2 == 0).OrderBy(v => v).ToBlockingEnumerable();
        Assert.Equal(Enumerable.Range(0, range).Where(v => v % 2 == 0), values);
    }

    [Theory]
    [MemberData(nameof(GetImplementations))]
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
        var values = implementation(Enumerable.Range(0, range).ToArray(), GetValueRandomlyAsync, maxThreads).Where(v => v % 2 == 0).OrderBy(v => v).ToBlockingEnumerable();
        Assert.Equal(Enumerable.Range(0, range).Where(v => v % 2 == 0), values);
    }

    [Theory]
    [MemberData(nameof(GetImplementations))]
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
        var values = implementation(Enumerable.Range(0, range).ToArray(), GetValueRandomlyAsync, maxThreads).Where(v => v % 2 == 0).OrderBy(v => v).ToBlockingEnumerable();
        Assert.Equal(Enumerable.Range(0, range).Where(v => v % 2 == 0), values);
        Assert.True(flag);
    }
}