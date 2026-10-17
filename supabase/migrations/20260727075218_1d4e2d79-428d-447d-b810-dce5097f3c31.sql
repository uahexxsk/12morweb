ALTER TABLE public.script_users ADD COLUMN IF NOT EXISTS blacklisted boolean NOT NULL DEFAULT false;
CREATE INDEX IF NOT EXISTS script_users_script_id_idx ON public.script_users(script_id);
CREATE INDEX IF NOT EXISTS script_users_last_seen_idx ON public.script_users(last_seen DESC);