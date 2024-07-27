# SelectParallel Project

This project explores several implementations of `Select` on a collection with a specified maximum degree of parallelism. The project includes:

- An implementation library with the `Extensions` class
- A console app for playground purposes
- A test project using xUnit for requirement verification

## Requirements

1. **Signature** - `IAsyncEnumerable<R> SelectParallelDelegate<T, R>(IEnumerable<T> source, Func<T, Task<R>> body, int maxDegreeOfParallelism)`. `IAsyncEnumerable` makes most sense for async context, as different results will be ready in different order, especially due to `maxDegreeOfParallelism`.
2. **Accuracy of the Result** - Results should be accurate as in LINQ `Select`. Note: the order of output elements is not guaranteed, for the purpose of exploring different implementations. `SelectParallelDataFlow` is the only implementation presented which guarantees the order of production. It is an advantage in cases where order needs to be preserved, but a disadvantage in many cases when an order can be enforced later independently of the input when the tasks vary in complexity.
3. **Max Degree of Parallelism** - No more than `N` items should be making progress at any time.
4. **Utilization of Max Degree of Parallelism** - At some point, exactly `N` items should be making progress. This ensures the implementation is efficient, especially compared to single-threaded versions.
5. **First Result Returned as Soon as Ready** - The first result should be returned as soon as it is ready, verified by unit tests with delayed tasks. This ensures efficiency over naive `Task.WhenAll` implementations.

## Implementations

### SelectParallelSemaphoreWhenAny
A naive implementation using `SemaphoreSlim` for concurrency control and `Task.WhenAny` for returning results when ready. The downside is the inefficiency of `Task.WhenAny`, leading to at least O(N^2) time complexity.

### SelectParallelInterleaved
Implementation based on Stephen Toub’s blog post from 2012. [Link](https://devblogs.microsoft.com/pfxteam/processing-tasks-as-they-complete/) It smarly utilizes an array of `TaskCompletionSource` which is supplied with real results as soon as tasks get completed using `ContinueWith`, pulling the best of async and callback worlds.

### SelectParallelDataFlow
Utilizes the Dataflow API introduced in .NET 6.0. [Learn more](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/dataflow-task-parallel-library) It is a modern and efficient solution which guarantees the order of production of the outputs. In the unit tests the implementation performed by best.

### SelectParallelQuery
Uses PLINQ with blocking `Select` to ensure `WithDegreeOfParallelism` is effective. Blocking version is used as in async version the rule 2 is not satisfied as explained [here](https://devblogs.microsoft.com/pfxteam/paralleloptions-maxdegreeofparallelism-vs-plinqs-withdegreeofparallelism/). The fakeness of the IAsyncEnumerable becomes apparent in the test `FastFirstResultTest` which verifies only the first ready result - a task with 2 seconds delay, while the PLINQ query processes all the tasks, taking the time of the longest task (6 seconds) to complete.

### SelectParallelForEach
A bonus straightforward implementation for cases where capturing the result is not necessary, only ensuring the max degree of parallelism.

## Try Your Own Implementation

1. Add your implementation to `Extensions.cs`.
2. Update `SelectTests.cs` with your implementation in `Implementations` static field and verify it against the requirements.
3. Experiment in the console project `Playground`.
4. Compare performance on the unit tests in the Test explorer.

## TODO
- Add benchmarks. Initial unit tests suggest that the Dataflow implementation offers the best performance.