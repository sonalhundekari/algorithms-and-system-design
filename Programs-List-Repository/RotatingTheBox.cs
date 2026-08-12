/*
Rotating the Box (LeetCode 1861)

box is m x n of '#' (stone), '*' (fixed obstacle), '.' (empty). Rotate it 90
degrees clockwise, then let gravity pull every stone down until it hits the
floor, an obstacle, or a settled stone. Return the resulting n x m grid.

The trap is doing it in the order the statement says. After rotation gravity
runs *down a column*, which is a strided walk through jagged rows -- awkward and
cache-hostile. But rotation is a pure relabelling of coordinates: it never moves
a stone relative to its neighbours. So settle first, rotate second.

Under a 90-degree clockwise rotation, original column n-1 becomes the bottom
row. "Down" after rotating is therefore "toward the last column" before
rotating -- i.e. gravity is a *row-wise* operation on the input, and each row is
independent.

Settling one row (two pointers, right to left):

  `write` = the rightmost cell a falling stone may land in.
  Scan j from n-1 down to 0:
    '*' -> obstacle. Nothing can pass it, so the next landing slot is j-1.
    '#' -> stone. It falls to `write`; if write != j the source becomes '.'.
           write--.
    '.' -> ignore; `write` already points at the lowest free slot at or right
           of it.

`write` never moves left past an obstacle without being reset to it, which is
exactly what "obstacle-separated segments compact independently" means -- the
segments fall out of the invariant, no explicit segment boundaries needed.
Writing right-to-left is also why the in-place update is safe: `write >= j`
always, so we only ever overwrite cells already scanned.

Then the rotation, size m x n -> n x m:

    rotated[j][m - 1 - i] = box[i][j]

Row i of the input becomes column m-1-i of the output, read top to bottom.

Complexity
  Time:  O(m * n) -- one settling pass, one copy pass.
  Space: O(m * n) for the returned grid; the settling itself is O(1) extra.

Note: settling mutates `box` in place (LeetCode allows this). Snapshot the input
first if you still need it -- Run() below does exactly that to print "before".

Edge cases the invariant already absorbs:
  - a row that is all obstacles      -> write is reset every step, nothing moves
  - a row with no obstacles          -> every stone packs against column n-1
  - stones already settled           -> write == j each time, writes are no-ops
  - 1 x n or m x 1 boxes             -> nothing special; the loops just degenerate
*/

namespace CodingPatterns.ArraysStrings;

public class RotatingTheBox
{
    public static char[][] RotateTheBox(char[][] box)
    {
        int m = box.Length;
        int n = box[0].Length;

        // Phase 1: gravity, in the ORIGINAL orientation, so it runs along rows.
        // Post-rotation "down" == pre-rotation "toward the last column".
        for (int i = 0; i < m; i++)
        {
            int write = n - 1; // lowest free landing slot in this row

            for (int j = n - 1; j >= 0; j--)
            {
                if (box[i][j] == '*')
                {
                    write = j - 1; // stones can't pass an obstacle; reset the floor
                }
                else if (box[i][j] == '#')
                {
                    box[i][write] = '#';
                    if (write != j)
                        box[i][j] = '.'; // vacate only if the stone actually moved
                    write--;
                }
            }
        }

        // Phase 2: rotate 90 degrees clockwise. Row i -> column m-1-i.
        var rotated = new char[n][];
        for (int j = 0; j < n; j++)
        {
            rotated[j] = new char[m];
            for (int i = 0; i < m; i++)
                rotated[j][m - 1 - i] = box[i][j];
        }

        return rotated;
    }

    public static void Main()
    {
        var cases = new (char[][] Box, char[][] Expected)[]
        {
            // Single row: both stones drop to the bottom of the rotated column.
            (
                Grid("#.#"),
                Grid(".", "#", "#")
            ),
            // Obstacle in the middle: the '#' pair above it cannot fall past.
            (
                Grid("#.*.", "##*."),
                Grid("#.", "##", "**", "..")
            ),
            // Three obstacle-separated segments, each compacting on its own.
            (
                Grid("##*.*.", "###*..", "###.#."),
                Grid(".##", ".##", "##*", "#*.", "#.*", "#..")
            ),
            // No obstacles at all: every stone packs against the floor.
            (
                Grid("#..#", ".#.."),
                Grid("..", "..", ".#", "##")
            ),
            // All obstacles: nothing can move, we only rotate.
            (
                Grid("**", "**"),
                Grid("**", "**")
            ),
            // Single column: gravity is a no-op, rotation makes it a single row.
            (
                Grid("#", ".", "#"),
                Grid("#.#")
            ),
        };

        Console.WriteLine("Rotating the Box (LeetCode 1861)");
        Console.WriteLine("================================");

        foreach (var (box, expected) in cases)
        {
            string[] before = box.Select(row => new string(row)).ToArray();
            char[][] actual = RotateTheBox(box); // mutates `box`, hence the snapshot
            string status = Matches(actual, expected) ? "OK" : "MISMATCH!";

            Console.WriteLine();
            Console.WriteLine($"input {box.Length}x{before[0].Length} -> output {actual.Length}x{actual[0].Length}  [{status}]");
            PrintSideBySide(before, actual.Select(row => new string(row)).ToArray());

            if (status != "OK")
            {
                Console.WriteLine("  expected:");
                foreach (var row in expected)
                    Console.WriteLine($"    {new string(row)}");
            }
        }
    }

    private static char[][] Grid(params string[] rows) =>
        rows.Select(r => r.ToCharArray()).ToArray();

    private static bool Matches(char[][] a, char[][] b) =>
        a.Length == b.Length && a.Zip(b).All(p => p.First.AsSpan().SequenceEqual(p.Second));

    private static void PrintSideBySide(string[] before, string[] after)
    {
        int width = Math.Max(before.Max(r => r.Length), "before".Length);
        int rows = Math.Max(before.Length, after.Length);

        Console.WriteLine($"  {"before".PadRight(width)}     after");
        for (int r = 0; r < rows; r++)
        {
            string left = (r < before.Length ? before[r] : "").PadRight(width);
            string arrow = r == rows / 2 ? " --> " : "     ";
            string right = r < after.Length ? after[r] : "";
            Console.WriteLine($"  {left}{arrow}{right}");
        }
    }
}
