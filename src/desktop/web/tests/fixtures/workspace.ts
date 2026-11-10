import {expect, type Page} from '@playwright/test';
import {readFile} from 'node:fs/promises';
import path from 'node:path';
export async function installWorkspace(page:Page){
 await page.route('https://preview-fixture.invalid/**',async route=>{
  const pathname=new URL(route.request().url()).pathname;
  const file=pathname==='/'?'index.html':pathname.slice(1);
  if(file.includes('..')){await route.abort();return;}
  const body=await readFile(path.join(process.cwd(),process.env.WORKSPACE_WEB_DIST??'dist',file));
  await route.fulfill({status:200,body,contentType:file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'text/html'});
 });
 await page.addInitScript(()=>{
  const listeners:((event:{data:unknown})=>void)[]=[];
  Object.defineProperty(window,'testEmit',{value:(data:unknown)=>listeners.forEach(handler=>handler({data}))});
  let updateChecks=0;
  const calls:{command:string;payload:Record<string,unknown>}[]=[];(window as unknown as {testCalls:typeof calls}).testCalls=calls;
  const settings={scheme:'Dark',shortcuts:{Manager:393293,Grid:393266},notifications:{showProgress:false,showCompletion:false},imported:false};
  const monitor={id:'m1',label:'Display 1',work:{x:0,y:0,width:1920,height:1080}};
  const telemetry={enabled:false,available:true,configured:false,queuedErrors:0,lastResult:null};
  const robloxPasses=[['2075890740',50],['2074990581',100],['2076244661',200],['2075560682',300],['2074516680',400],['2075716661',500],['2076124629',700],['2074156741',1000],['2074480739',1250],['2074492774',1500],['2074708731',2000],['2076184723',2500],['2075872725',3000],['2074474669',3500],['2074894638',4000],['2075140665',4500],['2076214698',5000],['2076136766',7500],['2075278696',10000],['2074162732',50000],['2074942659',100000]].map(([id,credit])=>({id:String(id),credit:Number(credit)}));
  const support={totalRobux:0,robloxTier:-1,usdTier:-1,robloxPasses,configured:location.search.includes('stripe-ready'),robloxConfigured:location.search.includes('roblox-ready'),supported:false,showBadge:false,pending:false,receiptAvailable:false,verifiedTier:-1,totalCents:0,recoveryAvailable:false,message:'Support is optional. All features are free and stay free.'};
  const accounts={available:true,launchAvailable:false,notice:'Automatic account launching is unavailable until verified.',accounts:[] as {id:string;nickname:string;username:string;status:string;group:string;notes:string;color:string;favorite:boolean;order:number;bypassOptimizations?:boolean;hasStoredCredentials?:boolean}[]};
  if(location.search.includes('accounts-demo'))accounts.accounts=[{id:'first-local',nickname:'First account',username:'FixtureOne',status:'Saved locally',group:'Main',notes:'First notes',color:'',favorite:true,order:0},{id:'second-local',nickname:'Second account',username:'FixtureTwo',status:'Saved locally',group:'',notes:'',color:'',favorite:false,order:1}];
  if(location.search.includes('launch-testing'))Object.assign(accounts,{launchAvailable:true,notice:'Saved-account launching is available for testing.'});
  if(location.search.includes('supporter-verified'))Object.assign(support,{supported:true,showBadge:true,receiptAvailable:true,verifiedTier:0,usdTier:0,message:'Thank you for supporting development. Your badge works offline.'});
  if(location.search.includes('cumulative-support'))Object.assign(support,{configured:true,supported:true,showBadge:true,receiptAvailable:true,verifiedTier:0,usdTier:0,totalCents:500,recoveryAvailable:true});
  if(location.search.includes('robux-credit'))Object.assign(support,{robloxConfigured:true,supported:true,showBadge:true,verifiedTier:0,robloxTier:0,totalRobux:500});
  const responses:Record<string,unknown>={
   'games.recent':[],'games.remove':[],'games.clear':[],
   'previews.configure':{supported:true,sources:[]},'previews.stop':{stopped:true},
   'app.state':{version:'fixture',helperVersion:'1.1.0',helperInstalled:true,readiness:'Installed — not tested'},
   'settings.get':settings,'host.state':{tray:false,shortcuts:false},'background.status':{enabled:false,owns:false,waiting:false},
   'telemetry.get':telemetry,'telemetry.set':telemetry,'telemetry.clear':telemetry,'telemetry.send':telemetry,
   'support.recovery':{code:'w'.repeat(43)},'support.refresh':support,'support.restore':support,'support.get':support,'support.devResetRoblox':support,'support.checkRoblox':support,'support.start':support,'support.claim':support,'support.roblox':support,'support.visibility':support,'support.remove':support,'support.forgetPending':support,
   'accounts.create':accounts,'accounts.credentials.open':accounts,'accounts.optimizationBypass':accounts,'accounts.state':accounts,'accounts.connect':accounts,'accounts.remove':accounts,'accounts.rename':accounts,'accounts.organize':accounts,'accounts.order':accounts,
   'instances.list':{available:true,unavailable:0,instances:[{id:'opaque-test-id',pid:123,title:'Roblox — Test window',cpuPercent:1.2,workingSet:104857600,hasWindow:true}]},
   'instances.stopAll':{outcome:'Succeeded',code:'STOP_ALL_RESULT',message:'Stop all: 2 stopped, 0 skipped, 0 failed, 0 unconfirmed.'},
   'layouts.get':{monitors:[monitor],presets:[],shortcuts:{}},
   'layouts.preview':{planId:'plan-1',valid:true,notes:[],monitors:[monitor],placements:[{id:'opaque-test-id',x:8,y:8,width:1904,height:1064,slot:1}]},
   'activity.list':{available:true,records:[]},
   'system.health':{browserIntegrationEnabled:false,checks:[{id:'helper',name:'Installed helper',state:'Pass',message:'Helper is installed.'}]},
   'updates.download':{available:true,version:'1.2.0',downloaded:true,canDownload:true},
   'updates.install':{outcome:'Warning',code:'SETUP_HANDOFF',message:'Setup accepted the update.'},
   'updates.check':{available:true,version:'1.2.0',notes:'Synthetic release fixture.',downloaded:false},
  };
  const game={placeId:'123456',universeId:'456',rootPlaceId:'123456',name:'Fixture Adventure',artwork:'data:image/svg+xml,'+encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="768" height="432" viewBox="0 0 768 432"><rect width="768" height="432" fill="#19334c"/><circle cx="580" cy="100" r="46" fill="#ffd77a"/><path d="M0 432 250 100 480 432M300 432 560 160 768 432" fill="#3c788c"/><text x="34" y="385" font-family="sans-serif" font-size="42" fill="white">Fixture Adventure</text></svg>'),artworkState:'Ready'};
  if(location.search.includes('games-demo'))responses['games.recent']=[{...game,accountIds:location.search.includes('legacy-game')?null:location.search.includes('missing-game-account')?['removed-local']:['second-local','first-local'],preset:''}];
  if(location.search.includes('grid-game'))(responses['games.recent'] as Record<string,unknown>[]).forEach(game=>game.arrangement='Grid');
  if(location.search.includes('readiness-cycle'))responses['system.health']={browserIntegrationEnabled:false,helperVersion:'1.1.0',readiness:'Installed — not tested',checks:[]};
  if(location.search.includes('launch-testing'))responses['accounts.launchBatch']={batch:{batchId:'fixture-batch',status:'Partial',canApplyLayout:false,items:[{accountId:'first-local',slot:1,status:'Unknown',code:'ACCOUNT_JOIN_UNCONFIRMED'}]},layoutStatus:'No automatic layout selected.'};
  if(location.search.includes('failure-outcomes')){responses['launch']={outcome:'Unknown',code:'LEGACY_LAUNCH_UNCONFIRMED',message:'Launch was unsuccessful or unconfirmed.'};responses['instances.focus']={outcome:'Warning',code:'FOCUS_DENIED',message:'Windows denied foreground focus.'};responses['instances.close']={outcome:'Succeeded',code:'CLOSE_REQUESTED',message:'Close requested.'};}
  if(location.search.includes('migration-warning'))responses['app.state']={version:'fixture',helperVersion:'1.1.0',helperInstalled:true,sharedNotice:'Could not import Preview appearance.json. The original is preserved; reset this setting explicitly to use defaults.'};
  if(location.search.includes('activity-rows'))responses['activity.list']={available:true,records:[1,2,3].map(id=>({id:'launch-'+id,source:'Desktop',outcome:'Confirmed',durationMs:id*100}))};
  if(location.search.includes('two-instances'))(responses['instances.list'] as {instances:unknown[]}).instances.push({id:'second-test-id',pid:456,title:'Roblox — Second window',username:'FixturePlayer',hasWindow:true});
  Object.defineProperty(window,'chrome',{value:{webview:{addEventListener:(_type:string,handler:typeof listeners[number])=>listeners.push(handler),postMessage:(message:{version:number;id:string;command:string;payload:Record<string,unknown>})=>{
   if(message.command==='layouts.apply')listeners.forEach(handler=>handler({data:{version:1,event:'host.busy',data:{busy:true}}}));
   if(location.search.includes('readiness-cycle')){
    const health=responses['system.health'] as {readiness:string;helperVersion:string};const app=responses['app.state'] as {readiness:string;helperVersion:string;helperInstalled:boolean};
    if(message.command==='system.testWorker'){health.readiness='Ready';app.readiness='Ready';}
    if(message.command==='system.maintenance'&&message.payload.action==='uninstall'){health.readiness='Not installed';health.helperVersion='not installed';app.readiness='Not installed';app.helperVersion='not installed';app.helperInstalled=false;responses['system.maintenance']={outcome:'Succeeded',code:'COMPLETED',message:'Uninstall completed.'};}
   }
   if(message.command==='support.checkRoblox'&&location.search.includes('roblox-owned'))Object.assign(support,{supported:true,showBadge:true,verifiedTier:0,robloxTier:0,totalRobux:300,message:'Purchases checked. Thank you!'});
   if(message.command==='accounts.create'&&!location.search.includes('creation-outcome=')){
    accounts.accounts.push({id:'created-local',nickname:'Created account',username:'Created001',status:'Saved locally',group:'',notes:'',color:'',favorite:false,order:accounts.accounts.length,hasStoredCredentials:true});
    listeners.forEach(handler=>handler({data:{version:1,event:'accounts.creationProgress',data:{completed:1,skipped:0,remaining:0,status:'Finished'}}}));
   }
   if(message.command==='accounts.create'&&location.search.includes('creation-outcome=')){
    const status=new URLSearchParams(location.search).get('creation-outcome');
    listeners.forEach(handler=>handler({data:{version:1,event:'accounts.creationProgress',data:{completed:0,skipped:status==='Skipped'?1:0,remaining:status==='Stopped'?1:0,status}}}));
   }
   calls.push(message);if(message.command==='system.integration')(responses['system.health'] as {browserIntegrationEnabled:boolean}).browserIntegrationEnabled=Boolean(message.payload.enabled);if(message.command==='settings.save'&&message.payload.scheme)settings.scheme=String(message.payload.scheme);
   if(message.command==='telemetry.set'){telemetry.enabled=Boolean(message.payload.enabled);if(!telemetry.enabled)telemetry.queuedErrors=0;}
   if(message.command==='telemetry.clear')telemetry.queuedErrors=0;
   if(location.search.includes('robux-credit')&&message.command==='support.checkRoblox')Object.assign(support,{totalRobux:700,robloxTier:1,verifiedTier:1,supported:true,showBadge:true});
   if(location.search.includes('cumulative-support')&&message.command==='support.claim')Object.assign(support,{pending:false,verifiedTier:1,usdTier:1,totalCents:700});
   if(location.search.includes('cumulative-support')&&message.command==='support.start')support.pending=true;
   if(message.command==='support.devResetRoblox')Object.assign(support,{totalRobux:0,robloxTier:-1,verifiedTier:support.usdTier,supported:support.usdTier>=0,showBadge:support.usdTier>=0,message:'Robux credit and purchase checks reset. Verify a pass to start fresh.'});
   if(message.command==='support.visibility')support.showBadge=Boolean(message.payload.visible);
   if(message.command==='support.remove'){support.supported=false;support.showBadge=false;}
   if(message.command==='accounts.remove')accounts.accounts=accounts.accounts.filter(a=>a.id!==message.payload.id);
   if(message.command==='accounts.optimizationBypass')accounts.accounts=accounts.accounts.map(a=>a.id===message.payload.id?{...a,bypassOptimizations:Boolean(message.payload.bypass)}:a);
   if(message.command==='accounts.rename')accounts.accounts=accounts.accounts.map(a=>a.id===message.payload.id?{...a,nickname:String(message.payload.nickname)}:a);
   if(message.command==='accounts.organize')accounts.accounts=accounts.accounts.map(a=>a.id===message.payload.id?{...a,group:String(message.payload.group),notes:String(message.payload.notes),color:String(message.payload.color),favorite:Boolean(message.payload.favorite)}:a);
   if(message.command==='accounts.order')accounts.accounts=(message.payload.ids as string[]).map((id,order)=>({...accounts.accounts.find(a=>a.id===id)!,order}));
   if(message.command==='accounts.target')responses['accounts.target']={placeId:'123456',jobId:message.payload.jobId||null,game:location.search.includes('games-demo')?game:null};
   if(message.command==='games.remove'||message.command==='games.clear')responses['games.recent']=[];
   const startupUpdate=message.command==='updates.check'&&updateChecks++===0&&!location.search.includes('update-available');
   const data=startupUpdate?{available:false,downloaded:false}:message.command==='layouts.preview'?{planId:'plan-1',valid:true,notes:[],monitors:[monitor],placements:(message.payload.ids as string[]).map((id,i,ids)=>({id,x:8+i*1904/ids.length,y:8,width:1904/ids.length,height:1064,slot:i+1}))}:message.command==='settings.save'?settings:responses[message.command]??{};
   setTimeout(()=>{listeners.forEach(handler=>handler({data:{version:1,id:message.id,data}}));if(message.command==='layouts.apply')listeners.forEach(handler=>handler({data:{version:1,event:'host.busy',data:{busy:false}}}));},message.command==='layouts.preview'?50:0);
  }}},configurable:true});
 });
 await page.goto('/');await expect(page.getByRole('button',{name:'↻ Refresh',exact:true})).toBeEnabled();
 await expect(page.getByText('Roblox — Test window',{exact:true})).toBeVisible();
}
