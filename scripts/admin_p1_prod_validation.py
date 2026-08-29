#!/usr/bin/env python3
"""ADMIN.P.1 — prod validation UI.3.2 (GenerateName=3) + USAGE.1 (/v1/admin/ai-usage).
Never prints secrets, full rai1 tokens, SessionTokens, or license codes.
"""
from __future__ import annotations

import json
import os
import sys
import urllib.error
import urllib.request

BASE = os.environ.get("RUFUS_API_BASE", "https://vmi3502135.contaboserver.net").rstrip("/")
ADMIN = os.environ["RUFUS_ADMIN_API_SECRET_PROD"]
RESULTS: list[tuple[str, bool, str]] = []


def add(name: str, ok: bool, detail: str = "") -> None:
    RESULTS.append((name, ok, detail))
    print(f"{'OK' if ok else 'FAIL'}  {name}" + (f"  ({detail})" if detail else ""))


def req(method: str, path: str, *, token: str | None = None, body: dict | None = None, timeout: int = 120):
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
            "role": "NPC minero",
            "attitude": "brusco",
            "narrativeContext": "ADMIN.P.1 validacion produccion",
            "additionalInstruction": "Prueba breve",
            "length": "corta",
            "style": "fantasia",
            "currentNpcName": "ProbeNpc",
        },
        "prompt": {
            "master": "Eres un asistente creativo para un editor.",
            "context": "Prueba ADMIN.P.1",
            "task": "Propón exactamente 3 nombres diferentes para el NPC.",
        },
    }


def count_names(payload: dict | None) -> int | None:
    if not isinstance(payload, dict):
        return None
    # Wire envelope: success + result / structured JSON string or object
    result = payload.get("result") or payload.get("data") or payload
    if isinstance(result, str):
        try:
            result = json.loads(result)
        except json.JSONDecodeError:
            return None
    if not isinstance(result, dict):
        return None
    names = result.get("nombres")
    if isinstance(names, list):
        return len(names)
    # nested under generation / content
    for key in ("content", "generation", "structured"):
        inner = result.get(key)
        if isinstance(inner, str):
            try:
                inner = json.loads(inner)
            except json.JSONDecodeError:
                continue
        if isinstance(inner, dict) and isinstance(inner.get("nombres"), list):
            return len(inner["nombres"])
    return None


def main() -> int:
    # Licensing / ADMIN regression
    st, body, _ = req("GET", "/v1/admin/licenses", token=ADMIN)
    add("admin_list_licenses", st == 200 and isinstance(body, list), f"status={st}")

    # Usage auth
    st, _, _ = req("GET", "/v1/admin/ai-usage")
    add("ai_usage_no_auth", st in (401, 403), f"status={st}")

    st, _, _ = req("GET", "/v1/admin/ai-usage", token="wrong-admin-secret-xxxxxxxxx")
    add("ai_usage_bad_auth", st in (401, 403), f"status={st}")

    st, usage, raw = req("GET", "/v1/admin/ai-usage", token=ADMIN)
    usage_ok = (
        st == 200
        and isinstance(usage, dict)
        and all(k in usage for k in ("today", "month", "allTime", "byAction"))
    )
    # Must not leak secrets / prompts
    lower = (raw or "").lower()
    leak = any(
        x in lower
        for x in (
            "openai_api_key",
            "\"rai1.",
            "sessiontoken",
            "licensecode",
            "sk-",
        )
    )
    add("ai_usage_ok", usage_ok and not leak, f"status={st} leak={leak}")
    if usage_ok:
        today = usage.get("today") or {}
        add(
            "ai_usage_has_token_fields",
            all(k in today for k in ("generations", "inputTokens", "outputTokens", "totalTokens")),
            "today fields",
        )

    # Admin AI session
    st, sess, _ = req("POST", "/v1/admin/ai-session", token=ADMIN, body={})
    token = (sess or {}).get("accessToken") if isinstance(sess, dict) else None
    add("admin_ai_session", st == 200 and bool(token) and str(token).startswith("rai1."), f"status={st}")

    if not token:
        failed = sum(1 for _, ok, _ in RESULTS if not ok)
        print(f"\nSUMMARY {len(RESULTS) - failed}/{len(RESULTS)} OK (aborted: no rai1)")
        return 2

    # Admin secret must not work on /v1/ai/generate
    st, _, _ = req("POST", "/v1/ai/generate", token=ADMIN, body=sample_generate_body())
    add("admin_secret_rejected_on_generate", st in (401, 403), f"status={st}")

    # GenerateName → exactly 3
    st, gen, raw = req("POST", "/v1/ai/generate", token=token, body=sample_generate_body("generate_name"), timeout=180)
    n = count_names(gen)
    success = isinstance(gen, dict) and gen.get("success") is True
    add("generate_name_http", st == 200 and success, f"status={st}")
    add("generate_name_exactly_3", n == 3, f"count={n}")

    # Dialogue / conversation smoke (structure only)
    st, body, _ = req(
        "POST", "/v1/ai/generate", token=token, body=sample_generate_body("generate_dialogue"), timeout=180
    )
    add("generate_dialogue", st == 200 and isinstance(body, dict) and body.get("success") is True, f"status={st}")

    st, body, _ = req(
        "POST",
        "/v1/ai/generate",
        token=token,
        body=sample_generate_body("generate_conversation"),
        timeout=180,
    )
    add(
        "generate_conversation",
        st == 200 and isinstance(body, dict) and body.get("success") is True,
        f"status={st}",
    )

    # Legacy still off
    st, _, _ = req("POST", "/v1/ai/generate", token="legacy-should-fail-token", body=sample_generate_body())
    add("legacy_rejected", st == 401, f"status={st}")

    failed = sum(1 for _, ok, _ in RESULTS if not ok)
    print(f"\nSUMMARY {len(RESULTS) - failed}/{len(RESULTS)} OK")
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
