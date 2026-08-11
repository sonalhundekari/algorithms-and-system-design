# Min Coins to Pay When Change Is Allowed (Coin Change with overpayment)
# Difficulty: Medium (Hard if you don't spot the overpayment branch)
# Pattern: 1D unbounded-knapsack DP + a bounded search over how much to overpay
#
# Denominations {1, 5, 10, 50, 100, 200}, unlimited supply. Pay an exact amount
# n -- except you are allowed to OVERPAY, and the recipient hands back exact
# change out of the same denominations. Minimize the total number of coins that
# change hands: coins you paid PLUS coins you got back.
#
#     n = 41  ->  3     pay 50 + 1 = 51, get a 10 back.  2 paid + 1 returned
#
# This is the whole trap. Plain LeetCode 322 on 41 gives 5 (10+10+10+10+1), and
# candidates who pattern-match to 322 and stop there fail the round. The coins
# coming back are coins too, but they are cheap: one 50 buys you four 10s.
#
# THE REDUCTION. A transaction is fully described by what you hand over. If you
# hand over `paid >= n`, the change is forced -- it is exactly `paid - n`, and
# both sides will use the fewest coins they can. So:
#
#     answer = min over paid >= n of  coins(paid) + coins(paid - n)
#
# where coins(x) is ordinary 322: fewest coins summing to exactly x. Note the
# two halves are independent -- the payer's coins and the recipient's coins are
# separate piles -- which is why this decomposes so cleanly.
#
# HOW FAR UP DOES `paid` GO? Unbounded, `paid` is infinite; we need a cutoff.
# Write paid = n + c, so c >= 0 is the change amount, and minimize
# coins(n + c) + coins(c). For this denomination set the largest coin D = 200
# satisfies
#
#     coins(x + 200) = coins(x) + 1        (one more 200, same remainder)
#
# so if c >= 200 then
#
#     coins(n + c) + coins(c) = coins(n + c - 200) + coins(c - 200) + 2
#
# -- strictly worse by 2 coins. Handing over an extra 200 and getting a 200
# straight back is never anything but two wasted coins. So c in [0, 199] is
# enough; the loop below runs c in [0, 200] because the extra iteration costs
# nothing and makes the bound obviously safe. State this bound out loud; "I'll
# try a few hundred over" is the part interviewers push on.
#
# WHY GREEDY IS SAFE HERE (and only here). {1, 5, 10, 50, 100, 200} is a
# divisibility chain: 1|5, 5|10, 10|50, 50|100, 100|200. When each coin divides
# the next, no optimal solution can hold d_{i+1}/d_i coins of d_i -- swap them
# for one d_{i+1} and you saved coins. So everything below d_j sums to less
# than d_j, which forces the count of d_j to be exactly floor(remaining / d_j):
# greedy. Say this before using greedy. For a non-canonical set ({1,3,4}, 6:
# greedy 4+1+1 = 3 coins, optimal 3+3 = 2) you must use the DP.
#
# Time:  O((n + D) * len(denoms)) to build the table, O(D) for the sweep
# Space: O(n + D)

from typing import Dict, List, Tuple

DENOMS = (1, 5, 10, 50, 100, 200)


def coins_table(limit: int, denoms=DENOMS) -> List[float]:
    """dp[x] = fewest coins summing to exactly x, for every x in [0, limit].

    Plain LeetCode 322, tabulated once for all amounts instead of re-solved per
    amount -- that reuse is what keeps the outer sweep cheap.
    """
    dp = [float('inf')] * (limit + 1)
    dp[0] = 0
    for x in range(1, limit + 1):
        for coin in denoms:
            if coin <= x and dp[x - coin] + 1 < dp[x]:
                dp[x] = dp[x - coin] + 1
    return dp


def coins_to_make_greedy(x: int, denoms=DENOMS) -> int:
    """Same value as coins_table(x)[x], but O(len(denoms)) and no table.

    Correct ONLY because this denomination set is a divisibility chain -- see
    the header. Kept as the answer to "can you do it without the DP?".
    """
    count = 0
    for coin in sorted(denoms, reverse=True):
        count += x // coin
        x %= coin
    return count


def min_coins_with_change(n: int, denoms=DENOMS) -> int:
    """Fewest coins exchanged in total (paid + returned) to settle n."""
    if n <= 0:
        return 0

    max_denom = max(denoms)
    dp = coins_table(n + max_denom, denoms)

    # c is the change handed back; paid = n + c.
    return min(dp[n + c] + dp[c] for c in range(max_denom + 1))


def min_coins_with_change_explained(n: int, denoms=DENOMS) -> Tuple[int, int, Dict[int, int], Dict[int, int]]:
    """(total_coins, paid_amount, coins_paid, coins_returned) -- for the demo.

    Same sweep, but it keeps the winning `paid` and reconstructs both piles, so
    you can show the interviewer the actual transaction and not just a number.
    """
    if n <= 0:
        return 0, 0, {}, {}

    max_denom = max(denoms)
    dp = coins_table(n + max_denom, denoms)

    best_total, best_paid = dp[n], n
    for c in range(1, max_denom + 1):
        total = dp[n + c] + dp[c]
        if total < best_total:
            best_total, best_paid = total, n + c

    return best_total, best_paid, _breakdown(best_paid, denoms), _breakdown(best_paid - n, denoms)


def _breakdown(x: int, denoms=DENOMS) -> Dict[int, int]:
    """Which coins make up x, greedily (valid on a divisibility chain)."""
    out = {}
    for coin in sorted(denoms, reverse=True):
        if x >= coin:
            out[coin], x = x // coin, x % coin
    return out


# ---- Tests ----
if __name__ == "__main__":
    # The interview example, worked by hand in the header.
    assert min_coins_with_change(41) == 3          # 50 + 1 paid, 10 back
    assert coins_table(41)[41] == 5                # what plain LC 322 answers

    # Edge cases.
    assert min_coins_with_change(0) == 0           # nothing changes hands
    assert min_coins_with_change(-7) == 0          # guard
    assert min_coins_with_change(1) == 1
    assert min_coins_with_change(200) == 1         # multiple of the largest coin
    assert min_coins_with_change(400) == 2
    assert min_coins_with_change(199) == 2         # 200 paid, 1 back
    assert min_coins_with_change(3) == 3           # 1+1+1; overpaying can't help

    # Greedy agrees with the DP on this denomination set, everywhere.
    dp = coins_table(3000)
    assert all(coins_to_make_greedy(x) == dp[x] for x in range(3001))

    # ...and is wrong on a non-canonical one, which is why the DP stays.
    assert coins_to_make_greedy(6, (1, 3, 4)) == 3      # 4+1+1
    assert coins_table(6, (1, 3, 4))[6] == 2            # 3+3

    # Brute force: search paid far past the proven cutoff and confirm nothing
    # beyond n + 200 ever wins.
    wide = coins_table(2000 + 5000)
    for n in range(0, 2000):
        want = min(wide[n + c] + wide[c] for c in range(5000))
        assert min_coins_with_change(n) == want, n

    # The reconstruction really settles the bill.
    total, paid, out, back = min_coins_with_change_explained(41)
    assert (total, paid, out, back) == (3, 51, {50: 1, 1: 1}, {10: 1})
    assert sum(c * k for c, k in out.items()) - sum(c * k for c, k in back.items()) == 41

    for n in (0, 1, 3, 41, 199, 200, 244, 999):
        total, paid, out, back = min_coins_with_change_explained(n)
        assert sum(c * k for c, k in out.items()) - sum(c * k for c, k in back.items()) == n
        assert sum(out.values()) + sum(back.values()) == total == min_coins_with_change(n)
        print(f"n={n:>4}  pay {paid:>4} as {out or '{}'}, get back {back or '{}'}  ->  {total} coins")

    print("All tests passed.")
