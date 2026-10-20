#!/usr/bin/env python3
"""Deploy RufusAiBackend to VPS. Never prints secret values."""
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
LOCAL_PUBLISH = Path(os.environ["RUFUS_DEPLOY_LOCAL"])
USER = os.environ.get("RUFUS_DEPLOY_USER", "ubuntu")
PWD = os.environ["RUFUS_DEPLOY_PWD"]
ADMIN_SECRET = os.environ["RUFUS_ADMIN_API_SECRET_PROD"]

APACHE_CONF = """ProxyPreserveHost On

# DIALOG.AI.BACKEND_TIMEOUT.DIAGNOSE.2 — conversation OpenAI can exceed default Apache 60s.
ProxyTimeout 240

ProxyPass        /v1/ai/ http://127.0.0.1:5088/v1/ai/ timeout=240
ProxyPassReverse /v1/ai/ http://127.0.0.1:5088/v1/ai/

ProxyPass        /v1/admin/ http://127.0.0.1:5088/v1/admin/ timeout=60
ProxyPassReverse /v1/admin/ http://127.0.0.1:5088/v1/admin/

<Location "/v1/ai/">
    Require all granted
</Location>

<Location "/v1/admin/">
    Require all granted
</Location>
"""


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

    print("=== stop service ===")
    run(ssh, "sudo systemctl stop rufus-ai")

    print("=== backup ===")
    run(ssh, f"mkdir -p /home/ubuntu/backups && cp -a {REMOTE_DIR} {backup_root}")
    run(ssh, f"cp -a {REMOTE_ENV} {backup_root}/rufus-ai.env.bak && chmod 600 {backup_root}/rufus-ai.env.bak")
    print("backup:", backup_root)

    print("=== upload publish (exclude env) ===")
    files = [p for p in LOCAL_PUBLISH.rglob("*") if p.is_file()]
    for i, p in enumerate(files, 1):
        rel = p.relative_to(LOCAL_PUBLISH).as_posix()
        remote = f"{REMOTE_DIR}/{rel}"
        remote_parent = remote.rsplit("/", 1)[0]
        run(ssh, f"mkdir -p {remote_parent}", check=False)
        sftp.put(str(p), remote)
        if i % 50 == 0:
            print(f"  uploaded {i}/{len(files)}")
    print(f"  uploaded {len(files)} files")

    print("=== chmod binary + data dir ===")
    run(ssh, f"chmod +x {REMOTE_DIR}/RufusMapEditor.AiBackend")
    run(ssh, f"mkdir -p {REMOTE_DATA} && chmod 700 {REMOTE_DATA}")

    print("=== merge env keys (no overwrite OpenAI/AI token) ===")
    tmp_add = f"/tmp/rufus-env-add-{stamp}.env"
    with sftp.file(tmp_add, "w") as f:
        f.write(f"RUFUS_ADMIN_API_SECRET={ADMIN_SECRET}\n")
    run(ssh, f"chmod 600 {tmp_add}")
    merge_script = f"""
python3 - <<'PY'
from pathlib import Path
path = Path({REMOTE_ENV!r})
add_path = Path({tmp_add!r})
text = path.read_text(encoding='utf-8') if path.exists() else ''
lines = [l for l in text.splitlines() if l.strip()]
# drop old admin/license keys to replace admin; strip obsolete license DB path
lines = [l for l in lines if not l.startswith('RUFUS_ADMIN_API_SECRET=') and not l.startswith('RUFUS_LICENSE_DB_PATH=')]
add_lines = [l for l in add_path.read_text(encoding='utf-8').splitlines() if l.strip()]
new_text = '\\n'.join(lines + add_lines) + '\\n'
path.write_text(new_text, encoding='utf-8')
path.chmod(0o600)
add_path.unlink(missing_ok=True)
keys = sorted({{l.split('=',1)[0] for l in path.read_text().splitlines() if '=' in l and not l.strip().startswith('#')}})
print('env_keys', keys)
PY
"""
    run(ssh, merge_script)

    print("=== apache proxy ai/admin ===")
    conf_b64 = __import__("base64").b64encode(APACHE_CONF.encode()).decode()
    run(
        ssh,
        f"echo {conf_b64} | base64 -d | sudo tee /etc/apache2/conf-available/rufus-ai.conf >/dev/null "
        f"&& sudo apache2ctl configtest && sudo systemctl reload apache2",
    )

    print("=== start service ===")
    run(ssh, "sudo systemctl start rufus-ai && sleep 2 && systemctl is-active rufus-ai")
    status = run(ssh, "systemctl is-active rufus-ai").strip()
    print("status:", status)
    run(ssh, f"test -f {REMOTE_DIR}/RufusMapEditor.AiBackend.dll && echo HAS_AIBACKEND")
    run(ssh, "journalctl -u rufus-ai -n 30 --no-pager")

    sftp.close()
    ssh.close()
    print("DEPLOY_COMPLETE")
    return 0 if status == "active" else 2


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as e:
        print("DEPLOY_ERROR", type(e).__name__, str(e))
        raise
