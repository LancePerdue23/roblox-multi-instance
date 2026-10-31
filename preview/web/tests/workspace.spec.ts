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
   'app.state':{previewVersion:'fixture',helperVersion:'1.1.0',helperInstalled:true},
   'settings.get':settings,'host.state':{tray:false,shortcuts:false},'background.status':{enabled:false,owns:false,waiting:false},
   'instances.list':{available:true,unavailable:0,instances:[{id:'opaque-test-id',pid:123,title:'Roblox — Test window',cpuPercent:1.2,workingSet:104857600,hasWindow:true}]},
   'layouts.get':{monitors:[monitor],presets:[],shortcuts:{}},
   'layouts.preview':{planId:'plan-1',valid:true,notes:[],monitors:[monitor],placements:[{id:'opaque-test-id',x:8,y:8,width:1904,height:1064,slot:1}]},
   'activity.list':{available:true,records:[]},
   'system.health':{checks:[{id:'helper',name:'Installed helper',state:'Pass',message:'Helper is installed.'}]},
   'updates.check':{available:true,version:'1.2.0',notes:'Synthetic release fixture.',downloaded:false},
  };
  Object.defineProperty(window,'chrome',{value:{webview:{addEventListener:(_type:string,handler:typeof listeners[number])=>listeners.push(handler),postMessage:(message:{version:number;id:string;command:string;payload:Record<string,unknown>})=>{
   calls.push(message);if(message.command==='settings.save'&&message.payload.scheme)settings.scheme=String(message.payload.scheme);
   const data=message.command==='settings.save'?settings:responses[message.command]??{};
   setTimeout(()=>listeners.forEach(handler=>handler({data:{version:1,id:message.id,data}})),0);
  }}},configurable:true});
 });
 await page.goto('/');await expect(page.getByRole('button',{name:'↻ Refresh',exact:true})).toBeEnabled();
 await expect(page.getByText('Roblox — Test window',{exact:true})).toBeVisible();
});
test('renders all pages and keeps layout apply behind confirmation',async({page})=>{
 await page.getByLabel('Select Roblox — Test window').check();
 await page.getByRole('button',{name:'Arrange selected'}).click();
 await page.getByRole('button',{name:'Preview layout',exact:true}).click();
 await expect(page.getByRole('img',{name:'Preview of numbered window placements'})).toBeVisible();
 await page.getByRole('button',{name:'Apply layout…',exact:true}).click();
 await expect(page.getByRole('dialog')).toBeVisible();
 expect(await page.evaluate(()=>(window as unknown as {testCalls:{command:string}[]}).testCalls.filter(call=>call.command==='layouts.apply').length)).toBe(0);
 await page.getByRole('button',{name:'Cancel',exact:true}).click();
 await page.getByRole('button',{name:'Activity',exact:true}).click();await expect(page.getByText('No matching launch results')).toBeVisible();
 await page.getByRole('button',{name:'System',exact:true}).click();await expect(page.getByText('Helper is installed.')).toBeVisible();
 await page.getByRole('tab',{name:'Updates',exact:true}).click();await page.getByRole('button',{name:'Check for updates'}).click();await expect(page.getByText('1.2.0',{exact:true})).toBeVisible();
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
 for(const scheme of ['Dark','Light','WarmNeutral','FollowWindows']){
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
