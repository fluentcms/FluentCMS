#!/usr/bin/env bash
# Installs FluentCMS.Templates from a folder feed that holds it and the FluentCMS packages of the same version, creates every
# combination of the templates, builds each with warnings as errors and runs the plugin's tests. Then it starts the SQLite
# hosts in Development and reads their sample type through the Delivery API, and, when FLUENTCMS_TEMPLATES_POSTGRES holds a
# connection string, brings up a PostgreSQL host with `fluentcms migrations add Initial` and `fluentcms migrate`.
#
#   templates/test-templates.sh <feed> <version> <work directory>
#
# Everything it writes is under the work directory, the NuGet packages and the installed templates included.
set -euo pipefail

if [ $# -ne 3 ]; then
  echo "usage: $0 <feed> <version> <work directory>" >&2
  exit 2
fi

version=$2
mkdir -p "$3"
work=$(cd "$3" && pwd)
feed=$(cd "$1" && pwd)

# NuGet on Windows needs a Windows path; Git Bash gives one with pwd -W.
native() { (cd "$1" && (pwd -W 2>/dev/null || pwd)); }

export NUGET_PACKAGES="$work/packages"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
hive="$work/hive"

step() { echo "::group::$*"; }
end() { echo "::endgroup::"; }
fail() { echo "::error::$*"; echo "FAILED: $*" >&2; exit 1; }

# The FluentCMS packages come from the folder feed only, everything else from nuget.org.
cat > "$work/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$(native "$feed")" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="FluentCMS.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF

new() { (cd "$work" && dotnet new "$@" --debug:custom-hive "$hive"); }

step "Install FluentCMS.Templates $version from the folder feed"
new install "FluentCMS.Templates@$version" --add-source "$(native "$feed")"
end

hosts=()
for provider in sqlite postgresql sqlserver; do
  for sample in none blog; do
    name="Host${provider^}${sample^}"
    step "dotnet new fluentcms --provider $provider --sample $sample"
    new fluentcms -n "$name" -o "$work/$name" --provider "$provider" --sample "$sample"
    dotnet build "$work/$name/$name.csproj" -c Release -warnaserror
    hosts+=("$name")
    end
  done
done

plugin=Acme.ReviewNotes
step "dotnet new fluentcms-plugin -n $plugin"
new fluentcms-plugin -n "$plugin" -o "$work/$plugin"
dotnet build "$work/$plugin/$plugin.slnx" -c Release -warnaserror
dotnet test "$work/$plugin/$plugin.slnx" -c Release --no-build
end

step "The FluentCMS packages and the tool of every project are version $version"
if grep -rl "{{version:" "$work"/Host* "$work/$plugin" --include=*.csproj --include=*.json; then
  fail "a version token was left in a created project"
fi
versions=$(grep -rhoE 'Include="FluentCMS\.[A-Za-z.]+" Version="[^"]+"' "$work"/Host* "$work/$plugin" --include=*.csproj | sed -E 's/.*Version="([^"]+)"/\1/' | sort -u)
[ "$versions" = "$version" ] || fail "the FluentCMS packages of the created projects are $versions, not $version"
for host in "${hosts[@]}"; do
  grep -q "\"version\": \"$version\"" "$work/$host/.config/dotnet-tools.json" || fail "$host does not pin fluentcms $version"
done
grep -q '\[FluentCmsPlugin(Name, ' "$work/$plugin/src/$plugin/ReviewNotesPlugin.cs" || fail "the plugin class is not ReviewNotesPlugin"
grep -q 'Name = "review-notes"' "$work/$plugin/src/$plugin/ReviewNotesPlugin.cs" || fail "the plugin is not named review-notes"
end

secret=$(head -c 24 /dev/urandom | od -An -tx1 | tr -d ' \n')

# Starts a built host in Development as `dotnet run` does with its launch profile, with an operator client that the checks
# sign in with, and waits until it is ready. The host's output goes to <host>.log.
start() {
  local host=$1 port=$2
  shift 2
  origin="https://localhost:$port"
  (
    cd "$work/$host"
    exec env ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="$origin" \
      FluentCms__Clients__Applications__ops__Secret="$secret" \
      FluentCms__Clients__Applications__ops__Scopes__0=management \
      FluentCms__Clients__Applications__ops__Roles__0="Super Admin" \
      "$@" dotnet "bin/Release/net10.0/$host.dll"
  ) > "$work/$host.log" 2>&1 &
  pid=$!
  for _ in $(seq 1 90); do
    if curl -kfsS "$origin/health/ready" > /dev/null 2>&1; then
      return 0
    fi
    if ! kill -0 "$pid" 2>/dev/null; then
      cat "$work/$host.log"
      fail "$host stopped before it was ready"
    fi
    sleep 2
  done
  cat "$work/$host.log"
  fail "$host was not ready after three minutes"
}

stop() {
  kill "$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
}

# GET or POST as the operator; prints the body and fails on another status than the one expected.
call() {
  local expected=$1 method=$2 url=$3 body=${4:-}
  local response status
  if [ -n "$body" ]; then
    response=$(curl -ksS -w '\n%{http_code}' -X "$method" -H "Authorization: Bearer $token" -H 'Content-Type: application/json' --data "$body" "$origin$url")
  else
    response=$(curl -ksS -w '\n%{http_code}' -X "$method" -H "Authorization: Bearer $token" "$origin$url")
  fi
  status=${response##*$'\n'}
  [ "$status" = "$expected" ] || fail "$method $url answered $status, not $expected: ${response%$'\n'*}"
  printf '%s' "${response%$'\n'*}"
}

# The authorization server answers on HTTPS only, so the hosts listen on HTTPS with the development certificate.
signin() {
  local response
  response=$(curl -ksS -X POST "$origin/connect/token" \
    -d grant_type=client_credentials -d client_id=ops -d "client_secret=$secret" -d scope=management)
  token=$(printf '%s' "$response" | sed -nE 's/.*"access_token": *"([^"]+)".*/\1/p')
  [ -n "$token" ] || fail "the operator could not sign in: $response"
}

# The sample type is served through the Delivery API: an article written and published through the Management API, or the
# welcome article of the Blog seed. The Delivery API hides a type from a caller who may not read it, so the operator reads.
check_delivery() {
  local sample=$1
  signin
  if [ "$sample" = blog ]; then
    call 200 GET "/api/delivery/v1/article/by/slug/welcome-to-fluentcms" | grep -qE '"title": *"Welcome to FluentCMS"' \
      || fail "the Delivery API did not serve the welcome article"
  else
    local id
    id=$(call 201 POST "/api/management/v1/content/article" '{"title":"Hello","slug":"hello"}' | grep -oE '"id": *"[^"]+"' | head -1 | cut -d'"' -f4)
    [ -n "$id" ] || fail "the Management API answered no id"
    call 200 POST "/api/management/v1/content/article/$id/publish" > /dev/null
    call 200 GET "/api/delivery/v1/article/$id" | grep -qE '"title": *"Hello"' || fail "the Delivery API did not serve the article"
  fi
}

step "Development certificate"
dotnet dev-certs https
end

port=5443
for sample in none blog; do
  host="HostSqlite${sample^}"
  step "$host starts on SQLite in Development and serves its sample type through the Delivery API"
  start "$host" "$port"
  check_delivery "$sample"
  stop
  port=$((port + 1))
  end
done

if [ -n "${FLUENTCMS_TEMPLATES_POSTGRES:-}" ]; then
  host=HostPostgresqlNone
  step "$host: fluentcms migrations add Initial and fluentcms migrate bring up a PostgreSQL database the host starts on"
  (
    cd "$work/$host"
    dotnet tool restore
    dotnet fluentcms migrations add Initial
    ConnectionStrings__FluentCms="$FLUENTCMS_TEMPLATES_POSTGRES" dotnet fluentcms migrate
    ConnectionStrings__FluentCms="$FLUENTCMS_TEMPLATES_POSTGRES" dotnet fluentcms migrate --check
  )
  ls "$work/$host/Migrations/Content" | grep -q '_Initial\.cs$' || fail "migrations add wrote no Initial migration"
  dotnet build "$work/$host/$host.csproj" -c Release -warnaserror
  start "$host" "$port" ConnectionStrings__FluentCms="$FLUENTCMS_TEMPLATES_POSTGRES"
  check_delivery none
  stop
  end
fi

echo "The templates of FluentCMS.Templates $version passed."
