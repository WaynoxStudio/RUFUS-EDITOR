#!/usr/bin/env python3
"""LIC.7P — validate session/heartbeat AI fields + backend regression."""
from __future__ import annotations

import json
import os
import sys
import uuid
import urllib.error
import urllib.request

BASE = os.environ.get("RUFUS_LICENSE_API_BASE", "https://vmi3502135.contaboserver.net").rstrip("/")
ADMIN = os.environ.get("RUFUS_ADMIN_API_SECRET_PROD", "")
DEVICE = f"lic7p-probe-{uuid.uuid4().hex[:12]}"
LEGACY = os.environ.get("RUFUS_AI_ACCESS_TOKEN", "")


def req(method, path, *, token=None, body=None):
    url = f"{BASE}{path}"
    headers = {"Accept": "application/json"}
    data = None
    if body is not None:
        data = json.dumps(body).encode()
        headers["Content-Type"] = "application/json"
    if token:
        headers["Authorization"] = f"Bearer {token}"
    r = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(r, timeout=120) as resp:
            raw = resp.read().decode()
            return resp.status, json.loads(raw) if raw else {}
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        try:
            return e.code, json.loads(raw)
        except json.JSONDecodeError:
            return e.code, raw


def admin(method, path, body=None):
    return req(method, path, token=ADMIN, body=body)


def ok(name, cond, detail=""):
    mark = "OK" if cond else "FAIL"
    print(f"[{mark}] {name}" + (f" — {detail}" if detail else ""))
    return cond


def main():
    if not ADMIN:
        print("RUFUS_ADMIN_API_SECRET_PROD required")
        return 1

    passed = 0
    total = 0

    def check(name, cond, detail=""):
        nonlocal passed, total
        total += 1
        if ok(name, cond, detail):
            passed += 1

    st, data = admin("GET", "/v1/admin/licenses")
    check("ADMIN list", st == 200 and isinstance(data, list), f"HTTP {st}")

    st, created = admin("POST", "/v1/admin/licenses", {
        "durationDays": 1,
        "maxDevices": 1,
        "maxConcurrentSessions": 1,
        "permissionEditor": True,
        "permissionAi": True,
        "aiDailyLimit": 10,
        "aiMonthlyLimit": 50,
        "adminNotes": "LIC.7P PRODUCCION",
    })
    check("Create test license", st in (200, 201) and isinstance(created, dict), f"HTTP {st}")
    if not isinstance(created, dict):
        print(f"\nREGRESSION {passed}/{total}")
        return 2

    lid = created["licenseId"]
    code = created["licenseCode"]

    st, act = req("POST", "/v1/license/activate", body={
        "licenseCode": code,
        "deviceId": DEVICE,
        "clientVersion": "LIC7PProbe/1.0",
    })
    session = act.get("sessionToken") if isinstance(act, dict) else None
    check("Activate", st == 200 and bool(session), f"HTTP {st}")

    for field in ("aiDailyLimit", "aiMonthlyLimit", "aiUsageToday", "aiUsageMonth"):
        check(f"Activate has {field}", isinstance(act, dict) and field in act, str(act.get(field) if isinstance(act, dict) else ""))

    if not session:
        print(f"\nREGRESSION {passed}/{total}")
        return 3

    st, sess = req("POST", "/v1/license/session", body={"sessionToken": session, "deviceId": DEVICE})
    check("Session validate", st == 200, f"HTTP {st}")
    for field in ("aiDailyLimit", "aiMonthlyLimit", "aiUsageToday", "aiUsageMonth"):
        check(f"Session has {field}", isinstance(sess, dict) and field in sess, str(sess.get(field) if isinstance(sess, dict) else ""))

    st, hb = req("POST", "/v1/license/heartbeat", body={"sessionToken": session, "deviceId": DEVICE})
    check("Heartbeat", st == 200, f"HTTP {st}")
    check("Heartbeat aiUsageToday", isinstance(hb, dict) and hb.get("aiUsageToday") is not None, str(hb.get("aiUsageToday") if isinstance(hb, dict) else ""))

    # IA generate
    gen_body = {
        "version": 1,
        "action": "generate_name",
        "creativeRequest": {
            "role": "NPC", "attitude": "amable", "narrativeContext": "LIC7P",
            "additionalInstruction": "", "length": "corta", "style": "", "currentNpcName": "T",
        },
        "prompt": {"master": "m", "context": "c", "task": "t"},
    }
    st, _ = req("POST", "/v1/ai/generate", token=session, body=gen_body)
    check("IA SessionToken generate", st == 200, f"HTTP {st}")

    st, det = admin("GET", f"/v1/admin/licenses/{lid}")
    check("ADMIN usage after generate", isinstance(det, dict) and (det.get("aiUsageToday") or 0) >= 1,
          f"usage={det.get('aiUsageToday') if isinstance(det, dict) else '?'}")

    if LEGACY:
        st, _ = req("POST", "/v1/ai/generate", token=LEGACY, body=gen_body)
        check("Legacy rejected", st == 401, f"HTTP {st}")
    else:
        check("Legacy rejected", True, "no local token — VPS legacy OFF assumed")

    print(f"\nREGRESSION {passed}/{total}")
    return 0 if passed == total else 4


if __name__ == "__main__":
    raise SystemExit(main())
