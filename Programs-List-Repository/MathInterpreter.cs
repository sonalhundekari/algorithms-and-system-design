// Math Interpreter with Function Definitions
// Difficulty: Medium base, Hard follow-ups
// Pattern: state machine over a command stream + compile each body to a
//          composable closed form (a stack of frames once nesting is allowed)
//
// Problem: one global integer x, initially 0, and a list of commands.
//
//     ADD k       x += k
//     MUL k       x *= k
//     FUN name    begin defining a function -- the body does NOT run
//     END         end the current definition
//     INV name    apply a previously defined function to x
//
// A body holds ADD, MUL, and calls to earlier functions. No recursion, no
// nested definitions, every INV target is already defined.
//
//     ADD 2  MUL 3            -> x = 6
//     FUN f  ADD 1  MUL 2  END-> x = 6 still; defining executes nothing
//     INV f                   -> x = (6 + 1) * 2 = 14
//
// THE OBSERVATION THE PROBLEM IS BUILT ON. A body made of ADD and MUL is an
// affine map:
//
//     ADD k  ->  x -> 1*x + k
//     MUL k  ->  x -> k*x + 0
//
// and affine maps are closed under composition. Applying (a1,b1) then (a2,b2):
//
//     a2*(a1*x + b1) + b2  =  (a2*a1)*x + (a2*b1 + b2)
//
// So a whole function -- however long, however many other functions it calls --
// collapses to ONE pair (a, b), computed as you read the body. INV is then two
// multiplies and an add, no matter what is inside. Storing bodies and replaying
// them is the obvious solution and it is quadratic at best:
//
//     FUN f0  ADD 1  END
//     FUN f1  INV f0  INV f0  END
//     FUN f2  INV f1  INV f1  END        ... 3n commands
//
// f_n replays 2^n operations. Compilation makes it 4n commands, O(1) each. The
// Run() block below times this at n = 20: 3.1 million replayed operations
// against 84 compiled ones, same answer.
//
// The unifying trick in the code: every command produces an Affine `step`, and
// there are exactly two things to do with it --
//
//     defining ?  fold it into the open body      (compose)
//     else     ?  fold it into x                  (apply)
//
// which is why Execute() is one switch and no special cases.
//
// WHAT TO ASK BEFORE WRITING CODE. The spec is silent on all of these, and each
// one changes the answer:
//
//   1. Redefinition of a name?         RedefinitionPolicy. Reject by default;
//                                      Shadow gives EARLY binding, see below.
//   2. Overflow?                       Arithmetic. Exact (BigInteger) or
//                                      Wrapping64 (mod 2^64, like C/Java long).
//   3. Nested definitions?             Off by default per the spec; the frame
//                                      stack supports them with no new logic.
//   4. Malformed input?                Every violation throws with the command
//                                      index -- "guaranteed valid" is a promise
//                                      about the tests, not about production.
//
// Time:  O(n) commands, O(1) arithmetic ops each (fixed width)
// Space: O(f) for f functions, plus O(depth) frames
//
// Related: DecodeString (same stack-of-frames shape), CourseSchedule (the
// "defined earlier" guarantee is a topological order handed to you for free).

using System.Diagnostics;
using System.Numerics;

namespace CodingPatterns.StackQueue;

/// <summary>What to do when a FUN reuses a name that is already defined.</summary>
public enum RedefinitionPolicy
{
    /// <summary>Throw. The spec never says redefinition is legal, so do not invent semantics.</summary>
    Reject,

    /// <summary>
    /// Replace the binding. Callers compiled EARLIER keep the old body, because
    /// they baked its coefficients in -- early binding. See the Run() demo.
    /// </summary>
    Shadow,
}

/// <summary>How arithmetic behaves. The spec says "integer" and stops there.</summary>
public enum Arithmetic
{
    /// <summary>Arbitrary precision. Never overflows, never lies.</summary>
    Exact,

    /// <summary>Wrap to 64 bits after every operation, i.e. work in Z/2^64 (C, Java, C# long).</summary>
    Wrapping64,
}

/// <summary>Any malformed program. Carries the index of the offending command.</summary>
public sealed class MathInterpreterException : Exception
{
    public MathInterpreterException(int commandIndex, string message)
        : base(commandIndex >= 0 ? $"command {commandIndex}: {message}" : message)
        => CommandIndex = commandIndex;

    /// <summary>Zero-based position in the command list, or -1 for whole-program errors.</summary>
    public int CommandIndex { get; }
}

/// <summary>
/// The map <c>x -> A*x + B</c>. Every command, every function body, and every
/// composition of them is one of these -- that is the entire solution.
/// </summary>
public readonly record struct Affine(BigInteger A, BigInteger B)
{
    public static readonly Affine Identity = new(BigInteger.One, BigInteger.Zero);

    public static Affine Add(BigInteger k) => new(BigInteger.One, k);

    public static Affine Mul(BigInteger k) => new(k, BigInteger.Zero);

    public BigInteger Apply(BigInteger x) => A * x + B;

    /// <summary>This map, then <paramref name="next"/>: <c>next(this(x))</c>.</summary>
    public Affine Then(Affine next) => new(next.A * A, next.A * B + next.B);

    public override string ToString()
    {
        if (A.IsZero)
            return B.ToString();

        string ax = A.IsOne ? "x" : A == BigInteger.MinusOne ? "-x" : $"{A}x";
        if (B.IsZero)
            return ax;
        return B.Sign < 0 ? $"{ax} - {BigInteger.Abs(B)}" : $"{ax} + {B}";
    }
}

public sealed class MathInterpreter
{
    private enum Op { Add, Mul, Fun, End, Inv }

    private readonly record struct Parsed(Op Op, string Name, BigInteger Value);

    /// <summary>One open FUN. A list is the stack; nesting just pushes another.</summary>
    private sealed class Frame
    {
        public Frame(string name, int openedAt) => (Name, OpenedAt, Body) = (name, openedAt, Affine.Identity);

        public string Name { get; }

        public int OpenedAt { get; }

        public Affine Body { get; set; }
    }

    private static readonly BigInteger Mask64 = (BigInteger.One << 64) - 1;

    private static readonly string[] Reserved = { "ADD", "MUL", "FUN", "END", "INV" };

    private readonly Dictionary<string, Affine> _functions = new(StringComparer.Ordinal);
    private readonly List<Frame> _frames = new();
    private readonly List<string> _trace = new();

    private readonly Arithmetic _arithmetic;
    private readonly RedefinitionPolicy _redefinition;
    private readonly bool _allowNested;
    private readonly BigInteger _start;

    private BigInteger _x;
    private int _index;

    public MathInterpreter(
        BigInteger start = default,
        Arithmetic arithmetic = Arithmetic.Exact,
        RedefinitionPolicy redefinition = RedefinitionPolicy.Reject,
        bool allowNestedDefinitions = false)
    {
        _arithmetic = arithmetic;
        _redefinition = redefinition;
        _allowNested = allowNestedDefinitions;
        _start = Normalize(start);
        _x = _start;
    }

    /// <summary>The global x.</summary>
    public BigInteger X => _x;

    /// <summary>True while a FUN is open, i.e. commands are being recorded rather than run.</summary>
    public bool IsDefining => _frames.Count > 0;

    /// <summary>Every completed function, as its compiled closed form.</summary>
    public IReadOnlyDictionary<string, Affine> Functions => _functions;

    /// <summary>The body compiled so far for the innermost open FUN, or null if none is open.</summary>
    public Affine? Pending => _frames.Count == 0 ? null : _frames[^1].Body;

    /// <summary>One human-readable line per command; what the Run() demo prints.</summary>
    public IReadOnlyList<string> Trace => _trace;

    // ------------------------------------------------------------- the entry

    /// <summary>
    /// Runs one command. ADD/MUL/INV are folded into the open body when a FUN is
    /// open and into x otherwise -- the single branch this whole problem is.
    /// </summary>
    public void Execute(string command)
    {
        int at = _index++;
        var parsed = Parse(command, at);

        switch (parsed.Op)
        {
            case Op.Fun:
                if (_frames.Count > 0 && !_allowNested)
                    throw new MathInterpreterException(
                        at, $"FUN {parsed.Name} inside the definition of '{_frames[^1].Name}'; nested definitions are disabled");
                if (_frames.Any(f => f.Name == parsed.Name))
                    throw new MathInterpreterException(at, $"FUN {parsed.Name} is already open");
                if (_functions.ContainsKey(parsed.Name) && _redefinition == RedefinitionPolicy.Reject)
                    throw new MathInterpreterException(at, $"'{parsed.Name}' is already defined");

                _frames.Add(new Frame(parsed.Name, at));
                _trace.Add($"{command,-16} begin '{parsed.Name}'");
                return;

            case Op.End:
                if (_frames.Count == 0)
                    throw new MathInterpreterException(at, "END without a matching FUN");

                var frame = _frames[^1];
                _frames.RemoveAt(_frames.Count - 1);
                _functions[frame.Name] = frame.Body;             // flat namespace, even when nested
                _trace.Add($"{command,-16} '{frame.Name}' = {frame.Body}");
                return;
        }

        // ADD / MUL / INV. One step, two possible destinations.
        Affine step = parsed.Op switch
        {
            Op.Add => Affine.Add(Normalize(parsed.Value)),
            Op.Mul => Affine.Mul(Normalize(parsed.Value)),
            Op.Inv => Lookup(parsed.Name, at),
            _ => throw new MathInterpreterException(at, $"unhandled op {parsed.Op}"),
        };

        if (_frames.Count > 0)
        {
            var open = _frames[^1];
            open.Body = Normalize(open.Body.Then(step));         // compose: O(1), body length irrelevant
            _trace.Add($"{command,-16} '{open.Name}' so far: {open.Body}");
        }
        else
        {
            _x = Normalize(step.Apply(_x));                      // apply
            _trace.Add($"{command,-16} x = {_x}");
        }
    }

    public void ExecuteAll(IEnumerable<string> commands)
    {
        if (commands is null)
            throw new ArgumentNullException(nameof(commands));

        foreach (var command in commands)
            Execute(command);
    }

    /// <summary>Throws if the program ended mid-definition -- an unterminated FUN is not a no-op, it is a bug.</summary>
    public void AssertComplete()
    {
        if (_frames.Count > 0)
            throw new MathInterpreterException(
                _frames[^1].OpenedAt, $"FUN {_frames[^1].Name} is never closed by an END");
    }

    /// <summary>Back to x = start with no functions defined.</summary>
    public void Reset()
    {
        _functions.Clear();
        _frames.Clear();
        _trace.Clear();
        _x = _start;
        _index = 0;
    }

    /// <summary>Run a whole program and return the final x.</summary>
    public static BigInteger Evaluate(
        IEnumerable<string> commands,
        BigInteger start = default,
        Arithmetic arithmetic = Arithmetic.Exact,
        RedefinitionPolicy redefinition = RedefinitionPolicy.Reject,
        bool allowNestedDefinitions = false)
    {
        var interpreter = new MathInterpreter(start, arithmetic, redefinition, allowNestedDefinitions);
        interpreter.ExecuteAll(commands);
        interpreter.AssertComplete();
        return interpreter.X;
    }

    /// <summary>Splits a multi-line program, dropping blank lines. Convenience for tests and demos.</summary>
    public static List<string> Lines(string program) => (program ?? string.Empty)
        .Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 0)
        .ToList();

    // -------------------------------------------------------------- plumbing

    private Affine Lookup(string name, int at)
    {
        if (_functions.TryGetValue(name, out var compiled))
            return compiled;

        // Distinguish the two ways this fails; "undefined" is a confusing thing
        // to be told about a function whose FUN line you are staring at.
        if (_frames.Any(f => f.Name == name))
            throw new MathInterpreterException(
                at, $"INV {name} inside its own definition; recursion is not supported (an affine map cannot express it)");

        throw new MathInterpreterException(at, $"INV {name}: no function named '{name}' has been defined");
    }

    private BigInteger Normalize(BigInteger value) => _arithmetic switch
    {
        Arithmetic.Exact => value,
        // v & Mask64 takes the low 64 bits (BigInteger bitwise ops are two's
        // complement), then the cast reinterprets them as signed.
        Arithmetic.Wrapping64 => unchecked((long)(ulong)(value & Mask64)),
        _ => throw new ArgumentOutOfRangeException(nameof(_arithmetic)),
    };

    private Affine Normalize(Affine affine) => _arithmetic == Arithmetic.Exact
        ? affine
        : new Affine(Normalize(affine.A), Normalize(affine.B));

    /// <summary>
    /// Tokenizes one command. Operators are case-insensitive; function names are
    /// not, and may not be an operator keyword. Every arity and format error is
    /// caught here so Execute() only ever sees a well-formed instruction.
    /// </summary>
    private static Parsed Parse(string command, int at)
    {
        if (command is null)
            throw new MathInterpreterException(at, "null command");

        var tokens = command.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            throw new MathInterpreterException(at, "blank command");

        string keyword = tokens[0].ToUpperInvariant();

        switch (keyword)
        {
            case "ADD":
            case "MUL":
                if (tokens.Length != 2)
                    throw new MathInterpreterException(at, $"{keyword} takes exactly one integer, got {tokens.Length - 1} argument(s)");
                if (!BigInteger.TryParse(tokens[1], out var value))
                    throw new MathInterpreterException(at, $"{keyword} {tokens[1]}: '{tokens[1]}' is not an integer");
                return new Parsed(keyword == "ADD" ? Op.Add : Op.Mul, null, value);

            case "FUN":
            case "INV":
                if (tokens.Length != 2)
                    throw new MathInterpreterException(at, $"{keyword} takes exactly one name, got {tokens.Length - 1} argument(s)");
                if (Reserved.Contains(tokens[1].ToUpperInvariant()))
                    throw new MathInterpreterException(at, $"'{tokens[1]}' is a keyword and cannot be a function name");
                return new Parsed(keyword == "FUN" ? Op.Fun : Op.Inv, tokens[1], BigInteger.Zero);

            case "END":
                if (tokens.Length != 1)
                    throw new MathInterpreterException(at, "END takes no arguments");
                return new Parsed(Op.End, null, BigInteger.Zero);

            default:
                throw new MathInterpreterException(at, $"unknown command '{tokens[0]}'");
        }
    }

    // ------------------------------------------------------------- reference
    //
    // The solution most people write first: keep each body as a list of steps
    // and replay it on every INV. It is the right thing to have in the test
    // harness -- it shares no arithmetic with the compiler, so agreeing with it
    // is real evidence -- and the wrong thing to ship, because a call tree of
    // depth d costs O(2^d).
    //
    // One subtlety it must copy: an INV inside a body is resolved to a body
    // INDEX at definition time, not to a name at call time. That is early
    // binding, and it is what compiling coefficients implicitly does. Resolve by
    // name instead and a later redefinition retroactively changes what old
    // functions do -- a genuinely different language.

    public static class Reference
    {
        private readonly record struct Step(Op Op, BigInteger Value, int Target);

        public static BigInteger Evaluate(
            IEnumerable<string> commands,
            BigInteger start = default,
            Arithmetic arithmetic = Arithmetic.Exact,
            RedefinitionPolicy redefinition = RedefinitionPolicy.Reject,
            bool allowNestedDefinitions = false)
            => Evaluate(commands, out _, start, arithmetic, redefinition, allowNestedDefinitions);

        /// <summary>
        /// <paramref name="steps"/> counts primitive operations actually executed
        /// -- the number the compiled interpreter never pays.
        /// </summary>
        public static BigInteger Evaluate(
            IEnumerable<string> commands,
            out long steps,
            BigInteger start = default,
            Arithmetic arithmetic = Arithmetic.Exact,
            RedefinitionPolicy redefinition = RedefinitionPolicy.Reject,
            bool allowNestedDefinitions = false)
        {
            var bodies = new List<List<Step>>();                 // one per completed or open definition
            var latest = new Dictionary<string, int>(StringComparer.Ordinal);
            var open = new List<(string Name, int Body, int At)>();
            var main = new List<Step>();
            int at = -1;

            foreach (var command in commands ?? throw new ArgumentNullException(nameof(commands)))
            {
                var parsed = Parse(command, ++at);
                var target = open.Count > 0 ? bodies[open[^1].Body] : main;

                switch (parsed.Op)
                {
                    case Op.Fun:
                        if (open.Count > 0 && !allowNestedDefinitions)
                            throw new MathInterpreterException(at, $"FUN {parsed.Name} inside the definition of '{open[^1].Name}'; nested definitions are disabled");
                        if (open.Any(f => f.Name == parsed.Name))
                            throw new MathInterpreterException(at, $"FUN {parsed.Name} is already open");
                        if (latest.ContainsKey(parsed.Name) && redefinition == RedefinitionPolicy.Reject)
                            throw new MathInterpreterException(at, $"'{parsed.Name}' is already defined");

                        bodies.Add(new List<Step>());
                        open.Add((parsed.Name, bodies.Count - 1, at));
                        break;

                    case Op.End:
                        if (open.Count == 0)
                            throw new MathInterpreterException(at, "END without a matching FUN");
                        latest[open[^1].Name] = open[^1].Body;
                        open.RemoveAt(open.Count - 1);
                        break;

                    case Op.Add:
                    case Op.Mul:
                        target.Add(new Step(parsed.Op, parsed.Value, -1));
                        break;

                    case Op.Inv:
                        if (!latest.TryGetValue(parsed.Name, out int body))
                            throw new MathInterpreterException(at, $"INV {parsed.Name}: no function named '{parsed.Name}' has been defined");
                        target.Add(new Step(Op.Inv, BigInteger.Zero, body));   // resolved NOW: early binding
                        break;
                }
            }

            if (open.Count > 0)
                throw new MathInterpreterException(open[^1].At, $"FUN {open[^1].Name} is never closed by an END");

            BigInteger x = Normalize(start, arithmetic);
            long executed = 0;

            void RunBody(List<Step> body)
            {
                foreach (var step in body)
                {
                    executed++;
                    switch (step.Op)
                    {
                        case Op.Add: x = Normalize(x + step.Value, arithmetic); break;
                        case Op.Mul: x = Normalize(x * step.Value, arithmetic); break;
                        case Op.Inv: RunBody(bodies[step.Target]); break;
                    }
                }
            }

            RunBody(main);
            steps = executed;
            return x;
        }

        private static BigInteger Normalize(BigInteger value, Arithmetic arithmetic) => arithmetic == Arithmetic.Exact
            ? value
            : unchecked((long)(ulong)(value & Mask64));
    }

    // ---------------------------------------------------------------- tests

    public static void Main()
    {
        Console.WriteLine("== a program, traced ==");

        var program = Lines(@"
            ADD 2
            MUL 3
            FUN f
              ADD 1
              MUL 2
            END
            INV f
            ADD 5
            FUN g
              INV f
              INV f
            END
            INV g
            MUL 0
            INV g");

        var interpreter = new MathInterpreter();
        interpreter.ExecuteAll(program);
        interpreter.AssertComplete();

        foreach (var line in interpreter.Trace)
            Console.WriteLine("  " + line);

        Console.WriteLine($"  final x = {interpreter.X} (expect 6)");
        Console.WriteLine($"  compiled: f = {interpreter.Functions["f"]}, g = {interpreter.Functions["g"]}");
        Console.WriteLine("  g is two calls to f, but it is STORED as one pair -- 'INV g' is a multiply and an add,");
        Console.WriteLine("  and it would still be a multiply and an add if g's body were a million commands.");

        Console.WriteLine();
        Console.WriteLine("== defining is not executing ==");

        var body = Lines("ADD 7\nFUN f\nMUL 1000\nADD 1000\nEND");
        var inert = new MathInterpreter();
        inert.ExecuteAll(body);
        Console.WriteLine($"  after 'ADD 7' then a body full of MUL 1000: x = {inert.X} (expect 7)");
        Console.WriteLine($"  the body went somewhere -- f = {inert.Functions["f"]} -- it just did not run");
        Console.WriteLine($"  invoking it now: x = {Evaluate(body.Append("INV f"))} (expect 8000)");

        Console.WriteLine();
        Console.WriteLine("== empty body, and other degenerate shapes ==");

        Console.WriteLine($"  no commands at all:            x = {Evaluate(Array.Empty<string>())} (expect 0)");
        Console.WriteLine($"  'FUN f END' then 'INV f':      x = {Evaluate(Lines("ADD 9\nFUN f\nEND\nINV f"))} (expect 9; the empty body is the identity)");
        Console.WriteLine($"  invoking the same function 3x: x = {Evaluate(Lines("ADD 1\nFUN d\nMUL 2\nEND\nINV d\nINV d\nINV d"))} (expect 8)");
        Console.WriteLine($"  negative and zero arguments:   x = {Evaluate(Lines("ADD 5\nFUN f\nMUL -3\nADD -1\nEND\nINV f\nMUL 0\nINV f"))} (expect -1)");
        Console.WriteLine($"  a function that only calls:    x = {Evaluate(Lines("ADD 3\nFUN a\nADD 1\nEND\nFUN b\nINV a\nEND\nFUN c\nINV b\nEND\nINV c"))} (expect 4)");

        Console.WriteLine();
        Console.WriteLine("== every way a program can be wrong ==");

        foreach (var (label, bad) in new (string Label, List<string> Program)[]
                 {
                     ("END with no FUN",        Lines("ADD 1\nEND")),
                     ("unterminated FUN",       Lines("FUN f\nADD 1")),
                     ("nested FUN (default)",   Lines("FUN f\nFUN g\nADD 1\nEND\nEND")),
                     ("call before definition", Lines("INV f\nFUN f\nADD 1\nEND")),
                     ("unknown function",       Lines("FUN f\nADD 1\nEND\nINV g")),
                     ("recursive call",         Lines("FUN f\nADD 1\nINV f\nEND")),
                     ("redefinition",           Lines("FUN f\nADD 1\nEND\nFUN f\nADD 2\nEND")),
                     ("ADD with no argument",   Lines("ADD")),
                     ("ADD with two arguments", Lines("ADD 1 2")),
                     ("ADD of a non-integer",   Lines("ADD 1.5")),
                     ("END with an argument",   Lines("FUN f\nEND f")),
                     ("keyword as a name",      Lines("FUN END\nADD 1\nEND")),
                     ("unknown command",        Lines("SUB 4")),
                 })
        {
            try
            {
                Evaluate(bad);
                Console.WriteLine($"  {label,-22} NOT rejected -- bug");
            }
            catch (MathInterpreterException ex)
            {
                Console.WriteLine($"  {label,-22} {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("== nested definitions, when they are allowed ==");

        // The frame stack already handles this; only the guard in FUN changes.
        // Note that 'inner' is globally visible after its END -- one flat
        // namespace, no scoping. Say so out loud; the alternative is a real
        // design decision, not an oversight.
        var nested = new MathInterpreter(allowNestedDefinitions: true);
        nested.ExecuteAll(Lines(@"
            ADD 1
            FUN outer
              ADD 10
              FUN inner
                MUL 5
              END
              INV inner
            END
            INV outer
            INV inner"));
        nested.AssertComplete();
        Console.WriteLine($"  outer = {nested.Functions["outer"]}, inner = {nested.Functions["inner"]}");
        Console.WriteLine($"  x = {nested.X} (expect 275: 1 -> outer -> 55 -> inner -> 275)");
        Console.WriteLine("  'inner' is defined while 'outer' is being defined -- so it exists before outer RUNS,");
        Console.WriteLine("  which is the only reason 'INV inner' inside outer's body can resolve.");

        Console.WriteLine();
        Console.WriteLine("== redefinition binds early ==");

        var shadowed = new MathInterpreter(redefinition: RedefinitionPolicy.Shadow);
        shadowed.ExecuteAll(Lines(@"
            FUN f
              ADD 1
            END
            FUN g
              INV f
            END
            FUN f
              ADD 100
            END
            INV g
            INV f"));
        Console.WriteLine($"  x = {shadowed.X} (expect 101: g still means the OLD f, then the new f adds 100)");
        Console.WriteLine("  g compiled f's coefficients into itself, so redefining f cannot reach back into g.");
        Console.WriteLine("  Late binding -- where g follows the name and picks up the new f -- needs an");
        Console.WriteLine("  indirection table and recompilation, and it is what you MUST have once recursion");
        Console.WriteLine("  is on the table. Worth one sentence to the interviewer either way.");

        Console.WriteLine();
        Console.WriteLine("== why compiling is not a micro-optimization ==");

        // f0 adds 1; every f_k calls f_(k-1) twice. 3n+1 commands, 2^n operations.
        const int depth = 20;
        var doubling = new List<string> { "FUN f0", "ADD 1", "END" };
        for (int k = 1; k <= depth; k++)
            doubling.AddRange(new[] { $"FUN f{k}", $"INV f{k - 1}", $"INV f{k - 1}", "END" });
        doubling.Add($"INV f{depth}");

        var clock = Stopwatch.StartNew();
        var replayed = Reference.Evaluate(doubling, out long steps);
        var replayTime = clock.Elapsed;

        clock.Restart();
        var compiled = Evaluate(doubling);
        var compileTime = clock.Elapsed;

        Console.WriteLine($"  {doubling.Count} commands, call depth {depth}");
        Console.WriteLine($"  replay:   x = {replayed}, {steps:n0} operations executed, {replayTime.TotalMilliseconds:0.0} ms");
        Console.WriteLine($"  compiled: x = {compiled}, {doubling.Count} operations executed, {compileTime.TotalMilliseconds:0.00} ms");
        Console.WriteLine($"  same answer, {steps / (double)doubling.Count:n0}x the work -- and each extra level DOUBLES the");
        Console.WriteLine("  replay while adding four commands to the compile. That is the whole question.");

        Console.WriteLine();
        Console.WriteLine("== the honest caveat: coefficients grow faster than x does ==");

        var huge = new MathInterpreter();
        huge.ExecuteAll(Lines("FUN big\nMUL 1000000000000000000\nMUL 1000000000000000000\nEND"));
        var big = huge.Functions["big"];
        Console.WriteLine($"  'MUL 1e18' twice compiles to a = {big.A.ToString().Length} digits, applied to x = 0 -> {big.Apply(0)}");
        Console.WriteLine("  A replay never computes anything bigger than x itself; compilation computes 10^36 up");
        Console.WriteLine("  front. On a CHECKED 64-bit build that throws on a program the replay handles fine --");
        Console.WriteLine("  the price of doing the work before you know the input.");

        // The escape hatch: if the language wraps instead of throwing, the two
        // agree exactly again. Z/2^64 is a ring, and a2*(a1*x+b1)+b2 =
        // (a2*a1)*x + (a2*b1+b2) is a ring identity -- it does not care that the
        // ring is finite. So compiling is exactly equivalent under C/Java/C#
        // `long` semantics, which is the answer to "what about overflow?".
        long native = unchecked(1L * 3037000500L * 3037000500L);
        var wrapped = Evaluate(Lines("ADD 1\nMUL 3037000500\nMUL 3037000500"), arithmetic: Arithmetic.Wrapping64);
        Console.WriteLine($"  Wrapping64: {wrapped}");
        Console.WriteLine($"  unchecked long in C#: {native}   match: {wrapped == native}");
        Console.WriteLine("  Composition is a ring identity, so it survives wraparound intact. If the spec means");
        Console.WriteLine("  64-bit ints, compiling is not an approximation of the replay -- it is equal to it.");

        Console.WriteLine();
        Console.WriteLine("== randomized cross-checks ==");

        var rng = new Random(20250810);
        bool agrees = true, coefficientsCorrect = true, deadCodeIsFree = true, wrapAgrees = true, wrapFits = true;
        int programs = 0, functionsChecked = 0;

        for (int trial = 0; trial < 3000; trial++)
        {
            var (commands, names, topLevel) = RandomProgram(rng, length: 24);
            programs++;

            // 1. The compiler and the replay must agree, in both arithmetics.
            agrees &= Evaluate(commands) == Reference.Evaluate(commands);

            var wrapCompiled = Evaluate(commands, arithmetic: Arithmetic.Wrapping64);
            var wrapReplayed = Reference.Evaluate(commands, arithmetic: Arithmetic.Wrapping64);
            wrapAgrees &= wrapCompiled == wrapReplayed;
            wrapFits &= wrapCompiled >= long.MinValue && wrapCompiled <= long.MaxValue;

            // 2. A compiled (a, b) must be the function it claims to be:
            //    b = f(0) and a = f(1) - f(0), measured by the replay alone.
            if (names.Count > 0)
            {
                var machine = new MathInterpreter();
                machine.ExecuteAll(commands);
                string name = names[rng.Next(names.Count)];

                var atZero = Reference.Evaluate(commands.Concat(new[] { "MUL 0", $"INV {name}" }));
                var atOne = Reference.Evaluate(commands.Concat(new[] { "MUL 0", "ADD 1", $"INV {name}" }));

                coefficientsCorrect &= machine.Functions[name] == new Affine(atOne - atZero, atZero);
                functionsChecked++;
            }

            // 3. Defining a function nobody calls cannot change the answer --
            //    the "bodies do not execute" rule, stated as a property.
            var withDeadCode = commands.ToList();
            withDeadCode.InsertRange(
                topLevel[rng.Next(topLevel.Count)],
                new[] { "FUN unused_zzz", "MUL 999999", "ADD 12345", "END" });
            deadCodeIsFree &= Evaluate(withDeadCode) == Evaluate(commands);
        }

        Console.WriteLine($"  {$"{programs:n0} random programs, compiled == replayed:",-56}{agrees}");
        Console.WriteLine($"  {$"{functionsChecked:n0} compiled functions match f(0), f(1) - f(0):",-56}{coefficientsCorrect}");
        Console.WriteLine($"  {"inserting an uncalled definition never changes x:",-56}{deadCodeIsFree}");
        Console.WriteLine($"  {"Wrapping64: compiled == replayed:",-56}{wrapAgrees}");
        Console.WriteLine($"  {"Wrapping64: every result fits in a signed 64-bit int:",-56}{wrapFits}");
    }

    /// <summary>
    /// A random VALID program: definitions never nest, INV only names already
    /// closed, and every FUN gets an END. Returns the commands, the functions it
    /// defines, and the indices where a new top-level command may be inserted.
    /// </summary>
    private static (List<string> Commands, List<string> Names, List<int> TopLevel) RandomProgram(Random rng, int length)
    {
        var commands = new List<string>(length);
        var names = new List<string>();
        var topLevel = new List<int> { 0 };
        string defining = null;

        void Emit()
        {
            // MUL magnitudes stay small: nothing here bounds how fast a chain of
            // functions can square its own coefficient.
            int roll = rng.Next(names.Count > 0 ? 3 : 2);
            commands.Add(roll switch
            {
                0 => $"ADD {rng.Next(-9, 10)}",
                1 => $"MUL {rng.Next(-2, 3)}",
                _ => $"INV {names[rng.Next(names.Count)]}",
            });
        }

        while (commands.Count < length)
        {
            if (defining is null)
            {
                topLevel.Add(commands.Count);
                if (rng.Next(4) == 0)
                {
                    defining = $"f{names.Count}";
                    commands.Add($"FUN {defining}");
                    continue;
                }
            }
            else if (rng.Next(3) == 0)
            {
                commands.Add("END");
                names.Add(defining);
                defining = null;
                continue;
            }

            Emit();
        }

        if (defining is not null)
        {
            commands.Add("END");
            names.Add(defining);
        }

        return (commands, names, topLevel);
    }
}

// ---- Notes for the follow-up questions ----
//
// "Allow recursion."
//     Compilation dies here, and saying why is the point: f(x) = f(x) + 1 has no
//     affine closed form, and even a terminating recursion needs a base case,
//     which needs a conditional, which is not affine. Keep the bodies as step
//     lists (the Reference above), resolve calls LATE through the name table so
//     a body can see itself, and interpret with an explicit frame stack rather
//     than the C# stack so a runaway recursion is a clean error instead of a
//     StackOverflowException you cannot catch. You lose O(1) invocation; that is
//     not a regression, it is the cost of a strictly more expressive language.
//
// "Add IF x > 0 ... ELSE ... or a loop."
//     Same wall. The value set stops being one affine map and becomes a piecewise
//     one, and the composition of two piecewise-affine maps has more pieces than
//     either -- the representation grows instead of collapsing. Interpret. What
//     you can still keep is compiling each straight-line BLOCK between branches
//     to one (a, b), which is exactly what a real compiler's basic-block constant
//     folding does.
//
// "Add SUB and DIV."
//     SUB k is ADD -k, free. Integer DIV is not affine -- floor division does not
//     compose -- so it forces interpretation, and it drags in division by zero
//     and the rounding-toward-zero-vs-negative-infinity question. Rational
//     division would compose (track a and b as fractions), but that is a
//     different language than "integer x".
//
// "Several variables: ADD_X, MUL_Y, SWAP."
//     The generalization is the reason to phrase the answer as "affine" in the
//     first place. With k variables the state is a vector, a step is a
//     (k+1)x(k+1) matrix in homogeneous coordinates, and composition is matrix
//     multiplication -- still associative, still precomputable, O(k^3) per
//     compose instead of O(1). Affine-in-one-variable is the k=1 case.
//
// "Everything mod m."
//     Free, and it fixes the coefficient blowup: Z/m is a ring, so reduce a and b
//     after every compose and both stay bounded. Same argument as Wrapping64,
//     which is just m = 2^64.
//
// "The command list is streamed and enormous."
//     Already handled -- Execute() is incremental, holds no history beyond the
//     compiled functions, and the frame stack is O(nesting depth). Drop the trace
//     list and memory is O(#functions).
//
// "Support UNDO."
//     Do not try to invert the affine map: MUL 0 destroys x and no inverse
//     exists. Snapshot instead. x is a single integer, so a stack of previous
//     values is O(1) per command; if function definitions must also be undoable,
//     make the table persistent (copy-on-write) or journal each (name, old value)
//     pair and roll it back.
//
// "How would you test it?"
//     The Run() block is the answer: a replay reference sharing no arithmetic
//     with the compiler, properties rather than fixtures (f(0) and f(1) pin an
//     affine map exactly; dead definitions are free), and an error case for every
//     branch of the parser. The doubling program is the one input that
//     distinguishes an O(1) invocation from a plausible-looking replay.
