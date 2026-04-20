# ─────────────────────────────────────────────────────────────────────────────
# SemaBuzz Relay Server — Docker image
#
# Deploy to Railway, Render, Fly.io, or any Docker host.
# The platform's reverse proxy provides HTTPS/WSS; the container runs HTTP/WS.
#
# Build:  docker build -t semabuzz-relay .
# Run:    docker run -p 7171:7171 semabuzz-relay
# ─────────────────────────────────────────────────────────────────────────────

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy only the projects the relay needs.
COPY src/SemaBuzz.Protocol/ SemaBuzz.Protocol/
COPY src/SemaBuzz.Relay/    SemaBuzz.Relay/

WORKDIR /src/SemaBuzz.Relay
RUN dotnet publish -c Release -o /app --no-self-contained

# ── Runtime image ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .

ENV PORT=7171
EXPOSE 7171

ENTRYPOINT ["dotnet", "SemaBuzz.Relay.dll"]
