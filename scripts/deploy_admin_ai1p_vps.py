#!/usr/bin/env python3
"""ADMIN.AI.1P — deploy AiBackend preserving rufus-ai.env (no secret rewrite). Never prints secrets."""
from __future__ import annotations

import os
import sys
import time
from pathlib import Path

import paramiko

HOST = "169.58.162.70"
REMOTE_DIR = "/home/ubuntu/RufusAiBackend"
REMOTE_ENV = f"{REMOTE_DIR}/rufus-ai.env"
REMOTE_DATA = f"{REMOTE_DIR}/data"
DB_PATH = os.environ.get(
    "RUFUS_LICENSE_DB_PATH_PROD", f"{REMOTE_DATA}/rufus-licenses.db"
)
LOCAL_PUBLISH = Path(os.environ["RUFUS_DEPLOY_LOCAL"])
USER = os.environ.get("RUFUS_DEPLOY_USER", "ubuntu")
PWD = os.environ["RUFUS_DEPLOY_PWD"]


def run(ssh: paramiko.SSHClient, cmd: str, check: bool = True) -> str:
    stdin, stdout, stderr = ssh.exec_command(cmd)
    out = stdout.read().decode()
    err = stderr.read().decode()
    code = stdout.channel.recv_exit_status()
    if check and code != 0:
        raise RuntimeError(f"cmd failed ({code}): {cmd}\n{err}\n{out}")
    return out


def main() -> int:
    if not LOCAL_PUBLISH.is_dir():
        print("MISSING local publish", LOCAL_PUBLISH)
        return 1

    ssh = paramiko.SSHClient()
    ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    ssh.connect(
        HOST,
        username=USER,
        password=PWD,
        timeout=30,
        allow_agent=False,
        look_for_keys=False,
    )
    sftp = ssh.open_sftp()

    stamp = time.strftime("%Y%m%d-%H%M%S")
    backup_root = f"/home/ubuntu/backups/RufusAiBackend-{stamp}"
    sqlite_backup = f"/home/ubuntu/backups/licenses/rufus-licenses-{stamp}.db"

    print("=== backup SQLite (copy only; no schema change) ===")
    run(
        ssh,
        f"mkdir -p /home/ubuntu/backups/licenses && "
        f"test -f {DB_PATH} && cp -a {DB_PATH} {sqlite_backup} && chmod 600 {sqlite_backup} "
        f"|| echo NO_DB_YET",
    )
    print("sqlite_backup:", sqlite_backup)

    print("=== stop service ===")
    run(ssh, "sudo systemctl stop rufus-ai")

    print("=== backup backend tree ===")
    run(ssh, f"mkdir -p /home/ubuntu/backups && cp -a {REMOTE_DIR} {backup_root}")
    run(
        ssh,
        f"cp -a {REMOTE_ENV} {backup_root}/rufus-ai.env.bak && chmod 600 {backup_root}/rufus-ai.env.bak",
    )
    print("backup:", backup_root)

    # Snapshot env fingerprint before upload (keys + file size/mtime) — no values
    before = run(
        ssh,
        f"stat -c '%s %Y' {REMOTE_ENV}; "
        f"awk -F= '{{print $1}}' {REMOTE_ENV} | paste -sd, -",
    ).strip()
    print("env_before:", before.replace("\n", " | "))

    print("=== upload publish (never overwrite rufus-ai.env) ===")
    files = [p for p in LOCAL_PUBLISH.rglob("*") if p.is_file()]
    uploaded = 0
    for p in files:
        rel = p.relative_to(LOCAL_PUBLISH).as_posix()
        if rel == "rufus-ai.env" or rel.endswith("/rufus-ai.env"):
            print("skip env in publish")
            continue
        remote = f"{REMOTE_DIR}/{rel}"
        remote_parent = remote.rsplit("/", 1)[0]
        run(ssh, f"mkdir -p {remote_parent}", check=False)
        sftp.put(str(p), remote)
        uploaded += 1
        if uploaded % 50 == 0:
            print(f"  uploaded {uploaded}/{len(files)}")
    print(f"  uploaded {uploaded} files")

    print("=== chmod binary + data dir ===")
    run(ssh, f"chmod +x {REMOTE_DIR}/RufusMapEditor.AiBackend")
    run(ssh, f"mkdir -p {REMOTE_DATA} && chmod 700 {REMOTE_DATA}")

    print("=== preserve env (optional session minutes default note only) ===")
    # Do NOT rewrite secrets. Optionally ensure RUFUS_ADMIN_AI_SESSION_MINUTES exists;
    # if missing, code default=60 applies. We leave env unchanged unless asked.
    after = run(
        ssh,
        f"stat -c '%s %Y' {REMOTE_ENV}; "
        f"awk -F= '{{print $1}}' {REMOTE_ENV} | paste -sd, -; "
        f"grep -E '^RUFUS_AI_LEGACY_TOKEN_ENABLED=' {REMOTE_ENV} >/dev/null "
        f"&& echo LEGACY_LINE=present || echo LEGACY_LINE=unset; "
        f"grep -E '^RUFUS_ADMIN_AI_SESSION_MINUTES=' {REMOTE_ENV} >/dev/null "
        f"&& echo SESSION_MINUTES_LINE=present || echo SESSION_MINUTES_LINE=unset_default_60",
    ).strip()
    print("env_after:", after.replace("\n", " | "))

    # Verify env file bytes unchanged vs bak
    same = run(
        ssh,
        f"cmp -s {REMOTE_ENV} {backup_root}/rufus-ai.env.bak && echo ENV_UNCHANGED || echo ENV_CHANGED",
    ).strip()
    print(same)
    if same != "ENV_UNCHANGED":
        raise RuntimeError("rufus-ai.env was modified — abort start")

    print("=== start service ===")
    run(ssh, "sudo systemctl start rufus-ai && sleep 2 && systemctl is-active rufus-ai")
    status = run(ssh, "systemctl is-active rufus-ai").strip()
    print("status:", status)
    run(ssh, f"test -f {REMOTE_DIR}/RufusMapEditor.Licensing.dll && echo HAS_LICENSING")
    # Safe journal lines (filter out Authorization-looking noise if any)
    journal = run(ssh, "journalctl -u rufus-ai -n 40 --no-pager")
    for line in journal.splitlines():
        low = line.lower()
        if any(x in low for x in ("authorization", "bearer ", "openai", "api_key", "rai1.")):
            print("[journal redacted line]")
        else:
            print(line)

    # SQLite schema fingerprint only
    run(
        ssh,
        f"python3 - <<'PY'\n"
        f"import sqlite3\n"
        f"db = {DB_PATH!r}\n"
        f"con = sqlite3.connect(db)\n"
        f"cur = con.cursor()\n"
        f"cur.execute(\"SELECT name FROM sqlite_master WHERE type='table' ORDER BY 1\")\n"
        f"tables = [r[0] for r in cur.fetchall()]\n"
        f"ver = None\n"
        f"if 'rufus_schema_meta' in tables:\n"
        f"    row=cur.execute(\"SELECT value FROM rufus_schema_meta WHERE key='schema_version'\").fetchone()\n"
        f"    ver = row[0] if row else None\n"
        f"lic = cur.execute('SELECT COUNT(*) FROM rufus_licenses').fetchone()[0] if 'rufus_licenses' in tables else -1\n"
        f"print('SCHEMA_VERSION', ver)\n"
        f"print('LICENSE_COUNT', lic)\n"
        f"print('HAS_AI_USAGE', 'rufus_ai_usage_events' in tables)\n"
        f"con.close()\n"
        f"PY",
    )

    sftp.close()
    ssh.close()
    print("DEPLOY_COMPLETE")
    print("BACKUP", backup_root)
    print("SQLITE_BACKUP", sqlite_backup)
    return 0 if status == "active" else 2


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as e:
        print("DEPLOY_ERROR", type(e).__name__, str(e))
        raise
