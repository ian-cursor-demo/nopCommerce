"""Playwright helpers for driving a nopverify instance and recording evidence.

Run scripts through `nopverify.sh py <script.py>` so NOPVERIFY_* env vars and PYTHONPATH are set.

    from nopdrive import Run
    with Run("guest-checkout") as run:
        page = run.page
        run.goto("/")
        run.shot("home")
        run.check(page.locator(".product-item").count() > 0, "home shows products")
        rows = run.db('select count(*) from "Order"')
"""

import json
import os
import re
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

from playwright.sync_api import Page, expect, sync_playwright

BASE_URL = os.environ.get("NOPVERIFY_URL", "http://127.0.0.1:8090")
DB = os.environ.get("NOPVERIFY_DB", "nopverify_default")
EVIDENCE = Path(os.environ.get("NOPVERIFY_EVIDENCE", "/tmp/nop-verify-evidence"))
ADMIN_EMAIL = os.environ.get("NOPVERIFY_ADMIN_EMAIL", "admin@yourstore.com")
ADMIN_PASSWORD = os.environ.get("NOPVERIFY_ADMIN_PASSWORD", "Admin123!")
CHANNEL = os.environ.get("NOPVERIFY_BROWSER_CHANNEL", "chrome")
HEADLESS = os.environ.get("NOPVERIFY_HEADED", "") == ""

__all__ = ["Run", "expect", "BASE_URL", "ADMIN_EMAIL", "ADMIN_PASSWORD"]


class CheckFailed(AssertionError):
    pass


class Run:
    """One journey: a browser context, an evidence folder, a step log, and a pass/fail result."""

    def __init__(self, journey: str, video: bool = True):
        stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
        self.journey = journey
        self.dir = EVIDENCE / f"{stamp}-{journey}"
        self.dir.mkdir(parents=True, exist_ok=True)
        self._video = video
        self._n = 0
        self._steps: list[str] = []
        self.server_errors: list[str] = []
        self.console_errors: list[str] = []
        self.checks: list[dict] = []

    def __enter__(self) -> "Run":
        self._pw = sync_playwright().start()
        try:
            self._open()
        except BaseException:
            self._pw.stop()
            raise
        self.log(f"journey `{self.journey}` against {BASE_URL} (db {DB})")
        return self

    def _open(self):
        self.browser = self._pw.chromium.launch(channel=CHANNEL, headless=HEADLESS)
        kwargs = {"viewport": {"width": 1366, "height": 900}, "base_url": BASE_URL}
        if self._video:
            kwargs["record_video_dir"] = str(self.dir / "video")
            kwargs["record_video_size"] = {"width": 1366, "height": 900}
        self.context = self.browser.new_context(**kwargs)
        self.context.tracing.start(screenshots=True, snapshots=True, sources=False)
        self.page: Page = self.context.new_page()
        self.page.set_default_timeout(30_000)
        self.page.on("response", self._on_response)
        self.page.on("console", lambda m: m.type == "error" and self.console_errors.append(m.text))

    def __exit__(self, exc_type, exc, tb):
        passed = exc is None and not self.server_errors
        if exc is not None:
            self.log(f"FAILED: {exc_type.__name__}: {exc}")
            try:
                self.shot("failure")
            except Exception:
                pass
        if self.server_errors:
            self.log("FAILED: server errors during journey: " + "; ".join(self.server_errors))
        try:
            self.context.tracing.stop(path=str(self.dir / "trace.zip"))
        finally:
            self.context.close()
            self.browser.close()
            self._pw.stop()
        result = {
            "journey": self.journey,
            "passed": passed,
            "base_url": BASE_URL,
            "checks": self.checks,
            "server_errors": self.server_errors,
            "console_errors": self.console_errors,
            "error": None if exc is None else f"{exc_type.__name__}: {exc}",
        }
        (self.dir / "result.json").write_text(json.dumps(result, indent=2))
        (self.dir / "steps.md").write_text("\n".join(self._steps) + "\n")
        print(f"{'PASS' if passed else 'FAIL'} {self.journey} -> {self.dir}")
        if not passed and exc is None:
            sys.exit(1)
        return False

    def _on_response(self, response):
        if response.status >= 500:
            self.server_errors.append(f"{response.status} {response.request.method} {response.url}")

    def log(self, msg: str):
        line = f"- `{datetime.now(timezone.utc).strftime('%H:%M:%S')}` {msg}"
        self._steps.append(line)
        print(line, flush=True)

    def goto(self, path: str):
        resp = self.page.goto(path)
        self.log(f"goto {path} -> {resp.status if resp else '?'} {self.page.url}")
        return resp

    def shot(self, label: str, full_page: bool = False) -> Path:
        self._n += 1
        safe = re.sub(r"[^a-z0-9-]+", "-", label.lower()).strip("-")
        path = self.dir / f"{self._n:02d}-{safe}.png"
        self.page.screenshot(path=str(path), full_page=full_page)
        self.log(f"screenshot {path.name} ({self.page.url})")
        return path

    def check(self, condition, description: str):
        ok = bool(condition)
        self.checks.append({"check": description, "ok": ok})
        self.log(f"{'ok  ' if ok else 'FAIL'} {description}")
        if not ok:
            raise CheckFailed(description)

    def db(self, sql: str) -> list[list[str]]:
        """Query the instance database (PostgreSQL; tables and columns are quoted PascalCase, e.g. "Order"."CustomOrderNumber")."""
        out = subprocess.run(
            ["psql", "-h", "localhost", "-U", "nopcommerce", "-d", DB, "-AtF", "\t", "-v", "ON_ERROR_STOP=1", "-c", sql],
            env={**os.environ, "PGPASSWORD": "nopCommerce_db_password"},
            capture_output=True, text=True, check=True,
        ).stdout
        rows = [line.split("\t") for line in out.splitlines() if line]
        self.log(f"db `{sql}` -> {rows[:5]}{' ...' if len(rows) > 5 else ''}")
        return rows

    def wait_ajax(self, timeout_ms: int = 15_000):
        """Wait for jQuery AJAX (one-page checkout, add-to-cart) to settle."""
        self.page.wait_for_function("() => !window.jQuery || jQuery.active === 0", timeout=timeout_ms)

    def login(self, email: str, password: str):
        self.goto("/login")
        self.page.fill("#Email", email)
        self.page.fill("#Password", password)
        self.page.click("button.login-button")
        self.page.wait_for_load_state("networkidle")
        self.check(self.page.locator(".ico-logout").count() > 0, f"logged in as {email}")

    def admin_login(self):
        self.login(ADMIN_EMAIL, ADMIN_PASSWORD)
        self.goto("/Admin")
        self.check("/Admin" in self.page.url and "/login" not in self.page.url.lower(), "admin area opened")


def wait_until(fn, timeout_s: float = 20, interval_s: float = 0.5):
    end = time.time() + timeout_s
    while time.time() < end:
        value = fn()
        if value:
            return value
        time.sleep(interval_s)
    return fn()
