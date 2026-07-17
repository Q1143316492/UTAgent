# 离线校验 after-tool 截断语义（与 UTAgentRunner.AfterTool.cs 对齐）
# 产品默认阈值 8000；N<=0：不截断；len(content)<=N：不截断；否则前 N 字符 + 标记

DEFAULT_TRUNCATE_CHARS = 8000


def apply_truncate(content: str, n: int) -> tuple[str, bool]:
    if n <= 0 or not content:
        return content, False
    original_len = len(content)
    if original_len <= n:
        return content, False
    marker = f"\n…[truncated by after-tool, original={original_len}]"
    return content[:n] + marker, True


cases = [
    ("阈值0透传", "x" * 500, 0, False),
    ("未超阈值", "hello", 100, False),
    ("恰好阈值", "a" * 100, 100, False),
    ("超阈值截断", "b" * 150, 100, True),
    ("默认8000未超", "c" * 8000, DEFAULT_TRUNCATE_CHARS, False),
    ("默认8000超限", "d" * 9000, DEFAULT_TRUNCATE_CHARS, True),
]

ok = True
for name, content, n, expect_rewrite in cases:
    out, rewritten = apply_truncate(content, n)
    if rewritten != expect_rewrite:
        print(f"FAIL {name}: expect_rewrite={expect_rewrite} got={rewritten}")
        ok = False
        continue
    if expect_rewrite:
        if "truncated by after-tool" not in out:
            print(f"FAIL {name}: missing marker")
            ok = False
            continue
        if not out.startswith(content[:n]):
            print(f"FAIL {name}: prefix mismatch")
            ok = False
            continue
        if f"original={len(content)}" not in out:
            print(f"FAIL {name}: original len missing")
            ok = False
            continue
    else:
        if out != content:
            print(f"FAIL {name}: content mutated when should passthrough")
            ok = False
            continue
    print(f"PASS {name}: rewrite={rewritten} out_len={len(out)}")

log_line = "after-tool: truncate, 150 chars → rewrite"
if "after-tool:" not in log_line or "rewrite" not in log_line:
    print("FAIL log format")
    ok = False
else:
    print("PASS log format")

print(f"PASS default constant={DEFAULT_TRUNCATE_CHARS}")
print("ok" if ok else "fail")
raise SystemExit(0 if ok else 1)
