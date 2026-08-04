# LeetCode 20 - Valid Parentheses
# Difficulty: Easy
# Pattern: Stack matching
#
# Problem: Given a string s of brackets, return True if the input
# string is valid (brackets are correctly opened and closed in order).
#
# Approach: Push open brackets onto stack. On closing bracket,
# check if stack top is the matching open bracket.
#
# Time: O(n)  Space: O(n)

def is_valid(s: str) -> bool:
    stack = []
    pairs = {')': '(', '}': '{', ']': '['}

    for ch in s:
        if ch in pairs:
            if not stack or stack[-1] != pairs[ch]:
                return False
            stack.pop()
        else:
            stack.append(ch)

    return not stack


# ---- Tests ----
if __name__ == "__main__":
    assert is_valid("()") == True
    assert is_valid("()[]{}") == True
    assert is_valid("(]") == False
    assert is_valid("([)]") == False
    assert is_valid("{[]}") == True
    assert is_valid("") == True
    print("All tests passed.")
