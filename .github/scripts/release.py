"""On master: tag the package.json version and publish a GitHub Release with the package .tgz.

Does nothing when the tag already exists, so merges that keep the version (docs, CI) release nothing.
Set DRY_RUN=1 to print what would happen.
"""
import json
import os
import re
import subprocess
import sys

DRY_RUN = os.environ.get("DRY_RUN") == "1"
REPO = os.environ.get("GITHUB_REPOSITORY", "pocketzworld/svgimporter")


def run(*args, **kw):
    print("+", " ".join(args))
    if not DRY_RUN:
        subprocess.run(args, check=True, **kw)


def changelog_section(version):
    text = open("CHANGELOG.txt", encoding="utf-8").read()
    m = re.search(rf"^Version {re.escape(version)}\s*$(.*?)(?=^Version |\Z)", text, re.M | re.S)
    return m.group(1).strip() if m else "See CHANGELOG.txt."


def main():
    package = json.load(open("package.json"))
    name, version = package["name"], package["version"]
    tag = f"v{version}"
    if subprocess.run(["git", "rev-parse", "-q", "--verify", f"refs/tags/{tag}"], capture_output=True).returncode == 0:
        print(f"{tag} already exists; nothing to release.")
        return 0

    notes = (
        f"Install in `Packages/manifest.json`:\n\n```json\n"
        f"\"{name}\": \"https://github.com/{REPO}.git#{tag}\"\n```\n\n"
        f"The attached `.tgz` holds the same content for offline use.\n\n"
        f"## Changes\n{changelog_section(version)}\n"
    )
    archive = f"{name}-{version}.tgz"
    with open("release-notes.md", "w", encoding="utf-8") as f:
        f.write(notes)

    run("git", "-c", "user.name=github-actions[bot]",
        "-c", "user.email=41898282+github-actions[bot]@users.noreply.github.com",
        "tag", "-a", tag, "-m", f"{name} {version}")
    run("git", "push", "origin", tag)
    run("git", "archive", "--format=tar.gz", "--prefix=package/", "-o", archive, tag)
    run("gh", "release", "create", tag, archive, "--verify-tag", "--title", version, "--notes-file", "release-notes.md")
    if DRY_RUN:
        print(notes)
    return 0


if __name__ == "__main__":
    sys.exit(main())
