
CREATE TABLE public.script_users (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  script_id uuid NOT NULL REFERENCES public.scripts(id) ON DELETE CASCADE,
  user_key text NOT NULL UNIQUE,
  display_name text,
  last_seen timestamptz NOT NULL DEFAULT now(),
  pending_effect_type text NOT NULL DEFAULT 'none',
  pending_effect_message text NOT NULL DEFAULT '',
  created_at timestamptz NOT NULL DEFAULT now()
);
GRANT ALL ON public.script_users TO service_role;
ALTER TABLE public.script_users ENABLE ROW LEVEL SECURITY;
CREATE POLICY "deny all" ON public.script_users FOR ALL USING (false) WITH CHECK (false);
CREATE INDEX script_users_script_id_last_seen_idx ON public.script_users(script_id, last_seen DESC);
