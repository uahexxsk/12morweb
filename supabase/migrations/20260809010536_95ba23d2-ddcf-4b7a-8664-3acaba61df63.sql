
CREATE TABLE public.script_auth_tokens (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  script_id uuid NOT NULL REFERENCES public.scripts(id) ON DELETE CASCADE,
  key_id uuid REFERENCES public.script_keys(id) ON DELETE CASCADE,
  token text NOT NULL UNIQUE,
  hwid text,
  expires_at timestamptz NOT NULL,
  used boolean NOT NULL DEFAULT false,
  created_at timestamptz NOT NULL DEFAULT now()
);
GRANT ALL ON public.script_auth_tokens TO service_role;
ALTER TABLE public.script_auth_tokens ENABLE ROW LEVEL SECURITY;
CREATE POLICY "deny all" ON public.script_auth_tokens FOR ALL USING (false) WITH CHECK (false);
CREATE INDEX script_auth_tokens_token_idx ON public.script_auth_tokens(token);
CREATE INDEX script_auth_tokens_expires_idx ON public.script_auth_tokens(expires_at);

ALTER TABLE public.script_keys ADD COLUMN IF NOT EXISTS format text NOT NULL DEFAULT 'lc';
