"""Rewrites the footer of package/README.md: Patreon, Discord, and a link to every *published*
Cartur mod on Thunderstore except this one.

The list comes from Thunderstore's live index at pack time, not from memory, so a sibling is
linked the moment it is live and never before (an unpublished link 404s on the store page).
pack.ps1 runs this before zipping.

    python tools/footer.py
"""
import json
import os
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
README = os.path.join(ROOT, "package", "README.md")
SELF = json.load(open(os.path.join(ROOT, "package", "manifest.json"), encoding="utf-8"))["name"]
MARK = "\n---\n\n*Free, and always will be."

req = urllib.request.Request("https://thunderstore.io/c/valheim/api/v1/package/",
                             headers={"User-Agent": "cartur-footer"})
with urllib.request.urlopen(req, timeout=180) as r:
    packages = json.load(r)
live = sorted((p for p in packages if p["owner"] == "Cartur" and not p["is_deprecated"] and p["name"] != SELF),
              key=lambda p: p["date_created"])
# Siblings shipping in the same batch are not in the index yet; CARTUR_ALSO="Carturs_A,Carturs_B"
# names them so the first of a pair does not ship without a link to the second.
also = [n.strip() for n in os.environ.get("CARTUR_ALSO", "").split(",") if n.strip()]
known = {p["name"] for p in live} | {SELF}
live += [{"name": n} for n in also if n not in known]
if not live:
    raise SystemExit("no live Cartur packages found - refusing to write an empty footer")

# The package name, made readable - except where the mod was renamed after its package was
# created: Carturs_UI_HUD has been "Cartur's UI" since 1.1.0.
LABELS = {"Carturs_UI_HUD": "UI"}


def label(name):
    return LABELS.get(name, name.replace("Carturs_", "").replace("_", " "))


links = " ·\n".join(f"[{label(p['name'])}](https://thunderstore.io/c/valheim/p/Cartur/{p['name']}/)" for p in live)
footer = (MARK + " If it improved your game you can [tip me on Patreon](https://www.patreon.com/c/cartur).*\n\n"
          "**[Discord](https://discord.gg/nd5RqpwNkz)** — bug reports, install help, and mod requests.\n"
          "Bug reports get their own thread so nothing is lost in a chat scroll, and requests are voted on.\n\n"
          "**More from Cartur:**\n" + links + "\n")

text = open(README, encoding="utf-8").read()
body = text.split(MARK)[0].rstrip("\n") + "\n"
open(README, "w", encoding="utf-8", newline="\n").write(body + footer)
print(f"footer: {len(live)} sibling(s) linked from {SELF}")
