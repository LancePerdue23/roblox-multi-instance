export interface BridgeError {code:string;message:string}
interface Response {version:number;id?:string;data?:unknown;error?:BridgeError;event?:string;operationId?:string}
interface WebView {postMessage(message:unknown):void;addEventListener(type:'message',handler:(event:MessageEvent<Response>)=>void):void}
declare global {interface Window {chrome?:{webview?:WebView}}}
const pending=new Map<string,{resolve:(value:unknown)=>void;reject:(error:Error)=>void;timeout:ReturnType<typeof setTimeout>}>();
const subscribers=new Set<(event:Response)=>void>();
window.chrome?.webview?.addEventListener('message',({data})=>{
  if(data?.version!==1)return;
  if(data.id){const request=pending.get(data.id);if(!request)return;clearTimeout(request.timeout);pending.delete(data.id);subscribers.forEach(handler=>handler({version:1,event:'operation.finished',operationId:data.id}));data.error?request.reject(new Error(data.error.message)):request.resolve(data.data);}
  else subscribers.forEach(handler=>handler(data));
});
export const connected=()=>!!window.chrome?.webview;
export function request<T=unknown>(command:string,payload:unknown={}):Promise<T>{
  if(!connected())return Promise.reject(new Error('Open this interface with RobloxMultiInstance.exe to connect to Windows. Browser previews cannot control Roblox.'));
  const id=crypto.randomUUID();
  return new Promise<T>((resolve,reject)=>{const timeout=setTimeout(()=>{pending.delete(id);reject(new Error('The operation did not respond. Check System → Health before retrying.'));},180000);pending.set(id,{resolve:value=>resolve(value as T),reject,timeout});if(command!=='cancel')subscribers.forEach(handler=>handler({version:1,event:'operation.started',operationId:id}));window.chrome!.webview!.postMessage({version:1,id,command,payload});});
}
export function subscribe(handler:(event:Response)=>void){subscribers.add(handler);return()=>{subscribers.delete(handler)};}
