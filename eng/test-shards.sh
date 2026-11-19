#!/usr/bin/env bash
# The shards of CI's test job, from the test projects of FluentCMS.slnx (those that reference xunit.v3), printed as the JSON
# of the job's matrix. A test project goes to the shard of the first rule its path matches, so a new one runs without a
# change here; one that no rule takes fails the run, as does a lockfile of an npm project that no job of ci.yml runs in.
set -euo pipefail
cd "$(dirname "$0")/.."

units="" integration="" plugins1="" plugins2="" admin="" missing=""
while IFS= read -r csproj; do
  grep -q 'Include="xunit.v3"' "$csproj" || continue
  project=$(dirname "$csproj")
  case "$project" in
    # Their own jobs: the data tests run once per provider, and the end-to-end tests in a browser.
    tests/FluentCMS.Data.Tests | admin/tests/FluentCMS.Admin.EndToEnd) ;;
    tests/FluentCMS.Core.IntegrationTests) integration+=" $project" ;;
    tests/FluentCMS.Host.Tests | tests/FluentCMS.Plugins.Tests | tests/FluentCMS.Plugins.Media.Tests | tests/FluentCMS.Plugins.Webhooks.Tests) plugins1+=" $project" ;;
    tests/FluentCMS.Plugins.*) plugins2+=" $project" ;;
    tests/*) units+=" $project" ;;
    admin/tests/*) admin+=" $project" ;;
    *) missing+=" $project" ;;
  esac
done < <(sed -n 's|.*<Project Path="\([^"]*\.csproj\)".*|\1|p' FluentCMS.slnx)

if [ -n "$missing" ]; then
  echo "::error::No shard of CI's test job takes these test projects; add a rule to eng/test-shards.sh:$missing" >&2
  exit 1
fi

if [ "$integration" != " tests/FluentCMS.Core.IntegrationTests" ]; then
  echo "::error::The integration shards expect tests/FluentCMS.Core.IntegrationTests alone, and found:$integration" >&2
  exit 1
fi

for shard in units plugins1 plugins2 admin; do
  if [ -z "${!shard}" ]; then
    echo "::error::The $shard shard of CI's test job has no test project; change the rules of eng/test-shards.sh." >&2
    exit 1
  fi
done

while IFS= read -r lockfile; do
  directory=$(dirname "$lockfile")
  if ! grep -qE "working-directory: $directory\$" .github/workflows/ci.yml; then
    echo "::error::No job of ci.yml runs in $directory, the npm project of $lockfile." >&2
    exit 1
  fi
done < <(git ls-files '*package-lock.json')

# The two integration shards carry the same namespaces: the first runs them and the second every other, so a new namespace
# runs without a change here. A minimum keeps a filter that matches nothing from passing.
namespaces='\"FluentCMS.Core.IntegrationTests.Identity*\" \"FluentCMS.Core.IntegrationTests.Api*\" \"FluentCMS.Core.IntegrationTests.Caching*\" \"FluentCMS.Core.IntegrationTests.Blog*\"'

shard() {
  printf '{"group":"%s","timeout":"%s","projects":"%s","args":"%s"}' "$1" "$2" "${3# }" "${4:-}"
}

echo "[$(shard units 5m "$units"),$(shard integration-1 8m "$integration" "--filter-namespace $namespaces --minimum-expected-tests 250"),$(shard integration-2 8m "$integration" "--filter-not-namespace $namespaces --minimum-expected-tests 550"),$(shard plugins-1 5m "$plugins1"),$(shard plugins-2 5m "$plugins2"),$(shard admin 5m "$admin")]"
