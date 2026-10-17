ALTER TABLE public.scripts ADD COLUMN IF NOT EXISTS version integer NOT NULL DEFAULT 1;
ALTER TABLE public.script_users ADD COLUMN IF NOT EXISTS menu_json text NOT NULL DEFAULT '';