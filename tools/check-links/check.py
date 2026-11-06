#!/usr/bin/env python3 -I
"""Fail when a relative link in any Markdown file points at a file that does not exist.

    python3 -I tools/check-links/check.py [--self-test]

External links, mailto: and in-page anchors are ignored. Run from the repo root (CI does).
"""
import glob
import os
import re
import sys

LINK = re.compile(r"\[[^\]]*\]\(([^)\s#]+)(?:#[^)]*)?\)")
SKIP = ("node_modules", "/bin/", "/obj/", "/.claude/", "/dist/")


def broken(path, text):
    for url in LINK.findall(text):
        if re.match(r"(https?:|mailto:|tel:)", url):
            continue
        if not os.path.exists(os.path.normpath(os.path.join(os.path.dirname(path), url))):
            yield url


def self_test():
    sample = "[ok](README.md) [web](https://x.y) [anchor](#top) [gone](missing/nowhere.md#h) [mail](mailto:a@b.c)"
    assert list(broken("README.md", sample)) == ["missing/nowhere.md"], "link regex or skip rules changed"
    print("self-test ok")


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        self_test()
    bad = {}
    files = [f for f in glob.glob("**/*.md", recursive=True) if not any(s in "/" + f for s in SKIP)]
    for f in files:
        b = list(broken(f, open(f, encoding="utf-8", errors="ignore").read()))
        if b:
            bad[f] = b
    for f, urls in sorted(bad.items()):
        print(f"{f}: {', '.join(urls)}")
    print(f"{len(files)} Markdown files checked, {sum(map(len, bad.values()))} broken links")
    sys.exit(1 if bad else 0)
