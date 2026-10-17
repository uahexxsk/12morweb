-- Authenticated projects and Pastefy-backed script storage.
CREATE TABLE IF NOT EXISTS public.projects (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  owner_id uuid NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
  name text NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS projects_owner_created_idx ON public.projects(owner_id, created_at DESC);
ALTER TABLE public.projects ENABLE ROW LEVEL SECURITY;
GRANT ALL ON public.projects TO service_role;
DROP POLICY IF EXISTS "deny all" ON public.projects;
CREATE POLICY "deny all" ON public.projects FOR ALL TO public USING (false) WITH CHECK (false);

ALTER TABLE public.scripts
  ADD COLUMN IF NOT EXISTS owner_id uuid REFERENCES auth.users(id) ON DELETE CASCADE,
  ADD COLUMN IF NOT EXISTS project_id uuid REFERENCES public.projects(id) ON DELETE CASCADE,
  ADD COLUMN IF NOT EXISTS pastefy_id text,
  ADD COLUMN IF NOT EXISTS pastefy_url text;
CREATE INDEX IF NOT EXISTS scripts_owner_created_idx ON public.scripts(owner_id, created_at DESC);
CREATE INDEX IF NOT EXISTS scripts_project_id_idx ON public.scripts(project_id);
GRANT ALL ON public.scripts TO service_role;
