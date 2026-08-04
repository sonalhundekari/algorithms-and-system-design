# Coding Patterns — C#

Runnable C# solutions, organized by pattern. Requires the [.NET SDK](https://dotnet.microsoft.com/download) 8.0 or newer.

## Running

Everything lives in one console project, so there is one command to learn:

```bash
cd CodingPatterns

dotnet run                          # usage + full problem list
dotnet run -- --list                # full problem list
dotnet run -- TwoSum                # run one problem
dotnet run -- ArraysStrings/TwoSum  # disambiguate with the category
dotnet run -- --all                 # run every problem (smoke test)
```

Names are matched case-insensitively, and a partial name works as long as it is
unambiguous (`dotnet run -- lru` lists both LRU variants instead of guessing).

Build everything, including the two standalone projects below:

```bash
dotnet build CodingPatterns.sln
```

## Layout

```
Coding-Patterns-CSharp/
├── CodingPatterns.sln
├── CodingPatterns/              <- all pattern solutions, one console app
│   ├── Program.cs               <- launcher (finds and invokes Run methods)
│   ├── 01_arrays_strings/
│   ├── 02_trees/
│   ├── 03_graphs/
│   ├── 04_dynamic_programming/
│   ├── 05_greedy/
│   ├── 06_stack_queue/
│   ├── 07_binary_search/
│   ├── 08_linked_lists/
│   └── 09_concurrency/
├── HospitalBookingApi/          <- ASP.NET Core minimal API (own entry point)
└── RedisRateLimiterDemo/        <- needs StackExchange.Redis + a live Redis
```

Each pattern folder carries the same `README.md` as its Python counterpart:
pattern overview, problem list, and a cheat sheet.

## How a solution file is wired up

A single project can only have one `Main`, so each problem instead exposes its
demo as a **`public static Run()`**:

```csharp
namespace CodingPatterns.ArraysStrings;   // namespace == pattern folder

public class TwoSum
{
    public int[] Solve(int[] nums, int target) { ... }

    // ---- Tests ----
    public static void Run()
    {
        var sol = new TwoSum();
        Console.WriteLine(string.Join(",", sol.Solve(new[] { 2, 7, 11, 15 }, 9))); // 0,1
    }
}
```

`Program.cs` finds every such method by reflection, so **adding a new problem
needs no registration** — drop a `.cs` file into a pattern folder, give it the
folder's namespace and a `public static Run()`, and it shows up in `--list`.

`Run()` may also take `string[]` (arguments after the problem name are forwarded
to it) or return `Task` for async demos.

## The two standalone projects

These are separated out so that `CodingPatterns` itself builds and runs with no
external dependencies whatsoever.

**HospitalBookingApi** — an ASP.NET Core minimal API. It uses top-level
statements, so it owns its own entry point and needs the Web SDK.

```bash
cd HospitalBookingApi
dotnet run
# then, against the port it prints:
curl -X POST http://localhost:5000/appointments \
     -H 'Content-Type: application/json' \
     -d '{"doctorId":7,"date":"2026-08-10"}'
```

**RedisRateLimiterDemo** — a distributed token bucket backed by Redis. Needs
`StackExchange.Redis` (restored automatically) *and* a Redis reachable on
`localhost:6379`:

```bash
docker run -p 6379:6379 redis    # in another terminal
cd RedisRateLimiterDemo
dotnet run
```
