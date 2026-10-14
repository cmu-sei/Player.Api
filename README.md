# Player.Api Readme

## Docker

The [Dockerfile](Dockerfile) builds on the official Microsoft .NET 10 images: `mcr.microsoft.com/dotnet/sdk:10.0` for the build, and `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` for the published image, which runs `./Player.Api` and listens on port 8080. Images are published as `cmusei/player-api`, tagged with the release version (or the branch name for builds of `main`). The `debug` target, on `mcr.microsoft.com/dotnet/aspnet:10.0`, is published with `-debug` appended to each tag.

### sample `docker-compose.yml`

```yml
version: "3.6"

services:
  player-api:
    image: cmusei/player-api:<version> # Replace <version>
    environment:
      - SSL_CERT_DIR=/usr/local/share/ca-certificates # Trusts the mounted CA certificates
    volumes:
      - sei-ca:/usr/local/share/ca-certificates # Mounts NFS for ca Certificates
    configs:
      - source: player-api-settings
        target: /app/appsettings.Production.json
volumes:
  sei-ca:
    driver_opts:
      type: "nfs"
      o: "addr=<NFS IP>,nolock,soft,rw" # Replace <NFS IP>
      device: ":/mnt/data/certificates/sei-ca"

configs:
  player-api-settings:
    file: ./player-api-settings.json
```

### sample `docker-stack.yml` (swarm) includes traefik reverse proxy labels

```yml
version: "3.6"

services:
  player-api:
    image: cmusei/player-api:<version> # Replace <version>
    environment:
      - SSL_CERT_DIR=/usr/local/share/ca-certificates # Trusts the mounted CA certificates
    deploy:
      replicas: 1
      labels:
        - "traefik.enable=true"
        - "traefik.backend=player-api"
        - "traefik.port=8080"
        - "traefik.docker.network=traefik-net"
        - "traefik.frontend.rule=Host:<Hostname>" # Replace <Hostname>
        - "traefik.frontend.entrypoints=http,https"
    networks:
      - utilities
      - traefik-net
    volumes:
      - sei-ca:/usr/local/share/ca-certificates
    configs:
      - source: player-api-settings
        target: /app/appsettings.Production.json
volumes:
  sei-ca:
    driver_opts:
      type: "nfs"
      o: "addr=<NFS IP>,nolock,soft,rw" # Replace <NFS IP>
      device: ":/mnt/data/certificates/sei-ca"
networks:
  utilities:
    external: true
  traefik-net:
    external: true

configs:
  player-api-settings:
    file: ./player-api-anvil-dev-settings.json
```

### SSL Considerations

The published image is Ubuntu Chiseled: it has no shell and no `update-ca-certificates`, and the application runs as a non-root user. To trust additional certificate authorities, mount their certificates into the container and set `SSL_CERT_DIR` to that directory, as the samples above do. The player-api chart in [cmu-sei/helm-charts](https://github.com/cmu-sei/helm-charts) does the same for its `certificateMap` value.

## Testing

`Player.Api.Tests` holds the automated test suite. `dotnet test`, from the repository root, runs it. Docker must be running: the suite starts its own PostgreSQL container through Testcontainers, so no local database is needed. See [docs/Testing.md](docs/Testing.md) for what the suite covers and how the harness is put together.

## Reporting bugs and requesting features

Think you found a bug? Please report all Crucible bugs - including bugs for the individual Crucible apps - in the [cmu-sei/crucible issue tracker](https://github.com/cmu-sei/crucible/issues).

Include as much detail as possible including steps to reproduce, specific app involved, and any error messages you may have received.

Have a good idea for a new feature? Submit all new feature requests through the [cmu-sei/crucible issue tracker](https://github.com/cmu-sei/crucible/issues).

Include the reasons why you're requesting the new feature and how it might benefit other Crucible users.

## License

Copyright 2022 Carnegie Mellon University. See the [LICENSE.md](./LICENSE.md) files for details.
