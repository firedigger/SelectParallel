# SelectParallel Project

One day at work, I needed to run an async `Task` against an `IEnumerable` of inputs with a limited degree of parallelism due to the potentially large number of entries, all while conveniently working with async/await. I couldn't find an existing method that met these requirements, so I decided to explore the available options in .NET. Initially, I used `SemaphoreSlim` as I didn't need to process any results. However, this project expands on that idea by exploring several implementations of `Select` on a collection with a specified maximum degree of parallelism. The project includes:

- An implementation library with the `Extensions` class
- A console app for playground purposes
- A test project using xUnit for requirement verification. The test project utilizes `Theory` attribute to conveniently run all specified implementations against the same test code, with randomness controlled via a constant seed.

## Requirements

1. **Signature** - `IAsyncEnumerable<R> SelectParallelDelegate<T, R>(IEnumerable<T> source, Func<T, Task<R>> body, int maxDegreeOfParallelism)`. `IAsyncEnumerable` makes most sense for async context, as different results will be ready in different order, especially due to `maxDegreeOfParallelism`.
2. **Accuracy of the Result** - Results should be accurate as in LINQ `Select`. Note: the order of output elements is not guaranteed, for the purpose of exploring different implementations. `SelectParallelDataFlow` is the only implementation presented which guarantees the order of production. It is an advantage in cases where order needs to be preserved, but a disadvantage in many cases when an order can be enforced later independently of the input when the tasks vary in complexity.
3. **Max Degree of Parallelism** - No more than `N` items should be making progress at any time.
4. **Utilization of Max Degree of Parallelism** - At some point, exactly `N` items should be making progress. This ensures the implementation is efficient, especially compared to single-threaded versions.
5. **First Result Returned as Soon as Ready** - The first result should be returned as soon as it is ready, verified by unit tests with delayed tasks. This ensures efficiency over naive `Task.WhenAll` implementations.

Rules are enforced via a unit test project which utilizies .NET parallelization and synchronization mechanism to emulate scenarios and monitor behaviour of the implementations. Of great use was the `Interlocked` class providing atomic `Increment` and `Decrement` methods.

## Implementations

### SelectParallelSemaphoreWhenAny
A naive implementation using `SemaphoreSlim` for concurrency control and `Task.WhenAny` for returning results when ready. The downside is the inefficiency of `Task.WhenAny`, leading to at least O(N^2) time complexity.

### SelectParallelInterleaved
Implementation based on Stephen Toub’s blog post from 2012. [Link](https://devblogs.microsoft.com/pfxteam/processing-tasks-as-they-complete/) It smarly utilizes an array of `TaskCompletionSource` which is supplied with real results as soon as tasks get completed using `ContinueWith`, pulling the best of async and callback worlds.

### SelectParallelDataFlow
Utilizes the `TransformBlock` from Dataflow API introduced in .NET 6.0. [Learn more](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/dataflow-task-parallel-library) It is a modern and efficient solution which guarantees the order of production of the outputs. In the unit tests the implementation performed by best.

### SelectParallelQuery
Uses PLINQ with blocking `Select` to ensure `WithDegreeOfParallelism` is effective. Blocking version is used as in async version the rule 2 is not satisfied as explained [here](https://devblogs.microsoft.com/pfxteam/paralleloptions-maxdegreeofparallelism-vs-plinqs-withdegreeofparallelism/). The blocking version fails rule 5, but otherwise it is the simplest way implementation-wise to enforce a degree of parallelism at the expense of async.

### SelectParallelForEach
A bonus straightforward implementation for cases where capturing the result is not necessary, only ensuring the max degree of parallelism.

## Conclusion

This project was a fun and enlightening experience, allowing me to study various parallel execution tools in modern .NET. While it's enjoyable to implement custom solutions tailored to specific needs, it is generally advisable to use recent solutions provided in the standard library whenever possible. 
`SelectParallelDataFlow` leverages the Dataflow API's parallelization mechanism, which maintains the order of inputs and is optimal when order preservation is important. For scenarios where task execution times vary significantly and you need to enforce a degree of parallelism, `SelectParallelInterleaved` is a viable choice, though its use should be justified by potential performance gains.
Looking forward, it would be beneficial if the .NET Standard Library expands to include methods like those explored in this project.

## Try Your Own Implementation

1. Add your implementation to `Extensions.cs`.
2. Update `SelectTests.cs` with your implementation in `Implementations` static field and verify it against the requirements.
3. Experiment in the console project `Playground`.
4. Compare performance on the unit tests in the Test explorer.
5. Feel free to propose your own implementation in a pull request if you believe it adds a valid alternative.

## TODO
- Add benchmarks using `BenchmarkDotNet`.
- Consider what happens if one of the tasks throws an exception.
- Use `CancellationToken` in the implementations.