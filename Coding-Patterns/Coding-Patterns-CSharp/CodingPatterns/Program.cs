// Entry point for the C# solution tree.
//
// Every problem file exposes a `public static void Run()` (or `Run(string[])`,
// or an async `Task Run()`) holding its demo/tests. A single project can only
// have one Main, so this launcher discovers those Run methods by reflection and
// invokes the one you name.
//
//   dotnet run                       -> usage + full problem list
//   dotnet run -- --list             -> full problem list
//   dotnet run -- TwoSum             -> run one problem
//   dotnet run -- ArraysStrings/TwoSum
//   dotnet run -- --all              -> run every problem (smoke test)

using System.Reflection;

namespace CodingPatterns;

public static class Launcher
{
    private sealed record Problem(string Category, string Name, MethodInfo Method)
    {
        public string Key => $"{Category}/{Name}";
    }

    public static async Task<int> Main(string[] args)
    {
        var problems = Discover();

        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            PrintUsage(problems);
            return 0;
        }

        if (args[0] == "--list")
        {
            PrintList(problems);
            return 0;
        }

        if (args[0] == "--all")
            return await RunAll(problems);

        var matches = Resolve(problems, args[0]);
        if (matches.Count == 0)
        {
            Console.Error.WriteLine($"No problem matches '{args[0]}'.");
            Console.Error.WriteLine("Run with --list to see everything available.");
            return 1;
        }
        if (matches.Count > 1)
        {
            Console.Error.WriteLine($"'{args[0]}' is ambiguous. Did you mean:");
            foreach (var m in matches)
                Console.Error.WriteLine($"  {m.Key}");
            return 1;
        }

        await Invoke(matches[0], args.Skip(1).ToArray());
        return 0;
    }

    private static List<Problem> Discover()
    {
        var problems = new List<Problem>();

        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        {
            if (type == typeof(Launcher) || type.Namespace is null)
                continue;

            var run = type.GetMethod(
                "Run",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

            if (run is null || !IsRunnable(run))
                continue;

            // "CodingPatterns.ArraysStrings" -> "ArraysStrings"
            var category = type.Namespace.Split('.').Last();
            problems.Add(new Problem(category, type.Name, run));
        }

        return problems
            .OrderBy(p => p.Category, StringComparer.Ordinal)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsRunnable(MethodInfo run)
    {
        if (run.ReturnType != typeof(void) && run.ReturnType != typeof(Task))
            return false;

        var ps = run.GetParameters();
        return ps.Length == 0 || (ps.Length == 1 && ps[0].ParameterType == typeof(string[]));
    }

    private static List<Problem> Resolve(List<Problem> problems, string query)
    {
        const StringComparison ic = StringComparison.OrdinalIgnoreCase;
        var normalized = query.Replace('\\', '/');

        var exact = problems
            .Where(p => p.Name.Equals(normalized, ic) || p.Key.Equals(normalized, ic))
            .ToList();
        if (exact.Count > 0)
            return exact;

        return problems
            .Where(p => p.Key.Contains(normalized, ic))
            .ToList();
    }

    private static async Task Invoke(Problem problem, string[] forwarded)
    {
        var parameters = problem.Method.GetParameters().Length == 0
            ? Array.Empty<object>()
            : new object[] { forwarded };

        var result = problem.Method.Invoke(null, parameters);
        if (result is Task task)
            await task;
    }

    private static async Task<int> RunAll(List<Problem> problems)
    {
        var failed = new List<string>();

        foreach (var problem in problems)
        {
            Console.WriteLine();
            Console.WriteLine($"===== {problem.Key} =====");
            try
            {
                await Invoke(problem, Array.Empty<string>());
            }
            catch (Exception ex)
            {
                // Unwrap the reflection wrapper so the real failure is readable.
                var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
                Console.WriteLine($"FAILED: {inner.GetType().Name}: {inner.Message}");
                failed.Add(problem.Key);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"===== {problems.Count - failed.Count}/{problems.Count} ran clean =====");
        foreach (var key in failed)
            Console.WriteLine($"  failed: {key}");

        return failed.Count == 0 ? 0 : 1;
    }

    private static void PrintUsage(List<Problem> problems)
    {
        Console.WriteLine("Coding Patterns - C#");
        Console.WriteLine();
        Console.WriteLine("  dotnet run -- <problem>   run one problem (name or Category/Name)");
        Console.WriteLine("  dotnet run -- --list      list every problem");
        Console.WriteLine("  dotnet run -- --all       run every problem");
        Console.WriteLine();
        PrintList(problems);
    }

    private static void PrintList(List<Problem> problems)
    {
        foreach (var group in problems.GroupBy(p => p.Category))
        {
            Console.WriteLine(group.Key);
            foreach (var problem in group)
                Console.WriteLine($"  {problem.Name}");
        }
        Console.WriteLine();
        Console.WriteLine($"{problems.Count} problems.");
    }
}
