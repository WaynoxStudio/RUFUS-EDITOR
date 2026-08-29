#!/usr/bin/env python3
"""ADMIN.AI.1P production validation. Never prints secrets or full rai1 tokens."""
from __future__ import annotations

import json
import os
import sys
import urllib.error
import urllib.request
from datetime import datetime, timezone

BASE = os.environ.get("RUFUS_API_BASE", "https://vmi3502135.contaboserver.net").rstrip("/")
ADMIN = os.environ["RUFUS_ADMIN_API_SECRET_PROD"]
RESULTS: list[tuple[str, bool, str]] = []


def add(name: str, ok: bool, detail: str = "") -> None:
    RESULTS.append((name, ok, detail))
    print(f"{'OK' if ok else 'FAIL'}  {name}" + (f"  ({detail})" if detail else ""))


def req(method: str, path: str, *, token: str | None = None, body: dict | None = None, timeout: int = 90):
    data = None if body is None else json.dumps(body).encode("utf-8")
    headers = {"Accept": "application/json"}
    if data is not None:
        headers["Content-Type"] = "application/json"
    if token is not None:
        headers["Authorization"] = f"Bearer {token}"
    r = urllib.request.Request(BASE + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(r, timeout=timeout) as resp:
            raw = resp.read().decode("utf-8", errors="replace")
            try:
                parsed = json.loads(raw) if raw else None
            except json.JSONDecodeError:
                parsed = None
            return resp.status, parsed, raw
    except urllib.error.HTTPError as e:
        raw = e.read().decode("utf-8", errors="replace")
        try:
            parsed = json.loads(raw) if raw else None
        except json.JSONDecodeError:
            parsed = None
        return e.code, parsed, raw


def sample_generate_body(action: str = "generate_name") -> dict:
    return {
        "version": 1,
        "action": action,
        "creativeRequest": {
            "role": "NPC comerciante",
            "attitude": "amable",
            "narrativeContext": "ADMIN.AI.1P prueba produccion",
            "additionalInstruction": "Respuesta breve de prueba",
            "length": "corta",
            "style": "fantasia",
            "currentNpcName": "TestNpc",
        },
        "prompt": {
            "master": "Eres un asistente creativo para un editor de mapas.",
            "context": "Prueba ADMIN.AI.1P",
            "task": "Generar contenido de prueba",
        },
    }


def main() -> int:
    # 1) Admin licenses list (regression)
    st, body, _ = req("GET", "/v1/admin/licenses", token=ADMIN)
    add("admin_list_licenses", st == 200 and isinstance(body, list), f"status={st}")

    # 2) ai-session without auth
    st, _, _ = req("POST", "/v1/admin/ai-session", body={"x": 1})
    add("ai_session_no_auth", st in (401, 403), f"status={st}")

    # 3) ai-session wrong secret
    st, _, _ = req("POST", "/v1/admin/ai-session", token="wrong-admin-secret-xxxxxxxxx", body=None)
    add("ai_session_bad_auth", st in (401, 403), f"status={st}")

    # 4) ai-session valid
    st, body, _ = req("POST", "/v1/admin/ai-session", token=ADMIN, body=None)
    token = (body or {}).get("accessToken") if isinstance(body, dict) else None
    exp = (body or {}).get("expiresAt") if isinstance(body, dict) else None
    prefix_ok = isinstance(token, str) and token.startswith("rai1.")
    add("ai_session_issue", st == 200 and prefix_ok and bool(exp), f"status={st} prefix={'rai1' if prefix_ok else 'no'}")
    if not (st == 200 and prefix_ok):
        return 2

    # 5) Admin secret direct on generate — REJECT
    st, body, _ = req("POST", "/v1/ai/generate", token=ADMIN, body=sample_generate_body())
    add("admin_secret_on_generate", st == 401, f"status={st}")

    # 6) Admin AI session generate name
    st, body, raw = req("POST", "/v1/ai/generate", token=token, body=sample_generate_body(), timeout=120)
    ok = st == 200 and isinstance(body, dict) and body.get("success") is True
    detail = f"status={st}"
    if not ok and isinstance(body, dict):
        err = body.get("error") if isinstance(body.get("error"), dict) else {}
        detail += f" code={err.get('code') or body.get('errorCode')}"
    add("admin_generate_name", ok, detail)

    # dialogue
    st, body, _ = req(
        "POST", "/v1/ai/generate", token=token, body=sample_generate_body("generate_dialogue"), timeout=120
    )
    add(
        "admin_generate_dialogue",
        st == 200 and isinstance(body, dict) and body.get("success") is True,
        f"status={st}",
    )

    # conversation
    st, body, _ = req(
        "POST",
        "/v1/ai/generate",
        token=token,
        body=sample_generate_body("generate_conversation"),
        timeout=120,
    )
    add(
        "admin_generate_conversation",
        st == 200 and isinstance(body, dict) and body.get("success") is True,
        f"status={st}",
    )

    # 7) Create AI-enabled license, activate, generate with USER token, check quota not bumped by ADMIN
    device = "a" + ("b" * 63)
    st, created, _ = req(
        "POST",
        "/v1/admin/licenses",
        token=ADMIN,
        body={
            "durationDays": 1,
            "maxDevices": 1,
            "maxConcurrentSessions": 1,
            "permissionEditor": True,
            "permissionAi": True,
            "aiDailyLimit": 5,
            "aiMonthlyLimit": 20,
            "adminNotes": "ADMIN.AI.1P probe — revoke after",
        },
    )
    if st not in (200, 201) or not isinstance(created, dict):
        add("user_license_create", False, f"status={st}")
        return 3
    license_id = created.get("licenseId")
    license_code = created.get("licenseCode")
    add("user_license_create", bool(license_id and license_code), f"id={license_id}")

    st, session, _ = req(
        "POST",
        "/v1/license/activate",
        body={
            "licenseCode": license_code,
            "deviceId": device,
            "clientVersion": "admin-ai-1p",
        },
    )
    user_token = (session or {}).get("sessionToken") if isinstance(session, dict) else None
    add("user_activate", st == 200 and bool(user_token), f"status={st}")

    # USER generate once
    st, body, _ = req("POST", "/v1/ai/generate", token=user_token, body=sample_generate_body(), timeout=120)
    add("user_generate", st == 200 and isinstance(body, dict) and body.get("success") is True, f"status={st}")

    # Read usage before admin extra generate
    st, detail_before, _ = req("GET", f"/v1/admin/licenses/{license_id}", token=ADMIN)
    usage_before = (detail_before or {}).get("aiUsageToday") if isinstance(detail_before, dict) else None
    add("user_usage_before", st == 200 and usage_before is not None, f"today={usage_before}")

    # ADMIN generate again — must not bump this license
    st, body, _ = req("POST", "/v1/ai/generate", token=token, body=sample_generate_body(), timeout=120)
    add("admin_generate_again", st == 200 and isinstance(body, dict) and body.get("success") is True, f"status={st}")

    st, detail_after, _ = req("GET", f"/v1/admin/licenses/{license_id}", token=ADMIN)
    usage_after = (detail_after or {}).get("aiUsageToday") if isinstance(detail_after, dict) else None
    add(
        "admin_does_not_consume_user_quota",
        usage_before is not None and usage_after == usage_before,
        f"before={usage_before} after={usage_after}",
    )

    # heartbeat / session validate
    st, _, _ = req("POST", "/v1/license/heartbeat", body={"sessionToken": user_token, "deviceId": device})
    add("user_heartbeat", st == 200, f"status={st}")
    st, _, _ = req("POST", "/v1/license/session", body={"sessionToken": user_token, "deviceId": device})
    add("user_session_validate", st == 200, f"status={st}")

    # legacy token rejected (use a fake shared token string; production legacy OFF)
    st, _, _ = req("POST", "/v1/ai/generate", token="legacy-should-fail-token", body=sample_generate_body())
    add("legacy_rejected", st == 401, f"status={st}")

    # expired-looking rai1 rejected
    st, _, _ = req(
        "POST",
        "/v1/ai/generate",
        token="rai1.1000000000.deadbeefcafe.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
        body=sample_generate_body(),
    )
    add("expired_or_fake_rai1_rejected", st == 401, f"status={st}")

    # logout
    st, _, _ = req("POST", "/v1/license/logout", body={"sessionToken": user_token, "deviceId": device})
    add("user_logout", st == 200, f"status={st}")

    # revoke probe license
    st, _, _ = req("POST", f"/v1/admin/licenses/{license_id}/revoke", token=ADMIN, body=None)
    add("cleanup_revoke", st == 200, f"status={st}")

    # expiry documentation
    try:
        exp_dt = datetime.fromisoformat(str(exp).replace("Z", "+00:00"))
        mins = (exp_dt - datetime.now(timezone.utc)).total_seconds() / 60.0
        add("session_ttl_approx_60min", 50 <= mins <= 70, f"remaining_min~{mins:.1f}")
    except Exception as ex:
        add("session_ttl_approx_60min", False, type(ex).__name__)

    failed = sum(1 for _, ok, _ in RESULTS if not ok)
    print(f"SUMMARY {len(RESULTS)-failed}/{len(RESULTS)} OK")
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
