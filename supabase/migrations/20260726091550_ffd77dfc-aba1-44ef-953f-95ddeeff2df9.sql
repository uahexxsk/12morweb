ALTER TABLE public.scripts
  ADD COLUMN IF NOT EXISTS effect_type text NOT NULL DEFAULT 'none',
  ADD COLUMN IF NOT EXISTS effect_message text NOT NULL DEFAULT '';