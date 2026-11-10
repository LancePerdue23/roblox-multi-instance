// Recovery codes are bearer credentials. Store only their SHA-256 identity, never the code.
const product = 'roblox-multi-instance';
const enc = new TextEncoder();
const validSecret = s => typeof s === 'string' && /^[A-Za-z0-9_-]{43}$/.test(s);
const validHash = s => typeof s === 'string' && /^[0-9a-f]{64}$/.test(s);
const fields = (b, names) => b && !Array.isArray(b) && Object.keys(b).length === names.length && names.every(n => Object.hasOwn(b,n));
const hash = async s => [...new Uint8Array(await crypto.subtle.digest('SHA-256',enc.encode(s)))].map(n=>n.toString(16).padStart(2,'0')).join('');
const b64 = b => btoa(String.fromCharCode(...new Uint8Array(b))).replace(/=/g,'').replace(/\+/g,'-').replace(/\//g,'_');
const response = (v,status=200) => new Response(JSON.stringify(v),{status,headers:{'content-type':'application/json','cache-control':'no-store','x-content-type-options':'nosniff'}});
const stmt = (env,sql,...args) => env.SUPPORT_DB.prepare(sql).bind(...args);

export async function walletReceipt(wallet,env) {
 const key=await crypto.subtle.importKey('jwk',JSON.parse(env.RECEIPT_PRIVATE_JWK),{name:'ECDSA',namedCurve:'P-256'},false,['sign']);
 const payload=enc.encode(JSON.stringify({product,badge:'supporter',receiptId:wallet.supporter_id,version:2,keyId:env.RECEIPT_KEY_ID,supporterId:wallet.supporter_id,tier:wallet.earned_tier,totalCents:wallet.total_cents,revision:wallet.revision,currency:'usd'}));
 return b64(payload)+'.'+b64(await crypto.subtle.sign({name:'ECDSA',hash:'SHA-256'},key,payload));
}

export async function cumulativeRoute(path,body,env,fetcher,now,origin,boundedBody) {
 if(path==='/support/wallet') {
  if(!fields(body,['walletSecret'])||!validSecret(body.walletSecret))return response({error:'request'},400);
  const wallet=await stmt(env,'SELECT * FROM supporter_wallets WHERE supporter_id=?',await hash(body.walletSecret)).first();
  if(!wallet)return response({error:'wallet'},404);
  return response({receipt:await walletReceipt(wallet,env)});
 }
 if(!fields(body,['claimHash','walletSecret','amountCents'])||!validHash(body.claimHash)||!validSecret(body.walletSecret)||!Number.isSafeInteger(body.amountCents)||body.amountCents<50||body.amountCents>100000)return response({error:'request'},400);
 const supporterId=await hash(body.walletSecret);
 let attempt=await stmt(env,'SELECT * FROM supporter_checkouts WHERE claim_hash=?',body.claimHash).first();
 if(attempt&&(attempt.supporter_id!==supporterId||attempt.amount_cents!==body.amountCents))return response({error:'attempt'},409);
 if(!attempt) {
  const budget=await stmt(env,'INSERT INTO support_daily_budget(day,used) VALUES(?,1) ON CONFLICT(day) DO UPDATE SET used=used+1 WHERE used<100 RETURNING used',Math.floor(now/86400)).first();
  if(!budget)return response({error:'daily_limit'},429);
  const id=b64(crypto.getRandomValues(new Uint8Array(24)));
  await env.SUPPORT_DB.batch([
   stmt(env,'INSERT OR IGNORE INTO supporter_wallets(supporter_id) VALUES(?)',supporterId),
   stmt(env,'INSERT OR IGNORE INTO supporter_checkouts(attempt_id,claim_hash,supporter_id,amount_cents,created_at) VALUES(?,?,?,?,?)',id,body.claimHash,supporterId,body.amountCents,now)
  ]);
  attempt=await stmt(env,'SELECT * FROM supporter_checkouts WHERE claim_hash=?',body.claimHash).first();
 }
 if(!attempt||attempt.supporter_id!==supporterId||attempt.amount_cents!==body.amountCents)return response({error:'attempt'},409);
 if(attempt.created_at+86400<=now)return response({error:'checkout_expired'},409);
 if(attempt.session_id&&attempt.checkout_url)return response({attemptId:attempt.attempt_id,checkoutUrl:attempt.checkout_url});
 const form=new URLSearchParams({mode:'payment','line_items[0][price_data][currency]':'usd','line_items[0][price_data][unit_amount]':String(attempt.amount_cents),'line_items[0][price_data][product_data][name]':'Optional support for Roblox Multi Instance','line_items[0][quantity]':'1','payment_method_types[0]':'card','metadata[supportProduct]':product,'metadata[attemptId]':attempt.attempt_id,'metadata[supporterId]':supporterId,'metadata[supportVersion]':'2',success_url:origin+'/support/thanks',cancel_url:origin+'/support/cancel',expires_at:String(attempt.created_at+86400)});
 const result=await fetcher('https://api.stripe.com/v1/checkout/sessions',{method:'POST',headers:{authorization:`Bearer ${env.STRIPE_SECRET_KEY}`,'content-type':'application/x-www-form-urlencoded','idempotency-key':attempt.attempt_id},body:form.toString(),redirect:'error',signal:AbortSignal.timeout(15000)});
 if(!result.ok)throw new Error('provider');
 const session=JSON.parse(await boundedBody(result,32768));const checkout=new URL(session.url);
 if(checkout.protocol!=='https:'||checkout.hostname!=='checkout.stripe.com'||checkout.port||checkout.username||checkout.password||typeof session.id!=='string'||!/^cs_[A-Za-z0-9_-]{1,250}$/.test(session.id)||session.livemode!==(env.STRIPE_LIVE_MODE==='true'))throw new Error('provider');
 await stmt(env,'UPDATE supporter_checkouts SET session_id=?,checkout_url=? WHERE attempt_id=?',session.id,checkout.href,attempt.attempt_id).run();
 return response({attemptId:attempt.attempt_id,checkoutUrl:checkout.href});
}

// Called only after raw Stripe signature and deployment mode verification.
export async function cumulativeEvent(event,env) {
 const s=event.data?.object;
 if(['charge.refunded','charge.dispute.closed'].includes(event.type)) {
  const chargeback=event.type==='charge.dispute.closed';
  if(chargeback&&s?.status!=='lost')return true;
  if(s?.currency!=='usd'||!s?.payment_intent)return true;
  if(!s||s.object!==(chargeback?'dispute':'charge')||s.currency!=='usd'||typeof s.payment_intent!=='string'||!/^pi_[A-Za-z0-9_-]{1,250}$/.test(s.payment_intent))throw new Error('adjustment');
  const refunded=chargeback?0:s.amount_refunded;const disputed=chargeback?s.amount:0;
  if(!Number.isSafeInteger(refunded)||refunded<0||!Number.isSafeInteger(disputed)||disputed<0)throw new Error('amount');
  // Monotonic deductions tolerate duplicate/reordered events, even before credit.
  await stmt(env,'INSERT INTO supporter_adjustments(payment_id,refunded_cents,chargeback_cents) VALUES(?,?,?) ON CONFLICT(payment_id) DO UPDATE SET refunded_cents=MAX(refunded_cents,excluded.refunded_cents),chargeback_cents=MAX(chargeback_cents,excluded.chargeback_cents)',s.payment_intent,refunded,disputed).run();
  return true;
 }
 if(s?.metadata?.supportVersion!=='2')return false;
 if(!['checkout.session.completed','checkout.session.async_payment_succeeded'].includes(event.type))return true;
 if(s.object!=='checkout.session'||s.mode!=='payment'||s.livemode!==(env.STRIPE_LIVE_MODE==='true')||s.metadata.supportProduct!==product)throw new Error('session');
 if(s.payment_status!=='paid')return true;
 const a=await stmt(env,'SELECT * FROM supporter_checkouts WHERE attempt_id=?',s.metadata.attemptId).first();
 if(!a||a.session_id!==s.id||a.supporter_id!==s.metadata.supporterId||s.currency!=='usd'||s.amount_total!==a.amount_cents||typeof s.payment_intent!=='string'||!/^pi_[A-Za-z0-9_-]{1,250}$/.test(s.payment_intent))throw new Error('binding');
 // Unique provider payment + unique checkout, with totals/revision updated in the same SQL transaction by trigger.
 await stmt(env,"INSERT OR IGNORE INTO supporter_payments(payment_id,attempt_id,supporter_id,amount_cents,currency) VALUES(?,?,?,?,'usd')",s.payment_intent,a.attempt_id,a.supporter_id,a.amount_cents).run();
 const saved=await stmt(env,'SELECT * FROM supporter_payments WHERE payment_id=?',s.payment_intent).first();
 if(!saved||saved.attempt_id!==a.attempt_id||saved.supporter_id!==a.supporter_id||saved.amount_cents!==a.amount_cents)throw new Error('payment_binding');
 return true;
}

export async function cumulativeClaim(body,env,now) {
 const a=await stmt(env,'SELECT * FROM supporter_checkouts WHERE attempt_id=?',body.attemptId).first();
 if(!a)return null;
 if(a.claim_hash!==await hash(body.secret)||a.created_at+30*86400<=now)return response({error:'claim'},404);
 const paid=await stmt(env,'SELECT payment_id FROM supporter_payments WHERE attempt_id=?',a.attempt_id).first();
 if(!paid)return response({status:'pending'});
 const wallet=await stmt(env,'SELECT * FROM supporter_wallets WHERE supporter_id=?',a.supporter_id).first();
 return response({status:'paid',receipt:await walletReceipt(wallet,env)});
}
