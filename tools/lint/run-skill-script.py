#!/usr/bin/env python3
"""Run a script from the local Codex skill directory."""

from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path


def main() -> int:
    if len(sys.argv) < 2:
        print("Usage: run-skill-script.py <relative-skill-script> [args...]", file=sys.stderr)
        return 2

    root = Path.cwd()
    skills_dir = resolve_skills_dir(root)
    script_path = skills_dir / sys.argv[1]
    if not script_path.exists():
        print(f"Skill script not found: {script_path}", file=sys.stderr)
        return 2

    result = subprocess.run([sys.executable, str(script_path), *sys.argv[2:]], cwd=root, check=False)
    return result.returncode


def resolve_skills_dir(root: Path) -> Path:
    candidates = [
        os.environ.get("CODEX_SKILLS_DIR"),
        str(root / ".codex" / "skills"),
        str(root / ".agents" / "skills"),
        "/home/ibis/AI/CodexSkill/skills",
    ]
    for candidate in candidates:
        if candidate and Path(candidate).exists():
            return Path(candidate)

    print("Codex skill directory was not found. Set CODEX_SKILLS_DIR or provide .codex/skills.", file=sys.stderr)
    raise SystemExit(2)


if __name__ == "__main__":
    raise SystemExit(main())
