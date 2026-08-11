# 🧠 Coding Interview Prep — By Pattern

A curated set of **30 problems** covering 80% of coding interviews, organized by pattern.
Each problem has solutions in both **Python** and **C#**.

## 📁 Structure

The two languages live in separate, independently runnable trees:

```
Coding-Patterns/
├── Coding-Patterns-CSharp/     # dotnet solution — see its README
│   ├── CodingPatterns.sln
│   ├── CodingPatterns/         # all pattern solutions, one console app
│   ├── HospitalBookingApi/     # ASP.NET Core minimal API
│   └── RedisRateLimiterDemo/   # needs a live Redis
└── Coding-Patterns-Python/     # scripts + run.py launcher — see its README
    └── run.py
```

Each language tree has the same pattern folders:

```
01_arrays_strings/   04_dynamic_programming/   07_binary_search/
02_trees/            05_greedy/                08_linked_lists/
03_graphs/           06_stack_queue/           09_concurrency/
```

and each of those folders contains:
- `README.md` — pattern overview + problem list
- the solutions for that pattern, each with its own tests/demo built in

## ▶️ Running

Every solution is executable, and each tree has one command that runs all of them:

```bash
# C#
cd Coding-Patterns-CSharp/CodingPatterns
dotnet run -- --list     # what's available
dotnet run -- TwoSum     # run one
dotnet run -- --all      # run everything

# Python
cd Coding-Patterns-Python
python3 run.py --list    # what's available
python3 run.py two_sum   # run one
python3 run.py --all     # run everything
```

See [Coding-Patterns-CSharp/README.md](Coding-Patterns-CSharp/README.md) and
[Coding-Patterns-Python/README.md](Coding-Patterns-Python/README.md) for details,
including how to add a new problem.

## 🗂️ Problem Index

| # | Pattern | Problem | Difficulty |
|---|---------|---------|------------|
| 1 | Arrays/Strings | Two Sum | Easy |
| 2 | Arrays/Strings | Best Time to Buy and Sell Stock | Easy |
| 3 | Arrays/Strings | Group Anagrams | Medium |
| 4 | Arrays/Strings | Longest Substring Without Repeating Characters | Medium |
| 5 | Arrays/Strings | Minimum Window Substring | Hard |
| 6 | Trees | Validate BST | Medium |
| 7 | Trees | Lowest Common Ancestor | Medium |
| 8 | Trees | Binary Tree Level Order Traversal | Medium |
| 9 | Trees | Serialize / Deserialize Binary Tree | Hard |
| 10 | Graphs | Number of Islands | Medium |
| 11 | Graphs | Course Schedule | Medium |
| 12 | Graphs | Word Ladder | Hard |
| 13 | Graphs | Clone Graph | Medium |
| 14 | Dynamic Programming | Coin Change | Medium |
| 15 | Dynamic Programming | Longest Increasing Subsequence | Medium |
| 16 | Dynamic Programming | Word Break | Medium |
| 17 | Dynamic Programming | Edit Distance | Medium |
| 18 | Dynamic Programming | House Robber | Medium |
| 19 | Intervals/Greedy | Merge Intervals | Medium |
| 20 | Intervals/Greedy | Non-overlapping Intervals | Medium |
| 21 | Intervals/Greedy | Meeting Rooms II | Medium |
| 22 | Stack/Queue | Valid Parentheses | Easy |
| 23 | Stack/Queue | Daily Temperatures | Medium |
| 24 | Stack/Queue | Min Stack | Medium |
| 25 | Stack/Queue | Largest Rectangle in Histogram | Hard |
| 26 | Stack/Queue | Decode String | Medium |
| 27 | Binary Search | Search in Rotated Sorted Array | Medium |
| 28 | Binary Search | Find Minimum in Rotated Sorted Array | Medium |
| 29 | Binary Search | Koko Eating Bananas | Medium |
| 30 | Binary Search | Leaderboard (design) | Hard |
| 31 | Binary Search | Event Stream Count in Time Range (design) | Medium |
| 32 | Linked Lists | Merge Two Sorted Lists | Easy |
| 33 | Linked Lists | Merge k Sorted Lists | Hard |
| 34 | Linked Lists | Reverse Linked List | Easy |
| 35 | Linked Lists | LRU Cache | Medium |
| 36 | Linked Lists | Happy Number | Easy |
| 37 | Graphs | N-Queens / N-Queens II | Hard |
