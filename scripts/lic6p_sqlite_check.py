#!/usr/bin/env python3
import os
import paramiko

DB = "/home/ubuntu/RufusAiBackend/data/rufus-licenses.db"
HOST = "169.58.162.70"
USER = os.environ.get("RUFUS_DEPLOY_USER", "ubuntu")
PWD = os.environ["RUFUS_DEPLOY_PWD"]

SCRIPT = f"""
import sqlite3
db = {DB!r}
con = sqlite3.connect(db)
cur = con.cursor()
cur.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY 1")
tables = [r[0] for r in cur.fetchall()]
ver = cur.execute("SELECT schema_version FROM rufus_schema_meta").fetchone()[0]
lic = cur.execute("SELECT COUNT(*) FROM rufus_licenses").fetchone()[0]
cols = [r[1] for r in cur.execute("PRAGMA table_info(rufus_licenses)")]
usage = cur.execute("SELECT COUNT(*) FROM rufus_ai_usage_events").fetchone()[0]
tok = cur.execute(
    "SELECT COUNT(*) FROM rufus_ai_usage_events WHERE input_tokens IS NOT NULL OR output_tokens IS NOT NULL"
).fetchone()[0]
print("SCHEMA_VERSION", ver)
print("LICENSE_COUNT", lic)
print("HAS_AI_DAILY", "ai_daily_limit" in cols)
print("HAS_AI_MONTHLY", "ai_monthly_limit" in cols)
print("HAS_USAGE_TABLE", "rufus_ai_usage_events" in tables)
print("HAS_QUOTA_TABLE", "rufus_ai_quota_counters" in tables)
print("USAGE_EVENTS", usage)
print("EVENTS_WITH_TOKENS", tok)
con.close()
"""

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect(HOST, username=USER, password=PWD, timeout=30, allow_agent=False, look_for_keys=False)
_, stdout, stderr = ssh.exec_command(f"python3 -c {SCRIPT!r}")
print(stdout.read().decode())
err = stderr.read().decode().strip()
if err:
    print("ERR:", err)
ssh.close()
