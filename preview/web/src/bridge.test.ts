// @vitest-environment node
import {afterEach,beforeEach,describe,expect,it,vi} from 'vitest';
describe('desktop bridge',()=>{
 let receive:(event:{data:unknown})=>void;let sent:Record<string,unknown>[];
 beforeEach(()=>{vi.resetModules();vi.useFakeTimers();sent=[];vi.stubGlobal('window',{chrome:{webview:{postMessage:(message:Record<string,unknown>)=>sent.push(message),addEventListener:(_type:string,handler:typeof receive)=>receive=handler}}});});
 afterEach(()=>{vi.useRealTimers();vi.unstubAllGlobals();});
 it('matches responses by ID and ignores unsupported protocol and late responses',async()=>{const bridge=await import('./bridge');const a=bridge.request('instances.list'),b=bridge.request('settings.get');receive({data:{version:9,id:sent[0].id,data:'wrong'}});receive({data:{version:1,id:sent[1].id,data:'settings'}});receive({data:{version:1,id:sent[0].id,data:'instances'}});expect(await a).toBe('instances');expect(await b).toBe('settings');expect(()=>receive({data:{version:1,id:sent[0].id,data:'late'}})).not.toThrow();});
 it('propagates safe host errors',async()=>{const bridge=await import('./bridge');const pending=bridge.request('launch');receive({data:{version:1,id:sent[0].id,error:{code:'helper-missing',message:'Install the helper first.'}}});await expect(pending).rejects.toThrow('Install the helper first.');});
 it('rejects an unavailable desktop connection',async()=>{vi.stubGlobal('window',{});const bridge=await import('./bridge');await expect(bridge.request('launch')).rejects.toThrow('Browser previews cannot control Roblox');});
 it('rejects timed out requests and safely ignores their eventual response',async()=>{const bridge=await import('./bridge');const pending=bridge.request('launch');const result=expect(pending).rejects.toThrow('did not respond');await vi.advanceTimersByTimeAsync(180001);await result;expect(()=>receive({data:{version:1,id:sent[0].id,data:{}}})).not.toThrow();});
});
