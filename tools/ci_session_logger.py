#!/usr/bin/env python3
import subprocess, sys, re
from pathlib import Path
from datetime import datetime
try:
    from zoneinfo import ZoneInfo
except Exception:
    ZoneInfo = None

REPO = Path(__file__).resolve().parents[1]
LOG = REPO / "docs" / "SESSION_LOG.md"


def sh(cmd):
    return subprocess.check_output(cmd, text=True).strip()


def safe_sh(cmd):
    try:
        return sh(cmd)
    except subprocess.CalledProcessError:
        return ""


def commits_in_range(before, after):
    zeros = "0" * 40
    if not after or len(after) != 40:
        return []
    if not before or before == zeros:
        return [after]
    revs = safe_sh(["git", "rev-list", "--ancestry-path", f"{before}..{after}"])
    commits = [c for c in revs.splitlines() if c]
    return commits or [after]


def parse_shortstat(text):
    add = rem = 0
    m = re.search(r"(\d+)\s+insertion", text)
    if m:
        add = int(m.group(1))
    m = re.search(r"(\d+)\s+deletion", text)
    if m:
        rem = int(m.group(1))
    return add, rem


def to_london(iso_str):
    try:
        dt = datetime.fromisoformat(iso_str.replace("Z", "+00:00"))
        if ZoneInfo:
            return dt.astimezone(ZoneInfo("Europe/London")).isoformat(timespec="seconds")
        return dt.isoformat(timespec="seconds")
    except Exception:
        return iso_str


def ensure_header(content):
    header = (
        "# Session Log\n"
        "This file is auto-updated by CI on every push. Times shown are Europe/London.\n\n"
    )
    if not content.strip():
        return header
    if not content.lstrip().startswith("# "):
        content = header + content
    if not content.endswith("\n"):
        content += "\n"
    return content


def prepend_entries(existing, entries_md):
    lines = existing.splitlines(keepends=True)
    if lines and lines[0].startswith("# "):
        i = 1
        while i < len(lines) and lines[i].strip() != "":
            i += 1
        if i < len(lines) and lines[i].strip() == "":
            i += 1
        prefix = "".join(lines[:i])
        if not prefix.endswith("\n\n"):
            prefix = prefix.rstrip("\n") + "\n\n"
        suffix = "".join(lines[i:])
        return prefix + entries_md + suffix
    return entries_md + existing


def format_entry(commit_sha):
    subject = safe_sh(["git", "show", "-s", "--format=%s", commit_sha]) or "(no subject)"
    body = safe_sh(["git", "show", "-s", "--format=%b", commit_sha]).rstrip() or "—"
    name = safe_sh(["git", "show", "-s", "--format=%an", commit_sha]) or "unknown"
    email = safe_sh(["git", "show", "-s", "--format=%ae", commit_sha]) or "unknown@example.com"
    aiso = safe_sh(["git", "show", "-s", "--format=%aI", commit_sha]) or ""
    when = to_london(aiso) if aiso else ""
    files_text = safe_sh(["git", "show", "--name-only", "--pretty=", commit_sha])
    files = [ln.strip() for ln in files_text.splitlines() if ln.strip()]
    files_list = ", ".join(files) if files else "—"
    add, rem = parse_shortstat(safe_sh(["git", "show", "--shortstat", "--pretty=", commit_sha]))
    notes = body.replace("\n", "\n  ")
    return (
        f"## {when} — {subject}\n\n"
        f"- Author: {name} <{email}>\n"
        f"- Changed files ({len(files)}): {files_list}\n"
        f"- Diff: {add} ++ / {rem} --\n"
        f"- Notes:\n  {notes}\n"
        f"---\n"
    )


def main():
    before = sys.argv[1] if len(sys.argv) > 1 else ""
    after = sys.argv[2] if len(sys.argv) > 2 else safe_sh(["git", "rev-parse", "HEAD"])
    commits = commits_in_range(before, after)
    commits = list(dict.fromkeys(commits))
    if not commits:
        print("Session logging is handled automatically on push; no local steps required.")
        return

    LOG.parent.mkdir(parents=True, exist_ok=True)
    existing = LOG.read_text(encoding="utf-8") if LOG.exists() else ""
    existing = ensure_header(existing)

    new_entries = [format_entry(commit) for commit in commits]
    block = "".join(new_entries)
    updated = prepend_entries(existing, block)
    if updated != existing:
        if not updated.endswith("\n"):
            updated += "\n"
        LOG.write_text(updated, encoding="utf-8")

    print("Session logging is handled automatically on push; no local steps required.")


if __name__ == "__main__":
    main()
