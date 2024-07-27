using SelectParallel;

Console.WriteLine("Hello, World!");
var _random = new Random();
async Task<int> GetValueRandomlyAsync(int v)
{
    await Task.Delay(_random.Next(1000));
    return v;
}

var values = Enumerable.Range(0, 10).ToArray().SelectParallelInterleaved(GetValueRandomlyAsync, 3).Where(v => v % 2 == 0);
await foreach (var value in values)
{
    Console.WriteLine(value);
}