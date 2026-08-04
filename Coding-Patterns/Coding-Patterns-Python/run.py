#!/usr/bin/env python3
"""Launcher for the Python solution tree.

Every solution file is a standalone script guarded by `if __name__ == "__main__"`,
so `python3 01_arrays_strings/two_sum.py` always works. This launcher just saves
you from remembering which folder a problem lives in, and gives you one command
that runs the whole tree as a smoke test.

    python3 run.py                 # usage + full problem list
    python3 run.py --list          # full problem list
    python3 run.py two_sum         # run one problem
    python3 run.py 01_arrays_strings/two_sum
    python3 run.py --all           # run every problem
"""

import argparse
import pathlib
import runpy
import sys
import traceback

ROOT = pathlib.Path(__file__).resolve().parent


def discover():
    """Every .py solution in a numbered pattern folder, sorted by category then name."""
    problems = []
    for folder in sorted(ROOT.glob("[0-9][0-9]_*")):
        if not folder.is_dir():
            continue
        for path in sorted(folder.glob("*.py")):
            problems.append((folder.name, path.stem, path))
    return problems


def resolve(problems, query):
    query = query.replace("\\", "/").removesuffix(".py").lower()

    exact = [
        p for p in problems
        if p[1].lower() == query or f"{p[0]}/{p[1]}".lower() == query
    ]
    if exact:
        return exact

    return [p for p in problems if query in f"{p[0]}/{p[1]}".lower()]


def run_one(path, argv=()):
    """Execute a solution file as if it were invoked directly, so its __main__ tests fire."""
    saved_argv = sys.argv
    sys.argv = [str(path), *argv]
    try:
        runpy.run_path(str(path), run_name="__main__")
    finally:
        sys.argv = saved_argv


def run_all(problems):
    failed = []

    for category, name, path in problems:
        print(f"\n===== {category}/{name} =====")
        try:
            run_one(path)
        except Exception:
            traceback.print_exc()
            failed.append(f"{category}/{name}")

    print(f"\n===== {len(problems) - len(failed)}/{len(problems)} ran clean =====")
    for key in failed:
        print(f"  failed: {key}")

    return 1 if failed else 0


def print_list(problems):
    current = None
    for category, name, _ in problems:
        if category != current:
            current = category
            print(category)
        print(f"  {name}")
    print(f"\n{len(problems)} problems.")


def main():
    parser = argparse.ArgumentParser(
        prog="run.py",
        description="Run a Python solution from the coding-patterns tree.",
    )
    parser.add_argument("problem", nargs="?", help="problem name, or category/name")
    parser.add_argument("--list", action="store_true", help="list every problem")
    parser.add_argument("--all", action="store_true", help="run every problem")
    parser.add_argument(
        "rest",
        nargs=argparse.REMAINDER,
        help="arguments forwarded to the solution script",
    )
    args = parser.parse_args()

    problems = discover()

    if args.list:
        print_list(problems)
        return 0

    if args.all:
        return run_all(problems)

    if not args.problem:
        print(__doc__.strip())
        print()
        print_list(problems)
        return 0

    matches = resolve(problems, args.problem)
    if not matches:
        print(f"No problem matches '{args.problem}'.", file=sys.stderr)
        print("Run with --list to see everything available.", file=sys.stderr)
        return 1
    if len(matches) > 1:
        print(f"'{args.problem}' is ambiguous. Did you mean:", file=sys.stderr)
        for category, name, _ in matches:
            print(f"  {category}/{name}", file=sys.stderr)
        return 1

    run_one(matches[0][2], args.rest)
    return 0


if __name__ == "__main__":
    sys.exit(main())
