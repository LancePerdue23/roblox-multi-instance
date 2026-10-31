import {test,expect} from '@playwright/test';
import {installScenario,recordedCalls} from '../fixtures/scenario';

for(const count of [0,1,10,100])test(`stress: ${count} synthetic instances`,async({page})=>{
  const errors:string[]=[];page.on('pageerror',e=>errors.push(e.message));
  await installScenario(page,{instances:count,delayMs:15});
  await expect(page.locator('tbody input[type=checkbox]')).toHaveCount(count);
  await page.getByRole('button',{name:'Layouts',exact:true}).click();
  await expect(page.locator('main')).toHaveAttribute('aria-busy','false');
  if(count){await page.getByRole('button',{name:'Select all',exact:true}).click();await expect(page.getByRole('button',{name:'Apply layout…',exact:true})).toBeEnabled();await expect(page.locator('.layout-svg .slot')).toHaveCount(count);}
  expect(errors).toEqual([]);
});

test('stress: seeded milestone soak',async({page},info)=>{
  const seconds=Number(process.env.AUDIT_SOAK_SECONDS??900);
  if(!Number.isFinite(seconds)||seconds<1||seconds>3600)throw Error('AUDIT_SOAK_SECONDS must be 1..3600');
  test.setTimeout((seconds+90)*1000);
  let seed=Number(process.env.AUDIT_SEED??3102026)>>>0;
  const initialSeed=seed,random=()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return seed/4294967296;};
  const errors:string[]=[],samples:{elapsedMs:number;heapBytes:number|null;operationMs:number}[]=[];
  page.on('pageerror',error=>errors.push(error.message));page.on('crash',()=>errors.push('Browser page crashed'));
  await installScenario(page,{instances:100,activity:1000,delayMs:35});
  const start=Date.now();let iteration=0;
  while(Date.now()-start<seconds*1000){
    const action=Math.floor(random()*5),at=Date.now();
    const name=['Instances','Layouts','Activity','System','Settings'][action];
    await page.getByRole('button',{name,exact:true}).click();await expect(page.locator('main')).toHaveAttribute('aria-busy','false');
    if(name==='Instances'){
      if(iteration%7===0)await page.evaluate(()=>(window as unknown as {audit:{fail:(command:string)=>void}}).audit.fail('instances.list'));
      await page.getByRole('button',{name:'↻ Refresh',exact:true}).click();await expect(page.getByRole('button',{name:'↻ Refresh',exact:true})).toBeEnabled();
      if(await page.getByRole('alert').count()){await page.getByRole('button',{name:'↻ Refresh',exact:true}).click();await expect(page.getByRole('alert')).toHaveCount(0);}
    }
    if(name==='Layouts'){
      await page.getByRole('button',{name:'Select all',exact:true}).click();await expect(page.getByRole('button',{name:'Apply layout…',exact:true})).toBeEnabled();
      await page.getByRole('button',{name:'Apply layout…',exact:true}).click();await page.keyboard.press('Escape');
      await page.getByRole('button',{name:'Clear selection',exact:true}).click();await expect(page.getByRole('button',{name:'Apply layout…',exact:true})).toBeDisabled();
    }
    if(name==='Activity'){await page.getByRole('button',{name:'View details',exact:true}).nth(Math.floor(random()*1000)).click();await expect(page.locator('tr.activity-detail-row')).toHaveCount(1);}
    if(name==='System'&&iteration%3===0){
      await page.getByRole('tab',{name:'Version & updates',exact:true}).click();await page.getByRole('button',{name:'Check for updates',exact:true}).click();await page.getByRole('button',{name:'Download update',exact:true}).click();await page.getByRole('button',{name:'Cancel operation',exact:true}).click();await expect(page.locator('main')).toHaveAttribute('aria-busy','false');
    }
    const heap=await page.evaluate(()=>(performance as unknown as {memory?:{usedJSHeapSize:number}}).memory?.usedJSHeapSize??null);
    samples.push({elapsedMs:Date.now()-start,heapBytes:heap,operationMs:Date.now()-at});iteration++;
    await page.waitForTimeout(200);
  }
  const calls=await recordedCalls(page);
  const durations=samples.map(s=>s.operationMs).sort((a,b)=>a-b),heap=samples.flatMap(s=>s.heapBytes===null?[]:[s.heapBytes]);
  const growth=heap.length>1?heap.at(-1)!-heap[0]:null;
  const reviewFlags=growth!==null&&growth>50*1024*1024?['JavaScript heap increased by more than 50 MiB; review samples and garbage collection behavior.']:[];
  await info.attach('soak-metrics',{body:JSON.stringify({seed:initialSeed,seconds,iterations:iteration,operationP95Ms:durations[Math.floor(durations.length*.95)],heapGrowthBytes:growth,reviewFlags,samples,commandDurations:calls.map(c=>({command:c.command,durationMs:c.durationMs,outcome:c.outcome})),note:'Heap samples describe trends; they do not certify absence of leaks.'},null,2),contentType:'application/json'});
  expect(errors).toEqual([]);expect(calls.filter(c=>c.command==='layouts.apply')).toHaveLength(0);
  await expect(page.locator('main')).toHaveAttribute('aria-busy','false');
});
