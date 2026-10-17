CREATE TABLE public.script_keys (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  script_id uuid NOT NULL REFERENCES public.scripts(id) ON DELETE CASCADE,
  key text NOT NULL UNIQUE,
  hwid text,
  label text NOT NULL DEFAULT '',
  last_used timestamptz,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX script_keys_script_id_idx ON public.script_keys(script_id);
GRANT ALL ON public.script_keys TO service_role;
ALTER TABLE public.script_keys ENABLE ROW LEVEL SECURITY;
CREATE POLICY "deny all" ON public.script_keys FOR ALL TO public USING (false) WITH CHECK (false);