import {useEffect,useRef,useState} from 'react';
import {connected,conflicts,request,subscribe} from './bridge';
import type {AccountBatchDto,AccountsStateDto,GameCardDto,LaunchBatchProgressDto,LaunchTargetDto,LayoutStateDto,PresetDto} from './backend.generated';

type Setup={target:string;jobId:string;ids:string[];layout:string};
type Step='home'|'edit'|'review'|'result';
const emptySetup=():Setup=>({target:'',jobId:'',ids:[],layout:'Grid'});
const message=(error:unknown)=>error instanceof Error?error.message:'The launch could not complete.';
const accountName=(state:AccountsStateDto|undefined,id:string)=>{const row=state?.accounts.find(a=>a.id===id);return row?.nickname||row?.username||'Removed account';};
const savedLayout=(game:GameCardDto)=>game.preset?'preset:'+game.preset:game.arrangement||'';
const layoutName=(value:string)=>value.startsWith('preset:')?value.slice(7):value?value+' layout':'Leave windows as they are';
const layoutPayload=(value:string)=>value.startsWith('preset:')?{preset:value.slice(7),arrangement:''}:{preset:'',arrangement:value};

export function LaunchPanel({open,onClose,onInstances,onAccounts,onAppLaunch,helperInstalled,blocked}:{open:boolean;onClose:()=>void;onInstances:()=>void;onAccounts:()=>void;onAppLaunch:()=>void;helperInstalled:boolean;blocked:boolean}){
 const dialog=useRef<HTMLDialogElement>(null);const heading=useRef<HTMLHeadingElement>(null);
 const [step,setStep]=useState<Step>('home');const [state,setState]=useState<AccountsStateDto>();
 const [recent,setRecent]=useState<GameCardDto[]>([]);const [presets,setPresets]=useState<PresetDto[]>([]);
 const [draft,setDraft]=useState<Setup>(emptySetup);const [resolved,setResolved]=useState<LaunchTargetDto>();
 const [error,setError]=useState('');const [loading,setLoading]=useState(false);const [working,setWorking]=useState(false);
 const [checking,setChecking]=useState(false);const [result,setResult]=useState<AccountBatchDto>();
 const [resultActionsReady,setResultActionsReady]=useState(false);
 const [progress,setProgress]=useState<LaunchBatchProgressDto[]>([]);
 const launching=useRef(false);const launchId=useRef<string|undefined>(undefined);const controller=useRef<AbortController|null>(null);
 const loadVersion=useRef(0);const mounted=useRef(true);const canceled=useRef(false);
 useEffect(()=>()=>{mounted.current=false;loadVersion.current++;},[]);
 useEffect(()=>subscribe(event=>{
  if(event.event==='operation.reconciled'&&launchId.current&&event.operationId===launchId.current){
   if(event.error)setError(event.error.message);
   else if(event.data){setResult(event.data as AccountBatchDto);setError('');window.dispatchEvent(new Event('launch-library-changed'));}
  }
  if(!launching.current)return;
  if(event.event==='operation.started'&&!launchId.current)launchId.current=event.operationId;
  if(event.event==='accounts.progress'&&(!event.operationId||event.operationId===launchId.current)){
   const item=event.data as LaunchBatchProgressDto;
   if(draft.ids.includes(item.accountId))setProgress(rows=>[...rows.filter(row=>row.accountId!==item.accountId),item].sort((a,b)=>a.slot-b.slot));
  }
 }),[draft.ids]);
 async function load(){
  const version=++loadVersion.current;setLoading(true);setError('');setState(undefined);setRecent([]);setPresets([]);
  const values=await Promise.allSettled([request<AccountsStateDto>('accounts.state',{includeAvatars:false}),request<GameCardDto[]>('games.recent'),request<LayoutStateDto>('layouts.get')]);
  if(!mounted.current||version!==loadVersion.current)return;
  if(values[0].status==='fulfilled')setState(values[0].value);
  if(values[1].status==='fulfilled')setRecent(values[1].value);
  if(values[2].status==='fulfilled')setPresets(values[2].value.presets);
  const problems=values.flatMap((item,index)=>item.status==='rejected'?[['Accounts','Recent games','Layouts'][index]+' could not be loaded.']:[]);
  setError(problems.join(' '));setLoading(false);
 }
 useEffect(()=>{
  if(open){dialog.current?.showModal();if(!launching.current&&step!=='result'){setStep('home');void load();}}
  else{dialog.current?.close();loadVersion.current++;setLoading(false);}
 },[open]);
 useEffect(()=>{if(open)heading.current?.focus();},[step,open]);
 // Finish the original pointer click sequence before a new action occupies Launch's position.
 useEffect(()=>{if(step!=='result'){setResultActionsReady(false);return;}const timer=setTimeout(()=>setResultActionsReady(true),400);return()=>clearTimeout(timer);},[step]);
 const last=recent[0];
 const selectedPreset=draft.layout.startsWith('preset:')?presets.find(p=>p.name===draft.layout.slice(7)):undefined;
 function setupIssue(setup:Setup,accounts=state,layouts=presets){
  if(!accounts?.available)return 'Account storage is unavailable. Refresh before launching.';
  if(!setup.ids.length)return 'Choose at least one account.';
  if(setup.ids.some(id=>!accounts.accounts.some(a=>a.id===id)))return 'A saved account was removed. Edit this setup before launching.';
  if(setup.layout.startsWith('preset:')){const preset=layouts.find(p=>p.name===setup.layout.slice(7));if(!preset||preset.slots.length!==setup.ids.length)return 'The saved layout changed or needs a different account count. Choose a layout before launching.';}
  else if(!['','Grid','Columns'].includes(setup.layout))return 'Choose an available layout before launching.';
  return '';
 }
 const fromGame=(game:GameCardDto):Setup=>({target:game.placeId,jobId:'',ids:[...(game.accountIds??[])],layout:savedLayout(game)});
 function edit(game?:GameCardDto){setError('');setResolved(undefined);setDraft(game?fromGame(game):emptySetup());setStep('edit');}
 async function review(setup:Setup){
  if(checking||launching.current)return;
  setChecking(true);setError('');setDraft({...setup,ids:[...setup.ids]});
  try{
   const [accounts,layouts,target]=await Promise.all([request<AccountsStateDto>('accounts.state',{includeAvatars:false}),request<LayoutStateDto>('layouts.get'),request<LaunchTargetDto>('accounts.target',{target:setup.target,jobId:setup.jobId})]);
   setState(accounts);setPresets(layouts.presets);
   const issue=setupIssue(setup,accounts,layouts.presets);if(issue)throw new Error(issue);
   if(!accounts.launchAvailable)throw new Error(accounts.notice||'Saved-account launching is unavailable in this build.');
   setResolved(target);setStep('review');
  }catch(e){setError(message(e));}finally{setChecking(false);}
 }
 async function launch(){
  if(!resolved||launching.current||conflicts('accounts.launchBatch'))return;
  const issue=setupIssue(draft);if(issue){setError(issue);return;}
  launching.current=true;launchId.current=undefined;canceled.current=false;controller.current=new AbortController();setWorking(true);setError('');setProgress([]);setResult(undefined);setStep('result');
  try{setResult(await request<AccountBatchDto>('accounts.launchBatch',{ids:[...draft.ids],target:resolved.placeId,jobId:resolved.jobId??'',...layoutPayload(draft.layout)},{signal:controller.current.signal}));}
  catch(e){setError(message(e));}
  finally{launching.current=false;controller.current=null;setWorking(false);window.dispatchEvent(new Event('launch-library-changed'));}
 }
 const locked=working||checking||loading;
 const canLaunch=helperInstalled&&state?.launchAvailable&&!blocked;
 const lastIssue=last?setupIssue(fromGame(last)):'';
 function dismiss(){if(checking)return;onClose();}
 function home(){setStep('home');void load();}
 return <dialog className="launch-panel" ref={dialog} aria-labelledby="launch-panel-title" onCancel={event=>{event.preventDefault();dismiss();}}>
  <div className="launch-panel-heading"><div><span className="eyebrow">ROBLOX MULTI INSTANCE</span><h2 id="launch-panel-title" ref={heading} tabIndex={-1}>{step==='home'?'Launch games':step==='edit'?'Set up your launch':step==='review'?'Ready to launch?':'Launch progress'}</h2></div><button aria-label={working?'Hide launch panel':'Close launch panel'} disabled={checking} onClick={dismiss}>×</button></div>
  <div className="launch-panel-body stack">
   {error&&<div className="error-banner" role="alert">{error}{step==='home'&&<button disabled={locked} onClick={()=>void load()}>Try again</button>}</div>}
   {!helperInstalled&&<p className="notice">Set up the helper in System → Health before launching games.</p>}
   {blocked&&!working&&<p className="notice">Another launch or window operation is still running. Wait for it to finish before starting a new launch.</p>}
   {state&&!state.launchAvailable&&<p className="notice">{state.notice||'Saved-account launching is unavailable in this build.'}</p>}
   {step==='home'&&<>
    {loading?<p role="status">Loading your launch setups…</p>:last?<section className="launch-repeat"><span className="eyebrow">PICK UP WHERE YOU LEFT OFF</span><h3>{last.name}</h3><p>{(last.accountIds??[]).length?last.accountIds!.map(id=>accountName(state,id)).join(' · '):'Choose accounts for this older entry.'}</p><p className="muted">{last.accountIds?.length??0} accounts · {layoutName(savedLayout(last))}</p>{lastIssue&&<p>{lastIssue}</p>}<div className="button-row"><button className="primary" disabled={locked||!canLaunch||!!lastIssue} onClick={()=>void review(fromGame(last))}>↻ Repeat last launch</button><button disabled={locked||!state?.available} onClick={()=>edit(last)}>Edit setup</button></div></section>:<p>No recent launches yet. Choose a game and accounts to get started.</p>}
    <div className="launch-choice"><div><h3>Start something new</h3><p>Choose a game, accounts, and layout.</p></div><button disabled={locked||!state?.available} onClick={()=>edit()}>New launch →</button></div>
    {!loading&&state?.accounts.length===0&&<div className="launch-choice"><p>Add a Roblox account to launch games.</p><button onClick={()=>{onClose();onAccounts();}}>Add accounts…</button></div>}
    {recent.length>1&&<section className="stack"><h3>Recent games</h3>{recent.slice(1,5).map(game=><div className="launch-choice" key={game.placeId}><div><strong>{game.name}</strong><p className="muted">{(game.accountIds??[]).map(id=>accountName(state,id)).join(' · ')||'Choose accounts'}</p></div><button aria-label={'Use setup for '+game.name} disabled={locked||!state?.available} onClick={()=>edit(game)}>Use setup →</button></div>)}</section>}
    <div className="launch-panel-secondary"><button disabled={locked||blocked||!connected()||!helperInstalled} onClick={()=>{onClose();onAppLaunch();}}>Open Roblox app ↗</button><span className="muted">Open Roblox without a saved-account game launch.</span></div>
   </>}
   {step==='edit'&&<>
    <label>Game URL or Place ID<input maxLength={2048} placeholder="https://www.roblox.com/games/…" value={draft.target} disabled={locked} onChange={e=>setDraft({...draft,target:e.target.value})}/></label>
    <fieldset className="launch-account-picker"><legend>Accounts · {draft.ids.length} selected</legend>{state?.accounts.map(account=><label className="check" key={account.id}><input type="checkbox" checked={draft.ids.includes(account.id)} disabled={locked} onChange={e=>setDraft({...draft,ids:e.target.checked?[...draft.ids,account.id]:draft.ids.filter(id=>id!==account.id)})}/><span>{account.nickname||account.username}<small>@{account.username}{account.status&&!account.status.startsWith('Saved locally')?' · '+account.status:''}</small></span></label>)}{!state?.accounts.length&&<button onClick={()=>{onClose();onAccounts();}}>Add accounts…</button>}</fieldset>
    {!!draft.ids.length&&<div className="launch-slot-list">{draft.ids.map((id,index)=><div className="launch-choice" key={id}><span>Slot {index+1}: {accountName(state,id)}</span><div className="button-row">{!state?.accounts.some(a=>a.id===id)&&<button disabled={locked} onClick={()=>setDraft({...draft,ids:draft.ids.filter(value=>value!==id)})}>Remove unavailable account</button>}{[-1,1].map(delta=><button key={delta} aria-label={'Move launch account '+(index+1)+(delta<0?' up':' down')} disabled={locked||index+delta<0||index+delta>=draft.ids.length} onClick={()=>{const ids=[...draft.ids];[ids[index],ids[index+delta]]=[ids[index+delta],ids[index]];setDraft({...draft,ids});}}>{delta<0?'↑':'↓'}</button>)}</div></div>)}</div>}
    <label>Arrange windows after launch<select aria-label="Quick launch layout" value={draft.layout} disabled={locked} onChange={e=>setDraft({...draft,layout:e.target.value})}><option value="">Leave windows as they are</option><option value="Grid">Grid</option><option value="Columns">Columns</option>{presets.map(p=><option key={p.name} value={'preset:'+p.name}>{p.name} ({p.slots.length} accounts)</option>)}{draft.layout.startsWith('preset:')&&!selectedPreset&&<option value={draft.layout}>Unavailable: {draft.layout.slice(7)}</option>}</select></label>
    {selectedPreset&&selectedPreset.slots.length!==draft.ids.length&&<p role="alert">This preset needs {selectedPreset.slots.length} accounts.</p>}
    <details><summary>Public server (optional)</summary><label>Public server Job ID<input maxLength={64} value={draft.jobId} disabled={locked} onChange={e=>setDraft({...draft,jobId:e.target.value})}/></label></details>
    <div className="launch-panel-actions"><button disabled={locked} onClick={home}>← Back</button><button className="primary" disabled={locked||!canLaunch||!draft.target.trim()||!!setupIssue(draft)} onClick={()=>void review(draft)}>{checking?'Checking setup…':'Review launch →'}</button></div>
   </>}
   {step==='review'&&<>
    <dl className="launch-review"><dt>Game</dt><dd>{resolved?.game?.name??'Place '+resolved?.placeId}</dd><dt>Accounts</dt><dd>{draft.ids.map((id,index)=><div key={id}>{index+1}. {accountName(state,id)}</div>)}</dd><dt>Layout</dt><dd>{layoutName(draft.layout)}</dd><dt>Server</dt><dd>{resolved?.jobId?'Selected public server':'Automatic matchmaking'}</dd></dl>
    <p className="muted">Each account opens its own game window. Verify the account in each game. Automatic placement requires every selected account’s window to be verified.</p>
    <div className="launch-panel-actions"><button disabled={working} onClick={()=>{setError('');setStep('edit');}}>Change setup</button><button className="primary" disabled={working||!canLaunch} onClick={()=>void launch()}>Launch {draft.ids.length} {draft.ids.length===1?'account':'accounts'}</button></div>
   </>}
   {step==='result'&&<>
    <div role="status" className="stack"><h3>{working?'Launching your accounts…':result?'Batch: '+result.batch.status:'Launch could not be confirmed'}</h3><p>{resolved?.game?.name??'Place '+resolved?.placeId}</p>{working?progress.length?progress.map(row=><p key={row.accountId}>{accountName(state,row.accountId)}: {row.status}</p>):<p>Preparing your launch…</p>:null}
     {result&&<><p>{result.layoutStatus}</p>{result.batch.items.map(row=><p key={row.accountId}>{accountName(state,row.accountId)}: {row.status} ({row.code})</p>)}</>}
    </div>
    {(error||result?.batch.items.some(row=>row.status==='Unknown'))&&<p className="notice">A game may already be running. Check Instances and the game before retrying. Unconfirmed launches are left open.</p>}
    {result?.batch.items.some(row=>row.status==='NeedsSignIn')&&<div className="launch-choice"><p>Reconnect affected accounts before trying again.</p><button onClick={()=>{onClose();onAccounts();}}>Open Accounts</button></div>}
    {working&&<p className="muted">You can hide this panel while launches continue. Canceling stops pending work; already started games stay open.</p>}
    <div className="launch-panel-actions">{working?<button disabled={canceled.current} onClick={()=>{canceled.current=true;controller.current?.abort();setError('Cancel requested. Waiting for the current launch to finish safely.');}}>Cancel pending launches</button>:<button disabled={!resultActionsReady} onClick={home}>Back to launcher</button>}<button className="primary" disabled={!resultActionsReady} onClick={()=>{onClose();onInstances();}}>View instances →</button></div>
   </>}
  </div>
 </dialog>;
}
