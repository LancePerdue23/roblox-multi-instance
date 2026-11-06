import {test} from 'node:test';
import assert from 'node:assert/strict';
import {DatabaseSync} from 'node:sqlite';
import {readFileSync} from 'node:fs';
import worker, {validateBatch,ingest} from '../src/index.js';

const now = new Date('2026-11-02T12:00:00Z');
const counter = () => ({day:'2026-11-02',appVersion:'1.0.0',helperVersion:'1.0.0',windowsVersion:'Windows11',architecture:'X64',source:'Desktop',code:'LaunchFailed',errorCode:null,count:2});
const batch = (id = 'a'.repeat(32)) => ({schemaVersion:1,batchId:id,errors:[counter()]});
function database() {
  const sqlite = new DatabaseSync(':memory:');
  sqlite.exec(readFileSync(new URL('../migrations/0001_errors.sql',import.meta.url),'utf8'));
  const db = {
    prepare(sql) { return {bind(...args) { return {run() { const result = sqlite.prepare(sql).run(...args); return {meta:{changes:Number(result.changes)}}; },first() { return sqlite.prepare(sql).get(...args) ?? null; }}; }}; },
    async batch(statements) { sqlite.exec('BEGIN'); try { const result = statements.map(s => s.run()); sqlite.exec('COMMIT'); return result; } catch(error) { sqlite.exec('ROLLBACK'); throw error; } }
  };
  return {db,sqlite};
}
test('schema rejects identifiers, free text, duplicate buckets, expired dates and excessive counters', () => {
  assert.equal(validateBatch(batch(),now),true);
  for (const invalid of [
    {...batch(),username:'privacy-canary'},
    {...batch(),errors:[{...counter(),cookie:'privacy-canary'}]},
    {...batch(),errors:[{...counter(),code:'privacy-canary'}]},
    {...batch(),errors:[{...counter(),day:'2026-10-26'}]},
    {...batch(),errors:[{...counter(),count:1001}]},
    {...batch(),errors:[counter(),counter()]},
    {...batch(),errors:[{...counter(),errorCode:'privacy-canary'}]}
  ]) assert.equal(validateBatch(invalid,now),false);
});
test('transactional receipt makes retries idempotent and aggregates distinct batches',async () => {
  const {db,sqlite} = database();
  try {
    assert.equal(await ingest(db,batch(),now),true);
    assert.equal(await ingest(db,batch(),now),true);
    assert.equal(await ingest(db,batch('b'.repeat(32)),now),true);
    assert.equal(sqlite.prepare('SELECT count FROM telemetry_errors').get().count,4);
    assert.equal(sqlite.prepare('SELECT accepted_batches FROM telemetry_budget').get().accepted_batches,2);
  } finally { sqlite.close(); }
});
test('quota refusal writes no receipts or error counts; existing retry still succeeds',async () => {
  const {db,sqlite} = database();
  try {
    await ingest(db,batch(),now);
    sqlite.prepare('UPDATE telemetry_budget SET accepted_batches=1000').run();
    assert.equal(await ingest(db,batch('c'.repeat(32)),now),false);
    assert.equal(await ingest(db,batch(),now),true);
    assert.equal(sqlite.prepare('SELECT count FROM telemetry_errors').get().count,2);
    assert.equal(sqlite.prepare('SELECT COUNT(*) AS count FROM telemetry_receipts').get().count,1);
  } finally { sqlite.close(); }
});
test('database error rolls back receipt, budget and counters together',async () => {
  const {db,sqlite} = database();
  try {
    const invalid = {...batch(),errors:[{...counter(),count:-1}]};
    await assert.rejects(ingest(db,invalid,now));
    assert.equal(sqlite.prepare('SELECT COUNT(*) AS count FROM telemetry_receipts').get().count,0);
    assert.equal(sqlite.prepare('SELECT COUNT(*) AS count FROM telemetry_budget').get().count,0);
  } finally { sqlite.close(); }
});
test('HTTP route rejects oversized streamed data and unknown fields without touching database',async () => {
  const env = {INGESTION_ENABLED:'true',DB:{batch(){throw new Error('must not write');}}};
  const send = body => worker.fetch(new Request('https://fixture.invalid/v1/error-batches',{method:'POST',headers:{'content-type':'application/json'},body}),env);
  assert.equal((await send('x'.repeat(8193))).status,413);
  assert.equal((await send(JSON.stringify({...batch(),cookie:'privacy-canary'}))).status,400);
  assert.equal((await worker.fetch(new Request('https://fixture.invalid/v1/error-batches',{method:'POST'}),{INGESTION_ENABLED:'false'})).status,503);
});
test('HTTP route accepts an allowed summary and returns retryable failures without request logging',async () => {
  const {db,sqlite} = database();
  const current = batch();current.errors[0].day = new Date().toISOString().slice(0,10);
  const request = () => new Request('https://fixture.invalid/v1/error-batches',{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify(current)});
  try {
    assert.equal((await worker.fetch(request(),{INGESTION_ENABLED:'true',DB:db})).status,200);
    assert.equal((await worker.fetch(request(),{INGESTION_ENABLED:'true',DB:db,INGEST_RATE:{limit:async()=>({success:false})}})).status,429);
    assert.equal((await worker.fetch(request(),{INGESTION_ENABLED:'true',DB:db,INGEST_RATE:{limit:async()=>{throw new Error('provider failure');}}})).status,503);
    assert.equal((await worker.fetch(request(),{INGESTION_ENABLED:'true',DB:{batch:async()=>{throw new Error('database unavailable');},prepare:db.prepare}})).status,503);
  } finally { sqlite.close(); }
});
test('retention removes expired aggregates and receipts while keeping recent data',async () => {
  const {db,sqlite} = database();
  try {
    sqlite.prepare('INSERT INTO telemetry_receipts VALUES (?,?,1)').run('x','2000-01-01');
    sqlite.prepare('INSERT INTO telemetry_budget VALUES (?,1)').run('2000-01-01');
    sqlite.prepare('INSERT INTO telemetry_errors VALUES (?,?,?,?,?,?,?,?,?)').run('2000-01-01','1.0.0','1.0.0','Other','X64','Desktop','LaunchFailed','none',1);
    await worker.scheduled({}, {DB:db});
    for (const name of ['telemetry_receipts','telemetry_budget','telemetry_errors']) assert.equal(sqlite.prepare('SELECT COUNT(*) AS count FROM '+name).get().count,0);
  } finally { sqlite.close(); }
});
