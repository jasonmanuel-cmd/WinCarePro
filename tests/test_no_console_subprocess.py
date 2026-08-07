"""All GUI-runtime child processes must suppress Windows console windows."""

import ast
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
EXCLUDED = {".venv", "build", "dist", "release", "tests", ".git", ".worktrees"}
SUBPROCESS_CALLS = {"run", "Popen", "call", "check_call", "check_output"}


def production_python_files():
    for path in ROOT.rglob("*.py"):
        if not any(part in EXCLUDED for part in path.relative_to(ROOT).parts):
            yield path


def test_every_runtime_subprocess_call_suppresses_console_windows():
    violations = []
    calls = 0
    for path in production_python_files():
        tree = ast.parse(path.read_text(encoding="utf-8-sig"), filename=str(path))
        for node in ast.walk(tree):
            if not isinstance(node, ast.Call) or not isinstance(node.func, ast.Attribute):
                continue
            if not isinstance(node.func.value, ast.Name) or node.func.value.id != "subprocess":
                continue
            if node.func.attr not in SUBPROCESS_CALLS:
                continue
            calls += 1
            if "creationflags" not in {keyword.arg for keyword in node.keywords}:
                violations.append(f"{path.relative_to(ROOT)}:{node.lineno}")

    assert calls > 0
    assert violations == []
