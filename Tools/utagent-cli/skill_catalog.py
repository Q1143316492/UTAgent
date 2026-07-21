"""UTAgent skill catalog — 与 Chat Available Skills / loadSkill 同真源。

list：仅 frontmatter；get：全文。包根由本文件位置解析，不依赖 cwd / Editor。
"""

from __future__ import annotations

import json
import os
import sys
from typing import Any


def resolve_package_root() -> str:
    """Tools/utagent-cli → Assets/UTAgent。"""
    here = os.path.abspath(os.path.dirname(__file__))
    # .../Assets/UTAgent/Tools/utagent-cli
    root = os.path.abspath(os.path.join(here, "..", ".."))
    skills = os.path.join(root, "Python", "agent", "skills")
    if not os.path.isdir(skills):
        raise FileNotFoundError(
            f"无法解析 UTAgent 包根（缺少 Python/agent/skills）：tried {root}"
        )
    return root


def skills_dir(package_root: str | None = None) -> str:
    root = package_root or resolve_package_root()
    return os.path.join(root, "Python", "agent", "skills")


def parse_frontmatter(text: str) -> dict[str, str]:
    """解析开头 YAML frontmatter（单行 name/description/assert），与 Chat 兼容。"""
    out: dict[str, str] = {}
    if not text.startswith("---"):
        return out
    end = text.find("---", 3)
    if end < 0:
        return out
    block = text[3:end]
    for line in block.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        if ":" not in line:
            continue
        key, val = line.split(":", 1)
        key = key.strip()
        val = val.strip().strip('"').strip("'")
        if key in ("name", "description", "assert") and val:
            out[key] = val
    return out


def _read_head(path: str, max_bytes: int = 4096) -> str:
    """list 只需 frontmatter：读文件开头即可。"""
    with open(path, "r", encoding="utf-8") as f:
        return f.read(max_bytes)


def list_skills(package_root: str | None = None) -> list[dict[str, Any]]:
    root = package_root or resolve_package_root()
    d = skills_dir(root)
    entries: list[dict[str, Any]] = []
    warnings: list[str] = []
    for fname in sorted(os.listdir(d)):
        if not fname.endswith(".md.txt"):
            continue
        skill_id = fname[: -len(".md.txt")]
        path = os.path.abspath(os.path.join(d, fname))
        head = _read_head(path)
        fm = parse_frontmatter(head)
        item: dict[str, Any] = {
            "id": skill_id,
            "name": fm.get("name") or skill_id,
            "description": fm.get("description") or "",
            "path": path,
        }
        rel_assert = fm.get("assert")
        if rel_assert:
            abs_assert = os.path.abspath(os.path.join(root, rel_assert.replace("/", os.sep)))
            if os.path.isfile(abs_assert):
                item["assert"] = abs_assert
            else:
                warnings.append(f"{skill_id}: assert 文件不存在: {abs_assert}")
        entries.append(item)
    for w in warnings:
        print(f"warning: {w}", file=sys.stderr)
    return entries


def get_skill(skill_id: str, package_root: str | None = None) -> dict[str, Any]:
    root = package_root or resolve_package_root()
    path = os.path.abspath(os.path.join(skills_dir(root), f"{skill_id}.md.txt"))
    if not os.path.isfile(path):
        raise FileNotFoundError(f"skill 不存在: {skill_id} ({path})")
    with open(path, "r", encoding="utf-8") as f:
        content = f.read()
    fm = parse_frontmatter(content)
    result: dict[str, Any] = {
        "id": skill_id,
        "name": fm.get("name") or skill_id,
        "description": fm.get("description") or "",
        "path": path,
        "content": content,
    }
    rel_assert = fm.get("assert")
    if rel_assert:
        abs_assert = os.path.abspath(os.path.join(root, rel_assert.replace("/", os.sep)))
        if os.path.isfile(abs_assert):
            result["assert"] = abs_assert
    return result


def format_list_human(entries: list[dict[str, Any]]) -> str:
    lines = ["id\tname\tdescription\tpath"]
    for e in entries:
        desc = (e.get("description") or "").replace("\t", " ")
        line = f"{e['id']}\t{e.get('name', '')}\t{desc}\t{e['path']}"
        if e.get("assert"):
            line += f"\n  assert: {e['assert']}"
        lines.append(line)
    return "\n".join(lines)


def cmd_skill_list(args: Any) -> int:
    try:
        entries = list_skills()
    except FileNotFoundError as e:
        print(str(e), file=sys.stderr)
        return 1
    if getattr(args, "json", False):
        print(json.dumps({"ok": True, "skills": entries}, ensure_ascii=False, indent=2))
    else:
        print(format_list_human(entries))
    return 0


def cmd_skill_get(args: Any) -> int:
    skill_id = getattr(args, "skill_id", None) or ""
    skill_id = skill_id.strip()
    if not skill_id:
        print("缺少 skill id", file=sys.stderr)
        return 1
    try:
        data = get_skill(skill_id)
    except FileNotFoundError as e:
        print(str(e), file=sys.stderr)
        return 1
    if getattr(args, "json", False):
        print(json.dumps({"ok": True, **data}, ensure_ascii=False, indent=2))
    else:
        print(data["content"], end="" if data["content"].endswith("\n") else "\n")
    return 0
