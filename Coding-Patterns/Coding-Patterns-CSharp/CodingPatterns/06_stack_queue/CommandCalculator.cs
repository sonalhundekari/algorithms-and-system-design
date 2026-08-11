// String-Command Calculator (with nested sub-expressions)
// Difficulty: Easy base, Medium follow-up
// Pattern: tokenize -> stack of frames, one per open sub-expression
//
// Problem: a stream of string commands drives one running accumulator.
//
//     ADD k    acc += k
//     SUB k    acc -= k
//     MULT k   acc *= k          (MUL accepted as a synonym)
//     DIV k    acc /= k
//
// Commands apply STRICTLY LEFT TO RIGHT against the accumulator. There is no
// operator precedence -- "ADD 1 ADD 2 MULT 3" is (0+1+2)*3 = 9, not 1+2*3 = 7.
// Confirm this out loud; if the interviewer wants precedence the problem is a
// different one (see the follow-up notes at the bottom).
//
//     ADD 2  MULT 3  SUB 1  DIV 5     ->  2, 6, 5, 1
//
// FOLLOW-UP: an operand may itself be a parenthesized command list, evaluated
// first and used as the operand.
//
//     ADD 2  MULT ( ADD 1 ADD 2 )     ->  2, then 2 * 3 = 6
//
// THE FOUR QUESTIONS TO ASK BEFORE WRITING CODE. Every one of them changes the
// answer, and the prompt is silent on all four.
//
//   1. What does the accumulator START at?   0 is the natural reading, but then
//      any MULT-first sequence is pinned at 0 forever -- 0 is the identity for
//      ADD, not for MULT. Pass `start: 1` for multiplicative-only input, or seed
//      it with the first operand. Do not guess; the trap is silent. Note that
//      `start` moves the OUTER accumulator only: an independent sub-expression
//      always starts at 0, so a MULT-only sub-expression hits the same trap and
//      wants either an ADD in front of it or an inherited seed.
//
//   2. What does DIV mean?                   `Division`. Exact rationals, or
//      integer division -- and if integer, truncating toward zero (C#, Java) or
//      flooring (Python)? "ADD 7 DIV 2 MULT 2" is 7, 6, or 6; "ADD -7 DIV 2" is
//      -3.5, -3, or -4. All three are defensible and they disagree immediately.
//
//   3. What does a sub-expression START at?  `SubExpressionSeed`. Independent
//      (a standalone program, seeded at 0 no matter what the outer accumulator
//      or the outer start is) or inheriting the current accumulator. This is not
//      a detail -- see the note below.
//
//   4. What about malformed input?           Every violation throws with the
//      offending token index. "Assume valid input" is a promise about the tests.
//
// THE SHAPE OF THE SOLUTION. Reading left to right there are exactly two things
// a frame can be waiting for: an operator, or the operand that operator needs.
// So one frame is `(accumulator, pending operator)` and four token kinds move it:
//
//     operator ?  record it as pending           (frame must be idle)
//     number   ?  apply it, clear pending        (frame must be pending)
//     '('      ?  push a new frame               (frame must be pending)
//     ')'      ?  pop, and the popped frame's accumulator IS the operand
//
// The last line is the whole follow-up: a sub-expression is not a special case,
// it is an operand that happens to be computed by the same loop. Recursive
// descent writes the same thing with the CLR stack instead of a list -- shorter
// to write in the room, and it dies on deeply nested input (see Recursive below,
// and the 100k-deep test in Run()).
//
// WHY QUESTION 3 IS THE INTERESTING ONE. Over the rationals every command is an
// affine map acc -> a*acc + b:
//
//     ADD k -> (1, k)    SUB k -> (1, -k)    MULT k -> (k, 0)    DIV k -> (1/k, 0)
//
// and affine maps compose, so with INDEPENDENT sub-expressions a whole program
// -- nesting and all -- is one map acc -> a*acc + b in its starting value. Run()
// verifies that with second differences. Let a sub-expression inherit the
// accumulator instead and "MULT ( ... )" becomes acc * (a*acc + b): quadratic,
// not affine, and the closed form is gone. One policy flag, two different
// languages. (Integer DIV also destroys it -- truncation is not linear.)
//
// Time:  O(n) tokens, O(1) each   Space: O(depth)
//
// Related: DecodeString (same stack of frames), MathInterpreter (the same affine
// observation, used to compile NAMED functions rather than anonymous nesting).

using System.Numerics;
using System.Text;

namespace CodingPatterns.StackQueue;

/// <summary>What DIV means. The prompt says "DIV 4" and stops there.</summary>
public enum Division
{
    /// <summary>Exact rationals. Never rounds, never lies; denominators can grow.</summary>
    Exact,

    /// <summary>Integer division rounding toward zero: -7/2 = -3. C#, Java, Go, Rust.</summary>
    TruncateTowardZero,

    /// <summary>Integer division rounding down: -7/2 = -4. Python, Ruby.</summary>
    Floor,
}

/// <summary>What the accumulator of a nested sub-expression starts at.</summary>
public enum SubExpressionSeed
{
    /// <summary>
    /// Zero -- the sub-expression is a standalone program whose result becomes an
    /// operand, independent of the enclosing accumulator AND of the outer start.
    /// This is what keeps the whole program affine in its start value.
    /// </summary>
    Independent,

    /// <summary>
    /// The enclosing accumulator, so the sub-expression continues the calculation
    /// rather than starting one. Makes MULT/DIV of a sub-expression nonlinear.
    /// </summary>
    InheritsAccumulator,
}

/// <summary>Any malformed program. Carries the index of the offending token.</summary>
public sealed class CalculatorException : Exception
{
    public CalculatorException(int tokenIndex, string message)
        : base(tokenIndex >= 0 ? $"token {tokenIndex}: {message}" : message)
        => TokenIndex = tokenIndex;

    /// <summary>Zero-based position in the token stream, or -1 for whole-program errors.</summary>
    public int TokenIndex { get; }
}

/// <summary>
/// An exact rational, always in lowest terms with a positive denominator. Only
/// DIV can produce a non-integer, but once it can, nothing else may round.
/// </summary>
public readonly record struct Rational
{
    private readonly BigInteger _numerator;
    private readonly BigInteger _denominator;   // 0 only in default(Rational); read as 1

    public Rational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
            throw new DivideByZeroException("a rational cannot have denominator 0");

        // Sign lives in the numerator, and reducing keeps repeated MULT/DIV from
        // growing a denominator that is really just an uncancelled factor.
        if (denominator.Sign < 0)
            (numerator, denominator) = (-numerator, -denominator);

        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        if (!gcd.IsOne)
            (numerator, denominator) = (numerator / gcd, denominator / gcd);

        _numerator = numerator;
        _denominator = denominator;
    }

    public static readonly Rational Zero = new(BigInteger.Zero, BigInteger.One);

    public static readonly Rational One = new(BigInteger.One, BigInteger.One);

    public BigInteger Numerator => _numerator;

    /// <summary>Always positive. default(Rational) has no stored denominator and reads as 1, i.e. 0.</summary>
    public BigInteger Denominator => _denominator.IsZero ? BigInteger.One : _denominator;

    public bool IsZero => _numerator.IsZero;

    public bool IsInteger => Denominator.IsOne;

    public static implicit operator Rational(BigInteger value) => new(value, BigInteger.One);

    public static implicit operator Rational(long value) => new(value, BigInteger.One);

    public static Rational operator +(Rational a, Rational b)
        => new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);

    public static Rational operator -(Rational a, Rational b)
        => new(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);

    public static Rational operator *(Rational a, Rational b)
        => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);

    /// <summary>Throws <see cref="DivideByZeroException"/>; callers turn that into a located error.</summary>
    public static Rational operator /(Rational a, Rational b)
        => b.IsZero
            ? throw new DivideByZeroException("division by zero")
            : new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);

    // default(Rational) must equal Zero, so compare through the properties rather
    // than the fields the compiler would use.
    public bool Equals(Rational other) => Numerator == other.Numerator && Denominator == other.Denominator;

    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    public override string ToString() => IsInteger ? Numerator.ToString() : $"{Numerator}/{Denominator}";
}

public sealed class CommandCalculator
{
    private enum Op { Add, Sub, Mul, Div }

    /// <summary>
    /// One accumulator and the operator still waiting for its operand. The top
    /// level is frame 0; every '(' pushes another.
    /// </summary>
    private sealed class Frame
    {
        public Frame(Rational accumulator, int openedAt) => (Accumulator, OpenedAt) = (accumulator, openedAt);

        public Rational Accumulator { get; set; }

        /// <summary>Non-null between an operator token and its operand.</summary>
        public Op? Pending { get; set; }

        /// <summary>Token index of the '(' that opened this frame, or -1 for the top level.</summary>
        public int OpenedAt { get; }
    }

    private readonly List<Frame> _frames = new();
    private readonly List<Rational> _results = new();
    private readonly List<string> _trace = new();
    private readonly List<string> _currentCommand = new();   // tokens of the top-level command in progress

    private readonly Rational _start;
    private readonly Division _division;
    private readonly SubExpressionSeed _seed;

    private int _index;

    public CommandCalculator(
        Rational start = default,
        Division division = Division.Exact,
        SubExpressionSeed seed = SubExpressionSeed.Independent)
    {
        _start = start;
        _division = division;
        _seed = seed;
        _frames.Add(new Frame(start, -1));
    }

    /// <summary>The top-level accumulator. Unchanged while a sub-expression is open.</summary>
    public Rational Accumulator => _frames[0].Accumulator;

    /// <summary>How many sub-expressions are currently open. 0 at the top level.</summary>
    public int Depth => _frames.Count - 1;

    /// <summary>True when every '(' is closed and no operator is waiting for an operand.</summary>
    public bool IsComplete => _frames.Count == 1 && _frames[0].Pending is null;

    /// <summary>The accumulator after each COMPLETED top-level command, in order.</summary>
    public IReadOnlyList<Rational> Results => _results;

    /// <summary>One human-readable line per completed top-level command.</summary>
    public IReadOnlyList<string> Trace => _trace;

    // ------------------------------------------------------------- the entry

    /// <summary>
    /// Consumes one chunk of input and returns the top-level accumulator. A chunk
    /// is whatever arrives -- "ADD 2", a bare "(", or a whole program on one line.
    /// Once operands can nest, "one command per string" is a convenience of the
    /// caller, not a property of the grammar, so the parser works in tokens and
    /// lets a command span as many chunks as it likes.
    /// </summary>
    public Rational Feed(string chunk)
    {
        foreach (var token in Tokenize(chunk))
            Consume(token);

        return Accumulator;
    }

    public Rational FeedAll(IEnumerable<string> chunks)
    {
        if (chunks is null)
            throw new ArgumentNullException(nameof(chunks));

        foreach (var chunk in chunks)
            Feed(chunk);

        return Accumulator;
    }

    /// <summary>Throws if input ended mid-command or inside a sub-expression.</summary>
    public void AssertComplete()
    {
        if (_frames.Count > 1)
            throw new CalculatorException(_frames[^1].OpenedAt, "'(' is never closed");

        if (_frames[0].Pending is not null)
            throw new CalculatorException(_index - 1, $"input ends after '{Spell(_frames[0].Pending.Value)}', which has no operand");
    }

    /// <summary>Back to the starting accumulator with nothing open.</summary>
    public void Reset()
    {
        _frames.Clear();
        _frames.Add(new Frame(_start, -1));
        _results.Clear();
        _trace.Clear();
        _currentCommand.Clear();
        _index = 0;
    }

    /// <summary>Runs a whole program and returns the final accumulator.</summary>
    public static Rational Evaluate(
        IEnumerable<string> commands,
        Rational start = default,
        Division division = Division.Exact,
        SubExpressionSeed seed = SubExpressionSeed.Independent)
        => Machine(commands, start, division, seed).Accumulator;

    /// <summary>
    /// The accumulator after each top-level command -- the "return the running
    /// result" half of the prompt. A nested command list contributes one entry,
    /// not one per inner command.
    /// </summary>
    public static IReadOnlyList<Rational> RunningResults(
        IEnumerable<string> commands,
        Rational start = default,
        Division division = Division.Exact,
        SubExpressionSeed seed = SubExpressionSeed.Independent)
        => Machine(commands, start, division, seed).Results;

    private static CommandCalculator Machine(IEnumerable<string> commands, Rational start, Division division, SubExpressionSeed seed)
    {
        var calculator = new CommandCalculator(start, division, seed);
        calculator.FeedAll(commands);
        calculator.AssertComplete();
        return calculator;
    }

    /// <summary>Splits a multi-line program into lines, dropping blanks. For tests and demos.</summary>
    public static List<string> Lines(string program) => (program ?? string.Empty)
        .Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 0)
        .ToList();

    // ---------------------------------------------------------- the tokenizer

    /// <summary>
    /// Whitespace separates, parentheses are tokens in their own right whether or
    /// not they are spaced -- "MULT(ADD 1)" and "MULT ( ADD 1 )" tokenize alike.
    /// Pre-writing this is what buys time for the follow-up.
    /// </summary>
    public static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        if (text is null)
            return tokens;

        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length == 0)
                return;
            tokens.Add(current.ToString());
            current.Clear();
        }

        foreach (char c in text)
        {
            if (c is '(' or ')')
            {
                Flush();
                tokens.Add(c.ToString());
            }
            else if (char.IsWhiteSpace(c))
            {
                Flush();
            }
            else
            {
                current.Append(c);
            }
        }

        Flush();
        return tokens;
    }

    public static List<string> Tokenize(IEnumerable<string> chunks)
        => (chunks ?? throw new ArgumentNullException(nameof(chunks))).SelectMany(Tokenize).ToList();

    // -------------------------------------------------------- the state machine

    private void Consume(string token)
    {
        int at = _index++;
        var frame = _frames[^1];
        _currentCommand.Add(token);

        if (token == "(")
        {
            if (frame.Pending is null)
                throw new CalculatorException(at, "'(' must follow an operator -- a sub-expression is an operand, not a command");

            _frames.Add(new Frame(_seed == SubExpressionSeed.Independent ? Rational.Zero : frame.Accumulator, at));
            return;
        }

        if (token == ")")
        {
            if (_frames.Count == 1)
                throw new CalculatorException(at, "')' with no matching '('");
            if (frame.Pending is not null)
                throw new CalculatorException(at, $"')' closes a sub-expression whose '{Spell(frame.Pending.Value)}' has no operand");

            _frames.RemoveAt(_frames.Count - 1);
            Apply(frame.Accumulator, at);            // the sub-expression's result IS the operand
            return;
        }

        if (TryParseOp(token, out var op))
        {
            if (frame.Pending is not null)
                throw new CalculatorException(at, $"'{token}' follows '{Spell(frame.Pending.Value)}', which is still waiting for an operand");

            frame.Pending = op;
            return;
        }

        if (BigInteger.TryParse(token, out var number))
        {
            if (frame.Pending is null)
                throw new CalculatorException(at, $"'{token}' has no operator in front of it");

            Apply(number, at);
            return;
        }

        throw new CalculatorException(at, $"unknown token '{token}' -- expected ADD, SUB, MULT, DIV, an integer, '(' or ')'");
    }

    /// <summary>Folds a resolved operand into the innermost frame, whose Pending callers have already checked.</summary>
    private void Apply(Rational operand, int at)
    {
        var frame = _frames[^1];
        var op = frame.Pending.Value;

        Rational next;
        try
        {
            next = op switch
            {
                Op.Add => frame.Accumulator + operand,
                Op.Sub => frame.Accumulator - operand,
                Op.Mul => frame.Accumulator * operand,
                Op.Div => frame.Accumulator / operand,
                _ => throw new CalculatorException(at, $"unhandled operator {op}"),
            };
        }
        catch (DivideByZeroException)
        {
            // Reachable from a literal "DIV 0" and from a sub-expression that
            // happens to evaluate to 0 -- the second is the one tests forget.
            throw new CalculatorException(at, "division by zero");
        }

        frame.Accumulator = Round(next, _division);
        frame.Pending = null;

        if (_frames.Count > 1)
            return;

        _results.Add(frame.Accumulator);
        _trace.Add($"{string.Join(' ', _currentCommand),-28} acc = {frame.Accumulator}");
        _currentCommand.Clear();
    }

    // -------------------------------------------------------------- plumbing

    /// <summary>
    /// Applies the integer-division policy. Only DIV can leave a fraction behind,
    /// and rounding it away immediately is the same as rounding at the DIV -- so
    /// this one call sites the whole question.
    /// </summary>
    private static Rational Round(Rational value, Division division)
    {
        if (division == Division.Exact || value.IsInteger)
            return value;

        var quotient = BigInteger.Divide(value.Numerator, value.Denominator);   // toward zero

        // Denominator is always positive, so a negative remainder is exactly the
        // case where truncation and flooring disagree.
        if (division == Division.Floor && value.Numerator.Sign < 0)
            quotient -= BigInteger.One;

        return quotient;
    }

    private static bool TryParseOp(string token, out Op op)
    {
        switch (token.ToUpperInvariant())
        {
            case "ADD": op = Op.Add; return true;
            case "SUB": op = Op.Sub; return true;
            case "MULT":
            case "MUL": op = Op.Mul; return true;
            case "DIV": op = Op.Div; return true;
            default: op = default; return false;
        }
    }

    private static string Spell(Op op) => op switch
    {
        Op.Add => "ADD",
        Op.Sub => "SUB",
        Op.Mul => "MULT",
        Op.Div => "DIV",
        _ => op.ToString(),
    };

    // ------------------------------------------------------------- reference
    //
    // The version to write in the room. Same grammar, same semantics, and the
    // frame stack is the CLR's:
    //
    //     expr    := command*
    //     command := op operand
    //     operand := integer | '(' expr ')'
    //
    // It is shorter and it reads better, and it shares no control flow with the
    // machine above, which is what makes agreeing with it real evidence. The one
    // thing it cannot do is survive input nested deeper than the thread's stack:
    // a StackOverflowException in .NET cannot be caught and takes the process
    // with it, so MaxDepth turns that into an ordinary error. If nesting depth is
    // attacker-controlled -- a parser reading a network payload, say -- that
    // limit is not optional, and the iterative machine is the honest answer.

    public static class Recursive
    {
        public const int MaxDepth = 1000;

        public static Rational Evaluate(
            IEnumerable<string> commands,
            Rational start = default,
            Division division = Division.Exact,
            SubExpressionSeed seed = SubExpressionSeed.Independent)
        {
            var tokens = Tokenize(commands);
            int i = 0;
            var value = Expr(tokens, ref i, start, division, seed, depth: 0);

            if (i < tokens.Count)
                throw new CalculatorException(i, "')' with no matching '('");

            return value;
        }

        private static Rational Expr(
            List<string> tokens, ref int i, Rational accumulator,
            Division division, SubExpressionSeed seed, int depth)
        {
            if (depth > MaxDepth)
                throw new CalculatorException(i, $"nesting deeper than {MaxDepth}; use the iterative evaluator");

            while (i < tokens.Count && tokens[i] != ")")
            {
                int opAt = i;
                if (!TryParseOp(tokens[i], out var op))
                    throw new CalculatorException(
                        i,
                        tokens[i] == "("
                            ? "'(' must follow an operator -- a sub-expression is an operand, not a command"
                            : BigInteger.TryParse(tokens[i], out _)
                                ? $"'{tokens[i]}' has no operator in front of it"
                                : $"unknown token '{tokens[i]}' -- expected ADD, SUB, MULT, DIV, an integer, '(' or ')'");
                i++;

                if (i == tokens.Count)
                    throw new CalculatorException(opAt, $"input ends after '{Spell(op)}', which has no operand");

                Rational operand;
                if (tokens[i] == "(")
                {
                    int openedAt = i++;
                    var inner = seed == SubExpressionSeed.Independent ? Rational.Zero : accumulator;
                    operand = Expr(tokens, ref i, inner, division, seed, depth + 1);

                    if (i == tokens.Count)
                        throw new CalculatorException(openedAt, "'(' is never closed");
                    i++;                                            // consume the ')'
                }
                else if (tokens[i] == ")")
                {
                    throw new CalculatorException(i, $"')' closes a sub-expression whose '{Spell(op)}' has no operand");
                }
                else if (TryParseOp(tokens[i], out _))
                {
                    // Same complaint the machine makes, worded the same way, so the
                    // two implementations can be compared on errors and not just
                    // on answers.
                    throw new CalculatorException(i, $"'{tokens[i]}' follows '{Spell(op)}', which is still waiting for an operand");
                }
                else if (BigInteger.TryParse(tokens[i], out var literal))
                {
                    operand = literal;
                    i++;
                }
                else
                {
                    throw new CalculatorException(i, $"unknown token '{tokens[i]}' -- expected ADD, SUB, MULT, DIV, an integer, '(' or ')'");
                }

                try
                {
                    accumulator = Round(op switch
                    {
                        Op.Add => accumulator + operand,
                        Op.Sub => accumulator - operand,
                        Op.Mul => accumulator * operand,
                        _ => accumulator / operand,
                    }, division);
                }
                catch (DivideByZeroException)
                {
                    throw new CalculatorException(i - 1, "division by zero");
                }
            }

            return accumulator;
        }
    }

    // ---------------------------------------------------------------- tests

    public static void Run()
    {
        Console.WriteLine("== the flat version, traced ==");

        var flat = new CommandCalculator();
        flat.FeedAll(new[] { "ADD 2", "MULT 3", "SUB 1", "DIV 5" });
        flat.AssertComplete();

        foreach (var line in flat.Trace)
            Console.WriteLine("  " + line);

        Console.WriteLine($"  running results: [{string.Join(", ", flat.Results)}]");
        Console.WriteLine($"  strictly left to right: ADD 1 ADD 2 MULT 3 = {Evaluate(Lines("ADD 1\nADD 2\nMULT 3"))}, i.e. (0+1+2)*3.");
        Console.WriteLine("  An expression evaluator with precedence would read that as 1 + 2*3 = 7. Pick an");
        Console.WriteLine("  example where the two DISAGREE and confirm which one the interviewer wants.");

        Console.WriteLine();
        Console.WriteLine("== the follow-up: an operand can be a command list ==");

        var nested = new CommandCalculator();
        nested.FeedAll(Lines(@"
            ADD 2
            MULT ( ADD 1 ADD 2 )
            SUB ( ADD 10 DIV 2 MULT ( ADD 3 ) )"));
        nested.AssertComplete();

        foreach (var line in nested.Trace)
            Console.WriteLine("  " + line);

        Console.WriteLine("  the inner list is evaluated first and its accumulator becomes the operand;");
        Console.WriteLine("  one top-level command produces one running result no matter how deep it goes.");

        Console.WriteLine($"  tokenizing is punctuation-insensitive: MULT(ADD 1 ADD 2) -> {Evaluate(new[] { "ADD 2", "MULT(ADD 1 ADD 2)" })} (6)");

        Console.WriteLine();
        Console.WriteLine("== question 1: what does the accumulator start at ==");

        Console.WriteLine($"  MULT 3 MULT 4 from 0: {Evaluate(Lines("MULT 3\nMULT 4"))}  <- 0 is the identity for ADD, not MULT");
        Console.WriteLine($"  MULT 3 MULT 4 from 1: {Evaluate(Lines("MULT 3\nMULT 4"), start: 1)}");
        Console.WriteLine($"  no commands at all:   {Evaluate(Array.Empty<string>())} (the start value, whatever it is)");

        Console.WriteLine();
        Console.WriteLine("== question 2: what does DIV mean ==");

        foreach (var program in new[] { "ADD 7 DIV 2 MULT 2", "ADD -7 DIV 2", "ADD 1 DIV 3 MULT 3" })
        {
            Console.WriteLine($"  {program,-20} exact {Evaluate(new[] { program }),-6} " +
                              $"truncate {Evaluate(new[] { program }, division: Division.TruncateTowardZero),-4} " +
                              $"floor {Evaluate(new[] { program }, division: Division.Floor)}");
        }

        Console.WriteLine("  Three defensible answers that disagree on the second command. Ask.");

        Console.WriteLine();
        Console.WriteLine("== question 3: what does a SUB-EXPRESSION start at ==");

        var subject = Lines("ADD 5\nMULT ( ADD 2 )");
        Console.WriteLine($"  ADD 5 MULT ( ADD 2 )   independent: {Evaluate(subject)} (5 * 2)");
        Console.WriteLine($"                         inherits:    {Evaluate(subject, seed: SubExpressionSeed.InheritsAccumulator)} (5 * 7)");
        Console.WriteLine("  Not a detail -- inheriting makes 'MULT (expr)' compute acc * (a*acc + b), which is");
        Console.WriteLine("  QUADRATIC in the accumulator. The affine closed form below only exists under the");
        Console.WriteLine("  independent reading; the property test right after shows exactly that.");

        Console.WriteLine();
        Console.WriteLine("== the program is one affine map in its start value ==");

        // Over the rationals ADD/SUB/MULT/DIV are all acc -> a*acc + b, and affine
        // maps compose, so f(start) = a*start + b for the whole program. An affine
        // function has zero second difference: f(2) - f(1) == f(1) - f(0).
        var affine = Lines("ADD 3\nMULT 4\nDIV 7\nSUB ( ADD 2 MULT 5 )\nMULT ( SUB 3 )");
        Rational f0 = Evaluate(affine, start: 0), f1 = Evaluate(affine, start: 1), f2 = Evaluate(affine, start: 2);
        Console.WriteLine($"  f(0) = {f0}, f(1) = {f1}, f(2) = {f2}");
        Console.WriteLine($"  affine? {(f2 - f1).Equals(f1 - f0)}   =>  f(start) = {f1 - f0} * start + {f0}");

        Rational g0 = Evaluate(affine, start: 0, seed: SubExpressionSeed.InheritsAccumulator);
        Rational g1 = Evaluate(affine, start: 1, seed: SubExpressionSeed.InheritsAccumulator);
        Rational g2 = Evaluate(affine, start: 2, seed: SubExpressionSeed.InheritsAccumulator);
        Console.WriteLine($"  same program, inherited seeds: affine? {(g2 - g1).Equals(g1 - g0)} -- the nesting made it nonlinear");

        Rational t0 = Evaluate(affine, start: 0, division: Division.TruncateTowardZero);
        Rational t1 = Evaluate(affine, start: 1, division: Division.TruncateTowardZero);
        Rational t2 = Evaluate(affine, start: 2, division: Division.TruncateTowardZero);
        Console.WriteLine($"  same program, integer DIV:     affine? {(t2 - t1).Equals(t1 - t0)} -- truncation is not linear either");
        Console.WriteLine("  Worth saying out loud: 'if DIV is exact and sub-expressions are independent, I can");
        Console.WriteLine("  collapse any command list to one (a, b) and apply it in O(1)' -- that is the answer to");
        Console.WriteLine("  'what if the same sub-expression is used a million times'.");

        Console.WriteLine();
        Console.WriteLine("== streaming: a command may arrive in pieces ==");

        var streamed = new CommandCalculator();
        foreach (var token in Tokenize("ADD 2 MULT ( ADD 1 ADD 2 ) SUB 1"))
        {
            streamed.Feed(token);
            if (streamed.Depth > 0)
                Console.WriteLine($"  fed {token,-6} depth {streamed.Depth}, top-level acc still {streamed.Accumulator}");
        }
        streamed.AssertComplete();
        Console.WriteLine($"  fed one token at a time: {streamed.Accumulator}, results [{string.Join(", ", streamed.Results)}]");
        Console.WriteLine("  The accumulator does not move while a sub-expression is open -- there is no partial");
        Console.WriteLine("  answer to report until the operand exists.");

        Console.WriteLine();
        Console.WriteLine("== degenerate shapes ==");

        Console.WriteLine($"  empty sub-expression:      ADD 5 MULT ( ) -> {Evaluate(new[] { "ADD 5 MULT ( )" })} (the seed, 0)");
        Console.WriteLine($"  sub-expression as the 1st: MULT ( ADD 3 ) -> {Evaluate(new[] { "MULT ( ADD 3 )" })} (0 * 3)");
        Console.WriteLine($"  deeply nested by hand:     {Evaluate(new[] { "ADD ( ADD ( ADD ( ADD 1 ) ) )" })} (1)");
        Console.WriteLine($"  negative operands:         {Evaluate(Lines("ADD -3\nMULT -2\nSUB -1"))} (7)");
        Console.WriteLine($"  MULT 0 then keep going:    {Evaluate(Lines("ADD 9\nMULT 0\nADD 4"))} (4)");
        Console.WriteLine($"  big integers:              {Evaluate(Lines("ADD 99999999999999999999\nMULT 99999999999999999999"))}");
        Console.WriteLine($"  lowercase operators:       {Evaluate(new[] { "add 2 mult 3" })} (6)");

        Console.WriteLine();
        Console.WriteLine("== every way a program can be wrong ==");

        foreach (var (label, bad) in new (string Label, string Program)[]
                 {
                     ("division by zero",        "ADD 5 DIV 0"),
                     ("nested result is zero",   "ADD 5 DIV ( ADD 2 SUB 2 )"),
                     ("unknown operator",        "ADD 2 POW 3"),
                     ("operator, no operand",    "ADD 2 MULT"),
                     ("operand, no operator",    "ADD 2 3"),
                     ("two operators in a row",  "ADD MULT 3"),
                     ("unclosed '('",            "ADD 2 MULT ( ADD 1"),
                     ("stray ')'",               "ADD 2 MULT 3 )"),
                     ("'(' not after operator",  "ADD 2 ( ADD 1 )"),
                     ("')' after an operator",   "ADD 2 MULT ( ADD )"),
                     ("non-integer operand",     "ADD 1.5"),
                     ("empty program is fine",   ""),
                 })
        {
            foreach (var (which, run) in new (string Which, Func<Rational> Run)[]
                     {
                         ("iterative", () => Evaluate(new[] { bad })),
                         ("recursive", () => Recursive.Evaluate(new[] { bad })),
                     })
            {
                try
                {
                    var value = run();
                    Console.WriteLine($"  {label,-24} {which,-9} -> {value}");
                }
                catch (CalculatorException ex)
                {
                    Console.WriteLine($"  {label,-24} {which,-9} -> {ex.Message}");
                }
            }
        }

        Console.WriteLine("  Two independent parsers, the same verdict and the same blamed token on every one.");

        Console.WriteLine();
        Console.WriteLine("== depth is the difference between the two evaluators ==");

        // ADD ( ADD ( ... ADD 1 ... ) ) -- 100k levels deep, one command at the core.
        const int deep = 100_000;
        var bomb = new StringBuilder();
        for (int k = 0; k < deep; k++)
            bomb.Append("ADD ( ");
        bomb.Append("ADD 1");
        for (int k = 0; k < deep; k++)
            bomb.Append(" )");

        var program100k = new[] { bomb.ToString() };
        Console.WriteLine($"  {deep:n0} levels deep, iterative: {Evaluate(program100k)}");

        try
        {
            Recursive.Evaluate(program100k);
            Console.WriteLine("  recursive: survived (it should not have)");
        }
        catch (CalculatorException ex)
        {
            Console.WriteLine($"  recursive: {ex.Message}");
        }

        Console.WriteLine("  Without that guard this is a StackOverflowException, which .NET does not let you");
        Console.WriteLine("  catch -- the process dies. Recursive descent is the right thing to write in an");
        Console.WriteLine("  interview and the wrong thing to point at untrusted input.");

        Console.WriteLine();
        Console.WriteLine("== randomized cross-checks ==");

        var rng = new Random(20260810);
        bool agrees = true, streams = true, seedIsAffine = true;
        bool inverseIsIdentity = true, subIsAddNegated = true, literalWrapsClean = true;
        int programs = 0;

        for (int trial = 0; trial < 3000; trial++)
        {
            var commands = RandomProgram(rng, length: 12, maxDepth: 4);
            programs++;

            var expected = Evaluate(commands);

            // 1. Two independent control flows, one answer.
            agrees &= Recursive.Evaluate(commands).Equals(expected);

            // 2. Feeding one token at a time is the same as feeding the whole thing.
            var machine = new CommandCalculator();
            foreach (var token in Tokenize(commands))
                machine.Feed(token);
            machine.AssertComplete();
            streams &= machine.Accumulator.Equals(expected);

            // 3. The affine law: zero second difference in the start value.
            Rational a0 = Evaluate(commands, start: 0), a1 = Evaluate(commands, start: 1), a2 = Evaluate(commands, start: 2);
            seedIsAffine &= (a2 - a1).Equals(a1 - a0);

            // 4. Exact arithmetic really is exact: MULT k then DIV k is a no-op.
            int k = rng.Next(1, 50);
            inverseIsIdentity &= Evaluate(commands.Append($"MULT {k}").Append($"DIV {k}")).Equals(expected);

            // 5. SUB k is ADD -k, everywhere, including inside sub-expressions.
            var rewritten = Tokenize(commands);
            for (int i = 0; i + 1 < rewritten.Count; i++)
            {
                if (rewritten[i] == "SUB" && BigInteger.TryParse(rewritten[i + 1], out var operand))
                {
                    rewritten[i] = "ADD";
                    rewritten[i + 1] = (-operand).ToString();
                }
            }
            subIsAddNegated &= Evaluate(rewritten).Equals(expected);

            // 6. Under an INDEPENDENT seed of 0, the literal n and the sub-expression
            //    ( ADD n ) are the same operand -- the nesting semantics, as a law.
            var wrapped = new List<string>();
            foreach (var token in Tokenize(commands))
            {
                if (BigInteger.TryParse(token, out var literal))
                    wrapped.AddRange(new[] { "(", "ADD", literal.ToString(), ")" });
                else
                    wrapped.Add(token);
            }
            literalWrapsClean &= Evaluate(wrapped).Equals(expected);
        }

        Console.WriteLine($"  {$"{programs:n0} random nested programs, iterative == recursive:",-58}{agrees}");
        Console.WriteLine($"  {"token-at-a-time streaming == whole-program:",-58}{streams}");
        Console.WriteLine($"  {"result is affine in the start value:",-58}{seedIsAffine}");
        Console.WriteLine($"  {"exact arithmetic: MULT k then DIV k changes nothing:",-58}{inverseIsIdentity}");
        Console.WriteLine($"  {"SUB k == ADD -k everywhere:",-58}{subIsAddNegated}");
        Console.WriteLine($"  {"operand n == sub-expression ( ADD n ):",-58}{literalWrapsClean}");
    }

    /// <summary>
    /// A random VALID program: every operator gets an operand, every '(' gets a
    /// ')', and DIV never sees a literal 0 (a nested zero is possible, so the
    /// generator retries rather than pretend division by zero cannot happen).
    /// </summary>
    private static List<string> RandomProgram(Random rng, int length, int maxDepth)
    {
        while (true)
        {
            var tokens = new List<string>();
            Emit(tokens, rng, length, maxDepth, depth: 0);

            var program = new List<string> { string.Join(' ', tokens) };
            try
            {
                Evaluate(program);
                return program;
            }
            catch (CalculatorException)
            {
                // Only division by zero can land here; draw again.
            }
        }
    }

    private static void Emit(List<string> tokens, Random rng, int length, int maxDepth, int depth)
    {
        for (int i = 0; i < length; i++)
        {
            string op = rng.Next(4) switch { 0 => "ADD", 1 => "SUB", 2 => "MULT", _ => "DIV" };
            tokens.Add(op);

            // Small magnitudes: nothing here bounds how fast repeated MULT can grow
            // a numerator, and the point of these trials is semantics, not bignums.
            if (depth < maxDepth && rng.Next(4) == 0)
            {
                tokens.Add("(");
                Emit(tokens, rng, rng.Next(1, 4), maxDepth, depth + 1);
                tokens.Add(")");
            }
            else
            {
                int value = rng.Next(-9, 10);
                if (op == "DIV" && value == 0)
                    value = 1;
                tokens.Add(value.ToString());
            }
        }
    }
}

// ---- Notes for the follow-up questions ----
//
// "Make MULT bind tighter than ADD."
//     Now it is not this problem. Left-to-right against an accumulator needs no
//     parser at all; precedence needs one. Shunting-yard is the short answer --
//     an operand stack, an operator stack, and pop-while-the-top-binds-at-least-
//     as-tightly before pushing -- or precedence-climbing if you are already
//     writing recursive descent, which you are. The frames here become the
//     parenthesis handling of that parser and nothing else changes. Say which one
//     you are building BEFORE you start; discovering halfway through that the
//     interviewer wanted precedence is the classic way to lose this round.
//
// "The same sub-expression appears thousands of times / the stream is replayed."
//     Compile, don't re-evaluate. Under exact division and independent seeds each
//     command list collapses to one affine pair (a, b) -- ADD k is (1, k), MULT k
//     is (k, 0), DIV k is (1/k, 0), and composing (a1,b1) then (a2,b2) gives
//     (a2*a1, a2*b1 + b2). Cache the pair per sub-expression and applying it is
//     two rationals of arithmetic regardless of length. MathInterpreter.cs is this
//     idea taken all the way, with named functions. Both preconditions matter: an
//     inherited seed makes it quadratic and integer DIV makes it non-linear, and
//     in both cases you are back to interpreting.
//
// "The commands arrive over a socket, one at a time, forever."
//     Already handled -- Feed() is incremental, keeps no history beyond the
//     results list, and its memory is O(nesting depth). Drop _results and _trace
//     and it is O(depth) flat. Note what it cannot do: report a running result
//     while a sub-expression is open, because the operand does not exist yet.
//
// "Support UNDO."
//     Do not invert the operations -- MULT 0 destroys the accumulator and has no
//     inverse. Snapshot instead: the state is one rational plus a shallow frame
//     stack, so pushing the previous value per command is O(depth) and exact.
//
// "Add variables, or SET/GET."
//     The frame grows an environment and the token switch grows two cases. Decide
//     scoping out loud: one flat namespace (simple, and what most interviewers
//     mean) or per-frame lexical scope (a dictionary per frame, resolve by walking
//     outward). The affine view survives if variables are constants at evaluation
//     time and dies the moment an operand can be a variable that a sub-expression
//     writes to.
//
// "What about overflow?"
//     BigInteger here, so there is none, and exact division means no rounding
//     either -- but exactness has a price the interviewer may care about:
//     denominators grow with every DIV and reducing costs a gcd per operation. If
//     the spec means 64-bit ints, say so and use them; if it means IEEE doubles,
//     say that too and expect 0.1 + 0.2 questions.
//
// "How would you test it?"
//     The Run() block is the answer: a recursive-descent reference that shares no
//     control flow with the machine, laws rather than fixtures (affine in the
//     start value; MULT k then DIV k is identity; n and ( ADD n ) are the same
//     operand), an error case for every branch of the parser, and one input --
//     100k levels deep -- that separates the two implementations instead of
//     confirming them.
