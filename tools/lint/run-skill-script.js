"use strict";

const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");

const root = process.cwd();
const args = process.argv.slice(2);
const printPath = args[0] === "--print-path";
const relativeScript = printPath ? args[1] : args[0];

if (!relativeScript) {
  console.error("Usage: run-skill-script.js [--print-path] <relative-skill-script> [args...]");
  process.exit(2);
}

const skillsDir = resolveSkillsDir();
const scriptPath = path.join(skillsDir, relativeScript);

if (!fs.existsSync(scriptPath)) {
  console.error(`Skill script not found: ${scriptPath}`);
  process.exit(2);
}

if (printPath) {
  process.stdout.write(scriptPath);
  process.exit(0);
}

const command = scriptPath.endsWith(".py") ? "python3" : "node";
const result = spawnSync(command, [scriptPath, ...args.slice(1)], {
  cwd: root,
  stdio: "inherit"
});

process.exit(result.status === null ? 1 : result.status);

function resolveSkillsDir() {
  const candidates = [
    process.env.CODEX_SKILLS_DIR,
    path.join(root, ".codex", "skills"),
    path.join(root, ".agents", "skills"),
    "/home/ibis/AI/CodexSkill/skills"
  ].filter(Boolean);

  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) {
      return candidate;
    }
  }

  console.error("Codex skill directory was not found. Set CODEX_SKILLS_DIR or provide .codex/skills.");
  process.exit(2);
}
