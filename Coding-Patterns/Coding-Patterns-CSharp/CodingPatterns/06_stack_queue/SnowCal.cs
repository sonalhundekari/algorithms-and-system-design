// SnowCal Language Interpreter
// Difficulty: Medium
// Pattern: one pass over a COMPLETE program; compile each function body to an
//          affine map instead of storing and replaying it
//
// Problem: SnowCal keeps exactly one integer x in memory, starting at 0. A whole
// program arrives as one list of instruction lines:
//
//     ADD Y       x += Y
//     MUL Y       x *= Y
//     FUN F       begin defining function F -- the body does NOT run
//     END         close the current definition
//     INV F       invoke F, applying its body to x
//
// Guarantees the prompt hands you: names are unique, definitions never nest, a
// function may be defined and never called, and every INV target already exists.
// 1 <= program.Count <= 10,000 and |Y| <= 1e9.
//
//     ["MUL 2", "ADD 3"]                                          -> 3
//     ["FUN INCREMENT", "ADD 1", "END", "INV INCREMENT",
//      "MUL 2", "ADD 3"]                                          -> 5
//
// WHY THIS FILE EXISTS NEXT TO MathInterpreter.cs. Same language, same algebra,
// different INPUT SHAPE -- and the shape is the interface question. MathInterpreter
// is built for a STREAM: a stateful object, `Execute(string)` one command at a
// time, with X / IsDefining / Pending / Trace exposed so a caller can look at the
// machine between commands. That is the right design when commands arrive one by
// one and someone may ask "what is x right now?" mid-definition.
//
// SnowCal hands you the entire list up front, and three things collapse:
//
//   1. The public interface is a PURE FUNCTION -- Evaluate(program) -> x. No
//      object to construct, no lifetime, no partially-run machine to observe.
//      IsDefining and Pending stop being API and become locals.
//   2. The parser state is a dictionary plus ONE optional open frame. The stream
//      version keeps a List<Frame> because it optionally allows nesting; SnowCal
//      forbids nesting, so that stack never exceeds depth 1 -- so it is not a
//      stack, it is a nullable.
//   3. "Unterminated FUN" becomes a check at the end rather than a state you must
//      keep answering questions about. A stream has no natural end, which is
//      exactly why MathInterpreter needs a separate AssertComplete().
//
// Batch input also PERMITS a pre-pass over the list -- and nothing here needs
// one. "Every INV target is already defined" means the definition order is a
// topological order handed to you for free, so one left-to-right pass suffices.
// Say that out loud rather than reaching for two passes on reflex.
//
// THE OBSERVATION THE PROBLEM IS BUILT ON. A body of ADD and MUL is an affine map
// x -> a*x + b, and affine maps compose:
//
//     ADD k -> (1, k)     MUL k -> (k, 0)
//     (a1,b1) then (a2,b2)  =  (a2*a1, a2*b1 + b2)
//
// So a function -- however long, however many other functions it calls -- collapses
// to ONE pair (a, b) computed as you read the body, and INV costs two multiplies
// and an add. Storing bodies and replaying them on each INV is the obvious
// solution and it is exponential: f0 adds 1, each f_k invokes f_(k-1) twice, so
// 3n+1 commands replay 2^n operations. Run() measures both at n = 18.
//
// The single branch this problem reduces to: every command produces a step, and
// there are two things to do with it --
//
//     a definition is open ?  compose it into that body
//     otherwise            ?  apply it to x
//
// WHAT TO ASK BEFORE WRITING CODE. The guarantees above are promises about the
// TESTS, not about production, so every violation here throws with the offending
// line index. The one question the prompt genuinely leaves open is overflow:
// "integer" is not a width. |Y| <= 1e9 with 10,000 commands overflows 64 bits in
// three multiplies, so Evaluate works in BigInteger and EvaluateInt64 tells you
// whether the answer fits. See the caveat at the bottom of Run(): compiled
// coefficients grow faster than x itself does.
//
// Time:  O(n) lines, O(1) arithmetic ops each   Space: O(f) for f functions
//
// Related: MathInterpreter (same language, streaming interface, plus nesting and
// redefinition policies -- this file reuses its Affine and its replay reference),
// CommandCalculator (the same algebra with anonymous nesting instead of names),
// CourseSchedule ("defined earlier" is a topological order you were given free).

using System.Diagnostics;
using System.Numerics;

namespace CodingPatterns.StackQueue;

/// <summary>Any malformed SnowCal program. Carries the index of the offending line.</summary>
public sealed class SnowCalException : Exception
{
    public SnowCalException(int lineIndex, string message)
        : base(lineIndex >= 0 ? $"line {lineIndex}: {message}" : message)
        => LineIndex = lineIndex;

    /// <summary>Zero-based position in the program list, or -1 for whole-program errors.</summary>
    public int LineIndex { get; }
}

public static class SnowCal
{
    private enum Op { Add, Mul, Fun, End, Inv }

    private static readonly string[] Reserved = { "ADD", "MUL", "FUN", "END", "INV" };

    // ------------------------------------------------------------- the entry

    /// <summary>Runs a whole SnowCal program and returns the final x.</summary>
    public static BigInteger Evaluate(IReadOnlyList<string> program) => Evaluate(program, out _);

    /// <summary>
    /// Runs a whole SnowCal program. <paramref name="functions"/> receives every
    /// completed function as its compiled closed form -- useful for tests, and the
    /// thing a replay-based solution can never hand you.
    /// </summary>
    public static BigInteger Evaluate(IReadOnlyList<string> program, out IReadOnlyDictionary<string, Affine> functions)
    {
        if (program is null)
            throw new ArgumentNullException(nameof(program));

        var compiled = new Dictionary<string, Affine>(StringComparer.Ordinal);
        BigInteger x = BigInteger.Zero;

        // The entire parser state. At most ONE definition is ever open, because
        // SnowCal has no nested functions -- so this is a nullable, not a stack.
        string openName = null;
        int openAt = -1;
        Affine openBody = Affine.Identity;

        for (int at = 0; at < program.Count; at++)
        {
            var (op, name, value) = Parse(program[at], at);

            switch (op)
            {
                case Op.Fun:
                    if (openName is not null)
                        throw new SnowCalException(
                            at, $"FUN {name} inside the definition of '{openName}'; SnowCal has no nested functions");
                    if (compiled.ContainsKey(name))
                        throw new SnowCalException(at, $"'{name}' is already defined; SnowCal function names are unique");

                    (openName, openAt, openBody) = (name, at, Affine.Identity);
                    continue;

                case Op.End:
                    if (openName is null)
                        throw new SnowCalException(at, "END without a matching FUN");

                    compiled[openName] = openBody;                  // never called is fine: it just sits here
                    openName = null;
                    continue;
            }

            // ADD / MUL / INV. One step, two possible destinations.
            Affine step = op switch
            {
                Op.Add => Affine.Add(value),
                Op.Mul => Affine.Mul(value),
                Op.Inv => Lookup(compiled, openName, name, at),
                _ => throw new SnowCalException(at, $"unhandled op {op}"),
            };

            if (openName is not null)
                openBody = openBody.Then(step);                     // defining: compose, O(1) whatever the body
            else
                x = step.Apply(x);                                  // running: apply
        }

        if (openName is not null)
            throw new SnowCalException(openAt, $"FUN {openName} is never closed by an END");

        functions = compiled;
        return x;
    }

    /// <summary>
    /// The final x as a 64-bit integer, throwing if it does not fit. Note what
    /// this does and does not promise: it validates the ANSWER, not the
    /// intermediates. A genuinely long-typed interpreter can overflow partway
    /// through a program whose answer is small -- "MUL 1000000000" three times
    /// then "MUL 0" ends at 0 having passed through 10^27.
    /// </summary>
    public static long EvaluateInt64(IReadOnlyList<string> program)
    {
        var x = Evaluate(program);
        if (x < long.MinValue || x > long.MaxValue)
            throw new SnowCalException(-1, $"final x = {x} does not fit in 64 bits");
        return (long)x;
    }

    /// <summary>Splits a multi-line program, dropping blank lines. Convenience for tests and demos.</summary>
    public static List<string> Lines(string program) => (program ?? string.Empty)
        .Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 0)
        .ToList();

    // -------------------------------------------------------------- plumbing

    private static Affine Lookup(Dictionary<string, Affine> compiled, string openName, string name, int at)
    {
        if (compiled.TryGetValue(name, out var body))
            return body;

        // Distinguish the two ways this fails. "undefined" is a confusing thing to
        // be told about a function whose FUN line is on screen above you.
        if (name == openName)
            throw new SnowCalException(
                at, $"INV {name} inside its own definition; SnowCal has no recursion (an affine map cannot express it)");

        throw new SnowCalException(at, $"INV {name}: no function named '{name}' has been defined");
    }

    /// <summary>
    /// Tokenizes one line. Keywords are case-insensitive; function names are not,
    /// and may not be a keyword. Every arity and format error is caught here, so
    /// the main loop only ever sees a well-formed instruction.
    /// </summary>
    private static (Op Op, string Name, BigInteger Value) Parse(string line, int at)
    {
        if (line is null)
            throw new SnowCalException(at, "null line");

        var tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            throw new SnowCalException(at, "blank line");

        string keyword = tokens[0].ToUpperInvariant();

        switch (keyword)
        {
            case "ADD":
            case "MUL":
                if (tokens.Length != 2)
                    throw new SnowCalException(at, $"{keyword} takes exactly one integer, got {tokens.Length - 1} argument(s)");
                if (!BigInteger.TryParse(tokens[1], out var value))
                    throw new SnowCalException(at, $"{keyword} {tokens[1]}: '{tokens[1]}' is not an integer");
                return (keyword == "ADD" ? Op.Add : Op.Mul, null, value);

            case "FUN":
            case "INV":
                if (tokens.Length != 2)
                    throw new SnowCalException(at, $"{keyword} takes exactly one name, got {tokens.Length - 1} argument(s)");
                if (Reserved.Contains(tokens[1].ToUpperInvariant()))
                    throw new SnowCalException(at, $"'{tokens[1]}' is a keyword and cannot be a function name");
                return (keyword == "FUN" ? Op.Fun : Op.Inv, tokens[1], BigInteger.Zero);

            case "END":
                if (tokens.Length != 1)
                    throw new SnowCalException(at, "END takes no arguments");
                return (Op.End, null, BigInteger.Zero);

            default:
                throw new SnowCalException(at, $"unknown command '{tokens[0]}'");
        }
    }

    // ---------------------------------------------------------------- tests

    public static void Run()
    {
        Console.WriteLine("== the three sample cases ==");

        var samples = new (string[] Program, int Expected, string Note)[]
        {
            (new[] { "MUL 2", "ADD 3" }, 3, "0*2 = 0, then 0+3"),
            (new[] { "FUN INCREMENT", "ADD 1", "END", "INV INCREMENT", "MUL 2", "ADD 3" },
             5, "defining runs nothing: 0 -> 1 -> 2 -> 5"),
            (new[] { "FUN INCREMENT", "ADD 1", "END", "FUN INCREMENT2", "ADD 1", "MUL 2", "END",
                     "MUL 2", "INV INCREMENT2", "ADD 3", "INV INCREMENT" },
             6, "0*2 = 0, INCREMENT2 -> 2, +3 = 5, INCREMENT -> 6"),
        };

        foreach (var (prog, expected, note) in samples)
        {
            var got = Evaluate(prog);
            Debug.Assert(got == expected);
            Console.WriteLine($"  x = {got,-3} (expect {expected})  {note}");
        }

        Console.WriteLine();
        Console.WriteLine("== what a body compiles to ==");

        var program = Lines(@"
            FUN INCREMENT
              ADD 1
            END
            FUN INCREMENT2
              ADD 1
              MUL 2
            END
            FUN UNUSED
              MUL 7
              ADD 4
            END
            MUL 2
            INV INCREMENT2
            ADD 3
            INV INCREMENT");

        var x = Evaluate(program, out var functions);
        foreach (var (name, body) in functions.OrderBy(f => f.Key, StringComparer.Ordinal))
            Console.WriteLine($"  {name,-12} = {body}");
        Console.WriteLine($"  x = {x}");
        Console.WriteLine("  UNUSED is compiled and never applied -- 'define but never call' costs one dictionary entry.");
        Console.WriteLine("  Each body is ONE pair regardless of its length; that is what makes INV O(1).");

        Console.WriteLine();
        Console.WriteLine("== edge cases ==");

        var edges = new (string Label, string Source, BigInteger Expected)[]
        {
            ("MUL before anything",   "MUL 1000000000",                                    0),
            ("MUL 0 erases x",        "ADD 5\nMUL 0\nADD 7",                               7),
            ("negative operands",     "ADD -5\nMUL -3",                                    15),
            ("only a definition",     "FUN f\nADD 1\nEND",                                 0),
            ("empty body",            "FUN f\nEND\nADD 4\nINV f",                          4),
            ("invoked twice",         "FUN f\nADD 1\nMUL 2\nEND\nINV f\nINV f",            6),
            ("function calls one",    "FUN a\nADD 2\nEND\nFUN b\nINV a\nINV a\nEND\nINV b", 4),
            ("call before more defs", "FUN a\nADD 1\nEND\nINV a\nFUN b\nMUL 9\nEND\nINV b", 9),
        };

        foreach (var (label, source, expected) in edges)
        {
            var got = Evaluate(Lines(source));
            Debug.Assert(got == expected, label);
            Console.WriteLine($"  {label,-22} x = {got,-4} (expect {expected})");
        }

        Console.WriteLine();
        Console.WriteLine("== the guarantees are promises about the tests, not about production ==");

        foreach (var (label, bad) in new (string, IReadOnlyList<string>)[]
                 {
                     ("nested definition",  Lines("FUN f\nFUN g\nADD 1\nEND\nEND")),
                     ("unclosed FUN",       Lines("ADD 1\nFUN f\nADD 2")),
                     ("END without FUN",    Lines("ADD 1\nEND")),
                     ("duplicate name",     Lines("FUN f\nADD 1\nEND\nFUN f\nADD 2\nEND")),
                     ("undefined INV",      Lines("INV nope")),
                     ("recursive INV",      Lines("FUN f\nINV f\nEND")),
                     ("forward INV",        Lines("INV f\nFUN f\nADD 1\nEND")),
                     ("unknown command",    Lines("SUB 4")),
                     ("ADD with no value",  Lines("ADD")),
                     ("keyword as a name",  Lines("FUN END\nADD 1\nEND")),
                 })
        {
            try
            {
                Evaluate(bad);
                Console.WriteLine($"  {label,-20} NOT rejected -- bug");
            }
            catch (SnowCalException ex)
            {
                Console.WriteLine($"  {label,-20} {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("== why compiling is not a micro-optimization ==");

        // f0 adds 1; every f_k invokes f_(k-1) twice. 3n+1 commands, 2^n operations.
        const int depth = 18;
        var doubling = new List<string> { "FUN f0", "ADD 1", "END" };
        for (int k = 1; k <= depth; k++)
            doubling.AddRange(new[] { $"FUN f{k}", $"INV f{k - 1}", $"INV f{k - 1}", "END" });
        doubling.Add($"INV f{depth}");

        var clock = Stopwatch.StartNew();
        var replayed = MathInterpreter.Reference.Evaluate(doubling, out long steps);
        var replayTime = clock.Elapsed;

        clock.Restart();
        var compiledAnswer = Evaluate(doubling);
        var compileTime = clock.Elapsed;

        Debug.Assert(replayed == compiledAnswer && compiledAnswer == BigInteger.Pow(2, depth));
        Console.WriteLine($"  {doubling.Count} lines, call depth {depth}");
        Console.WriteLine($"  replay:   x = {replayed}, {steps:n0} operations, {replayTime.TotalMilliseconds:0.0} ms");
        Console.WriteLine($"  compiled: x = {compiledAnswer}, {doubling.Count} operations, {compileTime.TotalMilliseconds:0.00} ms");
        Console.WriteLine("  Every extra level DOUBLES the replay and adds four lines to the compile. At the stated");
        Console.WriteLine("  limit of 10,000 lines a replay can be asked for roughly 2^2500 operations.");

        Console.WriteLine();
        Console.WriteLine("== 'integer' is not a width ==");

        // |Y| <= 1e9 and 10,000 lines: three multiplies leave 64 bits behind.
        var overflowing = Lines("ADD 1\nMUL 1000000000\nMUL 1000000000\nMUL 1000000000");
        Console.WriteLine($"  exact:  x = {Evaluate(overflowing)}");
        try
        {
            Console.WriteLine($"  int64:  x = {EvaluateInt64(overflowing)}");
        }
        catch (SnowCalException ex)
        {
            Console.WriteLine($"  int64:  {ex.Message}");
        }

        // And the sharper version: compilation computes coefficients the replay
        // never touches, so a checked 64-bit build can throw on a program a
        // replay handles fine.
        Evaluate(Lines("FUN big\nMUL 1000000000000000000\nMUL 1000000000000000000\nEND\nADD 0"), out var withBig);
        Console.WriteLine($"  'MUL 1e18' twice compiles to a coefficient of {withBig["big"].A.ToString().Length} digits,");
        Console.WriteLine("  even though applying it to x = 0 gives 0. Compilation does the work before it knows the");
        Console.WriteLine("  input; a replay never computes anything larger than x itself. Worth one sentence out loud.");
        Console.WriteLine("  (If the language WRAPS at 64 bits the two agree exactly again -- composition is a ring");
        Console.WriteLine("  identity, so it survives wraparound. MathInterpreter's Wrapping64 mode demonstrates it.)");

        Console.WriteLine();
        Console.WriteLine("== randomized cross-checks against the replay ==");

        var rng = new Random(20260811);
        bool agreesWithReplay = true, agreesWithStream = true, coefficientsCorrect = true;
        int programs = 0, functionsChecked = 0;

        for (int trial = 0; trial < 3000; trial++)
        {
            var (lines, names) = RandomProgram(rng, length: 24);
            programs++;

            var answer = Evaluate(lines, out var compiledFunctions);

            // 1. The compiler and a replay that shares none of its arithmetic.
            agreesWithReplay &= answer == MathInterpreter.Reference.Evaluate(lines);

            // 2. The batch interface and the streaming one must be the same language.
            agreesWithStream &= answer == MathInterpreter.Evaluate(lines);

            // 3. A compiled (a, b) must be the function it claims to be. Measure it
            //    with the replay alone: b = f(0) and a = f(1) - f(0).
            if (names.Count > 0)
            {
                string name = names[rng.Next(names.Count)];
                var claimed = compiledFunctions[name];

                var body = new List<string>(lines) { $"INV {name}" };
                var atZero = MathInterpreter.Reference.Evaluate(body, start: 0);
                var atOne = MathInterpreter.Reference.Evaluate(body, start: 1);
                var offset = MathInterpreter.Reference.Evaluate(lines, start: 0);   // x before the appended INV

                // f is applied to whatever x the program ended at, so undo that:
                // compare against the claimed map evaluated at the same point.
                coefficientsCorrect &= atZero == claimed.Apply(offset);
                coefficientsCorrect &= atOne - atZero == claimed.A * (MathInterpreter.Reference.Evaluate(lines, start: 1) - offset);
                functionsChecked++;
            }
        }

        Console.WriteLine($"  {programs:n0} random programs, {functionsChecked:n0} compiled bodies checked");
        Console.WriteLine($"  agrees with the replay:            {agreesWithReplay}");
        Console.WriteLine($"  agrees with the streaming version: {agreesWithStream}");
        Console.WriteLine($"  coefficients are the real f:       {coefficientsCorrect}");
        Debug.Assert(agreesWithReplay && agreesWithStream && coefficientsCorrect);

        Console.WriteLine();
        Console.WriteLine("== at the stated limit ==");

        // 10,000 lines, the maximum the prompt allows.
        var big = new List<string>();
        for (int k = 0; k < 1000; k++)
        {
            big.Add($"FUN f{k}");
            big.Add($"ADD {(k % 7) - 3}");
            big.Add(k == 0 ? "MUL 2" : $"INV f{k - 1}");
            big.Add("END");
        }
        while (big.Count < 9_000)
            big.Add($"INV f{big.Count % 1000}");
        big.Add("MUL 0");
        big.Add("ADD 42");

        clock.Restart();
        var final = Evaluate(big);
        Console.WriteLine($"  {big.Count:n0} lines -> x = {final} in {clock.Elapsed.TotalMilliseconds:0.0} ms");
        Debug.Assert(final == 42);
        Console.WriteLine("  A replay of this program is a 1000-deep call chain re-executed 8,000 times.");

        Console.WriteLine();
        Console.WriteLine("All tests passed.");
    }

    /// <summary>
    /// A random VALID SnowCal program: no nesting, unique names, every INV target
    /// already closed. Values stay small so the replay stays cheap.
    /// </summary>
    private static (List<string> Lines, List<string> Names) RandomProgram(Random rng, int length)
    {
        var lines = new List<string>();
        var names = new List<string>();          // closed, therefore invocable
        string openName = null;
        int nextName = 0;

        for (int i = 0; i < length; i++)
        {
            int roll = rng.Next(10);

            if (openName is null && roll < 2)                    // open a definition
            {
                openName = $"f{nextName++}";
                lines.Add($"FUN {openName}");
            }
            else if (openName is not null && roll < 3)           // close the open one
            {
                lines.Add("END");
                names.Add(openName);
                openName = null;
            }
            else if (roll < 5 && names.Count > 0)                // call something already closed
            {
                lines.Add($"INV {names[rng.Next(names.Count)]}");
            }
            else if (roll < 8)
            {
                lines.Add($"MUL {rng.Next(-3, 4)}");
            }
            else
            {
                lines.Add($"ADD {rng.Next(-9, 10)}");
            }
        }

        if (openName is not null)                                // never leave a FUN hanging
        {
            lines.Add("END");
            names.Add(openName);
        }

        return (lines, names);
    }
}
