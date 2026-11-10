import test from 'node:test';
import assert from 'node:assert/strict';
import { createHmac, generateKeyPairSync, webcrypto } from 'node:crypto';
import { DatabaseSync } from 'node:sqlite';
import { readFileSync } from 'node:fs';
import { createHandler, verifyStripeSignature, signReceipt } from '../src/worker.mjs';

const timestamp = 1800000000;
const secret = 'a'.repeat(43);
const hash = Buffer.from(await webcrypto.subtle.digest('SHA-256', new TextEncoder().encode(secret))).toString('hex');
const { privateKey, publicKey } = generateKeyPairSync('ec', { namedCurve: 'P-256' });
class Database {
  db = new DatabaseSync(':memory:');
  failPaid = false;
  constructor(){this.db.exec(readFileSync(new URL('../schema.sql',import.meta.url),'utf8'));this.db.exec(readFileSync(new URL('../cumulative.sql',import.meta.url),'utf8'));}
  get rows(){return new Map(this.db.prepare('SELECT * FROM support_attempts').all().map(row=>[row.attempt_id,row]));}
  get budgets(){return new Map(this.db.prepare('SELECT * FROM support_daily_budget').all().map(row=>[row.day,row.used]));}
  async batch(statements){this.db.exec('BEGIN');try{for(const statement of statements)await statement.run();this.db.exec('COMMIT');}catch(e){this.db.exec('ROLLBACK');throw e;}}
  prepare(sql) { return { bind: (...args) => ({
    run: async () => {
      if(sql.includes("SET status='paid'")&&this.failPaid)throw new Error('D1 unavailable');
      this.db.prepare(sql).run(...args);
      return {success:true};
    },
    first: async () => this.db.prepare(sql).get(...args)??null
  }) }; }
}
function setup() {
  const env={ SUPPORT_DB:new Database(),SUPPORT_RATE:{limit:async()=>({success:true})},STRIPE_SECRET_KEY:'sk_test_fixture',STRIPE_WEBHOOK_SECRET:'whsec_fixture',STRIPE_PRICE_ID:'price_fixture',STRIPE_LIVE_MODE:'false',RECEIPT_KEY_ID:'fixture',RECEIPT_PRIVATE_JWK:JSON.stringify(privateKey.export({format:'jwk'})),providerCalls:0,idempotencyKeys:[] };
  const handler=createHandler({now:()=>timestamp,fetch:async(url,options)=>{
    assert.equal(url,'https://api.stripe.com/v1/checkout/sessions');assert.equal(options.redirect,'error');
    if(!new URLSearchParams(options.body).has('line_items[0][price_data][unit_amount]'))assert.equal(new URLSearchParams(options.body).get('line_items[0][price]'),'price_fixture');
    const form=new URLSearchParams(options.body);
    assert.equal(form.get('mode'),'payment');
    assert.equal(form.get('success_url'),'https://support.invalid/support/thanks');
    assert.equal(form.get('cancel_url'),'https://support.invalid/support/cancel');
    env.providerCalls++;env.idempotencyKeys.push(options.headers['idempotency-key']);
    if(env.dropOnce){env.dropOnce=false;throw new Error('Lost start response');}
    return new Response(JSON.stringify({id:'cs_test_'+options.headers['idempotency-key'],url:'https://checkout.stripe.com/c/pay/fixture',livemode:false}));
  }});
  const request=(path,body,headers={})=>handler(new Request('https://support.invalid'+path,{method:'POST',headers,body:typeof body==='string'?body:JSON.stringify(body)}),env);
  return {env,request};
}
const signature=raw=>`t=${timestamp},v1=${createHmac('sha256','whsec_fixture').update(timestamp+'.'+raw).digest('hex')}`;
function event(attemptId,extra={}) {return {id:'evt_fixture',type:'checkout.session.completed',livemode:false,data:{object:{id:'cs_test_'+attemptId,object:'checkout.session',mode:'payment',livemode:false,payment_status:'paid',amount_total:100,metadata:{supportProduct:'roblox-multi-instance',attemptId},...extra}}};}

test('disabled deployment cannot create checkouts',async()=>{const response=await createHandler()(new Request('https://support.invalid/support/start',{method:'POST',body:'{}'}),{});assert.equal(response.status,503);});
test('browser return pages never issue receipts or access payment storage',async()=>{
  for(const path of ['/support/thanks','/support/cancel']){
    const response=await createHandler()(new Request('https://support.invalid'+path),{});
    assert.equal(response.status,200);assert.equal(response.headers.get('cache-control'),'no-store');
    const body=await response.text();assert.match(body,/Return to Roblox Multi Instance/);assert.doesNotMatch(body,/receiptId/);
    if(path.endsWith('cancel'))assert.match(body,/without confirming payment/);
  }
});
test('raw signature verification rejects changes, stale replay, and future timestamps',async()=>{
  const raw='{"fixture":true}';assert.equal(await verifyStripeSignature(raw,signature(raw),'whsec_fixture',timestamp),true);
  assert.equal(await verifyStripeSignature(raw+' ',signature(raw),'whsec_fixture',timestamp),false);
  assert.equal(await verifyStripeSignature(raw,signature(raw),'whsec_fixture',timestamp+301),false);
  assert.equal(await verifyStripeSignature(raw,signature(raw),'whsec_fixture',timestamp-301),false);
});
test('checkout -> pending -> verified webhook -> offline verifiable receipt is idempotent',async()=>{
  const {request,env}=setup();const start=await (await request('/support/start',{claimHash:hash})).json();assert.equal(start.attemptId.length,32);
  assert.equal((await (await request('/support/claim',{attemptId:start.attemptId,secret})).json()).status,'pending');
  let raw=JSON.stringify(event(start.attemptId));assert.equal((await request('/support/webhook',raw,{'stripe-signature':signature(raw)})).status,200);
  assert.equal((await request('/support/webhook',raw,{'stripe-signature':signature(raw)})).status,200);
  assert.equal(env.SUPPORT_DB.rows.size,1);
  const result=await (await request('/support/claim',{attemptId:start.attemptId,secret})).json();assert.equal(result.status,'paid');
  const [payload,signatureBytes]=result.receipt.split('.').map(x=>Buffer.from(x,'base64url'));
  const verifyKey=await webcrypto.subtle.importKey('jwk',publicKey.export({format:'jwk'}),{name:'ECDSA',namedCurve:'P-256'},false,['verify']);
  assert.equal(await webcrypto.subtle.verify({name:'ECDSA',hash:'SHA-256'},verifyKey,signatureBytes,payload),true);
  assert.deepEqual(Object.keys(JSON.parse(payload)).sort(),['badge','keyId','product','receiptId','version']);
  assert.equal(JSON.parse(payload).receiptId,start.attemptId);
  assert.equal((await request('/support/claim',{attemptId:start.attemptId,secret:'b'.repeat(43)})).status,404);
});
test('forged, wrong-mode, wrong-product, wrong-session and unpaid events do not grant status',async()=>{
  const {request,env}=setup();const {attemptId}=await (await request('/support/start',{claimHash:hash})).json();
  for(const session of [{payment_status:'unpaid'},{amount_total:0},{livemode:true},{id:'cs_test_other'},{metadata:{attemptId,supportProduct:'other'}}]){
    const raw=JSON.stringify(event(attemptId,session));await request('/support/webhook',raw,{'stripe-signature':signature(raw)});assert.equal(env.SUPPORT_DB.rows.get(attemptId).status,'pending');
  }
  const raw=JSON.stringify(event(attemptId));assert.equal((await request('/support/webhook',raw,{'stripe-signature':'forged'})).status,400);assert.equal(env.SUPPORT_DB.rows.get(attemptId).status,'pending');
});
test('malformed and oversized input never creates a checkout',async()=>{
  const {request,env}=setup();assert.equal((await request('/support/start',{claimHash:'bad'})).status,400);
  assert.equal((await request('/support/start',{claimHash:hash,extra:'bad'})).status,400);
  assert.equal((await request('/support/start',{claimHash:[hash]})).status,400);
  assert.equal((await request('/support/start','x'.repeat(2049))).status,400);assert.equal(env.SUPPORT_DB.rows.size,0);
});
test('receipt signing is ES256 with no payment identity',async()=>{const {env}=setup();const [payload,signatureBytes]=(await signReceipt('A'.repeat(32),env)).split('.').map(x=>Buffer.from(x,'base64url'));assert.equal(signatureBytes.length,64);assert.equal(JSON.parse(payload).badge,'supporter');assert.equal(payload.includes('email'),false);});
test('checkout retries recover one attempt and reuse Stripe idempotency key',async()=>{
  const {request,env}=setup();env.dropOnce=true;assert.equal((await request('/support/start',{claimHash:hash})).status,503);
  const resumed=await (await request('/support/start',{claimHash:hash})).json();assert.equal(resumed.attemptId.length,32);
  const repeated=await (await request('/support/start',{claimHash:hash})).json();assert.deepEqual(repeated,resumed);
  assert.equal(env.providerCalls,2);assert.equal(env.idempotencyKeys[0],env.idempotencyKeys[1]);assert.equal(env.SUPPORT_DB.rows.size,1);assert.equal([...env.SUPPORT_DB.budgets.values()][0],1);
});
test('payment is not acknowledged before a durable database transition succeeds',async()=>{
  const {request,env}=setup();const {attemptId}=await (await request('/support/start',{claimHash:hash})).json();env.SUPPORT_DB.failPaid=true;
  const raw=JSON.stringify(event(attemptId));assert.equal((await request('/support/webhook',raw,{'stripe-signature':signature(raw)})).status,503);
  assert.equal(env.SUPPORT_DB.rows.get(attemptId).status,'pending');env.SUPPORT_DB.failPaid=false;
  assert.equal((await request('/support/webhook',raw,{'stripe-signature':signature(raw)})).status,200);assert.equal(env.SUPPORT_DB.rows.get(attemptId).status,'paid');
});
test('rate limiting blocks public requests while allowing authenticated payment webhooks',async()=>{
  const {request,env}=setup();const {attemptId}=await (await request('/support/start',{claimHash:hash})).json();env.SUPPORT_RATE.limit=async()=>({success:false});
  assert.equal((await request('/support/start',{claimHash:hash})).status,429);assert.equal((await request('/support/claim',{attemptId,secret})).status,429);
  const raw=JSON.stringify(event(attemptId));assert.equal((await request('/support/webhook',raw,{'stripe-signature':signature(raw)})).status,200);
});
test('daily creation budget bounds Stripe checkouts without blocking existing attempts',async()=>{
  const {request,env}=setup();for(let i=0;i<100;i++)assert.equal((await request('/support/start',{claimHash:i.toString(16).padStart(64,'0')})).status,200);
  assert.equal((await request('/support/start',{claimHash:'f'.repeat(64)})).status,429);assert.equal(env.providerCalls,100);
  assert.equal((await request('/support/start',{claimHash:'0'.repeat(64)})).status,200);assert.equal(env.providerCalls,100);
});

const walletSecret='w'.repeat(43);
const walletId=Buffer.from(await webcrypto.subtle.digest('SHA-256',new TextEncoder().encode(walletSecret))).toString('hex');
async function contribute(ctx,amountCents,index=0){
 const secret=String(index).padStart(43,'x');const claimHash=Buffer.from(await webcrypto.subtle.digest('SHA-256',new TextEncoder().encode(secret))).toString('hex');
 const r=await ctx.request('/support/start-v2',{claimHash,walletSecret,amountCents});assert.equal(r.status,200);const a=await r.json();
 const notification=event(a.attemptId,{amount_total:amountCents,currency:'usd',payment_intent:'pi_'+a.attemptId,metadata:{attemptId:a.attemptId,supportProduct:'roblox-multi-instance',supportVersion:'2',supporterId:walletId}});
 return {a,secret,notification,send:async(e=notification)=>{const raw=JSON.stringify(e);return ctx.request('/support/webhook',raw,{'stripe-signature':signature(raw)});}};
}
async function wallet(ctx){const r=await ctx.request('/support/wallet',{walletSecret});assert.equal(r.status,200);const {receipt}=await r.json();return JSON.parse(Buffer.from(receipt.split('.')[0],'base64url'));}
test('500 + 200 + 800 cents credits once per provider payment, signs cumulative tiers, and survives cleanup',async()=>{
 const ctx=setup();
 for(const [index,amount,total,tier] of [[0,500,500,0],[1,200,700,1],[2,800,1500,2]]){
  const c=await contribute(ctx,amount,index);assert.equal((await c.send()).status,200);assert.equal((await c.send()).status,200);
  const current=await wallet(ctx);assert.equal(current.totalCents,total);assert.equal(current.tier,tier);assert.equal(current.revision,index+1);
  const claim=await (await ctx.request('/support/claim',{attemptId:c.a.attemptId,secret:c.secret})).json();assert.equal(claim.status,'paid');assert.equal(JSON.parse(Buffer.from(claim.receipt.split('.')[0],'base64url')).supporterId,walletId);
 }
 ctx.env.SUPPORT_DB.db.exec('DELETE FROM support_attempts; DELETE FROM support_daily_budget;');assert.equal((await wallet(ctx)).totalCents,1500);
 const {receipt}=await (await ctx.request('/support/wallet',{walletSecret})).json();const [payload,sig]=receipt.split('.').map(s=>Buffer.from(s,'base64url'));
 const verifyKey=await webcrypto.subtle.importKey('jwk',publicKey.export({format:'jwk'}),{name:'ECDSA',namedCurve:'P-256'},false,['verify']);assert.equal(await webcrypto.subtle.verify({name:'ECDSA',hash:'SHA-256'},verifyKey,sig,payload),true);
 assert.equal((await ctx.request('/support/wallet',{walletSecret:'z'.repeat(43)})).status,404);
});
test('sub-threshold payments, exact boundaries and skipping tiers use the verified total',async()=>{
 const ctx=setup();let index=0;
 for(const [amount,total,tier] of [[100,100,-1],[199,299,-1],[50,349,0],[351,700,1],[800,1500,2]]){const c=await contribute(ctx,amount,index++);await c.send();const w=await wallet(ctx);assert.equal(w.totalCents,total);assert.equal(w.tier,tier);}
 const fresh=setup();const c=await contribute(fresh,1500,10);await c.send();assert.equal((await wallet(fresh)).tier,2);
});
test('wrong currency, amount, wallet, session and unsigned events cannot credit a cumulative checkout',async()=>{
 const ctx=setup();const c=await contribute(ctx,500);
 for(const change of [{currency:'eur'},{amount_total:501},{id:'cs_wrong'},{metadata:{...c.notification.data.object.metadata,supporterId:'f'.repeat(64)}}]){const e=structuredClone(c.notification);Object.assign(e.data.object,change);assert.equal((await c.send(e)).status,503);assert.equal((await wallet(ctx)).totalCents,0);}
 const raw=JSON.stringify(c.notification);assert.equal((await ctx.request('/support/webhook',raw,{'stripe-signature':'forged'})).status,400);
 for(const amountCents of [0,49,100001,1.5,'500'])assert.equal((await ctx.request('/support/start-v2',{claimHash:'a'.repeat(64),walletSecret,amountCents})).status,400);
});
test('refunds and lost disputes reduce totals while preserving earned cosmetics; reordered deductions remain durable',async()=>{
 const ctx=setup();const c=await contribute(ctx,1500);await c.send();
 const refund=amount=>({type:'charge.refunded',livemode:false,data:{object:{object:'charge',currency:'usd',payment_intent:'pi_'+c.a.attemptId,amount_refunded:amount}}});
 await c.send(refund(800));await c.send(refund(200));await c.send(refund(800));let w=await wallet(ctx);assert.equal(w.totalCents,700);assert.equal(w.tier,2);assert.equal(w.revision,2);
 const dispute={type:'charge.dispute.closed',livemode:false,data:{object:{object:'dispute',currency:'usd',payment_intent:'pi_'+c.a.attemptId,amount:1500,status:'won'}}};await c.send(dispute);assert.equal((await wallet(ctx)).totalCents,700);
 dispute.data.object.status='lost';dispute.data.object.amount=200;await c.send(dispute);w=await wallet(ctx);assert.equal(w.totalCents,500);assert.equal(w.tier,2);dispute.data.object.amount=1500;await c.send(dispute);await c.send(dispute);w=await wallet(ctx);assert.equal(w.totalCents,0);assert.equal(w.tier,2);assert.equal(w.revision,4);
 const early=setup();const d=await contribute(early,700);const e=refund(700);e.data.object.payment_intent='pi_'+d.a.attemptId;await d.send(e);await d.send();assert.equal((await wallet(early)).totalCents,0);assert.equal((await wallet(early)).tier,-1);
});
test('a failed durable credit is retried safely and checkout ownership cannot be changed',async()=>{
 const ctx=setup();const c=await contribute(ctx,500);ctx.env.SUPPORT_DB.db.exec("CREATE TRIGGER fixture_fail BEFORE INSERT ON supporter_payments BEGIN SELECT RAISE(ABORT,'fixture unavailable'); END;");
 assert.equal((await c.send()).status,503);assert.equal((await wallet(ctx)).revision,0);ctx.env.SUPPORT_DB.db.exec('DROP TRIGGER fixture_fail;');assert.equal((await c.send()).status,200);assert.equal((await wallet(ctx)).totalCents,500);
 const a=ctx.env.SUPPORT_DB.db.prepare('SELECT * FROM supporter_checkouts').get();assert.equal((await ctx.request('/support/start-v2',{claimHash:a.claim_hash,walletSecret:'z'.repeat(43),amountCents:500})).status,409);
});
