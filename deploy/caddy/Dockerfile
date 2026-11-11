# The edge image: Caddy with the production Caddyfiles and the built React app (container-deployment.md §1.2,
# ADR-F013). Built from the repository root: docker build -f deploy/caddy/Dockerfile .
# Base images are pinned by digest; bump them in a pull request (infrastructure-operations-architecture.md §5.2).

FROM docker.io/library/node:24-alpine@sha256:ebfe2f90462722a7a4de65e91990e97fe0d401c70e0e762c5b53302f905ec1c1 AS app
WORKDIR /src
COPY App/package.json App/package-lock.json ./
RUN npm ci
COPY App/ ./
RUN npm run build

# The released Caddy 2.11.9 binary was built with a golang.org/x/net that has a High finding (HTML parser denial of
# service), and no newer Caddy exists yet. Rebuilding the same Caddy version with the patched toolchain and module
# keeps the Trivy gate free of fixable findings (ADR-008). Drop this stage, and the COPY below, once a Caddy release
# ships with them. go-jose v3 has no fix yet and is accepted in .trivyignore.
FROM docker.io/library/golang:1.26.10-alpine@sha256:87c65a688975c0b1d4585d204874c45ca78354670e3a5f11368c5a492303d696 AS caddy
WORKDIR /build
RUN go mod init healthyhires/caddy \
 && printf '%s\n' 'package main' \
      'import caddycmd "github.com/caddyserver/caddy/v2/cmd"' \
      'import _ "github.com/caddyserver/caddy/v2/modules/standard"' \
      'func main() { caddycmd.Main() }' > main.go \
 && go get github.com/caddyserver/caddy/v2@v2.11.9 golang.org/x/net@v0.62.0 \
 && go mod tidy \
 && CGO_ENABLED=0 go build -trimpath -ldflags '-s -w' -o /caddy .

FROM docker.io/library/caddy:2.11.9@sha256:0c2243efcf437ab7f7a978f1027fcf0d4f9be8a75df27b1b3fbc5dbe0ffa8cf0
COPY --from=caddy /caddy /usr/bin/caddy
COPY configs_production/Caddyfile.production /etc/caddy/Caddyfile
COPY configs_production/site.caddy /etc/caddy/site.caddy
COPY --from=app /src/dist /srv
