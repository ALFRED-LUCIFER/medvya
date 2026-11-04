#!/usr/bin/env python3 -I
"""Create or update GitHub issues from tickets/_source (+ tickets/_source/gherkin/*.feature).

One milestone per phase release, one parent issue per epic, one issue per ticket (sub-issue of its epic),
and one sub-issue per open Gherkin chunk of a ticket that still has more than one chunk to do.
Done tickets are closed with a comment; unfinished ones get a comment saying what is pending.
Idempotent: issues are matched by the "[KEY]" title prefix. Dry run unless --apply is passed.

    python3 -I tools/tickets/gh_issues.py [--apply] [--only MDV-P1-013,MDV-P0]
"""
import json
import os
import re
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
# build.py parses the .tickets files at import time and exits; run only its parser/validator half.
_src = open(os.path.join(HERE, "build.py"), encoding="utf-8").read()
_ns = {"__file__": os.path.join(HERE, "build.py")}
exec(compile(_src.split("by_phase = ")[0] if "by_phase = " in _src else _src, "build.py", "exec"), _ns)
epics, tickets, phases = _ns["all_epics"], _ns["all_tickets"], _ns["phases"]

APPLY = "--apply" in sys.argv
ONLY = next((a.split("=", 1)[1] if "=" in a else sys.argv[sys.argv.index(a) + 1]
             for a in sys.argv if a.startswith("--only")), "")
ONLY = [x for x in ONLY.split(",") if x]
REPO = subprocess.run(["gh", "repo", "view", "--json", "nameWithOwner", "--jq", ".nameWithOwner"],
                      capture_output=True, text=True, check=True).stdout.strip()
STATUS_LABEL = {"To do": "status: to do", "In progress": "status: in progress", "Partly done": "status: partly done",
                "Blocked": "status: blocked", "Done": "status: done"}
COLOURS = {"phase": "1D76DB", "type": "5319E7", "priority": "B60205", "status": "0E8A16", "area": "C5DEF5", "epic": "FBCA04"}


def gh(*args, data=None):
    for attempt in range(4):  # network resets happen over ~500 calls; reruns are idempotent but retrying is cheaper
        r = subprocess.run(["gh", *args], input=json.dumps(data) if data is not None else None,
                           capture_output=True, text=True)
        if not r.returncode or "HTTP 4" in r.stderr:
            break
        time.sleep(5 * (attempt + 1))
    if r.returncode:
        sys.exit(f"gh {' '.join(args[:3])} failed: {r.stderr.strip()}")
    return json.loads(r.stdout) if r.stdout.strip().startswith(("{", "[")) else r.stdout


def api(method, path, data=None):
    time.sleep(1.2)  # stay under the content-creation secondary rate limit
    return gh("api", "-X", method, f"repos/{REPO}/{path}", *(["--input", "-"] if data is not None else []), data=data)


def feature(key):
    p = os.path.join(HERE, "..", "..", "tickets", "_source", "gherkin", f"{key}.feature")
    if not os.path.exists(p):
        return None, []
    head, chunks, cur = [], [], None
    for line in open(p, encoding="utf-8").read().splitlines():
        m = re.match(r"\s*# chunk:\s*(.+)", line)
        if m:
            cur = {"title": m.group(1).strip(), "lines": []}
            chunks.append(cur)
        elif cur is None:
            head.append(line)
        else:
            cur["lines"].append(line)
    for c in chunks:
        body = "\n".join(c["lines"]).strip("\n")
        c["done"] = "@todo" not in body
        c["text"] = re.sub(r"^\s*@(done|todo)\s*\n", "", body, flags=re.M)
    return "\n".join(head).strip("\n"), chunks


def gherkin(head, chunks):
    return "```gherkin\n" + head + "\n\n" + "\n\n".join(
        f"  # chunk: {c['title']}\n  @{'done' if c['done'] else 'todo'}\n{c['text']}" for c in chunks) + "\n```"


def pending(t):
    left = [p for p in t["progress"] if p.lower().startswith(("left:", "blocked", "waiting"))]
    return left or t["progress"][-1:] or ["Not started."]


def ticket_body(t, head, chunks, child_titles):
    ph = phases[t["phase"]]
    rows = [("Phase", f"{t['phase']} · {ph['rel']}"), ("Epic", f"{t['epic']} · {epics[t['epic']]['title']}"),
            ("Type / priority", f"{t['type']} · {t['priority']}"), ("Roles", t.get("roles", "—")),
            ("Estimate", f"{t['estimate']} ideal dev-days (weeks {t['weeks']})"), ("PRD", t.get("prd", "—")),
            ("Screens", t.get("screens", "—")), ("Depends on", t.get("depends", "—")),
            ("Confidence", t["confidence"].replace("|", "·"))]
    out = ["| | |", "|---|---|", *(f"| {a} | {b} |" for a, b in rows), "", "## Description", "\n".join(t["desc"]).strip(), ""]
    out += ["## Acceptance criteria (Gherkin)",
            gherkin(head, chunks) if head else "\n".join(f"- {a}" for a in t["ac"]), ""]
    if child_titles:
        out += ["## Remaining work, in small chunks", *(f"- {c}" for c in child_titles), ""]
    if t["tests"]:
        out += ["## Tests", *(f"- {x}" for x in t["tests"]), ""]
    if t["progress"]:
        out += ["## Progress so far", *(f"- {x}" for x in t["progress"]), ""]
    out += ["---", f"Source of truth: `tickets/_source/{t['phase']}.tickets` · Sources: {t.get('sources', '—')}. "
            "Edit the source, not this issue's criteria."]
    return "\n".join(out)


def main():
    existing = {}
    for page in gh("api", "--paginate", "--slurp", f"repos/{REPO}/issues?state=all&per_page=100"):
        for i in page:
            m = re.match(r"\[([A-Z0-9·/ -]+?)\]", i["title"])
            if m and "pull_request" not in i:
                existing[m.group(1)] = i
    milestones = {m["title"]: m["number"] for m in gh("api", f"repos/{REPO}/milestones?state=all&per_page=100")}
    made = {"epics": 0, "tickets": 0, "chunks": 0, "closed": 0}
    todo_t = [t for t in tickets if not ONLY or any(t["key"].startswith(o) for o in ONLY)]

    def label(name, kind):
        if APPLY:
            subprocess.run(["gh", "label", "create", name, "--color", COLOURS[kind], "--force"], capture_output=True)

    def ensure(key, title, body, labels, milestone, status, parent=None, close=False, comment=None):
        """Create or update one issue; returns its API object (dry run: a stub)."""
        i = existing.get(key)
        if not APPLY:
            print(("UPDATE" if i else "CREATE"), f"[{key}] {title[:70]} · {status}")
            return i or {"id": 0, "number": 0}
        data = {"title": f"[{key}] {title}", "body": body, "labels": labels, "milestone": milestone}
        if i:
            i = api("PATCH", f"issues/{i['number']}", data)
        else:
            i = api("POST", "issues", data)
            if comment:
                api("POST", f"issues/{i['number']}/comments", {"body": comment})
        if close and i["state"] != "closed":
            api("PATCH", f"issues/{i['number']}", {"state": "closed", "state_reason": "completed"})
            made["closed"] += 1
        if parent and parent["number"] and not i.get("parent_issue_url"):
            api("POST", f"issues/{parent['number']}/sub_issues", {"sub_issue_id": i["id"]})
        existing[key] = i
        return i

    for code, ph in phases.items():
        title = f"{ph['rel']} ({code})"
        if APPLY and title not in milestones and any(t["phase"] == code for t in todo_t):
            milestones[title] = api("POST", "milestones", {"title": title, "description": f"{ph['weeks']} · {ph['goal']}"})["number"]

    epic_issue = {}
    for e in epics.values():
        if ONLY and not any(t["epic"] == e["key"] for t in todo_t):
            continue
        label("epic", "epic")
        ph = phases[e["phase"]]
        epic_issue[e["key"]] = ensure(e["key"], e["title"], f"**Epic goal:** {e['goal']}\n\n**PRD:** {', '.join(e['prd'])}\n\n"
                                      "Tickets are listed as sub-issues.", ["epic"], milestones.get(f"{ph['rel']} ({e['phase']})"),
                                      "epic")
        made["epics"] += 1

    for t in todo_t:
        k, ph = t["key"], phases[t["phase"]]
        head, chunks = feature(k)
        todo_chunks = [c for c in chunks if not c["done"]]
        splitting = t["status"] != "Done" and len(todo_chunks) > 1
        child_titles = [f"[{k} · {n}/{len(todo_chunks)}] {c['title']}" for n, c in enumerate(todo_chunks, 1)] if splitting else []
        labels = [f"phase: {t['phase']}", f"type: {t['type']}", f"priority: {t['priority']}", STATUS_LABEL[t["status"]],
                  *[x.strip() for x in t.get("labels", "").split(",") if x.strip()]]
        for lab, kind in [(labels[0], "phase"), (labels[1], "type"), (labels[2], "priority"), (labels[3], "status")]:
            label(lab, kind)
        for lab in labels[4:]:
            label(lab, "area")
        comment = None
        if t["status"] == "Done":
            comment = "Done. " + " ".join(t["progress"])[:3500]
        elif t["status"] != "To do":
            comment = f"**{t['status']}.** Still pending:\n" + "\n".join(f"- {x}" for x in pending(t))
        parent = ensure(k, t["title"], ticket_body(t, head, chunks, child_titles), labels,
                        milestones.get(f"{ph['rel']} ({t['phase']})"), t["status"], epic_issue.get(t["epic"]),
                        close=t["status"] == "Done", comment=comment)
        made["tickets"] += 1
        for n, c in enumerate(todo_chunks if splitting else [], 1):
            body = (f"Part of #{parent['number']} ({k} · {t['title']}).\n\n```gherkin\n{c['text']}\n```\n\n"
                    f"Estimate: about 0.5–1.5 dev-days. Definition of Done: AGENTS.md §5.")
            ensure(f"{k} · {n}/{len(todo_chunks)}", c["title"], body, [labels[0], labels[2], "status: to do"],
                   milestones.get(f"{ph['rel']} ({t['phase']})"), "chunk", parent)
            made["chunks"] += 1
    print(("APPLIED" if APPLY else "DRY RUN"), made)


main()
