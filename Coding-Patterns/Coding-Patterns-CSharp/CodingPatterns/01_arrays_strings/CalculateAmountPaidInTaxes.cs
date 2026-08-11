/*
Calculate Amount Paid in Taxes (LeetCode 2303)

brackets[i] = [upper_i, percent_i], sorted strictly increasing by upper_i.
The (i)th bracket taxes only the income that falls between upper_{i-1} and upper_i
-- NOT the whole income. That is the whole problem: a progressive (marginal) tax,
not a flat rate picked from the bracket you land in.

Walk the brackets in order carrying `prevUpper`, the top of the previous band:

    taxable_i = min(income, upper_i) - prevUpper

min(income, upper_i) clamps the band to where the money actually runs out, and
subtracting prevUpper strips off the part already taxed by earlier bands. Once
income <= prevUpper the remaining bands contribute nothing, so we stop early.

Edge cases the formula already absorbs:
  - income = 0            -> first band's taxable is 0, loop breaks immediately
  - income above the top  -> the last bracket's upper is guaranteed >= income by
                             the constraints, so no untaxed remainder is possible
  - percent = 0           -> band contributes 0, no special case needed

Complexity
  Time:  O(n) over the brackets, and we bail out as soon as the income is spent.
  Space: O(1).

Precision: accumulate in double and divide by 100.0 per band. Answers within
1e-5 are accepted; with the constraints (upper, income <= 1000, percent <= 100)
the worst-case sum stays far inside double's exact-integer range, so the only
rounding comes from the /100.0 divisions -- well under tolerance.
*/

namespace CodingPatterns.ArraysStrings;

public class CalculateAmountPaidInTaxes
{
    public static double CalculateTax(int[][] brackets, int income)
    {
        double tax = 0;
        int prevUpper = 0; // top of the previous band == bottom of this one

        foreach (int[] bracket in brackets)
        {
            if (income <= prevUpper)
                break; // income exhausted; every remaining band is empty

            int upper = bracket[0];
            int percent = bracket[1];

            int taxable = Math.Min(income, upper) - prevUpper;
            tax += taxable * percent / 100.0;

            prevUpper = upper;
        }

        return tax;
    }

    public static void Run()
    {
        var cases = new (int[][] Brackets, int Income, double Expected)[]
        {
            // 3 @ 50% + 4 @ 10% + 3 @ 25% = 1.5 + 0.4 + 0.75
            (new[] { new[] { 3, 50 }, new[] { 7, 10 }, new[] { 12, 25 } }, 10, 2.65),
            // 1 @ 0% + 1 @ 25% = 0 + 0.25
            (new[] { new[] { 1, 0 }, new[] { 4, 25 }, new[] { 5, 50 } }, 2, 0.25),
            // zero income is taxed zero, whatever the brackets say
            (new[] { new[] { 2, 50 } }, 0, 0.0),
            // income lands exactly on a bracket boundary
            (new[] { new[] { 3, 50 }, new[] { 7, 10 }, new[] { 12, 25 } }, 7, 1.9),
            // single bracket, full rate
            (new[] { new[] { 1000, 100 } }, 1000, 1000.0),
        };

        Console.WriteLine("Calculate Amount Paid in Taxes (LeetCode 2303)");
        Console.WriteLine("==============================================");

        foreach (var (brackets, income, expected) in cases)
        {
            double actual = CalculateTax(brackets, income);
            string shape = string.Join(",", brackets.Select(b => $"[{b[0]},{b[1]}]"));
            string status = Math.Abs(actual - expected) < 1e-5 ? "OK" : "MISMATCH!";
            Console.WriteLine($"income = {income,4}  brackets = {shape,-28} -> {actual,8:F5} (expected {expected:F5}) [{status}]");
        }
    }
}
