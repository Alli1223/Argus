# The Argus updater: installs the server releases administrators choose in the web app, with a backup
# before and a rollback when the new version does not start. See argus-updater.sh and docs/deployment.md.
#
#   docker build -t argus-updater deploy/updater
FROM docker:29-cli

RUN apk add --no-cache jq

ARG VERSION=dev
ENV UPDATER_VERSION=$VERSION
LABEL org.opencontainers.image.title="Argus updater" \
      org.opencontainers.image.version=$VERSION

COPY --chmod=0755 argus-updater.sh /usr/local/bin/argus-updater
ENTRYPOINT ["argus-updater"]
