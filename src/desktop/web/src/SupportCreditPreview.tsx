import {useEffect,useState} from 'react';
const key='rmi.local-support-credit';
function read(){
 if(!__LOCAL_SUPPORTER_PREVIEW__)return null;
 try{const value=localStorage.getItem(key);if(value===null)return null;const credit=Number(value);return Number.isSafeInteger(credit)&&credit>=0?credit:null;}catch{return null;}
}
export function useSupportCreditPreview(){
 const [credit,setCredit]=useState<number|null>(read);
 useEffect(()=>{if(!__LOCAL_SUPPORTER_PREVIEW__)return;const update=()=>setCredit(read());window.addEventListener('support-credit-preview-changed',update);return()=>window.removeEventListener('support-credit-preview-changed',update);},[]);
 return credit;
}
function save(credit:number|null){
 if(!__LOCAL_SUPPORTER_PREVIEW__)return;
 if(credit!==null&&(!Number.isSafeInteger(credit)||credit<0))return;
 try{if(credit===null)localStorage.removeItem(key);else localStorage.setItem(key,String(credit));}catch{return;}
 window.dispatchEvent(new Event('support-credit-preview-changed'));
}
export function SupportCreditPreview({realCredit,selectedCredit,disabled=false,onReset}:{realCredit:number;selectedCredit:number;disabled?:boolean;onReset:()=>Promise<boolean>}){
 const credit=useSupportCreditPreview(),[value,setValue]=useState(String(credit??realCredit));
 const [resetting,setResetting]=useState(false);
 useEffect(()=>setValue(String(credit??realCredit)),[credit,realCredit]);
 if(!__LOCAL_SUPPORTER_PREVIEW__)return null;
 const amount=/^\d+$/.test(value)?Number(value):NaN,valid=Number.isSafeInteger(amount)&&amount>=0;
 async function reset(){
  setResetting(true);
  try{if(await onReset()){
   save(null);
   // Keep the existing owner dropdown and its React preview in sync with real status.
   const role=document.querySelector<HTMLSelectElement>('select[aria-label="Local role preview"]');
   if(role){role.value='-2';role.dispatchEvent(new Event('change',{bubbles:true}));}
   else{try{localStorage.setItem('rmi.local-role-preview','-2');}catch{}window.dispatchEvent(new Event('local-role-changed'));}
  }}finally{setResetting(false);}
 }
 const locked=disabled||resetting;
 return <details className="support-credit-dev"><summary>Dev credit controls{credit!==null?' · simulation active':''}</summary><p className="muted">Simulate credit, or reset saved verification for a fresh speed test.</p><label>Simulated Robux credit<input inputMode="numeric" disabled={locked} value={value} onChange={event=>setValue(event.target.value)}/></label><div className="button-row"><button disabled={locked||!valid} onClick={()=>save(amount)}>Apply</button><button disabled={locked||!Number.isSafeInteger((credit??realCredit)+selectedCredit)} onClick={()=>save((credit??realCredit)+selectedCredit)}>Add selected pass</button><button disabled={locked||credit===null} onClick={()=>save(null)}>Use real credit</button></div><div className="button-row"><button className="danger-text" disabled={locked} onClick={()=>void reset()}>Reset credit checks</button></div><small>Reset clears verified Robux credit and checked passes on this computer. Your next verification checks ownership again.</small></details>;
}
