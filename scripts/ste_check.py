"""Approximate ASD-STE100 (Simplified Technical English) check for a Markdown file.

Usage: python3 scripts/ste_check.py docs/user-guide.md

It checks sentence length, contractions and obvious passive voice only. It does not check the STE dictionary,
so a passing file can still use words STE does not approve; review the wording by hand as well.

Counts prose sentences outside code blocks, tables and headings, and marks a sentence compliant when:
- it is at most 20 words (procedural: a list item or an imperative) or 25 words (descriptive),
- it has no contraction (n't, 're, 've, 'll, 'd, 'm),
- it has no obvious passive voice (a form of "be" followed by a past participle).
"""
import re
import sys

BE = r"\b(is|are|was|were|be|been|being)\b"
PARTICIPLE = r"\b\w+(ed|en)\b"
PASSIVE = re.compile(BE + r"\s+(\w+ly\s+)?" + PARTICIPLE, re.I)
# Adjectives that end in -ed/-en but are not passives in this text.
NOT_PASSIVE = {"open", "often", "hidden", "even", "seven", "listed", "red", "need", "used", "shared", "masked",
               "linked", "flagged", "closed", "approved", "rejected", "decided", "failed", "required", "missed",
               "selected", "complete", "investigated", "started", "stopped", "different", "given", "written", "seen"}
CONTRACTION = re.compile(r"\b\w+(n't|'re|'ve|'ll|'d|'m)\b", re.I)
IMPERATIVE_START = re.compile(r"^(?:\d+\.\s+|[-*]\s+)")


def prose_lines(text):
    in_code = False
    for line in text.splitlines():
        if line.strip().startswith("```"):
            in_code = not in_code
            continue
        if in_code or not line.strip():
            continue
        stripped = line.strip()
        if stripped.startswith(("|", "#", "---", ">")):
            continue
        yield stripped


def sentences(line):
    procedural = bool(IMPERATIVE_START.match(line))
    body = IMPERATIVE_START.sub("", line)
    body = re.sub(r"`[^`]*`", "X", body)            # code spans count as one word
    body = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", body)  # link text only
    body = body.replace("**", "")
    for s in re.split(r"(?<=[.!?:])\s+(?=[A-Z0-9])", body):
        if s.strip():
            yield s.strip(), procedural


def check(path):
    total = ok = 0
    failures = []
    for line in prose_lines(open(path, encoding="utf-8").read()):
        for s, procedural in sentences(line):
            words = len(re.findall(r"[\w£$%'-]+", s))
            limit = 20 if procedural else 25
            reasons = []
            if words > limit:
                reasons.append(f"{words} words > {limit}")
            if CONTRACTION.search(s):
                reasons.append("contraction")
            for m in PASSIVE.finditer(s):
                if m.group(0).split()[-1].lower() not in NOT_PASSIVE:
                    reasons.append(f"passive? '{m.group(0)}'")
                    break
            total += 1
            if reasons:
                failures.append((s, reasons))
            else:
                ok += 1
    print(f"{path}: {ok}/{total} sentences comply ({100 * ok / total:.1f}%)")
    for s, reasons in failures:
        print(f"  - {', '.join(reasons)}: {s[:110]}")


for p in sys.argv[1:]:
    check(p)
