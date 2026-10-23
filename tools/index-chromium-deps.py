# SPDX-License-Identifier: GPL-3.0-or-later
"""Index pinned Chromium DEPS without executing its Python or downloading code.

The output is an acquisition/review index, not a complete source release. Nested
DEPS, conditions and non-Git inputs deliberately remain visible and unapproved.
"""
import argparse
import ast
import hashlib
import json
import re
from pathlib import Path
from urllib.parse import urlsplit

PIN = "bd35b66a22eb81109589ee5093b7c492fd7ed32485574446a107e3f274affc55"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--deps", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    source = args.deps.read_bytes()
    if hashlib.sha256(source).hexdigest() != PIN:
        raise ValueError("Chromium DEPS input does not match the reviewed commit")
    tree = ast.parse(source.decode("utf-8"))
    assignments = {}
    for statement in tree.body:
        if not isinstance(statement, ast.Assign):
            raise ValueError("Unsupported DEPS statement; nothing was executed")
        for target in statement.targets:
            if not isinstance(target, ast.Name):
                raise ValueError("Unsupported assignment target")
            assignments[target.id] = statement.value

    variables = {}

    def read(node):
        if isinstance(node, ast.Constant):
            return node.value
        if isinstance(node, ast.List):
            return [read(item) for item in node.elts]
        if isinstance(node, ast.Dict):
            keys = [read(item) for item in node.keys]
            if len(set(keys)) != len(keys):
                raise ValueError("Duplicate DEPS keys require explicit review")
            return dict(zip(keys, (read(item) for item in node.values)))
        if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Add):
            left, right = read(node.left), read(node.right)
            if not isinstance(left, str) or not isinstance(right, str):
                raise ValueError("Only string concatenation is supported")
            return left + right
        if isinstance(node, ast.Call) and isinstance(node.func, ast.Name):
            if node.keywords or len(node.args) != 1:
                raise ValueError("Unsupported DEPS call signature")
            value = read(node.args[0])
            if node.func.id == "Str" and isinstance(value, str):
                return value
            if node.func.id == "Var" and isinstance(value, str):
                return variables[value]
        raise ValueError(f"Unsupported DEPS expression: {type(node).__name__}")

    variables.update(read(assignments["vars"]))
    dependencies = read(assignments["deps"])
    rows = []
    for destination, value in dependencies.items():
        row = {"path": destination, "condition": None, "kind": "git"}
        if isinstance(value, dict):
            row["condition"] = value.get("condition")
            row["kind"] = value.get("dep_type", "git")
            url = value.get("url")
            row["definition"] = value
        else:
            url = value
        if isinstance(url, str):
            url = re.sub(r"\{([A-Za-z_][A-Za-z0-9_]*)\}", lambda match: str(variables[match[1]]), url)
            if "{" in url or "}" in url:
                raise ValueError(f"Unresolved URL template: {destination}")
            repository, separator, revision = url.rpartition("@")
            if not separator or urlsplit(repository).scheme != "https":
                raise ValueError(f"Unpinned/non-HTTPS Git input: {destination}")
            row.update(repository=repository, revision=revision)
            row["commitPinned"] = len(revision) == 40 and all(c in "0123456789abcdef" for c in revision)
        rows.append(row)
    result = {
        "schemaVersion": 1,
        "chromiumCommit": "c51f2916e4a14157c276fd33c8bfb24155aac1ad",
        "depsSha256": PIN,
        "inputExecuted": False,
        "conditionsEvaluated": False,
        "recursiveDependencies": read(assignments["recursedeps"]) if "recursedeps" in assignments else [],
        "gitDependencies": sum(row["kind"] == "git" for row in rows),
        "otherDependencies": sum(row["kind"] != "git" for row in rows),
        "correspondingSourceComplete": False,
        "dependencies": rows,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"Indexed {len(rows)} dependencies; no code executed, no downloads, no completeness approval.")


if __name__ == "__main__":
    main()
