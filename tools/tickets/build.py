"""Build Jira-style ticket files from tickets/_source/*.tickets and verify PRD coverage.

Run from the repo root:  python3 -I tools/tickets/build.py
Exit code 1 if any PRD requirement, screen, exit criterion, NFR, delivery-package item or
dependency of a phase is not covered by a ticket, or if a ticket references an unknown ID.

Source format (one file per phase, e.g. tickets/_source/P1.tickets):

    @epic MDV-P1-E01 | Title | P1-01, P1-02 | One-line goal

    === MDV-P1-001
    title: ...
    type: Story | Task | Spike | Gate | Chore
    epic: MDV-P1-E01
    prd: P1-05, P1-12          # requirement IDs (PRD v2)
    screens: P-02, P-03        # screen IDs
    covers: exit:1, nfr:2, pkg:1, dep:3   # phase exit criteria / NFR / package / dependency indexes (1-based)
    priority: MUST | SHOULD | COULD
    roles: Receptionist, Practice admin
    labels: pms, registration
    estimate: 3                # ideal developer-days
    weeks: 5-6                 # phase-plan weeks (Week 1 = WEEK1 below)
    depends: MDV-P0-004, MDV-P1-002
    confidence: 85 | reason this is (not) certain; what would raise it
    sources: design/Medvya Patient Registration; docs/02-design/screen-specs.md#patient-registration-p-02
    desc:
    Free text, several lines.
    ac:
    - Given ... when ... then ...
    tests:
    - ...
    notes:
    - ...
"""
import csv
import datetime as dt
import json
import os
import re
import sys
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "tickets", "_source")
OUT = os.path.join(ROOT, "tickets")
PRD = json.load(open(os.path.join(ROOT, "docs/01-prd/data/prd-v2.json")))
WEEK1 = dt.date(2026, 10, 26)  # keep in sync with tools/prd-extract/render_docs.py

PHASE_DIR = {
    "P0": "P0-foundation", "P1": "P1-front-desk", "P2": "P2-doctor-workspace", "P3": "P3-ai-scribe",
    "P4": "P4-clinic-operations", "P5": "P5-commercial-launch", "P6": "P6-clip-advanced-ai",
    "P7": "P7-hospital-enterprise", "P8": "P8-scale-specialty",
}
SINGLE = {"title", "type", "epic", "prd", "screens", "covers", "priority", "roles", "labels",
          "estimate", "weeks", "depends", "confidence", "sources"}
MULTI = {"desc", "ac", "tests", "notes"}
TYPES = {"Story", "Task", "Spike", "Gate", "Chore"}
PRIOS = {"MUST", "SHOULD", "COULD"}
JIRA_PRIO = {"MUST": "Highest", "SHOULD": "High", "COULD": "Medium"}

phases = {p["code"]: p for p in PRD["phases"]}
req_ids = {f["id"]: (p["code"], f) for p in PRD["phases"] for f in p["feats"]}
screen_ids = {r["id"]: r for r in PRD["matrix"]}


def wk_start(n):
    return WEEK1 + dt.timedelta(weeks=n - 1)


def wk_end(n):
    return wk_start(n) + dt.timedelta(days=4)


def fmt(d):
    return d.strftime("%d %b %Y")


def split_list(v):
    return [x.strip() for x in v.split(",") if x.strip()]


def band(c):
    return "High" if c >= 85 else "Medium" if c >= 65 else "Low"


def slug(s):
    return re.sub(r"[^a-z0-9]+", "-", s.lower()).strip("-")[:60]


errors = []


def parse(path):
    epics, tickets = [], []
    cur, section = None, None
    for n, raw in enumerate(open(path, encoding="utf-8"), 1):
        line = raw.rstrip("\n")
        if line.startswith("@epic "):
            key, title, prd, goal = [x.strip() for x in line[6:].split("|")]
            epics.append({"key": key, "title": title, "prd": split_list(prd), "goal": goal})
            continue
        if line.startswith("=== "):
            cur = {"key": line[4:].strip(), "desc": [], "ac": [], "tests": [], "notes": [], "_src": f"{os.path.basename(path)}:{n}"}
            tickets.append(cur)
            section = None
            continue
        if cur is None or (not line.strip() and section not in ("desc",)):
            continue
        m = re.match(r"^([a-z]+):\s?(.*)$", line)
        if m and (m.group(1) in SINGLE or m.group(1) in MULTI):
            k, v = m.group(1), m.group(2).strip()
            if k in SINGLE:
                cur[k] = v
                section = None
            else:
                section = k
                if v:
                    (cur[k].append(v) if k == "desc" else cur[k].append(v.lstrip("- ")))
            continue
        if section == "desc":
            cur["desc"].append(line)
        elif section in ("ac", "tests", "notes"):
            if line.lstrip().startswith("- "):
                cur[section].append(line.lstrip()[2:].strip())
            elif cur[section]:
                cur[section][-1] += " " + line.strip()
        elif line.strip() and not line.startswith("#"):
            errors.append(f"{cur['_src']}: unparsed line {n}: {line[:60]}")
    return epics, tickets


all_epics, all_tickets = {}, []
for fn in sorted(os.listdir(SRC)):
    if not fn.endswith(".tickets"):
        continue
    eps, tks = parse(os.path.join(SRC, fn))
    for e in eps:
        e["phase"] = e["key"].split("-")[1]
        all_epics[e["key"]] = e
    all_tickets += tks

keys = set()
for t in all_tickets:
    k = t["key"]
    if k in keys:
        errors.append(f"duplicate key {k}")
    keys.add(k)
    t["phase"] = k.split("-")[1]
    for f in ("title", "type", "epic", "priority", "estimate", "weeks", "confidence"):
        if not t.get(f):
            errors.append(f"{k}: missing {f}")
    if t.get("type") not in TYPES:
        errors.append(f"{k}: bad type {t.get('type')}")
    if t.get("priority") not in PRIOS:
        errors.append(f"{k}: bad priority {t.get('priority')}")
    if t.get("epic") not in all_epics:
        errors.append(f"{k}: unknown epic {t.get('epic')}")
    if not t["ac"]:
        errors.append(f"{k}: no acceptance criteria")
    t["prd_l"] = split_list(t.get("prd", ""))
    t["scr_l"] = split_list(t.get("screens", ""))
    t["dep_l"] = split_list(t.get("depends", ""))
    for r in t["prd_l"]:
        if r not in req_ids:
            errors.append(f"{k}: unknown requirement {r}")
    for s in t["scr_l"]:
        if s not in screen_ids:
            errors.append(f"{k}: unknown screen {s}")
    t["cov"] = defaultdict(set)
    for c in split_list(t.get("covers", "")):
        kind, _, idx = c.partition(":")
        t["cov"][kind.strip()].add(int(idx))
    try:
        conf, _, why = t.get("confidence", "0|").partition("|")
        t["conf"], t["conf_why"] = int(conf.strip()), why.strip()
    except ValueError:
        errors.append(f"{k}: bad confidence")
        t["conf"], t["conf_why"] = 0, ""
    w = t.get("weeks", "1")
    a, _, b = w.partition("-")
    t["w0"], t["w1"] = int(a), int(b or a)
    t["est"] = float(t.get("estimate", "0"))
for t in all_tickets:
    for d in t["dep_l"]:
        if d not in keys:
            errors.append(f"{t['key']}: depends on unknown {d}")

# ---------------- coverage ----------------
cov_lines = ["<!-- GENERATED by tools/tickets/build.py. Do not edit by hand. -->\n",
             "# Coverage report: PRD v2 → tickets\n",
             "Every requirement, screen, exit criterion, NFR, delivery-package item and dependency in PRD v2 must be claimed by at least one ticket. The build fails otherwise.\n"]
missing = []
by_req, by_scr = defaultdict(list), defaultdict(list)
for t in all_tickets:
    for r in t["prd_l"]:
        by_req[r].append(t["key"])
    for s in t["scr_l"]:
        by_scr[s].append(t["key"])
cov_lines.append("## Requirements\n\n| Req | Phase | Priority | Tickets |\n|---|---|---|---|")
for rid, (ph, f) in req_ids.items():
    ts = by_req.get(rid, [])
    if not ts:
        missing.append(f"requirement {rid}")
    cov_lines.append(f"| {rid} | {ph} | {f['pr']} | {', '.join(ts) or '**MISSING**'} |")
cov_lines.append("\n## Screens (PRD v2 traceability matrix)\n\n| Screen | PRD phase | Status | Tickets |\n|---|---|---|---|")
for sid, r in sorted(screen_ids.items(), key=lambda x: (x[1]["ph"], x[0])):
    ts = by_scr.get(sid, [])
    if not ts:
        missing.append(f"screen {sid}")
    cov_lines.append(f"| {sid} {r['name'][:60]} | {r['ph']} | {r['st']} | {', '.join(ts) or '**MISSING**'} |")
for kind, field, label in (("exit", "exit", "Exit criteria"), ("nfr", "nfr", "Platform & NFR"), ("pkg", "pkg", "Delivery package"), ("dep", "deps", "Dependencies & risks")):
    cov_lines.append(f"\n## {label}\n\n| Phase | # | Item | Tickets |\n|---|---|---|---|")
    for code, p in phases.items():
        for i, text in enumerate(p[field], 1):
            ts = [t["key"] for t in all_tickets if t["phase"] == code and i in t["cov"].get(kind, set())]
            if not ts:
                missing.append(f"{code} {kind}:{i} {text[:50]}")
            cov_lines.append(f"| {code} | {i} | {text} | {', '.join(ts) or '**MISSING**'} |")
cov_lines.append(f"\n**Result:** {'✅ complete, nothing missing' if not missing else '❌ ' + str(len(missing)) + ' items missing'}\n")
if missing:
    cov_lines += [f"- {m}" for m in missing]

# ---------------- render tickets ----------------
by_phase = defaultdict(list)
for t in all_tickets:
    by_phase[t["phase"]].append(t)
dependents = defaultdict(list)
for t in all_tickets:
    for d in t["dep_l"]:
        dependents[d].append(t["key"])
title_of = {t["key"]: t["title"] for t in all_tickets}
path_of = {t["key"]: f"../{PHASE_DIR[t['phase']]}/{t['key']}.md" for t in all_tickets}
GEN = "<!-- GENERATED by tools/tickets/build.py from tickets/_source. Edit the source, then rebuild. -->\n\n"


def link(k):
    return f"[{k}]({path_of[k]})" if k in path_of else k


for code, tks in by_phase.items():
    d = os.path.join(OUT, PHASE_DIR[code])
    os.makedirs(d, exist_ok=True)
    for f in os.listdir(d):
        if f.startswith("MDV-") and f.endswith(".md"):
            os.remove(os.path.join(d, f))
    p = phases[code]
    for t in tks:
        reqs = "<br>".join(f"**{r}** {req_ids[r][1]['t']} ({req_ids[r][1]['pr']})" for r in t["prd_l"]) or "— (enabling work)"
        scrs = ", ".join(f"{s} {screen_ids[s]['name']}" for s in t["scr_l"]) or "—"
        cov = []
        for kind, field in (("exit", "exit"), ("nfr", "nfr"), ("pkg", "pkg"), ("dep", "deps")):
            for i in sorted(t["cov"].get(kind, [])):
                cov.append(f"{kind} {i}: {p[field][i - 1]}")
        L = [GEN, f"# {t['key']} · {t['title']}\n",
             "| Field | Value |\n|---|---|",
             f"| Type | {t['type']} |", f"| Epic | {t['epic']} · {all_epics[t['epic']]['title']} |",
             f"| Phase / release | {code} · {p['name']} · {p['rel']} |",
             f"| Priority | {t['priority']} (Jira: {JIRA_PRIO[t['priority']]}) |",
             f"| Requirements | {reqs} |", f"| Screens | {scrs} |",
             f"| Roles | {t.get('roles', '—')} |", f"| Labels | {t.get('labels', '—')} |",
             f"| Estimate | {t['est']:g} ideal dev-days |",
             f"| Window | Week {t['w0']}–{t['w1']} · {fmt(wk_start(t['w0']))} → {fmt(wk_end(t['w1']))} |",
             f"| Depends on | {', '.join(link(x) for x in t['dep_l']) or '—'} |",
             f"| Blocks | {', '.join(link(x) for x in dependents.get(t['key'], [])) or '—'} |",
             f"| **Confidence** | **{t['conf']}% · {band(t['conf'])}** — {t['conf_why']} |"]
        if cov:
            L.append(f"| Phase gate items | {'<br>'.join(cov)} |")
        L.append("\n## Description\n\n" + "\n".join(t["desc"]).strip() + "\n")
        L.append("## Acceptance criteria\n")
        L += [f"- [ ] **AC{i}.** {a}" for i, a in enumerate(t["ac"], 1)]
        if t["tests"]:
            L.append("\n## Test notes\n")
            L += [f"- {x}" for x in t["tests"]]
        if t["notes"]:
            L.append("\n## Notes / open questions\n")
            L += [f"- {x}" for x in t["notes"]]
        L.append("\n## Definition of Done\n\nAll acceptance criteria above, **plus** the cross-cutting X1–X8 checklist in [docs/05-delivery](../../docs/05-delivery/README.md#definition-of-done-every-ticket).\n")
        if t.get("sources"):
            L.append("## Sources\n\n" + "\n".join(f"- {s.strip()}" for s in t["sources"].split(";")) + "\n")
        open(os.path.join(d, f"{t['key']}.md"), "w").write("\n".join(L))

    # phase README
    est = sum(t["est"] for t in tks)
    cap = (p["e"] - p["s"] + 1) * 5
    L = [GEN, f"# {code} · {p['name']} — {p['rel']}\n",
         f"**Window:** {p['weeks']} · {fmt(wk_start(p['s']))} → {fmt(wk_end(p['e']))}  ",
         f"**Delivered to:** {p['to']}  ",
         f"**Day one:** {p['dayOne']}  ",
         f"**Goal:** {p['goal']}\n",
         f"**Tickets:** {len(tks)} · **Estimate:** {est:g} dev-days vs **{cap} working days** in the window "
         f"({'fits' if est <= cap else 'OVER by ' + format(est - cap, 'g') + ' days: needs scope trade-off or overlap'}) · "
         f"**Average confidence:** {round(sum(t['conf'] for t in tks) / len(tks))}%\n",
         "## Epics\n\n| Epic | Title | Requirements | Goal |\n|---|---|---|---|"]
    for e in [e for e in all_epics.values() if e["phase"] == code]:
        L.append(f"| {e['key']} | {e['title']} | {', '.join(e['prd']) or '—'} | {e['goal']} |")
    L.append("\n## Tickets\n\n| Key | Title | Type | Prio | PRD | Screens | Est | Weeks | Dates | Confidence |\n|---|---|---|---|---|---|---|---|---|---|")
    for t in sorted(tks, key=lambda t: (t["w0"], t["key"])):
        L.append(f"| [{t['key']}]({t['key']}.md) | {t['title']} | {t['type']} | {t['priority']} | {', '.join(t['prd_l']) or '—'} | {', '.join(t['scr_l']) or '—'} | {t['est']:g} | {t['w0']}–{t['w1']} | {wk_start(t['w0']).strftime('%d %b')} → {wk_end(t['w1']).strftime('%d %b %Y')} | {t['conf']}% {band(t['conf'])} |")
    L.append("\n## Exit criteria (release gate)\n")
    for i, x in enumerate(p["exit"], 1):
        ts = [t["key"] for t in tks if i in t["cov"].get("exit", set())]
        L.append(f"- [ ] {x} — {', '.join(f'[{k}]({k}.md)' for k in ts)}")
    L.append(f"\n**Not in this phase:** {p['notIn']}\n")
    open(os.path.join(OUT, PHASE_DIR[code], "README.md"), "w").write("\n".join(L))

# ---------------- schedule (date-wise) ----------------
max_w = max(t["w1"] for t in all_tickets)
L = [GEN, "# Date-wise schedule\n",
     f"Week 1 = Monday {fmt(WEEK1)}. A ticket appears in every week of its window. Load is the estimate spread evenly across the window, for one developer; flags show weeks over 5 dev-days.\n"]
for w in range(1, max_w + 1):
    act = [t for t in all_tickets if t["w0"] <= w <= t["w1"]]
    if not act:
        continue
    load = sum(t["est"] / (t["w1"] - t["w0"] + 1) for t in act)
    flag = " ⚠ over capacity" if load > 5.05 else ""
    L.append(f"\n## Week {w} · {fmt(wk_start(w))} – {fmt(wk_end(w))} · load {load:.1f} dev-days{flag}\n")
    L.append("| Key | Title | Phase | Window | Confidence |\n|---|---|---|---|---|")
    for t in sorted(act, key=lambda t: t["key"]):
        L.append(f"| {link(t['key'])} | {t['title']} | {t['phase']} | wk {t['w0']}–{t['w1']} | {t['conf']}% |")
open(os.path.join(OUT, "schedule.md"), "w").write("\n".join(L).replace("../", "", 0) + "\n")

# fix schedule links (schedule.md sits in tickets/, not in a phase dir)
s = open(os.path.join(OUT, "schedule.md")).read().replace("](../", "](")
open(os.path.join(OUT, "schedule.md"), "w").write(s)

# ---------------- index ----------------
tot = len(all_tickets)
dist = defaultdict(int)
for t in all_tickets:
    dist[band(t["conf"])] += 1
L = [GEN, "# Medvya delivery tickets\n",
     "Jira-style tickets for every PRD v2 phase (P0–P8). The source of truth is `tickets/_source/*.tickets`. "
     "Run `python3 -I tools/tickets/build.py` to regenerate the ticket files, [schedule.md](schedule.md), "
     "[jira-import.csv](jira-import.csv) and [coverage-report.md](coverage-report.md).\n",
     f"**{tot} tickets** in {len(all_epics)} epics · **{sum(t['est'] for t in all_tickets):g} dev-days** estimated · "
     f"confidence: High {dist['High']} · Medium {dist['Medium']} · Low {dist['Low']} · "
     f"coverage: {'✅ every requirement, screen, exit criterion, NFR, package item and dependency is ticketed' if not missing else '❌ gaps, see coverage report'}\n",
     "Conventions, Definition of Ready/Done and the confidence scale: [docs/05-delivery](../docs/05-delivery/README.md).\n",
     "| Phase | Folder | Window | Tickets | Estimate (dev-days) | Capacity (days) | Avg confidence |\n|---|---|---|---|---|---|---|"]
for code in PHASE_DIR:
    tks = by_phase.get(code, [])
    if not tks:
        continue
    p = phases[code]
    L.append(f"| {code} {p['name']} | [{PHASE_DIR[code]}]({PHASE_DIR[code]}/README.md) | {fmt(wk_start(p['s']))} → {fmt(wk_end(p['e']))} | {len(tks)} | {sum(t['est'] for t in tks):g} | {(p['e'] - p['s'] + 1) * 5} | {round(sum(t['conf'] for t in tks) / len(tks))}% |")
ga = [t for t in all_tickets if t["phase"] in ("P0", "P1", "P2", "P3", "P4", "P5")]
ga_est = sum(t["est"] for t in ga)
L.append("\n## Capacity check (read this before committing to dates)\n")
L.append(f"PRD v2 plans **v1.0 GA in week 32** for one full-time developer with AI help, about **160 working days**. "
         f"The P0–P5 tickets add up to **{ga_est:g} ideal dev-days**, roughly **{ga_est / 160:.1f}×** that capacity. "
         "Phase overlap does not create capacity for a single developer. Before scope freeze, choose one or more of: "
         "add a second developer (or a part-time contractor for infra, security and print layouts); "
         "cut SHOULD items from P1–P5 (public site, doctor discovery, dictation, voice assistant, clinic messages); "
         "launch one country only (ADR-002), which removes the UAE pack from the critical path; "
         "or move the GA date. Weeks over capacity are flagged ⚠ in [schedule.md](schedule.md). "
         "Estimates are ideal days and should be recalibrated against actual velocity after P0.\n")
L.append("\n## Lowest-confidence tickets (resolve these first)\n\n| Key | Title | Confidence | Why |\n|---|---|---|---|")
for t in sorted(all_tickets, key=lambda t: t["conf"])[:20]:
    L.append(f"| {link(t['key'])} | {t['title']} | {t['conf']}% | {t['conf_why']} |")
open(os.path.join(OUT, "README.md"), "w").write("\n".join(L).replace("](../P", "](P") + "\n")

# ---------------- jira csv ----------------
with open(os.path.join(OUT, "jira-import.csv"), "w", newline="") as f:
    w = csv.writer(f)
    w.writerow(["Issue Key", "Issue Type", "Summary", "Epic Link", "Priority", "Labels", "Original Estimate (days)",
                "Start date", "Due date", "Fix Version", "PRD Requirements", "Screens", "Depends On", "Confidence %",
                "Confidence Band", "Description", "Acceptance Criteria"])
    for e in all_epics.values():
        p = phases[e["phase"]]
        w.writerow([e["key"], "Epic", e["title"], "", "Highest", f"phase-{e['phase']}", "", wk_start(p["s"]).isoformat(),
                    wk_end(p["e"]).isoformat(), p["rel"], ", ".join(e["prd"]), "", "", "", "", e["goal"], ""])
    for t in all_tickets:
        w.writerow([t["key"], t["type"], t["title"], t["epic"], JIRA_PRIO[t["priority"]],
                    " ".join(["phase-" + t["phase"]] + [slug(x) for x in split_list(t.get("labels", ""))]),
                    f"{t['est']:g}", wk_start(t["w0"]).isoformat(), wk_end(t["w1"]).isoformat(), phases[t["phase"]]["rel"],
                    ", ".join(t["prd_l"]), ", ".join(t["scr_l"]), ", ".join(t["dep_l"]), t["conf"], band(t["conf"]),
                    "\n".join(t["desc"]).strip(), "\n".join(f"* {a}" for a in t["ac"])])

open(os.path.join(OUT, "coverage-report.md"), "w").write("\n".join(cov_lines) + "\n")

print(f"{tot} tickets, {len(all_epics)} epics, {sum(t['est'] for t in all_tickets):g} dev-days")
for code in PHASE_DIR:
    tks = by_phase.get(code, [])
    if tks:
        p = phases[code]
        print(f"  {code}: {len(tks):3d} tickets  {sum(t['est'] for t in tks):6g} d  cap {(p['e'] - p['s'] + 1) * 5:3d}  avg conf {round(sum(t['conf'] for t in tks) / len(tks))}%")
for e in errors:
    print("ERROR", e)
for m in missing:
    print("MISSING", m)
sys.exit(1 if errors or missing else 0)
