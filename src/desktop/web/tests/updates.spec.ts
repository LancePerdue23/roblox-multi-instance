import {test,expect} from '@playwright/test';
import {installWorkspace} from './fixtures/workspace';
test('startup offers a newer release without downloading or opening GitHub',async({page})=>{
 await installWorkspace(page);await page.goto('/?update-available');
 await expect(page.getByText('Update 1.2.0 is available',{exact:true})).toBeVisible();
 let calls=await page.evaluate(()=>(window as any).testCalls);
 expect(calls.some((c:any)=>['updates.download','updates.install','updates.openPage'].includes(c.command))).toBe(false);
 await page.getByRole('button',{name:'Review update',exact:true}).click();
 await expect(page.getByRole('heading',{name:/App and helper updates/})).toBeVisible();
 await page.getByRole('button',{name:'Download update',exact:true}).click();
 await expect(page.getByRole('button',{name:'Run setup…',exact:true})).toBeEnabled();
 await page.getByRole('button',{name:'Run setup…',exact:true}).click();
 await expect(page.getByRole('dialog')).toBeVisible();
 calls=await page.evaluate(()=>(window as any).testCalls);
 expect(calls.some((c:any)=>c.command==='updates.install')).toBe(false);
 await page.getByRole('button',{name:'Install update',exact:true}).click();
 await expect.poll(()=>page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='updates.install').length)).toBe(1);
 expect(await page.evaluate(()=>(window as any).testCalls.some((c:any)=>c.command==='updates.openPage'))).toBe(false);
});
test('update prompt can be deferred without starting a download',async({page})=>{
 await installWorkspace(page);await page.goto('/?update-available');
 await page.getByRole('button',{name:'Later',exact:true}).click();
 await expect(page.getByText('Update 1.2.0 is available',{exact:true})).toHaveCount(0);
 expect(await page.evaluate(()=>(window as any).testCalls.some((c:any)=>c.command==='updates.download'))).toBe(false);
});

test('public update review needs no credential controls and keeps release-page recovery available',async({page})=>{
 await installWorkspace(page);await page.goto('/?update-available');
 await page.getByRole('button',{name:'Review update',exact:true}).click();
 await expect(page.getByText('Public stable releases are checked anonymously. No GitHub sign-in is required.')).toBeVisible();
 await expect(page.getByRole('button',{name:'Open release page',exact:true})).toBeVisible();
 await expect(page.getByRole('checkbox',{name:/prerelease/i})).toHaveCount(0);
 await expect(page.getByRole('button',{name:/GitHub update access/})).toHaveCount(0);
 expect(await page.evaluate(()=>(window as any).testCalls.some((c:any)=>['updates.access','updates.connect','updates.disconnect','updates.prereleases'].includes(c.command)))).toBe(false);
});

for(const failure of ['Could not reach GitHub. Check your internet connection and retry.','GitHub anonymous rate limit reached. Retry later.','Operation canceled.','Update payload verification failed. Download again.']){
 test(`failed download keeps setup disabled: ${failure}`,async({page})=>{
  await installWorkspace(page);
  await page.addInitScript(message=>{const timer=setInterval(()=>{const bridge=(window as any).chrome?.webview;if(!bridge)return;clearInterval(timer);const post=bridge.postMessage;bridge.postMessage=(request:any)=>{if(request.command==='updates.download')(window as any).testEmit({version:1,id:request.id,error:{code:'update_failed',message}});else post(request);};},0);},failure);
  await page.goto('/?update-available');await page.getByRole('button',{name:'Review update',exact:true}).click();
  await page.getByRole('button',{name:'Download update',exact:true}).click();
  await expect(page.getByText(failure,{exact:true})).toBeVisible();
  await expect(page.getByRole('button',{name:'Run setup…',exact:true})).toBeDisabled();
  await expect(page.getByRole('button',{name:'Download update',exact:true})).toBeEnabled();
 });
}

test('missing edition asset disables download and exposes release-page recovery',async({page})=>{
 await installWorkspace(page);await page.goto('/?update-available');
 await page.evaluate(()=>{const bridge=(window as any).chrome.webview,post=bridge.postMessage;bridge.postMessage=(request:any)=>{if(request.command==='updates.check')(window as any).testEmit({version:1,id:request.id,data:{available:true,version:'1.3.0',canDownload:false,downloaded:false,downloadProblem:'This release is missing the desktop installer.'}});else post(request);};});
 await page.getByRole('button',{name:'Review update',exact:true}).click();await page.getByRole('button',{name:'Check for updates',exact:true}).click();
 await expect(page.getByText('This release is missing the desktop installer.')).toBeVisible();
 await expect(page.getByRole('button',{name:'Download update',exact:true})).toBeDisabled();
 await expect(page.getByRole('button',{name:'Run setup…',exact:true})).toBeDisabled();
 await expect(page.getByRole('button',{name:'Open release page',exact:true})).toBeEnabled();
});

test('failed recheck clears a previously downloaded setup offer',async({page})=>{
 await installWorkspace(page);await page.goto('/?update-available');await page.getByRole('button',{name:'Review update',exact:true}).click();
 await page.getByRole('button',{name:'Download update',exact:true}).click();await expect(page.getByRole('button',{name:'Run setup…',exact:true})).toBeEnabled();
 await page.evaluate(()=>{const bridge=(window as any).chrome.webview,post=bridge.postMessage;bridge.postMessage=(request:any)=>{if(request.command==='updates.check')(window as any).testEmit({version:1,id:request.id,error:{code:'offline',message:'Could not reach GitHub. Retry later.'}});else post(request);};});
 await page.getByRole('button',{name:'Check for updates',exact:true}).click();
 await expect(page.getByText('Could not reach GitHub. Retry later.',{exact:true})).toBeVisible();
 await expect(page.getByRole('button',{name:'Run setup…',exact:true})).toHaveCount(0);
 await expect(page.getByRole('button',{name:'Open release page',exact:true})).toBeEnabled();
});
