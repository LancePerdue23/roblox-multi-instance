-- A separate database from telemetry. No account IDs, email addresses, or payment bodies.
CREATE TABLE IF NOT EXISTS support_attempts (
  attempt_id TEXT PRIMARY KEY,
  claim_hash TEXT NOT NULL UNIQUE,
  session_id TEXT UNIQUE,
  checkout_url TEXT,
  status TEXT NOT NULL CHECK(status IN ('pending','paid')),
  created_at INTEGER NOT NULL,
  expires_at INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS support_attempts_expiry ON support_attempts(expires_at);
CREATE TABLE IF NOT EXISTS support_daily_budget (
  day INTEGER PRIMARY KEY,
  used INTEGER NOT NULL
);
