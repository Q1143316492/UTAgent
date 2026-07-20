"""One-shot crash repro rate probe via utagent CLI. Not a product feature."""
import json
import subprocess
import sys
import time
from pathlib import Path

CLI = Path(r"D:\Unity\Src\EqZeroUT2\Assets\UTAgent\Tools\utagent-cli\utagent.py")
OPTIN = Path(r"D:\Unity\Src\EqZeroUT2\Assets\UTAgent\Tools\ui-benchmark\opt-in")
ROUNDS = 10
VARIANTS = [
    ("A_ui_only", OPTIN / "repro_crash_A_ui_only.py"),
    ("B_save_only", OPTIN / "repro_crash_B_save_only.py"),
    ("C_full_combo", OPTIN / "repro_crash_C_full_combo.py"),
]


def run(args):
    p = subprocess.run(
        [sys.executable, str(CLI), *args],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def alive():
    code, out = run(["ping"])
    return code == 0 and "editor_alive: True" in out and "engine_available: True" in out, out


def main():
    results = []
    crashed = False
    crash_at = None
    t0 = time.time()
    ok_ping, ping_out = alive()
    print("initial ping ok=", ok_ping)
    if not ok_ping:
        print(ping_out)
        return 2

    for name, path in VARIANTS:
        for i in range(1, ROUNDS + 1):
            label = f"{name}#{i}"
            print(f"---- {label} ----", flush=True)
            code, out = run(["exec", "--file", str(path)])
            time.sleep(0.15)
            ok_alive, ping_out = alive()
            entry = {
                "variant": name,
                "round": i,
                "exec_exit": code,
                "alive": ok_alive,
                "ok": code == 0 and ok_alive,
                "snippet": out[:200].replace("\n", " "),
            }
            results.append(entry)
            print(
                f"  exec={code} alive={ok_alive} ok={entry['ok']}",
                flush=True,
            )
            if not ok_alive:
                crashed = True
                crash_at = label
                print("CRASH/DISCONNECT")
                print(out)
                print(ping_out)
                break
        if crashed:
            break

    summary = {
        "elapsed_s": round(time.time() - t0, 1),
        "crashed": crashed,
        "crash_at": crash_at,
        "by_variant": {},
        "results": results,
    }
    for name, _ in VARIANTS:
        group = [r for r in results if r["variant"] == name]
        summary["by_variant"][name] = {
            "ok": sum(1 for r in group if r["ok"]),
            "total": len(group),
        }
    out_path = OPTIN / "repro_crash_results.json"
    out_path.write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    print("==== SUMMARY ====")
    print(json.dumps({k: summary[k] for k in ("elapsed_s", "crashed", "crash_at", "by_variant")}, indent=2))
    print("wrote", out_path)
    return 1 if crashed else 0


if __name__ == "__main__":
    raise SystemExit(main())