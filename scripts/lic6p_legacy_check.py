#!/usr/bin/env python3
"""Verify legacy AI token rejected when RUFUS_AI_LEGACY_TOKEN_ENABLED is OFF."""
import json
import os
import re
import urllib.error
import urllib.request

import paramiko

HOST = "169.58.162.70"
USER = os.environ.get("RUFUS_DEPLOY_USER", "ubuntu")
PWD = os.environ["RUFUS_DEPLOY_PWD"]
BASE = os.environ.get("RUFUS_LICENSE_API_BASE", "https://vmi3502135.contaboserver.net").rstrip("/")
ENV = "/home/ubuntu/RufusAiBackend/rufus-ai.env"


def read_env_key(ssh, key: str) -> str | None:
    _, stdout, _ = ssh.exec_command(f"grep -E '^{key}=' {ENV} | head -1")
    line = stdout.read().decode().strip()
    if not line or "=" not in line:
        return None
    return line.split("=", 1)[1]


def try_generate(token: str) -> int:
    body = {
        "version": 1,
        "action": "generate_name",
        "creativeRequest": {
            "role": "x", "attitude": "x", "narrativeContext": "x",
            "additionalInstruction": "", "length": "corta", "style": "", "currentNpcName": "",
        },
        "prompt": {"master": "m", "context": "c", "task": "t"},
    }
    req = urllib.request.Request(
        f"{BASE}/v1/ai/generate",
        data=json.dumps(body).encode(),
        headers={
            "Authorization": f"Bearer {token}",
            "Content-Type": "application/json",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            return resp.status
    except urllib.error.HTTPError as e:
        return e.code


def main():
    ssh = paramiko.SSHClient()
    ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    ssh.connect(HOST, username=USER, password=PWD, timeout=30, allow_agent=False, look_for_keys=False)

    legacy_flag = read_env_key(ssh, "RUFUS_AI_LEGACY_TOKEN_ENABLED")
    token = read_env_key(ssh, "RUFUS_AI_ACCESS_TOKEN")
    ssh.close()

    enabled = legacy_flag and legacy_flag.lower() in ("1", "true", "yes", "on")
    print("LEGACY_FLAG", "ENABLED" if enabled else "OFF/unset")
    if enabled:
        print("SKIP legacy rejection test — flag enabled on VPS")
        return 0
    if not token:
        print("NO legacy token in env")
        return 1

    status = try_generate(token)
    ok = status == 401
    print("LEGACY_REJECTED", "OK" if ok else "FAIL", f"HTTP {status}")
    return 0 if ok else 2


if __name__ == "__main__":
    raise SystemExit(main())
