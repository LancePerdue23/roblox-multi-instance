-- Additive migration: safe on the existing v1 database. Never expire these tables.
CREATE TABLE IF NOT EXISTS supporter_wallets (
 supporter_id TEXT PRIMARY KEY,
 total_cents INTEGER NOT NULL DEFAULT 0 CHECK(total_cents>=0),
 earned_tier INTEGER NOT NULL DEFAULT -1 CHECK(earned_tier BETWEEN -1 AND 2),
 revision INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS supporter_checkouts (
 attempt_id TEXT PRIMARY KEY, claim_hash TEXT NOT NULL UNIQUE,
 supporter_id TEXT NOT NULL REFERENCES supporter_wallets(supporter_id),
 amount_cents INTEGER NOT NULL CHECK(amount_cents BETWEEN 50 AND 100000),
 session_id TEXT UNIQUE, checkout_url TEXT, created_at INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS supporter_payments (
 payment_id TEXT PRIMARY KEY, attempt_id TEXT NOT NULL UNIQUE REFERENCES supporter_checkouts(attempt_id),
 supporter_id TEXT NOT NULL REFERENCES supporter_wallets(supporter_id),
 amount_cents INTEGER NOT NULL CHECK(amount_cents BETWEEN 50 AND 100000), currency TEXT NOT NULL CHECK(currency='usd')
);
CREATE INDEX IF NOT EXISTS supporter_payments_wallet ON supporter_payments(supporter_id);
-- Adjustments may arrive before checkout completion. Retain them independently.
CREATE TABLE IF NOT EXISTS supporter_adjustments (
 payment_id TEXT PRIMARY KEY, refunded_cents INTEGER NOT NULL DEFAULT 0 CHECK(refunded_cents>=0),
 chargeback_cents INTEGER NOT NULL DEFAULT 0 CHECK(chargeback_cents>=0)
);
CREATE VIEW IF NOT EXISTS supporter_net AS
 SELECT p.supporter_id, SUM(MAX(0,p.amount_cents-COALESCE(a.refunded_cents,0)-COALESCE(a.chargeback_cents,0))) AS total
 FROM supporter_payments p LEFT JOIN supporter_adjustments a USING(payment_id) GROUP BY p.supporter_id;
CREATE TRIGGER IF NOT EXISTS supporter_credit AFTER INSERT ON supporter_payments BEGIN
 UPDATE supporter_wallets SET total_cents=(SELECT total FROM supporter_net WHERE supporter_id=NEW.supporter_id),
 earned_tier=MAX(earned_tier,CASE WHEN (SELECT total FROM supporter_net WHERE supporter_id=NEW.supporter_id)>=1500 THEN 2 WHEN (SELECT total FROM supporter_net WHERE supporter_id=NEW.supporter_id)>=700 THEN 1 WHEN (SELECT total FROM supporter_net WHERE supporter_id=NEW.supporter_id)>=300 THEN 0 ELSE -1 END), revision=revision+1
 WHERE supporter_id=NEW.supporter_id;
END;
CREATE TRIGGER IF NOT EXISTS supporter_refund_insert AFTER INSERT ON supporter_adjustments BEGIN
 UPDATE supporter_wallets SET total_cents=(SELECT total FROM supporter_net WHERE supporter_id=supporter_wallets.supporter_id),revision=revision+1
 WHERE supporter_id IN (SELECT supporter_id FROM supporter_payments WHERE payment_id=NEW.payment_id);
END;
CREATE TRIGGER IF NOT EXISTS supporter_refund_update AFTER UPDATE ON supporter_adjustments
 WHEN NEW.refunded_cents<>OLD.refunded_cents OR NEW.chargeback_cents<>OLD.chargeback_cents BEGIN
 UPDATE supporter_wallets SET total_cents=(SELECT total FROM supporter_net WHERE supporter_id=supporter_wallets.supporter_id),revision=revision+1
 WHERE supporter_id IN (SELECT supporter_id FROM supporter_payments WHERE payment_id=NEW.payment_id);
END;
