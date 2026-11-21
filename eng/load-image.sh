#!/usr/bin/env bash
# Loads the image archive of the Publish workflow, the OCI archive of both platforms, into Docker and checks that it holds the
# image the reference names by digest: eng/load-image.sh <archive> <repository>@sha256:<digest>. Docker keeps an image index
# whole, with its digest, only in the containerd image store, so the script turns that store on when the runner's Docker uses
# another, which restarts Docker.
set -euo pipefail

archive=$1 reference=$2
if [[ ! "$reference" =~ ^[^@]+@(sha256:[0-9a-f]{64})$ ]]; then
  echo "::error::$reference does not name an image by its digest, such as ghcr.io/fluentcms/fluentcms@sha256:…" >&2
  exit 1
fi
digest=${BASH_REMATCH[1]}

store() { docker info -f '{{range .DriverStatus}}{{if eq (index . 0) "driver-type"}}{{index . 1}}{{end}}{{end}}'; }
if [ "$(store)" != io.containerd.snapshotter.v1 ]; then
  echo "Turning on the containerd image store of Docker."
  config=/etc/docker/daemon.json
  current='{}'
  if sudo test -s "$config"; then current=$(sudo cat "$config"); fi
  jq '.features["containerd-snapshotter"] = true' <<< "$current" | sudo tee "$config" > /dev/null
  sudo systemctl restart docker
  timeout 60 sh -c 'until docker info > /dev/null 2>&1; do sleep 1; done'
  if [ "$(store)" != io.containerd.snapshotter.v1 ]; then
    echo "::error::Docker does not use the containerd image store after a restart with $config:" >&2
    sudo cat "$config" >&2
    exit 1
  fi
fi

docker load --input "$archive"
loaded=$(docker image inspect --format '{{.Id}}' "$reference" 2> /dev/null || true)
if [ "$loaded" != "$digest" ]; then
  echo "::error::$archive does not hold $reference${loaded:+; Docker names it $loaded}." >&2
  exit 1
fi
echo "Loaded $reference."
