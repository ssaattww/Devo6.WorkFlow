#!/usr/bin/env python3
"""Detect possible Japanese term variants with SudachiPy normalized forms."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from collections import defaultdict
from pathlib import Path


def main() -> int:
    args = parse_args()
    files = args.files or list_markdown_targets()
    if not files:
        return 0

    entries = extract_entries(files)
    groups: dict[tuple[str, str], list[dict]] = defaultdict(list)
    for entry in entries:
        if entry["kind"] not in {"japanese", "katakana"}:
            continue
        groups[(entry["kind"], entry["normalized"])].append(entry)

    variants = []
    for (kind, normalized), group_entries in groups.items():
        surfaces = sorted({entry["surface"] for entry in group_entries})
        if len(surfaces) <= 1:
            continue
        variants.append((kind, normalized, surfaces, group_entries))

    if not variants:
        print("SudachiPy term variants: none")
        return 0

    print("SudachiPy term variants:")
    for kind, normalized, surfaces, group_entries in sorted(variants, key=lambda item: (item[0], item[1])):
        total = sum(int(entry["count"]) for entry in group_entries)
        print(f"- {kind}: {normalized} ({total}) -> {', '.join(surfaces)}")

    return 1 if args.fail_on_variants else 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--files", nargs="+", help="Specific Markdown files to inspect.")
    parser.add_argument("--fail-on-variants", action="store_true", help="Exit with failure when variants are found.")
    return parser.parse_args()


def list_markdown_targets() -> list[str]:
    result = subprocess.run(
        ["node", "tools/lint/run-skill-script.js", "review-enforcer/scripts/list-markdown-targets.js"],
        text=True,
        stdout=subprocess.PIPE,
        check=True,
    )
    return [line for line in result.stdout.splitlines() if line.strip()]


def extract_entries(files: list[str]) -> list[dict]:
    script = Path("tools/lint/extract-sudachi-vocabulary-chunked.py")
    result = subprocess.run(
        [sys.executable, str(script), "--format", "json", "--files", *files],
        text=True,
        stdout=subprocess.PIPE,
        check=True,
    )
    return json.loads(result.stdout)


if __name__ == "__main__":
    raise SystemExit(main())
