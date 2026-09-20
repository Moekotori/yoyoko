"""Sample an existing desktop PID; does not infer memory for unimplemented features.
Run: python3 scripts/measure_idle.py PID --seconds 10
macOS/Linux only. RSS is KiB as reported by ps. Use Release without a debugger.
"""
import argparse
import json
import subprocess
import time

parser = argparse.ArgumentParser()
parser.add_argument("pid", type=int)
parser.add_argument("--seconds", type=int, default=10)
args = parser.parse_args()
samples = []
for _ in range(max(1, min(args.seconds, 60))):
    output = subprocess.check_output(["ps", "-p", str(args.pid), "-o", "rss=,%cpu="], text=True).split()
    samples.append({"rss_mib": round(int(output[0]) / 1024, 2), "cpu_percent_ps_lifetime": float(output[1])})
    time.sleep(1)
print(json.dumps({"pid": args.pid, "samples": samples, "scope": "one process; add worker PIDs when media exists"}, indent=2))
