-- ============================================================
-- SUR AIR v5.0 - scoring dual para ACARS 3.1.8
-- Ejecutar en Supabase SQL Editor
-- ============================================================

alter table if exists public.flight_reservations
  add column if not exists actual_block_minutes integer,
  add column if not exists procedure_score integer,
  add column if not exists performance_score integer,
  add column if not exists procedure_grade text,
  add column if not exists performance_grade text,
  add column if not exists mission_score integer,
  add column if not exists scoring_status text default 'pending',
  add column if not exists scoring_applied_at timestamptz,
  add column if not exists score_payload jsonb default '{}'::jsonb;

create table if not exists public.pw_flight_score_reports (
  id uuid primary key default gen_random_uuid(),
  reservation_id uuid,
  pilot_callsign text,
  route_code text,
  flight_mode_code text,
  block_minutes integer,
  block_hours numeric,
  landing_points integer default 0,
  taxi_out_points integer default 0,
  takeoff_climb_points integer default 0,
  approach_points integer default 0,
  cruise_points integer default 0,
  penalty_points integer default 0,
  procedure_score integer,
  performance_score integer,
  procedure_grade text,
  performance_grade text,
  mission_score integer,
  legado_credits integer,
  valid_for_progression boolean default false,
  score_payload jsonb default '{}'::jsonb,
  notes text,
  scored_at timestamptz default now(),
  created_at timestamptz default now()
);

alter table if exists public.pw_flight_score_reports
  add column if not exists reservation_id uuid,
  add column if not exists pilot_callsign text,
  add column if not exists route_code text,
  add column if not exists flight_mode_code text,
  add column if not exists block_minutes integer,
  add column if not exists block_hours numeric,
  add column if not exists landing_points integer default 0,
  add column if not exists taxi_out_points integer default 0,
  add column if not exists takeoff_climb_points integer default 0,
  add column if not exists approach_points integer default 0,
  add column if not exists cruise_points integer default 0,
  add column if not exists penalty_points integer default 0,
  add column if not exists procedure_score integer,
  add column if not exists performance_score integer,
  add column if not exists procedure_grade text,
  add column if not exists performance_grade text,
  add column if not exists mission_score integer,
  add column if not exists legado_credits integer,
  add column if not exists valid_for_progression boolean default false,
  add column if not exists score_payload jsonb default '{}'::jsonb,
  add column if not exists notes text,
  add column if not exists scored_at timestamptz default now(),
  add column if not exists created_at timestamptz default now();

create index if not exists idx_pw_flight_score_reports_reservation
  on public.pw_flight_score_reports (reservation_id);

create index if not exists idx_pw_flight_score_reports_scored_at
  on public.pw_flight_score_reports (scored_at desc);
