# LuaCrackVN — authenticated Lua projects and loaders

LuaCrackVN runs the supplied 12hub Lua 5.1 VM obfuscator on the server. Users sign in with Google/Discord through Supabase Auth, create projects, store source on Pastefy, and receive loader URLs on their own deployed domain.

## Develop

Requires Node.js and Bun for the project lockfile:

```sh
bun install --frozen-lockfile
bun run dev
```

## Railway

Follow [`RAILWAY_DEPLOY_VI.md`](./RAILWAY_DEPLOY_VI.md) for the Docker deploy, Supabase migrations, OAuth provider setup, Pastefy API token and environment variables.

Versioned loaders are served at `/files/v1/loaders/<slug>.lua` and `/files/v4/loaders/<slug>.lua`. Deleting a script disables the loader and requests deletion of its Pastefy paste.
