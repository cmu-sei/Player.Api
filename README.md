# Player.Api Readme

## Docker

This application has been updated to the official Microsoft docker sdk image: `mcr.microsoft.com/dotnet/core/sdk`.

### sample `docker-compose.yml`

```yml
version: '3.6'

services:
  player-api:
    image: cmusei/player-api:development
    # Overrides the default entrypoint to update certificates.
    entrypoint: bash -c "update-ca-certificates && dotnet Player.Api.dll"
    volumes:
      - sei-ca:/usr/local/share/ca-certificates # Mounts NFS for ca Certificates
    configs:
      - source: player-api-settings
        target: /app/appsettings.Production.json
volumes:
  sei-ca:
    driver_opts:
      type: 'nfs'
      o: 'addr=<NFS IP>,nolock,soft,rw' # Replace <NFS IP>
      device: ':/mnt/data/certificates/sei-ca'
```

### sample `docker-stack.yml` (swarm) includes traefik reverse proxy labels

```yml
version: '3.6'

services:
  player-api:
    image: cmusei/player-api:development
    # Overrides the default entrypoint to update certificates.
    entrypoint: bash -c "update-ca-certificates && dotnet Player.Api.dll"
    deploy:
      replicas: 1
      labels:
        - 'traefik.enable=true'
        - 'traefik.backend=player-api'
        - 'traefik.port=80'
        - 'traefik.docker.network=traefik-net'
        - 'traefik.frontend.rule=Host:<Hostname>' # Replace <Hostname>
        - 'traefik.frontend.entrypoints=http,https'
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
      type: 'nfs'
      o: 'addr=<NFS IP>,nolock,soft,rw' # Replace <NFS IP>
      device: ':/mnt/data/certificates/sei-ca'
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

The official microsoft docker image is based on Debian. SSL CA trusts and their entry scripts need to be updated to use `update-ca-certificates` please see [update-trusts.sh](entry.d/update-trusts.sh).

## Reporting bugs and requesting features

Think you found a bug? Please report all Crucible bugs - including bugs for the individual Crucible apps - in the [cmu-sei/crucible issue tracker](https://github.com/cmu-sei/crucible/issues). 

Include as much detail as possible including steps to reproduce, specific app involved, and any error messages you may have received.

Have a good idea for a new feature? Submit all new feature requests through the [cmu-sei/crucible issue tracker](https://github.com/cmu-sei/crucible/issues). 

Include the reasons why you're requesting the new feature and how it might benefit other Crucible users.

## License

Copyright 2021 Carnegie Mellon University. See the [LICENSE.md](./LICENSE.md) files for details.
