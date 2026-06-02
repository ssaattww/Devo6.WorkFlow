#!/usr/bin/env python3
"""Extract Markdown vocabulary with SudachiPy while chunking large files."""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
from collections import Counter
from dataclasses import dataclass, field
from pathlib import Path


ENGLISH_RE = re.compile(r"(?<![A-Za-z0-9])[A-Za-z][A-Za-z0-9]*(?:[._-][A-Za-z0-9]+)*")
FENCE_RE = re.compile(r"^[ \t]*(```|~~~)[^\n]*(?:\n[\s\S]*?)?^[ \t]*\1[^\n]*(?=\n|$)", re.MULTILINE)
INLINE_CODE_RE = re.compile(r"`[^`\n]+`")
FOOTNOTE_DEF_RE = re.compile(r"^\[\^[^\]]+\]:.*$", re.MULTILINE)
URL_RE = re.compile(r"https?://[^\s)]+")
MAILTO_RE = re.compile(r"mailto:[^\s)]+")
HTML_COMMENT_RE = re.compile(r"<!--[\s\S]*?-->")
REFERENCE_LINK_RE = re.compile(r"^\[[^\]\n]+\]:\s+\S+.*$", re.MULTILINE)
INLINE_LINK_RE = re.compile(r"!?\[[^\]\n]+\]\([^)]+\)")
KATAKANA_RE = re.compile(r"[\u30A0-\u30FF]")
CJK_RE = re.compile(r"[\u3400-\u9FFF]")
MAX_CHUNK_BYTES = 32000


@dataclass
class Entry:
    kind: str
    surface: str
    normalized: str
    reading: str = ""
    part_of_speech: str = ""
    count: int = 0
    sources: Counter[str] = field(default_factory=Counter)


def main() -> int:
    args = parse_args()
    tokenizer_obj, split_mode = create_tokenizer()
    vocabulary: dict[tuple[str, str, str, str], Entry] = {}

    for file_name in args.files:
        path = Path(file_name)
        text = strip_markdown_noise(path.read_text(encoding="utf-8"))
        collect_english(vocabulary, text, file_name)
        collect_japanese(vocabulary, tokenizer_obj, split_mode, text, file_name)

    entries = sorted(
        vocabulary.values(),
        key=lambda item: (-item.count, item.kind, item.normalized.casefold(), item.surface.casefold(), item.part_of_speech),
    )
    if args.format == "json":
        print(json.dumps([entry_to_dict(entry) for entry in entries], ensure_ascii=False, indent=2))
    else:
        print("type\tsurface\tnormalized\treading\tpartOfSpeech\tcount\tsources")
        for entry in entries:
            sources = ",".join(f"{source}:{count}" for source, count in sorted(entry.sources.items()))
            print(
                f"{entry.kind}\t{entry.surface}\t{entry.normalized}\t{entry.reading}\t"
                f"{entry.part_of_speech}\t{entry.count}\t{sources}"
            )
    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--files", nargs="+", required=True)
    parser.add_argument("--format", choices=("tsv", "json"), default="tsv")
    return parser.parse_args()


def create_tokenizer():
    try:
        from sudachipy import dictionary
        from sudachipy import tokenizer as sudachi_tokenizer
    except ImportError as exc:
        print(f"Missing dependency: {exc.name}. Run `pip install -r tools/lint/requirements.txt`.", file=sys.stderr)
        raise SystemExit(2) from exc

    return dictionary.Dictionary().create(), sudachi_tokenizer.Tokenizer.SplitMode.C


def strip_markdown_noise(text: str) -> str:
    cleaned = FENCE_RE.sub(blank_preserving, text)
    cleaned = INLINE_CODE_RE.sub(blank_preserving, cleaned)
    cleaned = FOOTNOTE_DEF_RE.sub(blank_preserving, cleaned)
    cleaned = URL_RE.sub(blank_preserving, cleaned)
    cleaned = MAILTO_RE.sub(blank_preserving, cleaned)
    cleaned = HTML_COMMENT_RE.sub(blank_preserving, cleaned)
    cleaned = REFERENCE_LINK_RE.sub(lambda match: re.sub(r":\s+\S+.*$", ": ", match.group(0)), cleaned)
    cleaned = INLINE_LINK_RE.sub(lambda match: re.sub(r"\([^)]+\)", blank_preserving, match.group(0)), cleaned)
    return cleaned


def blank_preserving(value: str | re.Match[str]) -> str:
    text = value.group(0) if isinstance(value, re.Match) else value
    return re.sub(r"[^\n]", " ", text)


def collect_english(vocabulary: dict[tuple[str, str, str, str], Entry], text: str, source: str) -> None:
    for match in ENGLISH_RE.finditer(text):
        surface = match.group(0)
        if len(surface) <= 1 or any(character.isdigit() for character in surface):
            continue
        add_entry(vocabulary, "english", surface, normalize(surface), "", "", source)


def collect_japanese(vocabulary: dict[tuple[str, str, str, str], Entry], tokenizer_obj, split_mode, text: str, source: str) -> None:
    for chunk in iter_chunks(text):
        for morpheme in tokenizer_obj.tokenize(chunk, split_mode):
            surface = morpheme.surface()
            if not should_collect_japanese(surface, morpheme):
                continue
            normalized = sudachi_value(morpheme, "normalized_form") or surface
            reading = sudachi_value(morpheme, "reading_form")
            part_of_speech = ",".join(str(part) for part in morpheme.part_of_speech() if part != "*")
            kind = "japanese" if CJK_RE.search(surface) else "katakana"
            add_entry(vocabulary, kind, surface, normalize(normalized), reading, part_of_speech, source)


def iter_chunks(text: str):
    current: list[str] = []
    current_bytes = 0
    for line in text.splitlines(keepends=True):
        line_bytes = len(line.encode("utf-8"))
        if current and current_bytes + line_bytes > MAX_CHUNK_BYTES:
            yield "".join(current)
            current = []
            current_bytes = 0
        if line_bytes > MAX_CHUNK_BYTES:
            for start in range(0, len(line), MAX_CHUNK_BYTES // 4):
                yield line[start : start + MAX_CHUNK_BYTES // 4]
            continue
        current.append(line)
        current_bytes += line_bytes
    if current:
        yield "".join(current)


def should_collect_japanese(surface: str, morpheme) -> bool:
    if not (KATAKANA_RE.search(surface) or CJK_RE.search(surface)):
        return False
    normalized = normalize(surface).replace("・", "").replace("ー", "").replace(".", "").replace("_", "").replace("-", "")
    if len(normalized) <= 1:
        return False
    part_of_speech = morpheme.part_of_speech()
    return bool(part_of_speech) and part_of_speech[0] == "名詞"


def sudachi_value(morpheme, name: str) -> str:
    value = getattr(morpheme, name, None)
    if value is None:
        return ""
    result = value()
    return "" if result == "*" else str(result)


def add_entry(
    vocabulary: dict[tuple[str, str, str, str], Entry],
    kind: str,
    surface: str,
    normalized: str,
    reading: str,
    part_of_speech: str,
    source: str,
) -> None:
    key = (kind, normalized, reading, part_of_speech)
    entry = vocabulary.get(key)
    if entry is None:
        entry = Entry(kind=kind, surface=surface, normalized=normalized, reading=reading, part_of_speech=part_of_speech)
        vocabulary[key] = entry
    entry.count += 1
    entry.sources[source] += 1


def normalize(value: str) -> str:
    return value.casefold()


def entry_to_dict(entry: Entry) -> dict[str, object]:
    return {
        "kind": entry.kind,
        "surface": entry.surface,
        "normalized": entry.normalized,
        "reading": entry.reading,
        "partOfSpeech": entry.part_of_speech,
        "count": entry.count,
        "sources": dict(entry.sources),
    }


if __name__ == "__main__":
    raise SystemExit(main())
