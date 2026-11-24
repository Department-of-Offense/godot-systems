#!/usr/bin/env bash
# Vendor Godot Systems modules (addons/dof_*) into a game project.
# Copy this script into the game's tools/ folder and run it from anywhere.
#
#   tools/sync-systems.sh v0.2.0                     # same modules as last time (or all, first run)
#   tools/sync-systems.sh v0.2.0 cameras units       # just these, plus whatever they depend on
#   tools/sync-systems.sh v0.2.0 all                 # every module
#   SYSTEMS_PATH=../godot-systems tools/sync-systems.sh [modules...]   # from a local checkout
#
# Writes SYSTEMS_VERSION (ref, commit, modules) so everyone can see what the
# game is built on, and so CI can check the vendored copy wasn't hand-edited.
# Commit the result: the game must build with no extra setup.
set -euo pipefail

REPO_URL="${SYSTEMS_REPO:-https://github.com/Department-of-Offense/godot-systems.git}"
GAME="$(cd "$(dirname "$0")/.." && pwd)"
VERSION_FILE="$GAME/SYSTEMS_VERSION"

if [[ -n "${SYSTEMS_PATH:-}" ]]; then
  SRC="$(cd "$SYSTEMS_PATH" && pwd)"
  REF="local:$(git -C "$SRC" describe --tags --always --dirty 2>/dev/null || echo unknown)"
  SHA="$(git -C "$SRC" rev-parse HEAD 2>/dev/null || echo unknown)"
else
  REF="${1:?usage: tools/sync-systems.sh <tag-or-branch> [module ...|all]   (or set SYSTEMS_PATH)}"
  shift
  TMP="$(mktemp -d)"
  trap 'rm -rf "$TMP"' EXIT
  git -c advice.detachedHead=false clone --quiet --depth 1 --branch "$REF" "$REPO_URL" "$TMP/systems"
  SRC="$TMP/systems"
  SHA="$(git -C "$SRC" rev-parse HEAD)"
fi

available() { (cd "$SRC/addons" && ls -d dof_*/ | tr -d /); }
depends_of() { sed -n 's/^depends="\(.*\)"/\1/p' "$SRC/addons/$1/module.cfg"; }
normalize() { local m="$1"; [[ "$m" == dof_* ]] || m="dof_$m"; echo "$m"; }

# Which modules were asked for: arguments, else last time's list, else all.
requested=()
if [[ $# -gt 0 ]]; then
  if [[ "$1" == "all" ]]; then mapfile -t requested < <(available); else
    for m in "$@"; do requested+=("$(normalize "$m")"); done
  fi
elif [[ -f "$VERSION_FILE" ]] && sed -n 3p "$VERSION_FILE" | grep -q .; then
  read -r -a requested <<< "$(sed -n 3p "$VERSION_FILE")"
else
  mapfile -t requested < <(available)
fi

# Add dependencies until nothing new appears.
declare -A want=()
queue=("${requested[@]}")
while [[ ${#queue[@]} -gt 0 ]]; do
  m="${queue[0]}"; queue=("${queue[@]:1}")
  [[ -n "${want[$m]:-}" ]] && continue
  if [[ ! -f "$SRC/addons/$m/module.cfg" ]]; then
    echo "error: no module '$m' in $REF. Available: $(available | tr '\n' ' ')" >&2
    exit 1
  fi
  want[$m]=1
  for d in $(depends_of "$m"); do queue+=("$d"); done
done
modules="$(printf '%s\n' "${!want[@]}" | sort | tr '\n' ' ' | sed 's/ $//')"

# Replace vendored modules; drop ones no longer wanted.
mkdir -p "$GAME/addons"
for existing in "$GAME"/addons/dof_*; do
  [[ -d "$existing" ]] && rm -rf "$existing"
done
for m in $modules; do cp -R "$SRC/addons/$m" "$GAME/addons/$m"; done

printf '%s\n%s\n%s\n' "$REF" "$SHA" "$modules" > "$VERSION_FILE"
echo "Synced $REF ($SHA)"
echo "Modules: $modules"
echo "Next: dotnet build, run your tests, then commit addons/dof_* and SYSTEMS_VERSION."
