import {test,expect,type Page} from '@playwright/test';
import {installWorkspace} from './fixtures/workspace';

test('presets are first, selection is a draft, and Apply activates live policy with honest compatibility',async({page},testInfo)=>{
 await installPerformance(page,false);
 await expect(page.getByRole('group',{name:'Optimization presets'})).toBeVisible();
 await expect(page.getByRole('heading',{name:'Manual process controls'})).toBeHidden();
 await expect(page.getByRole('heading',{name:'Per-process measurements'})).toBeHidden();
 await page.getByRole('button',{name:/^Maximum instances/}).click();
 await expect(page.getByLabel('Exact FPS cap',{exact:true})).toHaveValue('10');
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='performance.activate'))).toHaveLength(0);
 await page.getByRole('button',{name:'Apply optimizations',exact:true}).click();
 await expect(page.getByText('Policy activated. Live controls apply now; FPS and graphics apply on the next account launch.',{exact:true})).toBeVisible();
 const calls=await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='performance.activate'));
 expect(calls).toHaveLength(1);expect(calls[0].payload.selectedProfileId).toBe('many-accounts');
 const maximum=calls[0].payload.profiles.find((p:any)=>p.id==='many-accounts');
 expect(maximum).toMatchObject({fpsCap:10,ecoQos:'on',windowBehavior:'smallMinimized',disableGrass:true,graySky:true,textureQuality:'lowest'});
 await expect(page.getByRole('group',{name:'Optimization presets'})).toContainText('FPS');
 await expect(page.getByText(/FPS and graphics apply to the next account launch/)).toBeVisible();
 if(!process.env.CI)await page.screenshot({path:testInfo.outputPath('performance-presets.png'),fullPage:true});
});

test('fine tuning marks a preset Custom and preserves its draft after a stale Apply',async({page})=>{
 await installPerformance(page,false);await page.getByRole('button',{name:/^Balanced/}).focus();await page.keyboard.press('Enter');
 await page.getByLabel('Exact FPS cap',{exact:true}).fill('45');
 await expect(page.getByRole('button',{name:/^Balanced/})).toHaveAttribute('aria-pressed','false');
 await expect(page.getByText('Inactive · Custom',{exact:true})).toBeVisible();
 await page.evaluate(()=>(window as any).performanceStale=true);
 await page.getByRole('button',{name:'Apply optimizations',exact:true}).click();
 await expect(page.getByRole('alert')).toContainText('Profiles changed in another manager.');
 await expect(page.getByLabel('Exact FPS cap',{exact:true})).toHaveValue('45');
});
async function installPerformance(page:Page,expanded=true){
 await installWorkspace(page);
 await page.evaluate(()=>{
  const fixture=window as unknown as {chrome:{webview:{postMessage:(message:any)=>void}};testEmit:(data:unknown)=>void;testCalls:any[];performanceFailure:boolean;performanceDelay:number;performanceRecovery:boolean;performanceStale:boolean;performanceReplaced:boolean};
  const original=fixture.chrome.webview.postMessage;
  let generation=0;
  let controls={id:'123-lifetime-a',available:true,ownsLease:false,recoveryRequired:false,priority:'normal',ecoQos:false,ownsPriority:false,ownsEcoQos:false,code:'CURRENT',message:'Settings read.'};
  let profiles:any={enabled:false,focusBoost:true,revision:'fixture-1',version:1,available:true,selectedProfileId:'full-quality',profiles:[{id:'full-quality',name:'Full quality',description:'Preserve current quality.',cpuPriority:'unchanged',fpsCap:null,graphicsQuality:null},{id:'balanced',name:'Balanced',description:'Saved balanced preferences.',cpuPriority:'unchanged',fpsCap:null,graphicsQuality:null},{id:'many-accounts',name:'Many accounts',description:'Saved many-account preferences.',cpuPriority:'unchanged',fpsCap:null,graphicsQuality:null}],capabilities:[{key:'priority',label:'CPU priority',requested:'unchanged',effective:'Unchanged',source:'Saved profile',state:'Unsupported',applied:false,reason:'Native process changes are unavailable.'}]};
  fixture.chrome.webview.postMessage=(message:any)=>{
   if(!message.command.startsWith('performance.')){original(message);return;}
   fixture.testCalls.push(message);let data:unknown=profiles;let error:unknown;
   if(message.command==='performance.snapshot'){
    generation++;
    const metrics={cpuPercent:generation===1?null:12.5,workingSetBytes:104857600,privateWorkingSetBytes:null,privateCommitBytes:209715200,readBytesPerSecond:generation===1?null:2048,writeBytesPerSecond:generation===1?null:1024};
    data={generation,observedAt:new Date().toISOString(),available:true,unavailableProcesses:0,processes:[{...metrics,id:fixture.performanceReplaced?'456-lifetime-b':'123-lifetime-a',pid:fixture.performanceReplaced?456:123,startedAt:'2026-11-03T00:00:00Z',uptimeSeconds:65,status:'Available'}],totals:{...metrics,processCount:1}};
    if(fixture.performanceFailure)error={code:'METRICS_UNAVAILABLE',message:'Fixture collector denied access.'};
   }
   if(message.command==='performance.controls')data={...controls,id:message.payload.id,recoveryRequired:fixture.performanceRecovery??false};
   if(message.command==='performance.applyControls'){if(fixture.performanceFailure)data={...controls,code:'APPLY_FAILED',message:'Windows denied access to process settings.'};else{controls={...controls,priority:message.payload.priority==='unchanged'?controls.priority:message.payload.priority,ecoQos:message.payload.ecoQos==='unchanged'?controls.ecoQos:message.payload.ecoQos==='on',ownsPriority:controls.ownsPriority||message.payload.priority!=='unchanged',ownsEcoQos:controls.ownsEcoQos||message.payload.ecoQos!=='unchanged',ownsLease:true,code:'APPLIED',message:'Requested settings verified.'};data=controls;}}
   if(message.command==='performance.resetControls'){controls={...controls,priority:'normal',ecoQos:false,ownsPriority:false,ownsEcoQos:false,code:'RESET',message:'Owned settings restored; external changes preserved.'};data=controls;}
   if(message.command==='performance.recoverControls'){fixture.performanceRecovery=false;data={succeeded:true,pending:0,message:'Recovery checked.'};}
   if(message.command==='performance.optimizationState')data={enabled:profiles.enabled,applied:0,bypassed:0,unavailable:0,failed:0,instances:[],launchCompatibility:'Mixed-account isolation requires live validation.'};
   if(message.command==='performance.saveProfiles'||message.command==='performance.activate'){if(fixture.performanceStale){profiles={...profiles,revision:'fixture-2'};fixture.performanceStale=false;error={code:'STATE_CHANGED',message:'Profiles changed in another manager. Reload profiles before retrying.'};}else profiles={...profiles,...message.payload,enabled:message.command==='performance.activate'};data=profiles;}
   if(message.command==='performance.export')data={json:JSON.stringify({version:profiles.version,selectedProfileId:profiles.selectedProfileId,profiles:profiles.profiles})};
   if(message.command==='performance.previewImport')try{data={...profiles,...JSON.parse(message.payload.json)};}catch{error={code:'PROFILE_INVALID',message:'Invalid profile JSON.'};}
   if(message.command==='performance.import'){profiles={...profiles,...JSON.parse(message.payload.json)};data=profiles;}
   setTimeout(()=>fixture.testEmit({version:1,id:message.id,data,error}),fixture.performanceDelay??0);
  };
 });
 await page.getByRole('button',{name:'Performance',exact:true}).click();
 await expect(page.getByRole('heading',{name:'Client overview'})).toBeVisible();
 if(expanded)for(const summary of ['Detailed process measurements and charts','Manual process controls','Profile library and import / export'])await page.locator('summary').filter({hasText:summary}).first().click();
}
test('performance shows first samples honestly and saves preferences without applying controls',async({page},testInfo)=>{
 await installPerformance(page);
 await expect(page.getByRole('button',{name:'PID 123'})).toBeVisible();
 await expect(page.getByText('Waiting for sample').first()).toBeVisible();
 if(!process.env.CI)await page.screenshot({path:testInfo.outputPath('performance-overview.png'),fullPage:true});
 await page.getByRole('button',{name:'Pause monitoring'}).click();
 await page.getByRole('combobox',{name:'Performance profile',exact:true}).focus();await page.keyboard.press('ArrowDown');await page.keyboard.press('End');await page.keyboard.press('Enter');
 await page.getByLabel('Requested FPS cap (blank = unchanged)').fill('30');
 await page.getByRole('button',{name:'Save profile preferences'}).click();
 await expect(page.getByText('Preferences saved inactive. Automatic tuning stopped and app-owned live settings were restored where possible.')).toBeVisible();
 await page.getByText('Profile capabilities and limitations',{exact:true}).click();await expect(page.getByRole('cell',{name:'No',exact:true}).first()).toBeVisible();
 if(!process.env.CI)await page.screenshot({path:testInfo.outputPath('performance.png'),fullPage:true});
 const writes=await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='performance.saveProfiles'));
 expect(writes).toHaveLength(1);expect(writes[0].payload.expectedRevision).toBe('fixture-1');expect(writes[0].payload.selectedProfileId).toBe('many-accounts');expect(writes[0].payload.profiles[2].fpsCap).toBe(30);
 await page.emulateMedia({forcedColors:'active'});await expect(page.getByRole('heading',{name:'Per-process measurements'})).toBeVisible();
});
test('monitoring pauses on hidden and leaving page; collector errors retain explicitly stale data',async({page})=>{
 await installPerformance(page);await expect(page.getByRole('button',{name:'PID 123'})).toBeVisible();
 await page.evaluate(()=>{Object.defineProperty(document,'hidden',{configurable:true,value:true});document.dispatchEvent(new Event('visibilitychange'));});
 const count=await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='performance.snapshot').length);
 await page.waitForTimeout(2200);expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='performance.snapshot').length)).toBe(count);
 await page.evaluate(()=>{(window as any).performanceFailure=true;Object.defineProperty(document,'hidden',{configurable:true,value:false});document.dispatchEvent(new Event('visibilitychange'));});
 await expect(page.getByRole('alert')).toContainText('Last successful observations remain visible and may be stale.');await expect(page.getByRole('button',{name:'PID 123'})).toBeVisible();
 await page.getByRole('button',{name:'Instances',exact:true}).click();const after=await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='performance.snapshot').length);await page.waitForTimeout(2200);expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='performance.snapshot').length)).toBe(after);
});
test('import requires validated preview and explicit replacement',async({page})=>{
 await installPerformance(page);await page.getByText('Import / export performance profiles',{exact:true}).click();
 await page.getByLabel('Performance profile JSON').fill('invalid');await page.getByRole('button',{name:'Validate and preview import'}).click();await expect(page.getByRole('alert')).toContainText('Invalid profile JSON.');await expect(page.getByRole('button',{name:'Confirm profile replacement'})).toHaveCount(0);
 await page.getByRole('button',{name:'Prepare export'}).click();await page.getByRole('button',{name:'Validate and preview import'}).click();await expect(page.getByRole('heading',{name:'Import preview'})).toBeVisible();
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='performance.import'))).toHaveLength(0);
 await page.getByRole('button',{name:'Confirm profile replacement'}).click();await expect(page.getByText('Profiles imported inactive. App-owned live settings were restored where possible.')).toBeVisible();
});

test('profile validation errors survive successful monitoring refreshes',async({page})=>{
 await installPerformance(page);await page.getByText('Import / export performance profiles',{exact:true}).click();await page.getByLabel('Performance profile JSON').fill('invalid');await page.getByRole('button',{name:'Validate and preview import'}).click();await expect(page.getByRole('alert')).toContainText('Invalid profile JSON.');await page.waitForTimeout(2200);await expect(page.getByRole('alert')).toContainText('Invalid profile JSON.');
});

test('slow snapshots never overlap and table-only mode removes graph rendering',async({page})=>{
 await installPerformance(page);await expect(page.getByRole('button',{name:'PID 123'})).toBeVisible();await page.getByRole('button',{name:'Pause monitoring'}).click();
 await page.evaluate(()=>(window as any).performanceDelay=3000);const before=await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='performance.snapshot').length);
 await page.getByRole('button',{name:'Resume monitoring'}).click();await page.waitForTimeout(4200);expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='performance.snapshot').length)).toBe(before+1);
 await page.getByRole('button',{name:'Pause monitoring'}).click();await expect(page.locator('.performance-chart')).toHaveCount(2);await page.getByLabel('Show history charts').uncheck();await expect(page.locator('.performance-chart')).toHaveCount(0);await expect(page.getByRole('img',{name:'PID 123 working set recent history'})).toHaveCount(0);await expect(page.getByRole('button',{name:'PID 123'})).toBeVisible();
});

test('manual controls require confirmation and report real observed ownership separately from saved profiles',async({page},testInfo)=>{
 await installPerformance(page);await page.getByRole('button',{name:'Pause monitoring'}).click();await expect(page.getByRole('heading',{name:'Manual process controls'})).toBeVisible();
 await page.getByRole('combobox',{name:'Manual CPU priority',exact:true}).scrollIntoViewIfNeeded();await page.evaluate(()=>new Promise<void>(resolve=>requestAnimationFrame(()=>requestAnimationFrame(()=>resolve()))));await page.getByRole('combobox',{name:'Manual CPU priority',exact:true}).focus();await page.keyboard.press('ArrowDown');await page.getByRole('option',{name:'Below normal',exact:true}).click();
 await page.getByRole('combobox',{name:'Manual EcoQoS',exact:true}).focus();await page.keyboard.press('ArrowDown');await page.getByRole('option',{name:'On',exact:true}).click();
 await page.getByRole('button',{name:'Apply to selected process…'}).click();await expect(page.getByRole('dialog')).toContainText('PID 123');await page.getByRole('button',{name:'Cancel',exact:true}).click();
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='performance.applyControls'))).toHaveLength(0);
 await page.getByRole('button',{name:'Apply to selected process…'}).click();await page.getByRole('button',{name:'Apply process settings',exact:true}).click();await expect(page.getByText('Requested settings verified.',{exact:true})).toBeVisible();
 await expect(page.getByText('Owned by this manager',{exact:true})).toHaveCount(2);
 await expect(page.getByRole('combobox',{name:'Manual CPU priority',exact:true})).toBeDisabled();await expect(page.getByRole('combobox',{name:'Manual EcoQoS',exact:true})).toBeDisabled();await expect(page.getByText('Reset owned settings before changing that control again. You can still change a control this manager does not own.')).toBeVisible();
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='performance.applyControls').map((c:any)=>c.payload))).toEqual([{id:'123-lifetime-a',priority:'belowNormal',ecoQos:'on',confirmed:true}]);
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='performance.saveProfiles'))).toHaveLength(0);
 await page.getByRole('button',{name:'Reset owned settings…'}).click();await page.getByRole('button',{name:'Reset owned settings',exact:true}).click();await expect(page.getByText('Owned settings restored; external changes preserved.',{exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Reset owned settings…'})).toBeDisabled();await expect(page.getByRole('combobox',{name:'Manual CPU priority',exact:true})).toBeEnabled();await expect(page.getByRole('combobox',{name:'Manual EcoQoS',exact:true})).toBeEnabled();
 await page.getByRole('button',{name:'Recover interrupted settings…'}).click();await page.getByRole('button',{name:'Recover settings',exact:true}).click();await expect(page.getByText('Recovery checked. Pending changes: 0.',{exact:true})).toBeVisible();
 if(!process.env.CI)await page.screenshot({path:testInfo.outputPath('performance-manual-controls.png'),fullPage:true});
});
test('denied native changes remain visibly failed and never become owned',async({page})=>{
 await installPerformance(page);await page.getByRole('button',{name:'Pause monitoring'}).click();await page.evaluate(()=>(window as any).performanceFailure=true);
 await page.getByRole('combobox',{name:'Manual CPU priority',exact:true}).scrollIntoViewIfNeeded();await page.evaluate(()=>new Promise<void>(resolve=>requestAnimationFrame(()=>requestAnimationFrame(()=>resolve()))));await page.getByRole('combobox',{name:'Manual CPU priority',exact:true}).focus();await page.keyboard.press('ArrowDown');await page.getByRole('option',{name:'Idle',exact:true}).click();await page.getByRole('button',{name:'Apply to selected process…'}).click();await page.getByRole('button',{name:'Apply process settings',exact:true}).click();await expect(page.getByText('Windows denied access to process settings.',{exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Reset owned settings…'})).toBeDisabled();
});

test('interrupted ownership blocks new changes until explicit recovery succeeds',async({page})=>{
 await installPerformance(page);await page.getByRole('button',{name:'Pause monitoring'}).click();await page.evaluate(()=>(window as any).performanceRecovery=true);await page.getByRole('button',{name:'Refresh process settings'}).click();
 await expect(page.getByText('Interrupted changes need recovery before new settings can be applied.')).toBeVisible();await expect(page.getByRole('combobox',{name:'Manual CPU priority',exact:true})).toBeDisabled();
 await page.getByRole('button',{name:'Recover interrupted settings…'}).click();expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='performance.recoverControls'))).toHaveLength(0);await page.getByRole('button',{name:'Recover settings',exact:true}).click();
 await expect(page.getByText('Recovery checked. Pending changes: 0.',{exact:true})).toBeVisible();await expect(page.getByRole('combobox',{name:'Manual CPU priority',exact:true})).toBeEnabled();
});

test('stale profile writes offer explicit reload instead of silently overwriting',async({page})=>{
 await installPerformance(page);await page.getByRole('button',{name:'Pause monitoring'}).click();await page.getByLabel('Requested FPS cap (blank = unchanged)').fill('45');await page.evaluate(()=>(window as any).performanceStale=true);await page.getByRole('button',{name:'Save profile preferences'}).click();
 await expect(page.getByRole('alert')).toContainText('Profiles changed in another manager.');await expect(page.getByLabel('Requested FPS cap (blank = unchanged)')).toHaveValue('45');await page.getByRole('button',{name:'Reload profiles',exact:true}).click();await expect(page.getByLabel('Requested FPS cap (blank = unchanged)')).toHaveValue('');await expect(page.getByText('Profiles reloaded. Unsaved edits were discarded.',{exact:true})).toBeVisible();
});

test('renderer sends its readiness handshake once after mounting',async({page})=>{
 await installPerformance(page);await page.getByRole('button',{name:'Settings',exact:true}).click();await page.getByRole('button',{name:'Instances',exact:true}).click();
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='host.ready'))).toHaveLength(1);
});

test('a process disappearing during confirmation cannot retarget a replacement process',async({page})=>{
 await installPerformance(page);await page.getByRole('button',{name:'Pause monitoring'}).click();
 const priority=page.getByRole('combobox',{name:'Manual CPU priority',exact:true});await priority.scrollIntoViewIfNeeded();await expect(priority).toBeInViewport();
 // Settle the explicit preparatory scroll before opening a popup that intentionally closes on ancestor scroll.
 await page.evaluate(()=>new Promise<void>(resolve=>requestAnimationFrame(()=>resolve())));
 await priority.click();await expect(priority).toHaveAttribute('aria-expanded','true');await page.getByRole('option',{name:'Idle',exact:true}).click();
 await page.getByRole('button',{name:'Resume monitoring'}).click();await page.getByRole('button',{name:'Apply to selected process\u2026'}).click();await expect(page.getByRole('dialog')).toContainText('PID 123');
 await page.evaluate(()=>(window as any).performanceReplaced=true);await expect(page.getByRole('button',{name:'PID 456'})).toBeVisible();await expect(page.getByRole('dialog')).toHaveCount(0);
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='performance.applyControls'||c.command==='performance.resetControls'))).toHaveLength(0);await expect(page.getByRole('button',{name:'Apply to selected process\u2026'})).toBeDisabled();
});

 test('history plots align before and after samples arrive',async({page})=>{
 await installPerformance(page);
 const plots=page.locator('.performance-chart svg, .performance-chart-empty');
 await expect(plots).toHaveCount(2);
 async function aligned(){const boxes=await plots.evaluateAll(nodes=>nodes.map(node=>{const r=node.getBoundingClientRect();return {top:r.top,bottom:r.bottom,height:r.height};}));expect(Math.abs(boxes[0].top-boxes[1].top)).toBeLessThan(1);expect(Math.abs(boxes[0].bottom-boxes[1].bottom)).toBeLessThan(1);}
 await aligned();await expect(page.locator('.performance-chart svg')).toHaveCount(2);await aligned();
 });

test('charts share a timeline and label their measurement scales',async({page},testInfo)=>{
 await installPerformance(page);await expect(page.locator('.performance-chart svg')).toHaveCount(2);await page.getByRole('button',{name:'Pause monitoring'}).click();
 const series=page.locator('.performance-chart .chart-series');await expect(series).toHaveCount(2);
 expect(await series.evaluateAll(nodes=>nodes.map(node=>node.getAttribute('x1')))).toEqual(['0','0']);
 await expect(page.locator('.performance-axis').first()).toContainText('0.0%');await expect(page.locator('.performance-axis').nth(1)).toContainText('0.0 MB');
 await expect(page.locator('.chart-gridline')).toHaveCount(6);if(!process.env.CI)await page.locator('.performance-charts').screenshot({path:testInfo.outputPath('charts.png')});
});

test('saved profile priority uses explicit confirmation and preserves reset behavior',async({page})=>{
 await installPerformance(page);await page.getByRole('button',{name:'Pause monitoring'}).click();
 const choice=page.getByRole('combobox',{name:'Requested CPU priority',exact:true});await choice.scrollIntoViewIfNeeded();await choice.focus();await page.keyboard.press('ArrowDown');await page.getByRole('option',{name:'belowNormal',exact:true}).click();
 await expect(page.getByRole('button',{name:'Apply Full quality settings…'})).toBeDisabled();
 await page.getByRole('button',{name:'Save profile preferences',exact:true}).click();
 const apply=page.getByRole('button',{name:'Apply Full quality settings…'});await expect(apply).toBeEnabled();await apply.click();
 await expect(page.getByRole('dialog')).toContainText('priority belowNormal and EcoQoS unchanged to PID 123');
 await page.getByRole('button',{name:'Cancel',exact:true}).click();
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='performance.applyControls'))).toHaveLength(0);
 await apply.click();await page.getByRole('button',{name:'Apply process settings',exact:true}).click();
 await expect(apply).toBeDisabled();
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='performance.applyControls').map((c:any)=>c.payload))).toEqual([{id:'123-lifetime-a',priority:'belowNormal',ecoQos:'unchanged',confirmed:true}]);
 await page.getByRole('button',{name:'Reset owned settings…'}).click();await page.getByRole('button',{name:'Reset owned settings',exact:true}).click();await expect(apply).toBeEnabled();
});

test('saved EcoQoS applies through confirmation and restores owned settings',async({page})=>{
 await installPerformance(page);await page.getByRole('button',{name:'Pause monitoring'}).click();
 const choice=page.getByRole('combobox',{name:'Requested EcoQoS',exact:true});await choice.scrollIntoViewIfNeeded();await choice.focus();await page.keyboard.press('ArrowDown');await page.getByRole('option',{name:'On',exact:true}).click();
 await page.getByRole('button',{name:'Save profile preferences',exact:true}).click();
 const apply=page.getByRole('button',{name:'Apply Full quality settings…'});await apply.click();
 await expect(page.getByRole('dialog')).toContainText('priority unchanged and EcoQoS on to PID 123');
 await page.getByRole('button',{name:'Apply process settings',exact:true}).click();await expect(apply).toBeDisabled();
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='performance.applyControls').map((c:any)=>c.payload))).toEqual([{id:'123-lifetime-a',priority:'unchanged',ecoQos:'on',confirmed:true}]);
 await page.getByRole('button',{name:'Reset owned settings…'}).click();await page.getByRole('button',{name:'Reset owned settings',exact:true}).click();await expect(apply).toBeEnabled();
});

 test('FPS slider snaps to common stops and visual controls use labeled rows',async({page},testInfo)=>{
 await installPerformance(page,false);await page.getByRole('button',{name:/^Maximum instances/}).click();
 const slider=page.getByRole('slider',{name:'Preset FPS cap',exact:true});await slider.focus();await page.keyboard.press('ArrowRight');
 await expect(page.getByLabel('Exact FPS cap',{exact:true})).toHaveValue('15');await expect(slider).toHaveAttribute('aria-valuetext','15 FPS');
 await page.getByLabel('Exact FPS cap',{exact:true}).fill('37');await expect(slider).toHaveAttribute('aria-valuetext','37 FPS');
 const gray=page.getByRole('switch',{name:'Gray sky request',exact:true});await gray.click();await expect(gray).toHaveAttribute('aria-checked','false');
 await expect(page.getByText('Next launch',{exact:true})).toBeVisible();
 if(!process.env.CI)await page.locator('.performance-tuning').screenshot({path:testInfo.outputPath('fine-tune.png')});
 });
