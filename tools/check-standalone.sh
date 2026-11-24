#!/usr/bin/env bash
# Compile every module on its own, with only the modules its module.cfg
# depends on (recursively). Proves a game can vendor just that subset.
#   tools/check-standalone.sh            # all modules
#   tools/check-standalone.sh dof_cameras
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SDK="$(sed -n 's/.*Sdk="\([^"]*\)".*/\1/p' "$ROOT"/*.csproj | head -n1)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

closure() {
  local -A seen=(); local queue=("$1") m
  while [[ ${#queue[@]} -gt 0 ]]; do
    m="${queue[0]}"; queue=("${queue[@]:1}")
    [[ -n "${seen[$m]:-}" ]] && continue
    seen[$m]=1
    for d in $(sed -n 's/^depends="\(.*\)"/\1/p' "$ROOT/addons/$m/module.cfg"); do queue+=("$d"); done
  done
  printf '%s\n' "${!seen[@]}"
}

if [[ $# -gt 0 ]]; then modules=("$@"); else mapfile -t modules < <(cd "$ROOT/addons" && ls -d dof_*/ | tr -d /); fi

failed=0
for module in "${modules[@]}"; do
  dir="$WORK/$module"
  mkdir -p "$dir/addons"
  deps="$(closure "$module" | sort | tr '\n' ' ')"
  for m in $deps; do cp -R "$ROOT/addons/$m" "$dir/addons/$m"; done
  cat > "$dir/Standalone.csproj" <<EOF
<Project Sdk="$SDK">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <Nullable>enable</Nullable>
    <LangVersion>12</LangVersion>
  </PropertyGroup>
</Project>
EOF
  if out="$(dotnet build "$dir/Standalone.csproj" --nologo -v q -warnaserror 2>&1)"; then
    echo "ok    $module  ($deps)"
  else
    echo "FAIL  $module  ($deps)"
    echo "$out" | grep -E "error" | sed 's/^/      /' | head -n 10
    failed=1
  fi
done
exit $failed
