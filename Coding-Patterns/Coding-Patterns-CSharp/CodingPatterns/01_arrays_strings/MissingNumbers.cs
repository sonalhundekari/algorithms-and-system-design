namespace CodingPatterns.ArraysStrings;

public class MissingNumber {
    public int FindMissingNumber(int[] nums) {
        int expectedSum = nums.Length*(nums.Length + 1)/2;
        int actualSum = 0;
        foreach (int num in nums) 
            actualSum += num;
        return expectedSum - actualSum;
    }

    public static void Run() {
        var missingNumberFinder = new MissingNumber();
        int[] nums = {3, 0, 1};
        Console.WriteLine(missingNumberFinder.FindMissingNumber(nums)); // Output: 2
    }
}
