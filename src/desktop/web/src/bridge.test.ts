// @vitest-environment node
import {afterEach,beforeEach,describe,expect,it,vi} from 'vitest';
describe('desktop bridge',()=>{
 let receive:(event:{data:unknown})=>void;let sent:Record<string,unknown>[];
 beforeEach(()=>{vi.resetModules();vi.useFakeTimers();sent=[];vi.stubGlobal('window',{chrome:{webview:{postMessage:(message:Record<string,unknown>)=>sent.push(message),addEventListener:(_type:string,handler:typeof receive)=>receive=handler}}});});
 afterEach(()=>{vi.useRealTimers();vi.unstubAllGlobals();});
 it('serializes update checks against downloads',async()=>{
  const bridge=await import('./bridge');
  for(const command of ['updates.check','updates.download']){
   expect(bridge.category(command)).toBe('download');
   expect(bridge.conflicts(command,[{id:'download',command:'updates.download',category:'download',state:'Running',cancelable:true}])).toBe(true);
  }
 });
 it('classifies automatic policy activation and account bypass as writes',async()=>{
  const bridge=await import('./bridge');
  for(const command of ['performance.activate','accounts.optimizationBypass'])expect(bridge.category(command)).toBe('preferences');
  expect(bridge.category('performance.optimizationState')).toBe('read');
  expect(bridge.conflicts('performance.activate',[{id:'maintenance',command:'system.maintenance',category:'maintenance',state:'Running',cancelable:false}])).toBe(true);
 });
 it('classifies performance writes as preferences and preview/export as reads',async()=>{const bridge=await import('./bridge');expect(bridge.category('performance.saveProfiles')).toBe('preferences');expect(bridge.category('performance.import')).toBe('preferences');expect(bridge.category('performance.previewImport')).toBe('read');expect(bridge.category('performance.export')).toBe('read');for(const command of ['performance.applyControls','performance.resetControls','performance.recoverControls']){expect(bridge.category(command)).toBe('windows');expect(bridge.conflicts(command,[{id:'batch',command:'accounts.launchBatch',category:'launch',state:'Running',cancelable:true}])).toBe(true);}expect(bridge.category('performance.controls')).toBe('read');expect(bridge.conflicts('performance.import',[{id:'x',command:'settings.save',category:'preferences',state:'Running',cancelable:false}])).toBe(true);});
 it('classifies account organization mutations as preferences',async()=>{const bridge=await import('./bridge');expect(bridge.category('accounts.organize')).toBe('preferences');expect(bridge.category('accounts.order')).toBe('preferences');});
 it('matches responses by ID and ignores unsupported protocol and late responses',async()=>{const bridge=await import('./bridge');const a=bridge.request('instances.list'),b=bridge.request('settings.get');receive({data:{version:9,id:sent[0].id,data:'wrong'}});receive({data:{version:1,id:sent[1].id,data:'settings'}});receive({data:{version:1,id:sent[0].id,data:'instances'}});expect(await a).toBe('instances');expect(await b).toBe('settings');expect(()=>receive({data:{version:1,id:sent[0].id,data:'late'}})).not.toThrow();});
 it('propagates safe host errors',async()=>{const bridge=await import('./bridge');const pending=bridge.request('launch');receive({data:{version:1,id:sent[0].id,error:{code:'helper-missing',message:'Install the helper first.'}}});await expect(pending).rejects.toThrow('Install the helper first.');});
 it('rejects an unavailable desktop connection',async()=>{vi.stubGlobal('window',{});const bridge=await import('./bridge');await expect(bridge.request('launch')).rejects.toThrow('Browser previews cannot control Roblox');});
 it('rejects timed out requests and safely ignores their eventual response',async()=>{const bridge=await import('./bridge');const pending=bridge.request('launch');const result=expect(pending).rejects.toThrow('did not respond');await vi.advanceTimersByTimeAsync(180001);await result;expect(()=>receive({data:{version:1,id:sent[0].id,data:{}}})).not.toThrow();});

 it('keeps launch and download identities when an unrelated refresh completes',async()=>{
  const bridge=await import('./bridge');const launch=bridge.request('launch'),download=bridge.request('updates.download'),refresh=bridge.request('instances.list');
  expect(bridge.snapshot()).toHaveLength(3);receive({data:{version:1,id:sent[2].id,data:{}}});await refresh;
  expect(bridge.snapshot().map(op=>op.command)).toEqual(['launch','updates.download']);
  receive({data:{version:1,event:'progress',operationId:sent[1].id,data:{bytes:15,total:100}}});
  expect(bridge.snapshot().find(op=>op.command==='updates.download')?.progress).toEqual({bytes:15,total:100});
  receive({data:{version:1,id:sent[0].id,data:{}}});receive({data:{version:1,id:sent[1].id,data:{}}});await Promise.all([launch,download]);expect(bridge.snapshot()).toHaveLength(0);
 });
 it('retains unknown mutation conflicts until the actual host response',async()=>{
  const bridge=await import('./bridge');const launch=bridge.request('launch');const rejected=expect(launch).rejects.toThrow('unknown');await vi.advanceTimersByTimeAsync(120001);await rejected;
  expect(bridge.snapshot()[0].state).toBe('Unknown');expect(bridge.conflicts('launch')).toBe(true);expect(bridge.conflicts('instances.list')).toBe(false);
  receive({data:{version:1,id:sent[0].id,data:{}}});expect(bridge.conflicts('launch')).toBe(false);
 });
 it('cancels only the selected request and keeps commit stages noncancelable',async()=>{
  const bridge=await import('./bridge');const controller=new AbortController();const refresh=bridge.request('instances.list',{}, {signal:controller.signal});const launch=bridge.request('launch');controller.abort();
  expect(sent[2]).toMatchObject({command:'cancel',payload:{operationId:sent[0].id}});
  receive({data:{version:1,id:sent[2].id,data:{canceled:true}}});expect(bridge.snapshot()).toHaveLength(2);
  receive({data:{version:1,event:'operation.commit',operationId:sent[0].id}});expect(bridge.snapshot()[0].cancelable).toBe(false);
  receive({data:{version:1,id:sent[0].id,data:{}}});receive({data:{version:1,id:sent[1].id,data:{}}});await Promise.all([refresh,launch]);
 });
 it('keeps unsuccessful completed outcomes separate from transport errors',async()=>{
  const bridge=await import('./bridge');const action=bridge.request('instances.close');receive({data:{version:1,id:sent[0].id,data:{outcome:'Failed',code:'CLOSE_FAILED',message:'Could not request close.'}}});
  await expect(action).resolves.toMatchObject({outcome:'Failed'});
 });
 it('blocks window changes during an account batch while allowing discovery and canceling that batch',async()=>{
  const bridge=await import('./bridge');const controller=new AbortController();const batch=bridge.request('accounts.launchBatch',{ids:['local-account']},{signal:controller.signal});
  expect(bridge.conflicts('layouts.apply')).toBe(true);expect(bridge.conflicts('launch')).toBe(true);expect(bridge.conflicts('accounts.state')).toBe(false);
  controller.abort();expect(sent[1]).toMatchObject({command:'cancel',payload:{operationId:sent[0].id}});
  receive({data:{version:1,id:sent[1].id,data:{canceled:true}}});receive({data:{version:1,id:sent[0].id,data:{}}});await batch;
  expect(bridge.conflicts('layouts.apply')).toBe(false);
 });
it('blocks creation against launches in both directions while allowing account reads',async()=>{const bridge=await import('./bridge');for(const command of ['accounts.create','accounts.credentials.open']){expect(bridge.category(command)).toBe('preferences');expect(bridge.conflicts(command,[{id:'l',command:'accounts.launchBatch',category:'launch',state:'Running',cancelable:true}])).toBe(true);expect(bridge.conflicts('launch',[{id:'c',command,category:'preferences',state:'Running',cancelable:true}])).toBe(true);expect(bridge.conflicts('accounts.state',[{id:'c',command,category:'preferences',state:'Running',cancelable:true}])).toBe(false);}});

});

 it('Stop All conflicts with account launches and other window actions',async()=>{
  const bridge=await import('./bridge');expect(bridge.category('instances.stopAll')).toBe('windows');
  expect(bridge.conflicts('instances.stopAll',[{id:'batch',command:'accounts.launchBatch',category:'launch',state:'Running',cancelable:true}])).toBe(true);
 });
