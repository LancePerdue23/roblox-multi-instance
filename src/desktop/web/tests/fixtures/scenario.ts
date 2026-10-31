import {expect, type Page} from '@playwright/test';
import {readFile} from 'node:fs/promises';
import path from 'node:path';

export interface ScenarioOptions {
  instances?:number; activity?:number; helperInstalled?:boolean;
  delayMs?:number; failures?:Record<string,number>; corrupt?:boolean;
  historyUnavailable?:boolean; instancesUnavailable?:boolean;
}

export async function installScenario(page:Page, options:ScenarioOptions={}) {
  await page.route('https://audit-fixture.invalid/**', async route=>{
    const file=new URL(route.request().url()).pathname.slice(1)||'index.html';
    if(file.includes('..'))return route.abort();
    await route.fulfill({body:await readFile(path.join(process.env.AUDIT_WEB_ROOT??path.join(process.cwd(),'dist'),file)),contentType:file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'text/html'});
  });
  await page.addInitScript((options)=>{
    const calls:{id:string;command:string;payload:Record<string,unknown>;durationMs?:number;outcome?:string}[]=[];
    const started=new Map<string,number>();
    const listeners:((event:{data:unknown})=>void)[]=[];
    const failures={...options.failures};
    const settings={scheme:sessionStorage.getItem('audit-scheme')??'Dark',available:!options.corrupt,shortcuts:{Manager:393293,Grid:393266},notifications:{showProgress:false,showCompletion:false,available:true},imported:false};
    let instances=Array.from({length:options.instances??1},(_,i)=>({id:`fixture-${i}`,pid:i+100,title:`Synthetic window ${i+1}`,username:null,cpuPercent:1.2,workingSet:104857600,started:'2026-10-31T12:00:00Z',hasWindow:true}));
    let activity=Array.from({length:options.activity??0},(_,i)=>({id:`launch-${i}`,startedUtc:'2026-10-31T12:00:00Z',endedUtc:null,durationMs:i*10,source:'Desktop',outcome:i%2?'Warning':'Confirmed',code:'Fixture',message:'Synthetic result'}));
    const monitors=[{id:'m1',label:'Display 1',work:{x:0,y:0,width:1920,height:1080},dpi:96,primary:true},{id:'m2',label:'Display 2',work:{x:1920,y:0,width:1920,height:1080},dpi:144,primary:false}];
    const presets:{name:string;slots:unknown[]}[]=[];
    let preview=0,planIds:string[]=[],tray=false,integration=false;
    let downloaded=false;
    const emit=(data:unknown)=>listeners.forEach(handler=>handler({data}));
    const execute=(command:string,p:Record<string,unknown>):unknown=>{
      switch(command){
        case 'app.state':return {version:'audit',helperVersion:'fixture',helperInstalled:options.helperInstalled??true,readiness:options.helperInstalled===false?'Not installed':'Installed — not tested',sharedNotice:options.corrupt?'Synthetic corrupt preferences preserved.':''};
        case 'settings.get':return settings;
        case 'settings.save':if(p.scheme){settings.scheme=String(p.scheme);sessionStorage.setItem('audit-scheme',settings.scheme);}if(p.shortcuts)settings.shortcuts=p.shortcuts as typeof settings.shortcuts;return settings;
        case 'settings.notifications':Object.assign(settings.notifications,p);return settings;
        case 'settings.resetNotifications':settings.notifications.showProgress=false;settings.notifications.showCompletion=false;return settings;
        case 'settings.reset':settings.scheme='Dark';settings.available=true;presets.splice(0);sessionStorage.removeItem('audit-scheme');return settings;
        case 'settings.import':settings.imported=true;return settings;
        case 'host.state':return {tray,shortcuts:false};
        case 'host.tray':tray=!!p.enabled;return {enabled:tray};
        case 'host.shortcuts':return {enabled:false,conflicts:['Synthetic shortcut conflict']};
        case 'background.status':case 'background.set':return {enabled:false,owns:false,waiting:false};
        case 'instances.stopLabels':return {stopped:true};
        case 'instances.list':return {available:!options.instancesUnavailable,unavailable:0,instances};
        case 'instances.focus':return {result:'Focused'};
        case 'instances.close':instances=instances.filter(row=>row.id!==p.id);return {result:'CloseRequested'};
        case 'instances.cleanup':return {results:[]};
        case 'launch':if(options.helperInstalled===false)throw Error('Installed helper is missing.');return {exitCode:0};
        case 'layouts.get':return {monitors,presets,shortcuts:settings.shortcuts};
        case 'layouts.preview':planIds=p.ids as string[];return {planId:`plan-${++preview}`,valid:true,notes:[],monitors,placements:planIds.map((id,i)=>({id,x:8+i*100,y:8,width:100,height:300,slot:i+1}))};
        case 'layouts.apply':return {results:planIds.map((id,i)=>({id,message:i===1?'Window changed; skipped.':'Placed.'}))};
        case 'layouts.savePreset':presets.push({name:String(p.name),slots:[]});return {};
        case 'layouts.renamePreset':{const preset=presets.find(row=>row.name===p.name);if(preset)preset.name=String(p.newName);return {};}
        case 'layouts.deletePreset':{const i=presets.findIndex(row=>row.name===p.name);if(i>=0)presets.splice(i,1);return {};}
        case 'activity.list':return {available:!options.historyUnavailable,records:activity};
        case 'activity.clear':activity=[];return {};
        case 'system.health':return {overall:'Pass',helperVersion:'fixture',browserIntegrationEnabled:integration,checks:[{id:'helper',name:'Installed helper',state:'Pass',message:'Synthetic health result',action:'',code:'Fixture'}]};
        case 'system.integration':integration=!!p.enabled;return {};
        case 'system.testWorker':return {state:'Pass'};
        case 'system.maintenance':return {exitCode:0};
        case 'system.openLogs':return {};
        case 'report.export':return {canceled:true};
        case 'updates.check':return {available:true,version:'9.9.9',title:'Synthetic update',notes:'Synthetic notes '.repeat(100),canDownload:true,downloaded,helperVersion:'fixture'};
        case 'updates.download':downloaded=true;return {downloaded:true,available:true,version:'9.9.9',canDownload:true};
        case 'updates.install':return {exitCode:0};
        default:throw Error('Unsupported fixture command: '+command);
      }
    };
    const pending=new Map<string,ReturnType<typeof setTimeout>>();
    Object.defineProperty(window,'audit',{value:{calls,fail:(command:string,count=1)=>{failures[command]=count;}}});
    Object.defineProperty(window,'chrome',{configurable:true,value:{webview:{addEventListener:(_type:string,handler:typeof listeners[number])=>listeners.push(handler),postMessage:(message:{id:string;command:string;payload:Record<string,unknown>})=>{
      const start=performance.now(),call={id:message.id,command:message.command,payload:message.payload,durationMs:0,outcome:'pending'};calls.push(call);started.set(message.id,start);
      if(message.command==='cancel'){
        const target=String(message.payload.operationId);clearTimeout(pending.get(target));pending.delete(target);
        const canceled=calls.find(row=>row.id===target);if(canceled){canceled.outcome='canceled';canceled.durationMs=performance.now()-(started.get(target)??performance.now());}started.delete(target);started.delete(message.id);
        emit({version:1,id:target,error:{code:'canceled',message:'Operation canceled.'}});emit({version:1,id:message.id,data:{canceled:true}});call.outcome='passed';return;
      }
      pending.set(message.id,setTimeout(()=>{
        pending.delete(message.id);started.delete(message.id);call.durationMs=performance.now()-start;
        try{if((failures[message.command]??0)>0){failures[message.command]--;throw Error('Synthetic recoverable failure');}const data=execute(message.command,message.payload);call.outcome='passed';emit({version:1,id:message.id,data});}
        catch(error){call.outcome='failed';emit({version:1,id:message.id,error:{code:'fixture_error',message:String(error)}});}
      },message.command==='updates.download'?2000:options.delayMs??0));
    }}}});
  }, options);
  await page.goto('https://audit-fixture.invalid/');
  await expect(page.getByRole('button',{name:'↻ Refresh',exact:true})).toBeEnabled();
  await expect.poll(async()=>(await recordedCalls(page)).some(call=>call.command==='instances.list'&&call.outcome==='passed')).toBe(true);
}

export const recordedCalls=(page:Page)=>page.evaluate(()=>
  (window as unknown as {audit:{calls:{command:string;payload:Record<string,unknown>;durationMs:number;outcome:string}[]}}).audit.calls);
