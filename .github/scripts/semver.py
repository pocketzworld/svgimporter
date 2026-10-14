"""Minimal SemVer 2.0 parsing and ordering for package.json versions."""
import re

SEMVER = re.compile(
    r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
    r"(?:-((?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?"
    r"(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$"
)


def parse(version):
    m = SEMVER.match(version)
    if not m:
        raise ValueError(f"'{version}' is not a SemVer version (e.g. 1.1.6-h4k.2)")
    core = tuple(int(m.group(i)) for i in (1, 2, 3))
    pre = m.group(4).split(".") if m.group(4) else []
    return core, pre


def _pre_key(ident):
    # Numeric identifiers sort before alphanumeric ones and compare as numbers.
    return (0, int(ident), "") if ident.isdigit() else (1, 0, ident)


def compare(a, b):
    """Return -1, 0 or 1 like a <=> b, following SemVer precedence."""
    (ca, pa), (cb, pb) = parse(a), parse(b)
    if ca != cb:
        return -1 if ca < cb else 1
    if not pa or not pb:
        # A release sorts after any prerelease of the same version.
        return (not pa) - (not pb)
    for x, y in zip(pa, pb):
        kx, ky = _pre_key(x), _pre_key(y)
        if kx != ky:
            return -1 if kx < ky else 1
    return (len(pa) > len(pb)) - (len(pa) < len(pb))
