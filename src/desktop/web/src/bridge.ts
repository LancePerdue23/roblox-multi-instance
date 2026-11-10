export interface BridgeError {code:string;message:string}
export type Category='read'|'discovery'|'launch'|'download'|'maintenance'|'windows'|'preferences'|'probe';
export interface Operation {id:string;command:string;category:Category;state:'Running'|'Unknown';progress?:unknown;cancelable:boolean}
export interface Response {version:number;id?:string;data?:unknown;error?:BridgeError;event?:string;operationId?:string}
interface WebView {postMessage(message:unknown):void;addEventListener(type:'message',handler:(event:MessageEvent<Response>)=>void):void}
declare global {interface Window {chrome?:{webview?:WebView}}}
const pending=new Map<string,{resolve:(value:unknown)=>void;reject:(error:Error)=>void;timeout:ReturnType<typeof setTimeout>;cleanup:()=>void}>();
const operations=new Map<string,Operation>();
const subscribers=new Set<(event:Response)=>void>();
const emit=(event:Response)=>subscribers.forEach(handler=>handler(event));
const changed=()=>emit({version:1,event:'operations.changed',data:[...operations.values()]});
export const snapshot=()=>[...operations.values()];
export function category(command:string):Category {
 if(command==='launch'||command==='launch.start'||command==='accounts.launchBatch')return 'launch';
 if(command.startsWith('updates.')&&['updates.download','updates.check'].includes(command))return 'download';
 if(['system.maintenance','system.integration','updates.install'].includes(command))return 'maintenance';
 if(command==='system.testWorker')return 'probe';
 if(command==='instances.list'||command==='instances.stopLabels')return 'discovery';
 if(['performance.applyControls','performance.resetControls','performance.recoverControls','instances.focus','instances.close','instances.stopAll','instances.cleanup','layouts.preview','layouts.apply','layouts.savePreset'].includes(command))return 'windows';
 if(['performance.activate','accounts.optimizationBypass','performance.saveProfiles','performance.import','games.remove','games.clear'].includes(command))return 'preferences';
 if(command.startsWith('settings.')&&!['settings.get','settings.read'].includes(command)||['host.tray','host.shortcuts','shortcuts.save','layouts.renamePreset','layouts.deletePreset','background.set','activity.clear','telemetry.set','telemetry.clear','support.recovery','support.refresh','support.restore','support.start','support.claim','support.visibility','support.remove','support.forgetPending','support.import','support.importBadge','support.export','accounts.create','accounts.credentials.open','accounts.connect','accounts.remove','accounts.rename','accounts.organize','accounts.order'].includes(command))return 'preferences';
 return 'read';
}
export function conflicts(command:string,active:Operation[]=snapshot()) {
 const group=category(command),mutation=!['read','discovery'].includes(group);
 return active.some(op=>(['accounts.create','accounts.credentials.open'].includes(command)&&['launch','preferences'].includes(op.category))||(['accounts.create','accounts.credentials.open'].includes(op.command)&&['launch','preferences'].includes(group))||group!=='read'&&op.category===group||mutation&&op.category==='maintenance'||group==='maintenance'&&!['read','discovery'].includes(op.category)||command==='accounts.launchBatch'&&op.category==='windows'||group==='windows'&&op.command==='accounts.launchBatch');
}
window.chrome?.webview?.addEventListener('message',({data})=>{
 if(data?.version!==1)return;
 if(data.id){const item=pending.get(data.id);if(!item)return;if(operations.get(data.id)?.state==='Unknown')emit({version:1,event:'operation.reconciled',operationId:data.id,data:data.data,error:data.error});clearTimeout(item.timeout);item.cleanup();pending.delete(data.id);operations.delete(data.id);changed();emit({version:1,event:'operation.finished',operationId:data.id});data.error?item.reject(Object.assign(new Error(data.error.message),{code:data.error.code})):item.resolve(data.data);}
 else {
  const op=data.operationId?operations.get(data.operationId):undefined;
  if(op&&data.event==='progress'){op.progress=data.data;changed();}
  if(op&&data.event==='operation.commit'){op.cancelable=false;changed();}
  emit(data);
 }
});
export const connected=()=>!!window.chrome?.webview;
export function request<T=unknown>(command:string,payload:unknown={},options:{signal?:AbortSignal}={}):Promise<T>{
 if(!connected())return Promise.reject(new Error('Open this interface with RobloxMultiInstance.exe to connect to Windows. Browser previews cannot control Roblox.'));
 if(options.signal?.aborted)return Promise.reject(new Error('Operation canceled.'));
 const id=crypto.randomUUID(),group=category(command);
 return new Promise<T>((resolve,reject)=>{
  const cancel=()=>{void request('cancel',{operationId:id}).catch(()=>{});};
  const cleanup=()=>options.signal?.removeEventListener('abort',cancel);
  const limit=command==='layouts.preview'?20000:command==='accounts.launchBatch'?14400000:['accounts.create','accounts.credentials.open'].includes(command)?14400000:['accounts.connect','support.importBadge'].includes(command)?900000:group==='launch'?120000:command==='updates.download'?600000:group==='maintenance'?1800000:180000;
  const timeout=setTimeout(()=>{
   const op=operations.get(id);
   if(op&&command!=='layouts.preview'&&!['read','discovery'].includes(group)){op.state='Unknown';op.cancelable=false;changed();}
   else {pending.delete(id);operations.delete(id);cleanup();changed();}
   reject(new Error('The operation did not respond. Completion is unknown. Check System → Health before retrying.'));
  },limit);
  pending.set(id,{resolve:value=>resolve(value as T),reject,timeout,cleanup});
  if(command!=='cancel') {operations.set(id,{id,command,category:group,state:'Running',cancelable:command==='updates.download'||group==='discovery'||group==='launch'});changed();emit({version:1,event:'operation.started',operationId:id});}
  options.signal?.addEventListener('abort',cancel,{once:true});
  try {window.chrome!.webview!.postMessage({version:1,id,command,payload});}
  catch {clearTimeout(timeout);cleanup();pending.delete(id);operations.delete(id);changed();reject(new Error('The desktop connection could not send this request.'));}
 });
}
export function subscribe(handler:(event:Response)=>void){subscribers.add(handler);return()=>{subscribers.delete(handler)};}
