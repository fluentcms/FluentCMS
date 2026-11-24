#!/usr/bin/env bash
# Installs FluentCMS.Templates from a folder feed that holds it and the FluentCMS packages of the same version, creates every
# combination of the templates, builds each with warnings as errors and runs the plugin's tests, with its admin half and
# without, and checks that the half's manifest test fails on a manifest that disagrees. It publishes the admin app trimmed, adds
# the plugin's admin half to it and checks that the build stops without the half's lazy line. Then it starts the SQLite hosts in
# Development, reads their sample type through the Delivery API and the admin at /admin, or a 404 there for a host made with
# --admin false, and starts one with a reference to the admin app, which it then serves with the plugin's half. When
# FLUENTCMS_TEMPLATES_POSTGRES holds a connection string, it brings up a PostgreSQL host with `fluentcms migrations add Initial`
# and `fluentcms migrate`.
#
#   templates/test-templates.sh <feed> <version> <work directory>
#
# Everything it writes is under the work directory, the NuGet packages and the installed templates included. It needs bash 4
# or later (Git Bash on Windows has it), curl and the .NET SDK of global.json.
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
# To standard error, so that a failure inside a command substitution still shows, as an annotation on GitHub too.
fail() { echo "::error::$*" >&2; exit 1; }

# A host the checks started is stopped however the script ends.
pid=
trap '[ -n "$pid" ] && kill "$pid" 2>/dev/null; true' EXIT

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

# Two hosts without the admin, so that each conditional of the settings is made both ways: with no reference to the admin left
# anywhere in what the template writes.
apis=()
for variant in "sqlite none" "sqlserver blog"; do
  read -r provider sample <<< "$variant"
  name="Api${provider^}${sample^}"
  step "dotnet new fluentcms --provider $provider --sample $sample --admin false"
  new fluentcms -n "$name" -o "$work/$name" --provider "$provider" --sample "$sample" --admin false
  if grep -rnE 'FluentCMS\.Admin|AddAdmin|MapFluentCmsAdmin|"Admin"|/admin' "$work/$name"; then
    fail "$name, made with --admin false, refers to the admin"
  fi
  dotnet build "$work/$name/$name.csproj" -c Release -warnaserror
  apis+=("$name")
  end
done

for host in "${hosts[@]}"; do
  grep -q '<PackageReference Include="FluentCMS.Admin.Hosting" ' "$work/$host/$host.csproj" || fail "$host does not reference FluentCMS.Admin.Hosting"
  grep -q 'cms.AddAdmin();' "$work/$host/Program.cs" && grep -q 'app.MapFluentCmsAdmin();' "$work/$host/Program.cs" \
    || fail "the Program.cs of $host does not add and map the admin"
done
for host in "${hosts[@]}" "${apis[@]}"; do
  if grep -l '<!--#\|^#if\|^#else\|^#endif\|//#' "$work/$host/README.md" "$work/$host/Program.cs" "$work/$host/appsettings.json" "$work/$host/$host.csproj"; then
    fail "$host kept a template conditional"
  fi
done

plugin=Acme.ReviewNotes
step "dotnet new fluentcms-plugin -n $plugin"
new fluentcms-plugin -n "$plugin" -o "$work/$plugin"
dotnet build "$work/$plugin/$plugin.slnx" -c Release -warnaserror
# From inside the plugin, as its author runs them: the plugin's global.json chooses the test platform.
(cd "$work/$plugin" && dotnet test -c Release --no-build)
end

# The manifest test fails when the hand-written manifest and the attributes of the half disagree: here without the widget.
step "The manifest test of $plugin.Admin fails on a manifest without a contribution"
manifest="$work/$plugin/src/$plugin.Admin/wwwroot/fluentcms-admin.json"
cp "$manifest" "$work/fluentcms-admin.json"
sed -i '/ReviewNotesWidget"/d' "$manifest"
cmp -s "$manifest" "$work/fluentcms-admin.json" && fail "the widget was not removed from the manifest"
if (cd "$work/$plugin" && dotnet test --project "tests/$plugin.Admin.Tests" -c Release) > "$work/manifest-test.log" 2>&1; then
  fail "the manifest test passed on a manifest without the widget"
fi
grep -q "ReviewNotesWidget" "$work/manifest-test.log" || { cat "$work/manifest-test.log"; fail "the manifest test did not name the widget"; }
cp "$work/fluentcms-admin.json" "$manifest"
end

server=Acme.ServerOnly
step "dotnet new fluentcms-plugin -n $server --admin false"
new fluentcms-plugin -n "$server" -o "$work/$server" --admin false
[ ! -e "$work/$server/src/$server.Admin" ] && [ ! -e "$work/$server/tests/$server.Admin.Tests" ] || fail "--admin false kept the admin half"
if grep -q 'Admin' "$work/$server/$server.slnx"; then
  fail "the solution of $server names an admin project"
fi
if grep -qF '.Admin' "$work/$server/README.md"; then
  fail "the README of $server describes an admin half"
fi
if grep -l '<!--#' "$work/$plugin/README.md" "$work/$server/README.md"; then
  fail "a README kept a template conditional"
fi
dotnet build "$work/$server/$server.slnx" -c Release -warnaserror
(cd "$work/$server" && dotnet test -c Release --no-build)
end

# The one trimmed publish of the jobs: the app as the template makes it, with the six first-party halves.
admin=AcmeAdmin
halves=(Media Webhooks Mcp AiAssist MultiSite Search)
step "dotnet new fluentcms-admin -n $admin, built and published trimmed"
new fluentcms-admin -n "$admin" -o "$work/$admin"
dotnet build "$work/$admin/$admin.csproj" -c Release -warnaserror
dotnet publish "$work/$admin/$admin.csproj" -c Release --no-build -o "$work/$admin-publish" > "$work/$admin-publish.log" 2>&1 \
  || { cat "$work/$admin-publish.log"; fail "the trimmed publish of $admin failed"; }
# A trim warning of the app or its project references fails the publish; one of a package is an IL2104 that names it, which
# the framework's Blazor assemblies report and no FluentCMS package may.
if grep -E "warning IL[0-9]+: Assembly '(FluentCMS\.|$admin)" "$work/$admin-publish.log"; then
  fail "the trimmed publish of $admin reports a trim warning of FluentCMS"
fi
gathered="$work/$admin-publish/wwwroot/fluentcms-admin.plugins.json"
for half in "${halves[@]}"; do
  grep -q "\"assembly\": \"FluentCMS.Plugins.$half.Admin\"" "$gathered" || fail "the published $admin does not list the manifest of FluentCMS.Plugins.$half.Admin"
done
ls "$work/$admin-publish/wwwroot/_framework/" | grep -q "^$admin\.[a-z0-9]*\.wasm$" || fail "the publish of $admin holds no $admin.wasm"
end

# The plugin's half goes into the app as its README says: the build stops on the reference alone, and passes with the lazy line.
step "The admin half of $plugin in $admin"
dotnet add "$work/$admin/$admin.csproj" reference "$work/$plugin/src/$plugin.Admin/$plugin.Admin.csproj"
if dotnet build "$work/$admin/$admin.csproj" -c Release -warnaserror > "$work/$admin-eager.log" 2>&1; then
  fail "$admin built with the half $plugin.Admin not loaded lazily"
fi
grep -q "The admin half $plugin.Admin is referenced but not loaded lazily" "$work/$admin-eager.log" || { cat "$work/$admin-eager.log"; fail "the build of $admin did not name the half to load lazily"; }
sed -i "s|^\(\s*\)<ProjectReference Include=\"\(.*\)$plugin.Admin.csproj\" />|&\n\1<BlazorWebAssemblyLazyLoad Include=\"$plugin.Admin.wasm\" />|" "$work/$admin/$admin.csproj"
grep -q "<BlazorWebAssemblyLazyLoad Include=\"$plugin.Admin.wasm\" />" "$work/$admin/$admin.csproj" || fail "the lazy line of $plugin.Admin was not added"
dotnet build "$work/$admin/$admin.csproj" -c Release -warnaserror
grep -q "\"assembly\": \"$plugin.Admin\"" "$work/$admin/obj/Release/net10.0/fluentcms-admin/fluentcms-admin.plugins.json" \
  || fail "the gathered manifests of $admin do not list $plugin.Admin"
end

step "The FluentCMS packages and the tool of every project are version $version"
if grep -rl "{{version:" "$work"/Host* "$work"/Api* "$work/$admin" "$work/$plugin" "$work/$server" --include=*.csproj --include=*.json; then
  fail "a version token was left in a created project"
fi
versions=$( (grep -rhoE 'Include="FluentCMS\.[A-Za-z.]+" Version="[^"]+"' "$work"/Host* "$work"/Api* "$work/$admin" "$work/$plugin" "$work/$server" --include=*.csproj || true) \
  | sed -E 's/.*Version="([^"]+)"/\1/' | sort -u)
[ "$versions" = "$version" ] || fail "the FluentCMS packages of the created projects are '$versions', not $version"
for host in "${hosts[@]}" "${apis[@]}"; do
  grep -q "\"version\": \"$version\"" "$work/$host/.config/dotnet-tools.json" || fail "$host does not pin fluentcms $version"
done
grep -q '\[FluentCmsPlugin(Name, ' "$work/$plugin/src/$plugin/ReviewNotesPlugin.cs" || fail "the plugin class is not ReviewNotesPlugin"
grep -q 'Name = "review-notes"' "$work/$plugin/src/$plugin/ReviewNotesPlugin.cs" || fail "the plugin is not named review-notes"
grep -q '"assembly": "Acme.ReviewNotes.Admin"' "$work/$plugin/src/$plugin.Admin/wwwroot/fluentcms-admin.json" || fail "the manifest does not name Acme.ReviewNotes.Admin"
grep -q '"name": "review-notes"' "$work/$plugin/src/$plugin.Admin/wwwroot/fluentcms-admin.json" || fail "the manifest does not name the plugin review-notes"
end

step "The Dockerfile of each provider"
for host in "${hosts[@]}" "${apis[@]}"; do
  grep -q "^COPY $host.csproj ./" "$work/$host/Dockerfile" || fail "the Dockerfile of $host does not copy its project"
  grep -q "^ENTRYPOINT \[\"dotnet\", \"/app/$host.dll\"\]" "$work/$host/Dockerfile" || fail "the Dockerfile of $host does not start its assembly"
  if [[ $host == HostSqlite* || $host == ApiSqlite* ]]; then
    grep -q '^ENV ConnectionStrings__FluentCms="Data Source=/data/fluentcms.db"' "$work/$host/Dockerfile" || fail "the Dockerfile of $host has no SQLite database in /data"
  elif grep -q '^ENV ConnectionStrings__FluentCms' "$work/$host/Dockerfile"; then
    fail "the Dockerfile of $host sets a connection string"
  fi
  if grep -q '^#if\|^#else\|^#endif' "$work/$host/Dockerfile"; then
    fail "the Dockerfile of $host kept a template conditional"
  fi
  # The build stage copies the whole folder, so secrets stay out of the build context.
  for ignored in '**/.env' '**/*.pfx' '**/*.key' '**/*.local.json'; do
    grep -qxF "$ignored" "$work/$host/.dockerignore" || fail "the .dockerignore of $host does not leave out $ignored"
  done
done
end

# A name that is not a namespace keeps its files and project references, and its code gets a namespace that is one.
step "dotnet new fluentcms -n my-site and dotnet new fluentcms-plugin -n my-plugin"
new fluentcms -n my-site -o "$work/my-site"
dotnet build "$work/my-site/my-site.csproj" -c Release -warnaserror
grep -q '^COPY my-site.csproj ./' "$work/my-site/Dockerfile" || fail "the Dockerfile of my-site does not copy my-site.csproj"
new fluentcms-plugin -n my-plugin -o "$work/my-plugin"
dotnet build "$work/my-plugin/my-plugin.slnx" -c Release -warnaserror
(cd "$work/my-plugin" && dotnet test --project tests/my-plugin.Admin.Tests -c Release --no-build)
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
  pid=
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

# GET with no token; prints the body and fails on another status than the one expected.
fetch() {
  local expected=$1 url=$2
  local response status
  response=$(curl -ksS -w '\n%{http_code}' "$origin$url")
  status=${response##*$'\n'}
  [ "$status" = "$expected" ] || fail "GET $url answered $status, not $expected: ${response%$'\n'*}"
  printf '%s' "${response%$'\n'*}"
}

# The admin at /admin: the page with its base set, and config.json with the host's origin, which in Development without a
# public origin is the request's, and the admin's client.
check_admin() {
  local page config
  page=$(fetch 200 /admin/)
  grep -qF '<base href="/admin/" />' <<< "$page" || fail "/admin/ did not answer the page with its base set: $page"
  config=$(fetch 200 /admin/config.json)
  grep -qE "\"apiBaseUrl\": *\"$origin\"" <<< "$config" && grep -qE "\"authority\": *\"$origin/\"" <<< "$config" \
    && grep -qE '"clientId": *"fluentcms-admin"' <<< "$config" \
    || fail "/admin/config.json does not name $origin and the client fluentcms-admin: $config"
}

step "Development certificate"
dotnet dev-certs https
end

port=5443
for sample in none blog; do
  host="HostSqlite${sample^}"
  step "$host starts on SQLite in Development and serves its sample type through the Delivery API and the admin at /admin"
  start "$host" "$port"
  check_delivery "$sample"
  check_admin
  stop
  port=$((port + 1))
  end
done

host=ApiSqliteNone
step "$host, made with --admin false, answers 404 at /admin"
start "$host" "$port"
check_delivery none
for path in /admin /admin/ /admin/config.json; do
  fetch 404 "$path" > /dev/null
done
stop
port=$((port + 1))
end

# The template's own host with a reference to the app of fluentcms-admin, which FluentCMS.Admin.Hosting then serves in place of
# its prebuilt build: the gathered manifests hold the plugin's half, which the prebuilt build has not.
host=HostSqliteNone
step "$host with a reference to $admin serves that app, with the half of $plugin"
dotnet add "$work/$host/$host.csproj" reference "$work/$admin/$admin.csproj"
dotnet build "$work/$host/$host.csproj" -c Release -warnaserror
start "$host" "$port"
check_admin
served=$(fetch 200 /admin/fluentcms-admin.plugins.json)
grep -q "\"assembly\": \"$plugin.Admin\"" <<< "$served" || fail "$host does not serve the manifests of $admin"
fetch 200 "/admin/$admin.styles.css" > /dev/null
stop
port=$((port + 1))
end

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
