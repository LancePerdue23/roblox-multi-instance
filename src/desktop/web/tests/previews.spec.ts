import {test,expect} from '@playwright/test';
import {installWorkspace} from './fixtures/workspace';

test.beforeEach(async({page})=>installWorkspace(page));

test('minimized previews expose an explicit validated restore action',async({page})=>{
 await page.getByRole('button',{name:'Preview',exact:true}).click();
 await expect.poll(()=>page.evaluate(()=>(window as any).testCalls.some((call:any)=>call.command==='previews.configure'))).toBe(true);
 await page.evaluate(()=>(window as any).testEmit({version:1,event:'previews.frames',data:{supported:true,sources:[{id:'opaque-test-id',status:'Minimized',updated:null,url:null}]}}));
 await expect(page.getByRole('img')).toHaveCount(0);
 await page.getByRole('button',{name:'Resume preview for Roblox — Test window',exact:true}).click();
 expect(await page.evaluate(()=>(window as any).testCalls.some((call:any)=>call.command==='instances.focus'&&call.payload.id==='opaque-test-id'))).toBe(true);
});
test('Activity remains launch history after visiting the instance preview grid',async({page})=>{
 await page.goto('/?activity-rows');
 await page.getByRole('button',{name:'Preview',exact:true}).click();
 
 await page.getByRole('button',{name:'System',exact:true}).click();await page.getByRole('tab',{name:'Activity',exact:true}).click();
 await expect(page.getByRole('columnheader',{name:'Started',exact:true})).toBeVisible();
 await expect(page.getByRole('button',{name:'View details'})).toHaveCount(3);
 await expect(page.getByRole('group',{name:'Preview rate',exact:true})).toHaveCount(0);
 await page.getByRole('button',{name:'View details'}).first().click();
 await expect(page.getByRole('heading',{name:'Launch details',exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Instances',exact:true}).click();
 await expect(page.getByRole('group',{name:'Preview rate',exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Table',exact:true}).click();
 await page.getByRole('button',{name:'System',exact:true}).click();await page.getByRole('tab',{name:'Activity',exact:true}).click();
 await expect(page.getByRole('button',{name:'View details'})).toHaveCount(3);
});
test('preview starts automatically, preserves selection and clears pixels and names for privacy',async({page})=>{
 await page.getByLabel('Select Roblox — Test window').check();
 await page.getByRole('button',{name:'Preview',exact:true}).click();
 await expect(page.getByLabel('Select Roblox — Test window')).toBeChecked();
 
 
 await expect.poll(()=>page.evaluate(()=>(window as any).testCalls.some((c:any)=>c.command==='previews.configure'&&c.payload.ids.includes('opaque-test-id')))).toBe(true);
 await page.evaluate(()=>{(window as any).testEmit({version:1,event:'previews.frames',data:{supported:true,sources:[{id:'opaque-test-id',status:'Live',updated:'2026-11-04T12:00:00Z',url:'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7'}]}});});
 await expect(page.getByRole('img',{name:'Live preview of Roblox — Test window'})).toBeVisible();
 await page.getByRole('button',{name:'Hide previews and names'}).click();
 await expect(page.getByRole('img')).toHaveCount(0);
 await expect(page.getByLabel('Select Client 1')).toBeChecked();
 await expect(page.getByText('Roblox — Test window',{exact:true})).toHaveCount(0);
 await page.getByRole('button',{name:'Table',exact:true}).click();
 await expect(page.getByLabel('Select Roblox — Test window')).toBeChecked();
});
test('grid focuses through validated action and stops when leaving Instances',async({page})=>{
 await page.getByRole('button',{name:'Preview',exact:true}).click();
 
 await page.getByRole('button',{name:'Focus Roblox — Test window',exact:true}).click();
 expect(await page.evaluate(()=>(window as any).testCalls.some((c:any)=>c.command==='instances.focus'&&c.payload.id==='opaque-test-id'))).toBe(true);
 await page.getByRole('button',{name:'Layouts',exact:true}).click();
 await expect.poll(()=>page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command.startsWith('previews.')).at(-1)?.command)).toBe('previews.stop');
});
test('large lists form one responsive grid and preserve selections',async({page})=>{
 await page.evaluate(()=>{
  const view=window.chrome!.webview!,original=view.postMessage.bind(view);
  view.postMessage=(message:any)=>{
   if(message.command!=='instances.list'){original(message);return;}
   const instances=Array.from({length:30},(_,i)=>({id:'fixture-'+i,pid:1000+i,title:'Fixture client '+(i+1),hasWindow:true,cpuPercent:1,workingSet:10485760}));
   setTimeout(()=>(window as any).testEmit({version:1,id:message.id,data:{available:true,unavailable:0,instances}}),0);
  };
 });
 await page.getByRole('button',{name:'↻ Refresh',exact:true}).click();
 await page.getByRole('button',{name:'Preview',exact:true}).click();
 await expect(page.locator('.preview-card')).toHaveCount(30);
 await page.getByLabel('Select Fixture client 1',{exact:true}).check();
 
 await expect(page.locator('.preview-card')).toHaveCount(30);
 await expect(page.getByLabel('Select Fixture client 1',{exact:true})).toBeChecked();
 await expect(page.getByRole('group',{name:'Preview rate',exact:true})).toBeInViewport();
 await page.screenshot({path:'../../../.build/preview-grid.png'});
});

test('preview fit fills landscape cards or preserves the complete source without resizing clients',async({page})=>{
 await page.getByRole('button',{name:'Preview',exact:true}).click();
 await expect.poll(()=>page.evaluate(()=>(window as any).testCalls.some((c:any)=>c.command==='previews.configure'))).toBe(true);
 await page.evaluate(()=>{(window as any).testEmit({version:1,event:'previews.frames',data:{supported:true,sources:[{id:'opaque-test-id',status:'Live',url:'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7'}]}});});
 const img=page.getByRole('img',{name:'Live preview of Roblox — Test window'});await expect(img).toHaveCSS('object-fit','cover');await page.getByRole('button',{name:'Whole window',exact:true}).click();await expect(img).toHaveCSS('object-fit','contain');
 expect(await page.evaluate(()=>(window as any).testCalls.some((c:any)=>c.command==='layouts.apply'))).toBe(false);
});

test('preview rate selects caps directly',async({page})=>{
 await page.getByRole('button',{name:'Preview',exact:true}).click();
 for(const rate of [15,2,5]){
  const control=page.getByRole('button',{name:rate+' FPS',exact:true});
  await control.click();
  await expect(control).toHaveAttribute('aria-pressed','true');
  await expect(control).toBeVisible();
  await expect.poll(()=>page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='previews.configure').at(-1)?.payload.rate)).toBe(rate);
  await control.click();
 }
 await expect(page.getByRole('button',{name:'5 FPS',exact:true})).toHaveAttribute('aria-pressed','true');
});

test('preview rate persists when returning to the tab and reloading',async({page})=>{
 await page.getByRole('button',{name:'Preview',exact:true}).click();
 await page.getByRole('button',{name:'5 FPS',exact:true}).click();
 await page.getByRole('button',{name:'Table',exact:true}).click();
 await page.getByRole('button',{name:'Preview',exact:true}).click();
 await expect(page.getByRole('button',{name:'5 FPS',exact:true})).toHaveAttribute('aria-pressed','true');
 await page.reload();
 await page.getByRole('button',{name:'Preview',exact:true}).click();
 await expect(page.getByRole('button',{name:'5 FPS',exact:true})).toHaveAttribute('aria-pressed','true');
});

test('privacy and layout preferences survive navigation and reload without starting hidden captures',async({page})=>{
 await page.getByRole('button',{name:'Preview',exact:true}).click();
 await page.getByRole('button',{name:'Whole window',exact:true}).click();
 await page.getByRole('button',{name:'Compact',exact:true}).click();
 await page.getByRole('button',{name:'Hide previews and names',exact:true}).click();
 await page.getByRole('button',{name:'Table',exact:true}).click();
 await page.getByRole('button',{name:'Preview',exact:true}).click();
 await expect(page.getByRole('button',{name:'Hide previews and names'})).toHaveAttribute('aria-pressed','true');
 await page.reload();await page.getByRole('button',{name:'Preview',exact:true}).click();
 await expect(page.getByRole('button',{name:'Hide previews and names'})).toHaveAttribute('aria-pressed','true');
 await expect(page.getByRole('button',{name:'Whole window',exact:true})).toHaveAttribute('aria-pressed','true');
 await expect(page.getByRole('button',{name:'Compact',exact:true})).toHaveAttribute('aria-pressed','true');
 await expect(page.getByLabel('Select Client 1')).toBeVisible();
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='previews.configure'))).toHaveLength(0);
 await page.getByRole('button',{name:'Show previews and names'}).click();
 await expect.poll(()=>page.evaluate(()=>(window as any).testCalls.some((c:any)=>c.command==='previews.configure'))).toBe(true);
});

test('retry previews tears down failed captures and configures them again',async({page})=>{
 await page.getByRole('button',{name:'Preview',exact:true}).click();
 await expect.poll(()=>page.evaluate(()=>(window as any).testCalls.some((c:any)=>c.command==='previews.configure'&&c.payload.ids.length))).toBe(true);
 await page.evaluate(()=>{(window as any).testEmit({version:1,event:'previews.frames',data:{supported:true,sources:[{id:'opaque-test-id',status:'Unavailable',updated:null,url:null}]}});(window as any).testCalls.length=0;});
 await page.getByRole('button',{name:'Retry preview for Roblox — Test window',exact:true}).click();
 await expect.poll(()=>page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command.startsWith('previews.')).map((c:any)=>c.command))).toEqual(['previews.stop','previews.configure']);
});
