#!/usr/bin/env bash
#
# check-conventions.sh — machine-checkable guardrails for the rules in CLAUDE.md
# that prose alone tends to let slip. Run locally or in CI; exits non-zero on any
# violation so a bad change can't merge silently.
#
#   1. No empty catch blocks in src/ (the "never swallow silently" rule).
#   2. The version string is identical across the four files that must stay in
#      sync (csproj, both SubModule.xml files, and the top of CHANGELOG.md).
#
# Usage: tools/checks/check-conventions.sh   (run from the repo root)

set -uo pipefail
cd "$(dirname "$0")/../.." || exit 2

fail=0

# ── 1. No empty catch blocks ────────────────────────────────────────────────
# Matches `catch {}`, `catch { }`, and `catch (Foo) {}` with an empty body.
echo "==> Checking for empty catch blocks in src/ ..."
empty_catches=$(grep -rnE 'catch[[:space:]]*(\([^)]*\))?[[:space:]]*\{[[:space:]]*\}' \
    --include='*.cs' src/ || true)
if [ -n "$empty_catches" ]; then
    echo "FAIL: empty catch block(s) found — use: catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }"
    echo "$empty_catches"
    fail=1
else
    echo "  ok — none found"
fi

# ── 2. Version strings in sync ──────────────────────────────────────────────
# Normalise every source to a bare X.Y.Z and confirm they all agree.
echo "==> Checking version strings are in sync ..."
csproj_v=$(grep -oE '<Version>[0-9]+\.[0-9]+\.[0-9]+' src/TheWitheringArt.csproj | head -1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+')
sub_v=$(grep -oE 'Version value="v?[0-9]+\.[0-9]+\.[0-9]+' SubModule.xml | head -1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+')
dist_v=$(grep -oE 'Version value="v?[0-9]+\.[0-9]+\.[0-9]+' dist/AshAndEmber/SubModule.xml | head -1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+')
log_v=$(grep -oE '^## v?[0-9]+\.[0-9]+\.[0-9]+' CHANGELOG.md | head -1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+')

echo "  csproj=$csproj_v  SubModule.xml=$sub_v  dist/SubModule.xml=$dist_v  CHANGELOG=$log_v"
if [ -z "$csproj_v" ] || [ "$csproj_v" != "$sub_v" ] || [ "$csproj_v" != "$dist_v" ] || [ "$csproj_v" != "$log_v" ]; then
    echo "FAIL: version strings disagree — bump all four together (see behaviour.md)."
    fail=1
else
    echo "  ok — all four report $csproj_v"
fi

[ "$fail" -eq 0 ] && echo "All convention checks passed." || echo "Convention checks FAILED."
exit "$fail"
