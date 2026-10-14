"""Pull request check: a change to the package needs a new version and a changelog entry.

Usage: check_version.py <base sha>
Docs-only changes (README, CHANGELOG, Documentation~) and CI files under .github/ need no new version.
"""
import json
import subprocess
import sys

import semver

DOCS = {"README.md", "README.md.meta", "CHANGELOG.txt", "CHANGELOG.txt.meta"}


def git(*args):
    return subprocess.run(["git", *args], check=True, capture_output=True, text=True).stdout


def needs_version(path):
    return not (path.startswith(".github/") or path in DOCS or path.startswith("Documentation~/"))


def main(base):
    changed = [p for p in git("diff", "--name-only", f"{base}...HEAD").splitlines() if p]
    package_changes = [p for p in changed if needs_version(p)]
    if not package_changes:
        print("No package files changed; no new version needed.")
        return 0

    base_version = json.loads(git("show", f"{base}:package.json"))["version"]
    version = json.load(open("package.json"))["version"]
    errors = []
    try:
        if semver.compare(version, base_version) <= 0:
            errors.append(f"package.json version {version} must be higher than {base_version} on the base branch.")
    except ValueError as e:
        errors.append(str(e))
    if subprocess.run(["git", "rev-parse", "-q", "--verify", f"refs/tags/v{version}"], capture_output=True).returncode == 0:
        errors.append(f"Tag v{version} already exists; pick a new version.")
    if f"Version {version}" not in open("CHANGELOG.txt", encoding="utf-8").read():
        errors.append(f"CHANGELOG.txt needs a 'Version {version}' entry.")

    print(f"{len(package_changes)} package file(s) changed, e.g. {', '.join(package_changes[:5])}")
    for e in errors:
        print(f"::error::{e}")
    if not errors:
        print(f"Version {base_version} -> {version}: OK")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
