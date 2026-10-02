#!/usr/bin/env python3
"""Inject icons.json Data into PreviewWindow.axaml PathIcons (per x:Name)."""
import json, re, os

BASE = os.path.dirname(os.path.abspath(__file__))
AXAML = os.path.expanduser("~/workspace/mono-work/apps/pixacompact/Views/PreviewWindow.axaml")

with open(os.path.join(BASE, "icons.json")) as f:
    icons = json.load(f)

src = open(AXAML, encoding="utf-8").read()
changed = []
for name, data in icons.items():
    # cari blok Button x:Name="..." lalu PathIcon pertama setelahnya
    pat = re.compile(
        r'(x:Name="%s".*?<PathIcon[^>]*?Data=")[^"]*(")' % re.escape(name),
        re.DOTALL,
    )
    m = pat.search(src)
    assert m, f"tidak ketemu: {name}"
    src = pat.sub(lambda mm: mm.group(1) + data + mm.group(2), src, count=1)
    changed.append(name)

open(AXAML, "w", encoding="utf-8").write(src)
print("injected:", len(changed))
for n in changed:
    print(" ", n)
