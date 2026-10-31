import {test,expect} from '@playwright/test';
import {readFile} from 'node:fs/promises';
import path from 'node:path';
test.beforeEach(async({page})=>{
 await page.route('https://preview-fixture.invalid/**',async route=>{
  const pathname=new URL(route.request().url()).pathname;
  const file=pathname==='/'?'index.html':pathname.slice(1);
  if(file.includes('..')){await route.abort();return;}
  const body=await readFile(path.join(process.cwd(),'dist',file));
  await route.fulfill({status:200,body,contentType:file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'text/html'});
 });
 await page.addInitScript(()=>{
  const listeners:((event:{data:unknown})=>void)[]=[];
  const calls:{command:string;payload:Record<string,unknown>}[]=[];(window as unknown as {testCalls:typeof calls}).testCalls=calls;
  const settings={scheme:'Dark',shortcuts:{Manager:393293,Grid:393266},notifications:{showProgress:false,showCompletion:false},imported:false};
  const monitor={id:'m1',label:'Display 1',work:{x:0,y:0,width:1920,height:1080}};
  const responses:Record<string,unknown>={
   'app.state':{version:'fixture',helperVersion:'1.1.0',helperInstalled:true},
   'settings.get':settings,'host.state':{tray:false,shortcuts:false},'background.status':{enabled:false,owns:false,waiting:false},
   'instances.list':{available:true,unavailable:0,instances:[{id:'opaque-test-id',pid:123,title:'Roblox — Test window',cpuPercent:1.2,workingSet:104857600,hasWindow:true}]},
   'layouts.get':{monitors:[monitor],presets:[],shortcuts:{}},
   'layouts.preview':{planId:'plan-1',valid:true,notes:[],monitors:[monitor],placements:[{id:'opaque-test-id',x:8,y:8,width:1904,height:1064,slot:1}]},
   'activity.list':{available:true,records:[]},
   'system.health':{browserIntegrationEnabled:false,checks:[{id:'helper',name:'Installed helper',state:'Pass',message:'Helper is installed.'}]},
   'updates.check':{available:true,version:'1.2.0',notes:'Synthetic release fixture.',downloaded:false},
  };
  if(location.search.includes('migration-warning'))responses['app.state']={version:'fixture',helperVersion:'1.1.0',helperInstalled:true,sharedNotice:'Could not import Preview appearance.json. The original is preserved; reset this setting explicitly to use defaults.'};
  if(location.search.includes('activity-rows'))responses['activity.list']={available:true,records:[1,2,3].map(id=>({id:'launch-'+id,source:'Desktop',outcome:'Confirmed',durationMs:id*100}))};
  if(location.search.includes('two-instances'))(responses['instances.list'] as {instances:unknown[]}).instances.push({id:'second-test-id',pid:456,title:'Roblox — Second window',username:'FixturePlayer',hasWindow:true});
  Object.defineProperty(window,'chrome',{value:{webview:{addEventListener:(_type:string,handler:typeof listeners[number])=>listeners.push(handler),postMessage:(message:{version:number;id:string;command:string;payload:Record<string,unknown>})=>{
   if(message.command==='layouts.preview')listeners.forEach(handler=>handler({data:{version:1,event:'host.busy',data:{busy:true}}}));
   calls.push(message);if(message.command==='system.integration')(responses['system.health'] as {browserIntegrationEnabled:boolean}).browserIntegrationEnabled=Boolean(message.payload.enabled);if(message.command==='settings.save'&&message.payload.scheme)settings.scheme=String(message.payload.scheme);
   const data=message.command==='layouts.preview'?{planId:'plan-1',valid:true,notes:[],monitors:[monitor],placements:(message.payload.ids as string[]).map((id,i,ids)=>({id,x:8+i*1904/ids.length,y:8,width:1904/ids.length,height:1064,slot:i+1}))}:message.command==='settings.save'?settings:responses[message.command]??{};
   setTimeout(()=>{listeners.forEach(handler=>handler({data:{version:1,id:message.id,data}}));if(message.command==='layouts.preview')listeners.forEach(handler=>handler({data:{version:1,event:'host.busy',data:{busy:false}}}));},message.command==='layouts.preview'?50:0);
  }}},configurable:true});
 });
 await page.goto('/');await expect(page.getByRole('button',{name:'↻ Refresh',exact:true})).toBeEnabled();
 await expect(page.getByText('Roblox — Test window',{exact:true})).toBeVisible();
});
test('renders all pages and keeps layout apply behind confirmation',async({page})=>{
 await page.getByLabel('Select Roblox — Test window').check();
 await page.getByRole('button',{name:'Arrange selected'}).click();
 await expect(page.getByRole('img',{name:'Preview of numbered window placements'})).toBeVisible();
 await page.getByRole('button',{name:'Apply layout…',exact:true}).click();
 await expect(page.getByRole('dialog')).toBeVisible();
 expect(await page.evaluate(()=>(window as unknown as {testCalls:{command:string}[]}).testCalls.filter(call=>call.command==='layouts.apply').length)).toBe(0);
 await page.getByRole('button',{name:'Cancel',exact:true}).click();
 await page.getByRole('button',{name:'Activity',exact:true}).click();await expect(page.getByText('No matching launch results')).toBeVisible();
 await page.getByRole('button',{name:'System',exact:true}).click();await expect(page.getByRole('tab').first()).toHaveText('Version & updates');await expect(page.getByRole('tab',{name:'Version & updates',exact:true})).toHaveAttribute('aria-selected','true');await expect(page.getByText('Installed helper',{exact:true})).toBeVisible();await page.getByRole('tab',{name:'Health',exact:true}).click();await expect(page.getByText('Helper is installed.')).toBeVisible();
 await page.getByRole('tab',{name:'Version & updates',exact:true}).click();await page.getByRole('button',{name:'Check for updates'}).click();await expect(page.getByText('1.2.0',{exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Settings',exact:true}).click();await expect(page.getByRole('heading',{name:'Color scheme'})).toBeVisible();
 await page.screenshot({path:'../../.build/preview-ui-results/settings-dark.png',fullPage:true});
});
test('saves isolated themes and requires confirmation for shared notification changes',async({page})=>{
 await page.getByRole('button',{name:'Settings',exact:true}).click();
 await page.getByRole('button',{name:'Light Use this scheme'}).click();await expect(page.locator('html')).toHaveAttribute('data-theme','Light');
 await page.getByRole('tab',{name:'Notifications',exact:true}).click();await page.getByLabel('Show launch progress').check();await page.getByRole('button',{name:'Save notifications…'}).click();
 await expect(page.getByRole('dialog')).toContainText('all launches');await page.getByRole('button',{name:'Save shared notifications',exact:true}).click();
 await expect.poll(()=>page.evaluate(()=>(window as unknown as {testCalls:{command:string;payload:{confirmed?:boolean}}[]}).testCalls.find(call=>call.command==='settings.notifications')?.payload.confirmed)).toBe(true);
});
test('stays usable at narrow width and does not overflow the desktop',async({page})=>{
 await page.setViewportSize({width:850,height:700});await page.screenshot({path:'../../.build/preview-ui-results/instances-narrow.png',fullPage:true});
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
 await page.getByRole('button',{name:'Clean up background clients…'}).click();await expect(page.getByRole('dialog')).toBeVisible();await page.keyboard.press('Escape');await expect(page.getByRole('dialog')).not.toBeVisible();
});


test('dropdown popups follow every theme and support keyboard selection',async({page})=>{
 for(const scheme of ['Dark','Light','WarmNeutral','FollowWindows','Purple','Ocean','Forest','Rose']){
  await page.getByRole('button',{name:'Settings',exact:true}).click();
  await page.getByRole('tab',{name:'Appearance',exact:true}).click();
  const label=scheme==='WarmNeutral'?'Warm neutral':scheme==='FollowWindows'?'Follow Windows':scheme;
  const choice=page.getByRole('button',{name:new RegExp('^'+label+' (Selected|Use this scheme)$')});
  if(await choice.getAttribute('aria-pressed')!=='true')await choice.click();
  await expect(page.locator('html')).toHaveAttribute('data-theme',scheme);
  await page.getByRole('button',{name:'Layouts',exact:true}).click();
  const combo=page.getByRole('combobox',{name:'Arrangement',exact:true});
  await expect(combo).toBeEnabled();await combo.click();
  const popup=page.getByRole('listbox',{name:'Arrangement',exact:true});await expect(popup).toBeVisible();
  expect(await popup.evaluate(element=>{const reference=document.createElement('div');reference.style.background='var(--surface)';document.body.append(reference);const expected=getComputedStyle(reference).backgroundColor;reference.remove();return getComputedStyle(element).backgroundColor===expected;})).toBe(true);
  await page.screenshot({path:`../../.build/preview-ui-results/dropdown-${scheme}.png`,fullPage:true});
  await combo.press('End');await combo.press('Enter');await expect(combo).toHaveText('Recover off-screen⌄');await expect(popup).not.toBeVisible();
  await combo.press('ArrowDown');await combo.press('Home');await combo.press('Enter');await expect(combo).toHaveText('Grid⌄');
  await combo.click();await combo.press('Escape');await expect(combo).toBeFocused();
 }
 await page.getByRole('button',{name:'Activity',exact:true}).click();
 const filter=page.getByRole('combobox',{name:'Filter launch activity'});await filter.focus();await filter.press('n');await expect(filter).toHaveText('Needs attention⌄');
});

test('audits all page surfaces, light system mode, forced colors and dialog contrast',async({page})=>{
 await page.emulateMedia({colorScheme:'light'});
 await page.getByRole('button',{name:'Settings',exact:true}).click();await page.getByRole('button',{name:'Follow Windows Use this scheme'}).click();
 expect(await page.locator('html').evaluate(el=>getComputedStyle(el).getPropertyValue('color-scheme'))).toBe('light');
 for(const section of ['Instances','Layouts','Activity','System','Settings']){
  await page.getByRole('button',{name:section,exact:true}).click();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  await page.screenshot({path:`../../.build/preview-ui-results/audit-${section}.png`,fullPage:true});
 }
 const tab=page.getByRole('tab',{name:'Appearance'});await tab.focus();await tab.press('ArrowRight');await expect(page.getByRole('tab',{name:'Notifications'})).toHaveAttribute('aria-selected','true');
 await page.emulateMedia({forcedColors:'active'});
 await page.getByRole('button',{name:'Layouts',exact:true}).click();await page.getByRole('combobox',{name:'Arrangement',exact:true}).click();await expect(page.getByRole('listbox')).toBeVisible();
 await page.screenshot({path:'../../.build/preview-ui-results/dropdown-high-contrast.png',fullPage:true});
 await page.keyboard.press('Escape');
 await page.getByRole('button',{name:'Instances',exact:true}).click();await page.getByRole('button',{name:'Clean up background clients…'}).click();await expect(page.getByRole('dialog')).toBeVisible();await page.screenshot({path:'../../.build/preview-ui-results/dialog-high-contrast.png',fullPage:true});await page.keyboard.press('Escape');
});

 test('layouts select all and preview automatically after changes',async({page})=>{
  await page.getByRole('button',{name:'Layouts',exact:true}).click();
  await page.getByRole('button',{name:'Select all',exact:true}).click();
  await expect(page.getByRole('img',{name:'Preview of numbered window placements'})).toBeVisible();
  const previews=()=>page.evaluate(()=>(window as unknown as {testCalls:{command:string;payload:Record<string,unknown>}[]}).testCalls.filter(c=>c.command==='layouts.preview'));
  expect((await previews()).at(-1)?.payload.ids).toEqual(['opaque-test-id']);
  await page.waitForTimeout(800);
  expect(await previews()).toHaveLength(1);
  await expect(page.getByRole('img',{name:'Preview of numbered window placements'})).toBeVisible();
  await expect(page.getByText('Updating preview…',{exact:true})).toHaveCount(0);
  await page.getByRole('combobox',{name:'Arrangement',exact:true}).click();
  await page.getByRole('option',{name:'Side-by-side',exact:true}).click();
  await expect.poll(async()=>(await previews()).at(-1)?.payload.kind).toBe('SideBySide');
  await expect(page.getByRole('button',{name:'Apply layout…',exact:true})).toBeEnabled();
  await page.waitForTimeout(800);expect(await previews()).toHaveLength(2);
  await page.getByRole('button',{name:'Clear selection',exact:true}).click();
  await expect(page.getByRole('button',{name:'Apply layout…',exact:true})).toBeDisabled();
  await expect(page.getByRole('img',{name:'Preview of numbered window placements'})).toHaveCount(0);
 });

test('health refresh never flashes download cancellation controls',async({page})=>{
 await page.evaluate(()=>{
  const bridge=window.chrome!.webview!;
  const send=bridge.postMessage.bind(bridge);
  bridge.postMessage=(value:unknown)=>{
   const message=value as {command:string};
   if(message.command==='system.health'||message.command==='updates.download')setTimeout(()=>send(value),600);
   else send(value);
  };
 });
 await page.getByRole('button',{name:'System',exact:true}).click();
 await page.getByRole('tab',{name:'Version & updates',exact:true}).click();
 await expect(page.getByRole('status').last()).toContainText('Check health');
 await expect(page.getByRole('button',{name:'Cancel operation',exact:true})).toHaveCount(0);
 await expect(page.getByRole('button',{name:'Check for updates'})).toBeEnabled();
 await page.getByRole('button',{name:'Check for updates'}).click();
 await expect(page.getByRole('button',{name:'Download update',exact:true})).toBeEnabled();
 await page.getByRole('button',{name:'Download update',exact:true}).click();
 await expect(page.getByRole('button',{name:'Cancel operation',exact:true})).toBeVisible();
 await expect(page.getByRole('button',{name:'Cancel operation',exact:true})).toHaveCount(0);
});


test('browser integration toggle reflects confirmed state and supports cancel',async({page})=>{
 const toggle=page.getByRole('switch',{name:'Browser integration',exact:true});
 await expect(toggle).toHaveAttribute('aria-checked','false');
 await toggle.click();await page.getByRole('button',{name:'Cancel',exact:true}).click();
 await expect(toggle).toHaveAttribute('aria-checked','false');
 expect(await page.evaluate(()=>(window as unknown as {testCalls:{command:string}[]}).testCalls.some(c=>c.command==='system.integration'))).toBe(false);
 await toggle.click();await page.getByRole('button',{name:'Enable browser integration',exact:true}).click();
 await expect(toggle).toHaveAttribute('aria-checked','true');
 await toggle.focus();await page.keyboard.press('Space');await page.getByRole('button',{name:'Disable browser integration',exact:true}).click();
 await expect(toggle).toHaveAttribute('aria-checked','false');
});


test('reordering instances moves their numbers in the placement preview',async({page})=>{
 await page.goto('/?two-instances');
 await page.getByRole('button',{name:'Layouts',exact:true}).click();
 await page.getByRole('button',{name:'Select all',exact:true}).click();
 const labels=page.locator('.layout-svg .slot-label');
 await expect(labels).toHaveText(['1','2']);
 const firstX=await labels.nth(0).getAttribute('x');
 await page.getByRole('button',{name:'Move Roblox — Test window down',exact:true}).click();
 await expect(labels).toHaveText(['2','1']);
 expect(await labels.nth(0).getAttribute('x')).toBe(firstX);
 await expect(page.locator('.layout-window').first()).toContainText('Instance 2 · FixturePlayer');
 await expect(page.locator('.layout-window').first()).toContainText('Position 1');
 await expect(page.locator('.layout-window').nth(1)).toContainText('Instance 1 · Roblox — Test window');
 await expect(page.locator('.layout-window').nth(1)).toContainText('Position 2');
 await page.getByRole('button',{name:'Move Roblox — Test window up',exact:true}).click();
 await expect(labels).toHaveText(['1','2']);
 await expect(page.locator('.layout-window').first()).toContainText('Instance 1 · Roblox — Test window');
 await expect(page.locator('.layout-window').first()).toContainText('Position 1');
 await page.getByRole('button',{name:'Move Roblox — Test window down',exact:true}).click();
 await expect(labels).toHaveText(['2','1']);
 const calls=await page.evaluate(()=>(window as unknown as {testCalls:{command:string;payload:{ids?:string[]}}[]}).testCalls.filter(c=>c.command==='layouts.preview'));
 expect(calls.at(-1)?.payload.ids).toEqual(['second-test-id','opaque-test-id']);
});


test('instances keep compact account labels and layouts show names without a prefix',async({page})=>{
 await page.goto('/?two-instances');
 await expect(page.getByRole('columnheader',{name:'Username',exact:true})).toHaveCount(0);
 await expect(page.getByRole('row').filter({hasText:'Roblox — Second window'}).locator('.window-name strong')).toHaveText('FixturePlayer');
 await expect(page.getByRole('row').filter({hasText:'Roblox — Test window'}).locator('.window-name strong')).toHaveText('Roblox — Test window');
 await page.getByRole('button',{name:'Layouts',exact:true}).click();
 await expect(page.locator('.layout-window').filter({hasText:'Roblox — Second window'})).toContainText('Instance 2 · FixturePlayer · Roblox — Second window');await expect(page.locator('.layout-window')).not.toContainText(['@FixturePlayer']);
});


test('launch details expand immediately below the selected activity row',async({page})=>{
 await page.goto('/?activity-rows');
 await page.getByRole('button',{name:'Activity',exact:true}).click();
 const buttons=page.getByRole('button',{name:'View details',exact:true});
 await expect(buttons).toHaveCount(3);
 for(const index of [0,1,2]){
  await buttons.nth(index).click();
  const detail=page.locator('tr.activity-detail-row');
  await expect(detail).toHaveCount(1);
  await expect(detail).toContainText('launch-'+(index+1));
  expect(await detail.evaluate(row=>Array.from(row.parentElement!.children).indexOf(row))).toBe(index+1);
  await expect(buttons.nth(index)).toHaveAttribute('aria-expanded','true');
 }
 await page.getByRole('button',{name:'Close details',exact:true}).click();
 await expect(page.locator('tr.activity-detail-row')).toHaveCount(0);
});

test('shows a recoverable preference import warning',async({page})=>{
 await page.goto('/?migration-warning');
 await expect(page.getByRole('alert')).toContainText('The original is preserved');
 await expect(page.getByRole('alert')).toContainText('Open Settings');
});
