# Build the TanStack Start web app.
FROM oven/bun:1 AS web-build
WORKDIR /app
ARG VITE_SUPABASE_URL
ARG VITE_SUPABASE_PUBLISHABLE_KEY
ENV VITE_SUPABASE_URL=${VITE_SUPABASE_URL} \
    VITE_SUPABASE_PUBLISHABLE_KEY=${VITE_SUPABASE_PUBLISHABLE_KEY}
COPY package.json bun.lock bunfig.toml ./
RUN bun install --frozen-lockfile
COPY . .
ENV NITRO_PRESET=node-server
RUN bun run build

# Build the original 12hub .NET CLI and ship its Lua minifier files beside it.
FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS engine-build
RUN apt-get update && apt-get install -y --no-install-recommends lua5.1 luajit ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY ["12hub Engine/", "12hub Engine/"]
COPY ["12hub CLI/", "12hub CLI/"]
RUN dotnet publish "12hub CLI/12hub CLI.csproj" --configuration Release --output /engine-publish
RUN test -f /engine-publish/12hub.dll && test -f /engine-publish/Lua/Minifier/luasrcdiet.lua

# Node + .NET runtime + Lua 5.1 tools required by the VM pipeline.
FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS runtime
ENV NODE_ENV=production \
    PORT=3000 \
    HOST=0.0.0.0 \
    HUB12_CLI_DLL=/app/engine-publish/12hub.dll \
    HUB12_LUAC=luac5.1 \
    HUB12_LUAJIT=luajit
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates gnupg lua5.1 luajit \
    && mkdir -p /etc/apt/keyrings \
    && curl -fsSL https://deb.nodesource.com/gpgkey/nodesource-repo.gpg.key | gpg --dearmor -o /etc/apt/keyrings/nodesource.gpg \
    && echo "deb [signed-by=/etc/apt/keyrings/nodesource.gpg] https://deb.nodesource.com/node_22.x nodistro main" > /etc/apt/sources.list.d/nodesource.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=web-build /app/.output ./.output
COPY --from=engine-build /engine-publish ./engine-publish
RUN node --version && dotnet --list-runtimes && command -v luac5.1 && command -v luajit
EXPOSE 3000
CMD ["node", ".output/server/index.mjs"]
