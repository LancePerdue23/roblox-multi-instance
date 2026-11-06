CREATE TABLE IF NOT EXISTS telemetry_budget (
  day TEXT PRIMARY KEY,
  accepted_batches INTEGER NOT NULL DEFAULT 0 CHECK(accepted_batches >= 0 AND accepted_batches <= 1000)
);
CREATE TABLE IF NOT EXISTS telemetry_receipts (
  batch_id TEXT PRIMARY KEY,
  received_day TEXT NOT NULL,
  processed INTEGER NOT NULL DEFAULT 0 CHECK(processed IN (0, 1))
);
CREATE INDEX IF NOT EXISTS telemetry_receipts_retention ON telemetry_receipts(received_day);
CREATE TABLE IF NOT EXISTS telemetry_errors (
  day TEXT NOT NULL,
  app_version TEXT NOT NULL,
  helper_version TEXT NOT NULL,
  windows_version TEXT NOT NULL,
  architecture TEXT NOT NULL,
  source TEXT NOT NULL,
  code TEXT NOT NULL,
  error_code TEXT NOT NULL,
  count INTEGER NOT NULL CHECK(count > 0),
  PRIMARY KEY(day, app_version, helper_version, windows_version, architecture, source, code, error_code)
);
