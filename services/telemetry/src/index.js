// Only this explicit schema may cross the public ingest boundary. No request logging.
const codes = new Set([
  'NotInstalled','InvalidConfig','WrongAccount','PlayerMissing','SignatureRejected','HelperVersionMismatch',
  'ProtocolMismatch','TaskMismatch','PermissionsUnsafe','InspectionUnavailable','WorkerUnavailable','WorkerTimeout',
  'CompletionUnconfirmed','ProcessStartFailed','LaunchFailed','Interrupted','HistoryUnavailable','LogsUnavailable',
  'ProtocolChanged','ProtocolMissing','IntegrationDisabled','RecoveryPending','PlayerExited','LegacyInstallation',
  'OPERATION_FAILED','HELPER_VERSION_MISMATCH','ACCOUNT_SIGNIN_FAILED','ACCOUNT_SESSION_EXPIRED',
  'ACCOUNT_LAUNCH_FAILED','BATCH_LAUNCH_FAILED','LAYOUT_FAILED'
]);
const exact = (value, keys) => value !== null && typeof value === 'object' && !Array.isArray(value) && Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value,key));
const day = date => date.toISOString().slice(0,10);
const offset = (date, days) => day(new Date(date.getTime() + days * 86400000));
export function validateBatch(value, now = new Date()) {
  if (!exact(value,['schemaVersion','batchId','errors']) || value.schemaVersion !== 1 || typeof value.batchId !== 'string' || !/^[0-9a-f]{32}$/.test(value.batchId) || !Array.isArray(value.errors) || value.errors.length < 1 || value.errors.length > 20) return false;
  let count = 0;
  const unique = new Set();
  for (const error of value.errors) {
    if (!exact(error,['day','appVersion','helperVersion','windowsVersion','architecture','source','code','errorCode','count'])) return false;
    if (typeof error.day !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(error.day) || error.day < offset(now,-6) || error.day > day(now) || Number.isNaN(Date.parse(error.day)) || day(new Date(error.day)) !== error.day) return false;
    if ([error.appVersion,error.helperVersion].some(v => typeof v !== 'string' || !/^\d{1,5}(\.\d{1,5}){2,3}$/.test(v))) return false;
    if (!['Windows10','Windows11','Other'].includes(error.windowsVersion) || !['X64','X86','Arm64','Arm'].includes(error.architecture) || !['Desktop','Roblox','RobloxPlayer'].includes(error.source) || !codes.has(error.code)) return false;
    if (!(error.errorCode === null || Number.isInteger(error.errorCode) && error.errorCode >= -2147483648 && error.errorCode <= 2147483647) || !Number.isInteger(error.count) || error.count < 1 || error.count > 1000) return false;
    count += error.count;
    const key = JSON.stringify([error.day,error.appVersion,error.helperVersion,error.windowsVersion,error.architecture,error.source,error.code,error.errorCode]);
    if (unique.has(key)) return false;
    unique.add(key);
  }
  return count <= 1000;
}
const response = (status, message, retry) => new Response(JSON.stringify({message}), {status, headers:{'content-type':'application/json','cache-control':'no-store',...(retry ? {'retry-after':String(retry)} : {})}});
export async function ingest(db, batch, now = new Date()) {
  const received = day(now);
  const statements = [
    db.prepare('INSERT OR IGNORE INTO telemetry_budget(day,accepted_batches) VALUES (?,0)').bind(received),
    db.prepare('INSERT OR IGNORE INTO telemetry_receipts(batch_id,received_day,processed) SELECT ?,?,0 FROM telemetry_budget WHERE day=? AND accepted_batches<1000').bind(batch.batchId,received,received),
    db.prepare('UPDATE telemetry_budget SET accepted_batches=accepted_batches+1 WHERE day=? AND EXISTS (SELECT 1 FROM telemetry_receipts WHERE batch_id=? AND processed=0)').bind(received,batch.batchId)
  ];
  for (const e of batch.errors) statements.push(db.prepare('INSERT INTO telemetry_errors(day,app_version,helper_version,windows_version,architecture,source,code,error_code,count) SELECT ?,?,?,?,?,?,?,?,? WHERE EXISTS (SELECT 1 FROM telemetry_receipts WHERE batch_id=? AND processed=0) ON CONFLICT(day,app_version,helper_version,windows_version,architecture,source,code,error_code) DO UPDATE SET count=telemetry_errors.count+excluded.count').bind(e.day,e.appVersion,e.helperVersion,e.windowsVersion,e.architecture,e.source,e.code,e.errorCode === null ? 'none' : String(e.errorCode),e.count,batch.batchId));
  statements.push(db.prepare('UPDATE telemetry_receipts SET processed=1 WHERE batch_id=? AND processed=0').bind(batch.batchId));
  // D1 batch is transactional: receipt, budget and all counters commit together or roll back.
  const results = await db.batch(statements);
  if (results[1].meta.changes > 0) return true;
  return Boolean(await db.prepare('SELECT 1 AS present FROM telemetry_receipts WHERE batch_id=? AND processed=1').bind(batch.batchId).first());
}
export default {
  async fetch(request, env) {
    if (new URL(request.url).pathname !== '/v1/error-batches') return response(404,'Not found.');
    if (request.method !== 'POST') return response(405,'POST required.');
    if (env.INGESTION_ENABLED !== 'true') return response(503,'Reporting is temporarily unavailable.',3600);
    // Route-level approximate guard needs no client identifier or IP storage.
    try {
      if (env.INGEST_RATE && !(await env.INGEST_RATE.limit({key:'error-batches'})).success) return response(429,'Try later.',3600);
    } catch { return response(503,'Reporting is temporarily unavailable.',3600); }
    if ((request.headers.get('content-type') ?? '').split(';')[0].trim().toLowerCase() !== 'application/json') return response(415,'JSON required.');
    const advertised = request.headers.get('content-length');
    if (advertised !== null && (!/^\d+$/.test(advertised) || Number(advertised) > 8192)) return response(413,'Summary too large.');
    try {
      const reader = request.body?.getReader();
      if (!reader) return response(400,'Summary required.');
      let size = 0;
      const chunks = [];
      while (true) { const result = await reader.read(); if (result.done) break; size += result.value.length; if (size > 8192) { await reader.cancel(); return response(413,'Summary too large.'); } chunks.push(result.value); }
      const bytes = new Uint8Array(size); let start = 0; for (const chunk of chunks) { bytes.set(chunk,start); start += chunk.length; }
      let batch;
      try { batch = JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(bytes)); } catch { return response(400,'Invalid summary.'); }
      if (!validateBatch(batch)) return response(400,'Invalid summary.');
      return await ingest(env.DB,batch) ? response(200,'Accepted.') : response(429,'Daily reporting limit reached.',86400);
    } catch { return response(503,'Reporting is temporarily unavailable.',3600); }
  },
  async scheduled(_event, env) {
    const now = new Date();
    await env.DB.batch([
      env.DB.prepare('DELETE FROM telemetry_errors WHERE day<?').bind(offset(now,-89)),
      env.DB.prepare('DELETE FROM telemetry_receipts WHERE received_day<?').bind(offset(now,-6)),
      env.DB.prepare('DELETE FROM telemetry_budget WHERE day<?').bind(offset(now,-6))
    ]);
  }
};
