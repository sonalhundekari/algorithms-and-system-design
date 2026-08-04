# Coding Patterns — Python

Runnable Python solutions, organized by pattern. Requires Python 3.9+ and no
third-party packages — everything uses the standard library.

## Running

Each solution is a standalone script, so you can always run a file directly:

```bash
python3 01_arrays_strings/two_sum.py
```

`run.py` is a convenience launcher, so you don't have to remember which folder a
problem lives in:

```bash
python3 run.py                          # usage + full problem list
python3 run.py --list                   # full problem list
python3 run.py two_sum                  # run one problem
python3 run.py 01_arrays_strings/two_sum
python3 run.py --all                    # run every problem (smoke test)
```

Names are matched case-insensitively, and a partial name works as long as it is
unambiguous.

## Layout

```
Coding-Patterns-Python/
├── run.py
├── 01_arrays_strings/
├── 02_trees/
├── 03_graphs/
├── 04_dynamic_programming/
├── 05_greedy/
├── 06_stack_queue/
├── 07_binary_search/
├── 08_linked_lists/
└── 09_concurrency/
```

Each pattern folder carries the same `README.md` as its C# counterpart: pattern
overview, problem list, and a cheat sheet.

## How a solution file is wired up

Each file holds the solution plus its own tests under a `__main__` guard:

```python
def two_sum(nums: List[int], target: int) -> List[int]:
    ...


# ---- Tests ----
if __name__ == "__main__":
    assert two_sum([2, 7, 11, 15], 9) == [0, 1]
    print("All tests passed.")
```

`run.py` executes the file with `run_name="__main__"`, so that block fires
exactly as it would on a direct run. **Adding a new problem needs no
registration** — drop a `.py` file into a pattern folder and it shows up in
`--list`.
