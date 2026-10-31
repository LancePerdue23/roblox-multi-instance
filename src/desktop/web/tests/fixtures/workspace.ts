import {expect, type Page} from '@playwright/test';
import {readFile} from 'node:fs/promises';
import path from 'node:path';
export async function installWorkspace(page:Page){
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
   'app.state':{version:'fixture',helperVersion:'1.1.0',helperInstalled:true,readiness:'Installed — not tested'},
   'settings.get':settings,'host.state':{tray:false,shortcuts:false},'background.status':{enabled:false,owns:false,waiting:false},
   'instances.list':{available:true,unavailable:0,instances:[{id:'opaque-test-id',pid:123,title:'Roblox — Test window',cpuPercent:1.2,workingSet:104857600,hasWindow:true}]},
   'layouts.get':{monitors:[monitor],presets:[],shortcuts:{}},
   'layouts.preview':{planId:'plan-1',valid:true,notes:[],monitors:[monitor],placements:[{id:'opaque-test-id',x:8,y:8,width:1904,height:1064,slot:1}]},
   'activity.list':{available:true,records:[]},
   'system.health':{browserIntegrationEnabled:false,checks:[{id:'helper',name:'Installed helper',state:'Pass',message:'Helper is installed.'}]},
   'updates.check':{available:true,version:'1.2.0',notes:'Synthetic release fixture.',downloaded:false},
  };
  if(location.search.includes('readiness-cycle'))responses['system.health']={browserIntegrationEnabled:false,helperVersion:'1.1.0',readiness:'Installed — not tested',checks:[]};
  if(location.search.includes('failure-outcomes')){responses['launch']={outcome:'Unknown',code:'LEGACY_LAUNCH_UNCONFIRMED',message:'Launch was unsuccessful or unconfirmed.'};responses['instances.focus']={outcome:'Warning',code:'FOCUS_DENIED',message:'Windows denied foreground focus.'};responses['instances.close']={outcome:'Succeeded',code:'CLOSE_REQUESTED',message:'Close requested.'};}
  if(location.search.includes('migration-warning'))responses['app.state']={version:'fixture',helperVersion:'1.1.0',helperInstalled:true,sharedNotice:'Could not import Preview appearance.json. The original is preserved; reset this setting explicitly to use defaults.'};
  if(location.search.includes('activity-rows'))responses['activity.list']={available:true,records:[1,2,3].map(id=>({id:'launch-'+id,source:'Desktop',outcome:'Confirmed',durationMs:id*100}))};
  if(location.search.includes('two-instances'))(responses['instances.list'] as {instances:unknown[]}).instances.push({id:'second-test-id',pid:456,title:'Roblox — Second window',username:'FixturePlayer',hasWindow:true});
  Object.defineProperty(window,'chrome',{value:{webview:{addEventListener:(_type:string,handler:typeof listeners[number])=>listeners.push(handler),postMessage:(message:{version:number;id:string;command:string;payload:Record<string,unknown>})=>{
   if(message.command==='layouts.preview')listeners.forEach(handler=>handler({data:{version:1,event:'host.busy',data:{busy:true}}}));
   if(location.search.includes('readiness-cycle')){
    const health=responses['system.health'] as {readiness:string;helperVersion:string};const app=responses['app.state'] as {readiness:string;helperVersion:string;helperInstalled:boolean};
    if(message.command==='system.testWorker'){health.readiness='Ready';app.readiness='Ready';}
    if(message.command==='system.maintenance'&&message.payload.action==='uninstall'){health.readiness='Not installed';health.helperVersion='not installed';app.readiness='Not installed';app.helperVersion='not installed';app.helperInstalled=false;responses['system.maintenance']={outcome:'Succeeded',code:'COMPLETED',message:'Uninstall completed.'};}
   }
   calls.push(message);if(message.command==='system.integration')(responses['system.health'] as {browserIntegrationEnabled:boolean}).browserIntegrationEnabled=Boolean(message.payload.enabled);if(message.command==='settings.save'&&message.payload.scheme)settings.scheme=String(message.payload.scheme);
   const data=message.command==='layouts.preview'?{planId:'plan-1',valid:true,notes:[],monitors:[monitor],placements:(message.payload.ids as string[]).map((id,i,ids)=>({id,x:8+i*1904/ids.length,y:8,width:1904/ids.length,height:1064,slot:i+1}))}:message.command==='settings.save'?settings:responses[message.command]??{};
   setTimeout(()=>{listeners.forEach(handler=>handler({data:{version:1,id:message.id,data}}));if(message.command==='layouts.preview')listeners.forEach(handler=>handler({data:{version:1,event:'host.busy',data:{busy:false}}}));},message.command==='layouts.preview'?50:0);
  }}},configurable:true});
 });
 await page.goto('/');await expect(page.getByRole('button',{name:'↻ Refresh',exact:true})).toBeEnabled();
 await expect(page.getByText('Roblox — Test window',{exact:true})).toBeVisible();
}