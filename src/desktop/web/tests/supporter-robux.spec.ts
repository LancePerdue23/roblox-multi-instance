import {test,expect} from '@playwright/test';
import {existsSync,readFileSync,readdirSync} from 'node:fs';
import {installWorkspace} from './fixtures/workspace';

test('Robux slider shifts milestones and verifies all purchases without enabling Stripe',async({page})=>{
 await installWorkspace(page);await page.goto('/?robux-credit');await page.getByRole('button',{name:'Support development',exact:true}).click();
 const slider=page.getByRole('slider',{name:'Donation amount'});
 await expect(slider).toHaveAttribute('aria-valuetext','200 Robux');
 await expect(page.locator('.robux-slider-markers')).toContainText('Supporter+200 Robux');
 await expect(page.locator('.robux-slider-markers')).toContainText('Super Supporter1,000 Robux');
 await expect(page.locator('.support-credit')).toContainText('500');
 const plusProgress=page.getByRole('progressbar',{name:'Progress to Supporter+',exact:true});
 await expect(plusProgress).toHaveAttribute('aria-valuemin','300');await expect(plusProgress).toHaveAttribute('aria-valuemax','700');await expect(plusProgress).toHaveAttribute('aria-valuenow','500');
 const fills=()=>page.locator('.robux-tier-track').evaluateAll(tracks=>tracks.map(track=>Math.round(track.firstElementChild!.getBoundingClientRect().width/track.getBoundingClientRect().width*1000)/1000));
 await expect.poll(fills).toEqual([1,.5,0]);
 await page.screenshot({path:'../../../.build/support-robux-progress.png',fullPage:true});
 await expect(page.getByRole('button',{name:'Support with USD…',exact:true})).toBeDisabled();
 expect(await page.locator('#robux-heading').evaluate(el=>el.getBoundingClientRect().top)).toBeLessThan(await page.getByRole('heading',{name:'Support with USD',exact:true}).evaluate(el=>el.getBoundingClientRect().top));
 await slider.focus();await page.keyboard.press('ArrowRight');await expect(slider).toHaveAttribute('aria-valuetext','300 Robux');
 await page.getByRole('combobox',{name:'Pass amount',exact:true}).click();await page.getByRole('option',{name:'200 Robux',exact:true}).click();
 await expect(slider).toHaveAttribute('aria-valuetext','200 Robux');
 await page.getByRole('button',{name:'Buy pass',exact:true}).click();
 await expect.poll(fills).toEqual([1,.5,0]);
 expect(await page.evaluate(()=>(window as any).testCalls.find((c:any)=>c.command==='support.roblox').payload)).toEqual({passId:'2076244661'});
 await expect(page.locator('.support-credit')).toContainText('500');await expect(page.getByRole('dialog')).not.toBeVisible();
 await page.getByRole('button',{name:'Verify purchases',exact:true}).click();
 expect(await page.evaluate(()=>(window as any).testCalls.find((c:any)=>c.command==='support.checkRoblox').payload)).toEqual({});
 await expect(page.getByRole('dialog')).toBeVisible();await expect(page.getByRole('dialog').getByRole('heading')).toHaveText('Supporter+');
 await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.locator('.sidebar-supporter')).toContainText('Supporter+');
 await expect(page.locator('.support-credit')).toContainText('700');await expect(slider).toHaveAttribute('aria-valuetext','1,000 Robux');
 await expect.poll(fills).toEqual([1,1,0]);
 await page.getByRole('button',{name:'Verify purchases',exact:true}).click();await expect(page.getByRole('dialog')).not.toBeVisible();
 await page.getByRole('button',{name:'Instances',exact:true}).click();await page.getByRole('button',{name:'Support development',exact:true}).click();await expect(page.getByRole('dialog')).not.toBeVisible();
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>['support.start','support.claim'].includes(c.command)))).toHaveLength(0);
});

test('Robux controls fit compact viewports and all pass amounts are selectable',async({page})=>{
 await page.setViewportSize({width:800,height:600});await installWorkspace(page);await page.goto('/?robux-credit');await page.getByRole('button',{name:'Support development',exact:true}).click();
 await page.getByRole('combobox',{name:'Pass amount'}).click();await expect(page.getByRole('listbox',{name:'Pass amount'}).getByRole('option')).toHaveCount(21);
 await page.getByRole('option',{name:'100,000 Robux',exact:true}).click();await expect(page.getByRole('slider')).toHaveAttribute('aria-valuetext','100,000 Robux');
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
 await page.emulateMedia({reducedMotion:'reduce'});await expect(page.locator('.robux-selected-amount')).toHaveCSS('animation-name','none');await expect(page.locator('.robux-tier-track i').first()).toHaveCSS('transition-duration','0s');
 await page.screenshot({path:'../../../.build/support-robux-compact.png',fullPage:true});
});

test('purchase checks show pending feedback and respect reduced motion',async({page})=>{
 await installWorkspace(page);await page.goto('/?robux-credit');await page.getByRole('button',{name:'Support development',exact:true}).click();
 await page.evaluate(()=>{const bridge=(window as any).chrome.webview,post=bridge.postMessage;bridge.postMessage=(message:any)=>{if(message.command==='support.checkRoblox')(window as any).finishPurchaseCheck=()=>post(message);else post(message);};});
 const check=page.getByRole('button',{name:'Verify purchases',exact:true});await check.click();
 await expect(check).toHaveAttribute('aria-busy','true');await expect(check).toBeDisabled();await expect(check).toContainText('Checking…');
 await page.evaluate(()=>(window as any).testEmit({version:1,event:'support.robloxProgress',data:{message:'Roblox is limiting requests. Retrying automatically (14/21 checked)…'}}));
 await expect(page.locator('.purchase-check-status')).toHaveText('Roblox is limiting requests. Retrying automatically (14/21 checked)…');
 await expect(page.locator('.robux-check-icon.checking')).toHaveCSS('animation-name','robux-check-spin');
 await page.emulateMedia({reducedMotion:'reduce'});await expect(page.locator('.robux-check-icon.checking')).toHaveCSS('animation-name','none');
 await page.evaluate(()=>(window as any).finishPurchaseCheck());await expect(page.getByRole('dialog')).toBeVisible();await page.getByRole('button',{name:'Continue',exact:true}).click();
 await expect(check).toHaveAttribute('aria-busy','false');await expect(check).toBeEnabled();
 await expect(page.locator('.purchase-check-status')).not.toContainText('Retrying automatically');
});

test('one verification action discovers all purchases and cached page visits never launch a scan',async({page})=>{
 await installWorkspace(page);await page.goto('/?robux-credit');await page.getByRole('button',{name:'Support development',exact:true}).click();
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='support.checkRoblox'))).toHaveLength(0);
 await expect(page.getByRole('button',{name:'Verify purchases',exact:true})).toHaveCount(1);
 await expect(page.getByRole('button',{name:'Find all past purchases',exact:true})).toHaveCount(0);
 await expect(page.getByRole('button',{name:'Verify purchases',exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Verify purchases',exact:true}).click();
 expect(await page.evaluate(()=>(window as any).testCalls.find((c:any)=>c.command==='support.checkRoblox').payload)).toEqual({});
});

test('public builds contain no credit simulation controls or preference key',async({page})=>{
 test.skip(existsSync('dist/local-supporter-preview.json'),'Public build only');
 const scripts=readdirSync('dist/assets').filter(f=>f.endsWith('.js')).map(f=>readFileSync('dist/assets/'+f,'utf8')).join('');
 for(const text of ['Simulated Robux credit','Add selected pass','rmi.local-support-credit','Reset credit checks','support.devResetRoblox'])expect(scripts).not.toContain(text);
 await installWorkspace(page);await page.goto('/?robux-credit');await page.evaluate(()=>{localStorage.setItem('rmi.local-support-credit','1500');localStorage.setItem('rmi.local-role-preview','2');});await page.reload();
 await page.getByRole('button',{name:'Support development',exact:true}).click();await expect(page.locator('.support-credit')).toContainText('Verified Robux credit: 500');
 await expect(page.getByLabel('Simulated Robux credit')).toHaveCount(0);await expect(page.locator('.sidebar-supporter')).toContainText('Supporter');
});

test('dev credit simulation shifts tiers, persists, overrides roles, and resets without native writes',async({page})=>{
 test.skip(!existsSync('dist/local-supporter-preview.json'),'Development build only');
 await installWorkspace(page);await page.goto('/?robux-credit');await page.getByRole('button',{name:'Support development',exact:true}).click();
 await page.getByText('Dev credit controls',{exact:true}).click();const input=page.getByLabel('Simulated Robux credit',{exact:true});
 await input.fill('699');await page.getByRole('button',{name:'Apply',exact:true}).click();await expect(page.locator('.support-credit')).toContainText('Simulated Robux credit: 699');
 await expect(page.getByRole('slider')).toHaveAttribute('aria-valuetext','50 Robux');await expect(page.getByRole('dialog')).not.toBeVisible();
 await page.getByRole('button',{name:'Add selected pass',exact:true}).click();await expect(page.getByRole('dialog')).toBeVisible();await expect(page.getByRole('dialog').getByRole('heading')).toHaveText('Supporter+');await page.getByRole('button',{name:'Continue',exact:true}).click();
 await expect(page.locator('.local-role-button')).toContainText('Supporter+');await expect(page.locator('.support-credit')).toContainText('749');
 await input.fill('749');await page.getByRole('button',{name:'Apply',exact:true}).click();await expect(page.getByRole('dialog')).not.toBeVisible();
 await input.fill('-1');await expect(page.getByRole('button',{name:'Apply',exact:true})).toBeDisabled();await input.fill('1.5');await expect(page.getByRole('button',{name:'Apply',exact:true})).toBeDisabled();
 await page.evaluate(()=>{localStorage.setItem('rmi.local-role-preview','2');window.dispatchEvent(new Event('local-role-changed'));});await expect(page.locator('.local-role-button')).toContainText('Supporter+');
 await page.reload();await page.getByRole('button',{name:'Support development',exact:true}).click();await expect(page.locator('.support-credit')).toContainText('749');await expect(page.getByRole('dialog')).not.toBeVisible();
 await page.getByText('Dev credit controls · simulation active',{exact:true}).click();await page.getByRole('button',{name:'Use real credit',exact:true}).click();await expect(page.locator('.support-credit')).toContainText('Verified Robux credit: 500');
 expect(await page.evaluate(()=>localStorage.getItem('rmi.local-support-credit'))).toBeNull();
 await page.getByLabel('Simulated Robux credit',{exact:true}).fill('0');await page.getByRole('button',{name:'Apply',exact:true}).click();await expect(page.locator('.support-credit')).toContainText('No tier yet');
 await page.getByRole('button',{name:'Use real credit',exact:true}).click();await expect(page.locator('.support-credit')).toContainText('500');await expect(page.getByRole('dialog')).not.toBeVisible();
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command.startsWith('support.')&&!['support.get','support.badges'].includes(c.command)))).toHaveLength(0);
});

test('dev reset clears real credit and simulation before the next cold verification',async({page})=>{
 test.skip(!existsSync('dist/local-supporter-preview.json'),'Development build only');
 await installWorkspace(page);await page.goto('/?robux-credit');await page.getByRole('button',{name:'Support development',exact:true}).click();
 await page.getByText('Dev credit controls',{exact:true}).click();await page.getByLabel('Simulated Robux credit',{exact:true}).fill('699');await page.getByRole('button',{name:'Apply',exact:true}).click();
 await page.evaluate(()=>{localStorage.setItem('rmi.local-role-preview','2');window.dispatchEvent(new Event('local-role-changed'));});
 await page.getByRole('button',{name:'Reset credit checks',exact:true}).click();
 await expect(page.locator('.support-credit')).toContainText('Verified Robux credit: 0');await expect(page.locator('.support-credit')).toContainText('No tier yet');
 await expect(page.locator('.purchase-check-status')).toContainText('purchase checks reset');await expect(page.getByRole('dialog')).not.toBeVisible();
 expect(await page.evaluate(()=>[localStorage.getItem('rmi.local-support-credit'),localStorage.getItem('rmi.local-role-preview')])).toEqual([null,'-2']);
 expect(await page.evaluate(()=>(window as any).testCalls.find((c:any)=>c.command==='support.devResetRoblox').payload)).toEqual({confirmed:true});
 await page.getByRole('button',{name:'Verify purchases',exact:true}).click();await expect(page.locator('.support-credit')).toContainText('700');
});
