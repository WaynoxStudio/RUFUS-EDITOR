#!/usr/bin/env python3
"""Post-deploy VPS checks. Never prints secrets."""
import os
import paramiko

HOST = "169.58.162.70"
USER = os.environ.get("RUFUS_DEPLOY_USER", "ubuntu")
PWD = os.environ["RUFUS_DEPLOY_PWD"]
REMOTE_ENV = "/home/ubuntu/RufusAiBackend/rufus-ai.env"
DB = "/home/ubuntu/RufusAiBackend/data/rufus-licenses.db"

SQL = """
import sqlite3
db = %r
con = sqlite3.connect(db)
cur = con.cursor()
cur.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY 1")
tables = [r[0] for r in cur.fetchall()]
ver = None
if 'rufus_schema_meta' in tables:
    cur.execute('SELECT schema_version FROM rufus_schema_meta LIMIT 1')
    row = cur.fetchone()
    ver = row[0] if row else None
lic = cur.execute('SELECT COUNT(*) FROM rufus_licenses').fetchone()[0]
cols = [r[1] for r in cur.execute('PRAGMA table_info(rufus_licenses)')]
usage = cur.execute('SELECT COUNT(*) FROM rufus_ai_usage_events').fetchone()[0] if 'rufus_ai_usage_events' in tables else 0
print('SCHEMA_VERSION', ver)
print('LICENSE_COUNT', lic)
print('HAS_AI_DAILY', 'ai_daily_limit' in cols)
print('HAS_AI_USAGE_TABLE', 'rufus_ai_usage_events' in tables)
print('USAGE_EVENTS', usage)
con.close()
""" % DB


def run(ssh, cmd):
    _, stdout, stderr = ssh.exec_command(cmd)
    out = stdout.read().decode().strip()
    err = stderr.read().decode().strip()
    code = stdout.channel.recv_exit_status()
    return code, out, err


def main():
    ssh = paramiko.SSHClient()
    ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    ssh.connect(HOST, username=USER, password=PWD, timeout=30, allow_agent=False, look_for_keys=False)

    for label, cmd in [
        ("service", "systemctl is-active rufus-ai"),
        ("logs", "journalctl -u rufus-ai -n 15 --no-pager | grep -E 'IA AUTH|IA QUOTA|error|Error' || journalctl -u rufus-ai -n 8 --no-pager"),
        ("sqlite", f"sqlite3 {DB} \"SELECT schema_version FROM rufus_schema_meta;\" 2>/dev/null; sqlite3 {DB} \"SELECT COUNT(*) FROM rufus_licenses;\"; sqlite3 {DB} \"PRAGMA table_info(rufus_licenses);\" | grep ai_daily || true; sqlite3 {DB} \".tables\" | tr ' ' '\\n' | grep rufus_ai || true"),
        ("env_keys", f"grep -E '^(RUFUS_AI_LEGACY_TOKEN_ENABLED|RUFUS_LICENSE_DB_PATH|RUFUS_ADMIN_API_SECRET|RUFUS_AI_ACCESS_TOKEN|OPENAI_API_KEY)=' {REMOTE_ENV} | sed 's/=.*$/=***/' || true"),
        ("backups", "ls -la /home/ubuntu/backups/licenses/ | tail -5"),
    ]:
        print(f"=== {label} ===")
        code, out, err = run(ssh, cmd)
        print(out)
        if err:
            print("ERR:", err)
        print()

    ssh.close()


if __name__ == "__main__":
    main()
