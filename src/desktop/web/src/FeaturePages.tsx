import {SpecialBadge} from './SpecialBadges';
import {SupporterStudio,HeartBadge} from './SupporterCosmetics';
import {useEffect,useLayoutEffect,useRef,useState,type ReactNode} from 'react';
import {connected,request,subscribe} from './bridge';
import {ThemedSelect} from './ThemedSelect';
import type {TelemetryDto,SupportDto,AccountsStateDto,LaunchTargetDto,GameCardDto,LaunchBatchProgressDto,AccountBatchDto,PresetDto} from './backend.generated';

const accountAvatars=new Map<string,string>();
function AccountAvatar({account}:{account:AccountsStateDto['accounts'][number]}){
 if(account.avatar)accountAvatars.set(account.id,account.avatar);
 const src=account.avatar||accountAvatars.get(account.id);
 return <span className="account-avatar">{src?<img src={src} alt=""/>:<svg viewBox="0 0 24 24" width="24" height="24" fill="currentColor" aria-hidden="true"><circle cx="12" cy="8" r="4"/><path d="M4 22v-3a8 8 0 0 1 16 0v3Z"/></svg>}</span>;
}
export type ConfirmAction=(title:string,body:string)=>Promise<boolean>;
const failure=(e:unknown)=>e instanceof Error?e.message:'The action could not complete.';

export function PrivacyPage({busy}:{busy:boolean}){
 const [state,setState]=useState<TelemetryDto>();const [error,setError]=useState('');const [working,setWorking]=useState(false);
 async function act(command:string,payload:object={}){setWorking(true);setError('');try{setState(await request<TelemetryDto>(command,payload));}catch(e){setError(failure(e));}finally{setWorking(false);}}
 useEffect(()=>{void act('telemetry.get');},[]);
 return <section className="card"><div className="card-heading"><div><h2>Privacy and optional error reports</h2><p>Everything works with reporting off.</p></div></div><div className="padded stack feature-content">
  {error&&<p role="alert" className="error-banner">{error}</p>}
  <label className="check"><input type="checkbox" checked={state?.enabled??false} disabled={busy||working||!state} onChange={e=>void act('telemetry.set',{enabled:e.target.checked})}/>Share optional technical error reports</label>
  <p>Off by default. Help improve Roblox Multi Instance by sharing predefined error codes and app/system versions. You can turn this off whenever you want.</p>
  <h3>Exactly what can be sent</h3>
  <p>App and helper versions, coarse Windows version, processor architecture, predefined subsystem and error codes, approved numeric system error codes, and grouped error counts.</p>
  <p>No usernames, Roblox account details, passwords, cookies, game or server IDs, personal files, paths, raw logs, exception messages, or persistent device identifiers.</p>
  <p className="muted">The hosting provider processes connection information such as your IP address. The app does not put it in reports. Reports are error summaries, not a count of unique users.</p>
   {state&&!state.available&&<p role="alert">Your reporting preference could not be read. Reporting is off until you explicitly save a choice.</p>}
  {state&&!state.configured&&<p className="notice">The reporting service is not configured in this build. Nothing can be uploaded.</p>}
  <p>Queued error occurrences: <strong>{state?.queuedErrors??0}</strong>. Reports expire after seven days. Automatic uploads happen only while the desktop app is open; helper-only users can send the queue with the telemetry CLI.</p>
  {state?.lastResult&&<p role="status">{state.lastResult}</p>}
  <div className="button-row"><button disabled={busy||working||!state?.enabled||!state.configured||!state.queuedErrors} onClick={()=>void act('telemetry.send')}>Send queued reports</button><button disabled={busy||working||!state?.queuedErrors} onClick={()=>void act('telemetry.clear')}>Clear queued reports</button><button disabled={working} onClick={()=>void act('telemetry.get')}>Refresh reporting status</button></div>
  <p className="muted">Turning reporting off clears the queue and cancels pending uploads where possible. Already received summaries cannot be recalled. Enabling reporting starts with future errors; old logs are never uploaded.</p>
 </div></section>;
}

export function SupportPage({busy,confirm,onChange}:{busy:boolean;confirm:ConfirmAction;onChange:(s:SupportDto)=>void}){
 const [state,setState]=useState<SupportDto>();const [error,setError]=useState('');const [working,setWorking]=useState(false);
 const onChangeRef=useRef(onChange);onChangeRef.current=onChange;
 const save=(s:SupportDto)=>{setState(s);onChangeRef.current(s);};
 async function act(command:string,payload:object={}){setWorking(true);setError('');try{const s=await request<SupportDto>(command,payload);if(s&&typeof s.supported==='boolean')save(s);}catch(e){setError(failure(e));}finally{setWorking(false);}}
 useEffect(()=>{void act('support.get');},[]);
 useEffect(()=>{
  if(!state?.pending||!state.configured)return;
  let stopped=false;let timer:ReturnType<typeof setTimeout>;let attempts=0;
  const tick=async()=>{try{const s=await request<SupportDto>('support.claim');if(!stopped)save(s);if(!s.pending)return;}catch{/* Manual check remains available if checkout is offline. */}if(!stopped&&++attempts<120)timer=setTimeout(tick,5000);};
  timer=setTimeout(tick,5000);return()=>{stopped=true;clearTimeout(timer);};
 },[state?.pending,state?.configured]);
 return <section className="card"><div className="card-heading"><div><h2>Support development</h2><p>Roblox Multi Instance is free and will stay free.</p></div>{state?.supported&&<HeartBadge/>}</div><div className="padded stack feature-content">
  {error&&<p role="alert" className="error-banner">{error}</p>}
  <p>Contributions are optional and never unlock features. Support with Stripe or donate Robux through our Roblox gamepass.</p>
  <SupporterStudio unlockedTier={state?.supported?0:-1}/>
  <h3>Support with USD</h3><p>A completed one-time Stripe contribution earns cosmetic supporter recognition as a thank-you.</p>
  {state&&!state.configured&&<p className="notice">Stripe contributions are not available in this build yet. Trusted receipts can still be used offline when their signing key is included.</p>}
  <div className="button-row"><button className="primary" disabled={busy||working||!state?.configured} onClick={()=>void act('support.start')}>{state?.pending?'Resume checkout…':'Support with USD…'}</button>{state?.pending&&<><button disabled={busy||working} onClick={()=>void act('support.claim')}>Check payment status</button><button disabled={busy||working} onClick={()=>void (async()=>{if(await confirm('Forget pending checkout','Check payment status first if you paid. Forgetting this checkout removes its local claim secret; a completed contribution may then require manual receipt recovery through the developer.'))await act('support.forgetPending',{confirmed:true});})()}>Forget pending checkout…</button></>}</div>
  {state?.message&&<p role="status">{state.message}</p>}
  <p>Checkout opens in your browser. Stripe processes payment details and provides financial records to the developer. This app’s supporter service does not retain donor names or emails, and never links contributions to Roblox accounts or error reports.</p>
  <h3>Support with Robux</h3>
  <p>Open our donation gamepass on Roblox and purchase it with Robux. Roblox shows the creator and current price before you buy. A gamepass can be purchased once per Roblox account.</p>
  {state&&!state.robloxConfigured&&<p className="notice">Robux donations are coming soon while we set up our group gamepass.</p>}
  <div className="button-row"><button disabled={busy||working||!state?.robloxConfigured} onClick={()=>void act('support.roblox')}>Support with Robux…</button><button disabled={busy||working||!state?.robloxConfigured} onClick={()=>void act('support.checkRoblox')}>Check purchases</button></div>
  <p className="muted">Own any qualifying donation gamepass on a connected Roblox account to receive supporter recognition across this app. Successful verification stays on this installation, even offline or after removing the account. Purchase checks send connected Roblox user IDs to Roblox; no account cookies are sent.</p>
  <h3>Your supporter receipt</h3><p>A signed Stripe receipt verifies the contribution locally. No app account is needed, and the badge works offline. Keep an exported receipt if you want to move it to another computer; it is transferable.</p>
  <label className="check"><input type="checkbox" checked={state?.showBadge??false} disabled={busy||working||!state?.supported} onChange={e=>void act('support.visibility',{visible:e.target.checked})}/>Show my supporter badge</label>
  <div className="button-row"><button disabled={busy||working||!connected()} onClick={()=>void act('support.import')}>Import receipt…</button><button disabled={busy||working||!state?.receiptAvailable} onClick={()=>void act('support.export')}>Export receipt…</button><button className="danger-text" disabled={busy||working||!state?.receiptAvailable} onClick={()=>void (async()=>{if(await confirm('Remove supporter receipt','Remove the receipt from this computer? Export it first if you want to keep a backup. This does not refund the contribution.'))await act('support.remove',{confirmed:true});})()}>Remove receipt…</button></div>
 </div></section>;
}

export function AccountsPage({busy,confirm,presets,mode,onPlay}:{onPlay?:()=>void;mode:'accounts'|'play';busy:boolean;confirm:ConfirmAction;presets:PresetDto[]}){
 const [state,setState]=useState<AccountsStateDto>();const [selected,setSelected]=useState<string[]>([]);const [nickname,setNickname]=useState('');const [renaming,setRenaming]=useState<string>();
 const [target,setTarget]=useState('');const [jobId,setJobId]=useState('');const [preset,setPreset]=useState('');const [arrangement,setArrangement]=useState('');const [resolved,setResolved]=useState<LaunchTargetDto>();
 const [error,setError]=useState('');const [working,setWorking]=useState(false);const [progress,setProgress]=useState<LaunchBatchProgressDto[]>([]);const [result,setResult]=useState<AccountBatchDto>();
 const [search,setSearch]=useState('');const [groupFilter,setGroupFilter]=useState('');const [playGroupFilter,setPlayGroupFilter]=useState('');const [favoritesOnly,setFavoritesOnly]=useState(false);
 const [editing,setEditing]=useState<string>();const [group,setGroup]=useState('');const [color,setColor]=useState('');
 const [manageGroups,setManageGroups]=useState(false);const [groupName,setGroupName]=useState('');
 const accountCards=useRef(new Map<string,HTMLDivElement>());const cardPositions=useRef(new Map<string,{top:number;left:number}>());
 const controller=useRef<AbortController|null>(null);const operationKind=useRef<'signin'|'launch'|'creation'>('signin');
 const [recent,setRecent]=useState<GameCardDto[]>([]);const targetRevision=useRef(0);
 const [recentError,setRecentError]=useState('');const chooseHeading=useRef<HTMLHeadingElement>(null);const recentHeading=useRef<HTMLHeadingElement>(null);
 const previousLaunch=useRef<{selected:string[];target:string;jobId:string;preset:string;arrangement:string;resolved:LaunchTargetDto|undefined;playGroupFilter:string}|undefined>(undefined);
 const [changingAccounts,setChangingAccounts]=useState(false);
 const [focused,setFocused]=useState('');const [statusFilter,setStatusFilter]=useState('all');
 const [signInNeeded,setSignInNeeded]=useState<string[]>([]);

 const [creationProgress,setCreationProgress]=useState<{completed:number;skipped:number;remaining:number;status:string}>();
 const [creationSaved,setCreationSaved]=useState(0);
 const [managed,setManaged]=useState<string[]>([]);const [notes,setNotes]=useState('');
 const needsSignIn=(status:string)=>/needs.?sign.?in|signed out|expired|sign.?in required/i.test(status);
 const accountStatus=(a:AccountsStateDto['accounts'][number])=>signInNeeded.includes(a.id)?'Needs sign-in':a.status.startsWith('Saved locally')?'':a.status;
 const profile=state?.accounts.find(a=>a.id===focused);
 const [now,setNow]=useState(Date.now());const progressAt=useRef<Record<string,number>>({});
 useEffect(()=>{if(!working)return;const timer=setInterval(()=>setNow(Date.now()),1000);return()=>clearInterval(timer);},[working]);
 async function loadRecent(){
  try{
   const rows=await request<GameCardDto[]>('games.recent');setRecent(rows);setRecentError('');
   // Refresh legacy/missing artwork without blocking account controls or restoring
   // games removed while their metadata requests were in flight.
   const pending=rows.filter(game=>!game.artwork||game.artworkKind!=='Icon');
   async function refreshArt(){let game:GameCardDto|undefined;while((game=pending.shift())){
    try{const updated=await request<LaunchTargetDto>('accounts.target',{target:game.placeId});if(updated.game?.artwork){const fresh=updated.game;setRecent(current=>current.map(row=>row.placeId===fresh.placeId?{...row,...fresh,accountIds:row.accountIds,preset:row.preset,arrangement:row.arrangement}:row));}}catch{/* Keep the saved card and placeholder. */}
   }}
   void Promise.all([refreshArt(),refreshArt()]);
  }catch{setRecentError('Recent games could not be loaded. Try refreshing.');}
 }
 useEffect(()=>{void loadRecent();const changed=()=>void loadRecent();window.addEventListener('launch-library-changed',changed);return()=>window.removeEventListener('launch-library-changed',changed);},[]);
 async function launchGame(place:string,job:string,askFirst:boolean,ids=selected,layout=preset,kind=arrangement){
  if(askFirst&&!await confirm('Launch selected accounts','Launch the selected accounts? Verify the selected account inside each game. Unconfirmed launches are left running; check the game before retrying.'))return;
  setChangingAccounts(false);previousLaunch.current=undefined;setSelected(ids);setPreset(layout);setArrangement(kind);setTarget(place);setJobId(job);setResolved(undefined);targetRevision.current++;setProgress([]);setResult(undefined);progressAt.current={};operationKind.current='launch';controller.current=new AbortController();
  try{const response=await request<AccountBatchDto>('accounts.launchBatch',{ids,target:place,jobId:job,preset:layout,arrangement:kind},{signal:controller.current.signal});setResult(response);setSignInNeeded(current=>{const next=new Set(current);for(const item of response.batch.items){if(item.status==='NeedsSignIn')next.add(item.accountId);else if(item.status==='Started')next.delete(item.accountId);}return [...next];});}
  finally{controller.current=null;await loadRecent();window.dispatchEvent(new Event('launch-library-changed'));}
 }
 function changeRecentAccounts(game:GameCardDto){
  if(!changingAccounts)previousLaunch.current={selected,target,jobId,preset,arrangement,resolved,playGroupFilter};
  setChangingAccounts(true);setPlayGroupFilter('');
  setSelected((game.accountIds??[]).filter(id=>state?.accounts.some(a=>a.id===id)));
  setTarget(game.placeId);setJobId('');setArrangement(game.arrangement??'');setPreset(presets.some(p=>p.name===game.preset)?game.preset??'':'');
  setResolved({placeId:game.placeId,jobId:null,game});targetRevision.current++;setProgress([]);setResult(undefined);setError('');
  chooseHeading.current?.focus({preventScroll:true});chooseHeading.current?.scrollIntoView({block:'start',behavior:'smooth'});
 }
 function cancelChangeAccounts(){
  const previous=previousLaunch.current;
  if(previous){setSelected(previous.selected);setTarget(previous.target);setJobId(previous.jobId);setPreset(previous.preset);setArrangement(previous.arrangement);setResolved(previous.resolved);setPlayGroupFilter(previous.playGroupFilter);}
  targetRevision.current++;previousLaunch.current=undefined;setChangingAccounts(false);setError('');
  recentHeading.current?.focus({preventScroll:true});recentHeading.current?.scrollIntoView({block:'start',behavior:'smooth'});
 }
 async function checkTarget(){const revision=++targetRevision.current;const value=await request<LaunchTargetDto>('accounts.target',{target,jobId});if(revision===targetRevision.current)setResolved(value);}
 async function refresh(){const s=await request<AccountsStateDto>('accounts.state');setState(s);setSelected(ids=>ids.filter(id=>s.accounts.some(a=>a.id===id)));}
 async function act(work:()=>Promise<void>){setError('');setWorking(true);try{await work();}catch(e){setError(failure(e));}finally{setWorking(false);}}
 useEffect(()=>{void act(refresh);return()=>controller.current?.abort();},[]);
 useEffect(()=>subscribe(event=>{if(event.event==='accounts.creationProgress'&&(event.data as {status:string}).status!=='Refresh')setCreationProgress(event.data as {completed:number;skipped:number;remaining:number;status:string});if(event.event==='accounts.progress'){const item=event.data as LaunchBatchProgressDto;progressAt.current[item.accountId]=Date.now();setNow(Date.now());setProgress(rows=>[...rows.filter(r=>r.accountId!==item.accountId),item].sort((a,b)=>a.slot-b.slot));}}),[]);
 function move(id:string,delta:number){setSelected(ids=>{const next=[...ids],i=next.indexOf(id),j=i+delta;if(j>=0&&j<next.length)[next[i],next[j]]=[next[j],next[i]];return next;});}
 const selectedPreset=presets.find(p=>p.name===preset);const mismatch=!!selectedPreset&&selectedPreset.slots.length!==selected.length;
 const locked=busy||working;
 const groups=[...new Set(state?.accounts.map(a=>a.group).filter(Boolean)??[])].sort();
 const orderedAccounts=[...(state?.accounts??[])].sort((a,b)=>Number(b.favorite)-Number(a.favorite)||a.order-b.order);
 const playAccounts=orderedAccounts.filter(a=>!playGroupFilter||a.group===playGroupFilter);
 const visible=orderedAccounts.filter(a=>(!groupFilter||a.group===groupFilter)&&(!favoritesOnly||a.favorite)&&(statusFilter!=='signin'||needsSignIn(accountStatus(a)))&&[a.nickname,a.username,a.group].some(value=>(value??'').toLocaleLowerCase().includes(search.toLocaleLowerCase())));
 function editAccount(id:string){const a=state?.accounts.find(a=>a.id===id);if(!a)return;setFocused(id);setRenaming(undefined);setEditing(id);setNickname(a.nickname);setNotes(a.notes);setGroup(a.group);setColor(a.color);}
 useLayoutEffect(()=>{
  const next=new Map<string,{top:number;left:number}>();
  accountCards.current.forEach((card,id)=>{
   card.getAnimations().forEach(animation=>animation.cancel());
   const position={top:card.offsetTop,left:card.offsetLeft};next.set(id,position);
   const before=cardPositions.current.get(id);
   if(before&&!window.matchMedia('(prefers-reduced-motion: reduce)').matches){
    const x=before.left-position.left,y=before.top-position.top;
    if(x||y)card.animate([{transform:`translate(${x}px,${y}px)`},{transform:'translate(0,0)'}],{duration:360,easing:'cubic-bezier(.22,1,.36,1)'});
   }
  });cardPositions.current=next;
 },[state,search,groupFilter,favoritesOnly,statusFilter,mode,signInNeeded]);
 async function organize(account:AccountsStateDto['accounts'][number],patch:{group?:string;color?:string;favorite?:boolean;notes?:string}){
  const value=await request<AccountsStateDto>('accounts.organize',{id:account.id,group:account.group,notes:account.notes,color:account.color,favorite:account.favorite,...patch});setState(value);
 }
 async function changeGroup(ids:string[],name:string){
  try{for(const id of ids){const account=state?.accounts.find(a=>a.id===id);if(account)await organize(account,{group:name.trim()});}}
  finally{await refresh();}
 }
 async function connect(id?:string){operationKind.current='signin';controller.current=new AbortController();try{setState(await request<AccountsStateDto>('accounts.connect',{id,nickname:id?state?.accounts.find(a=>a.id===id)?.nickname??'':''},{signal:controller.current.signal}));}finally{controller.current=null;}}
 async function createAccounts(){operationKind.current='creation';controller.current=new AbortController();setCreationSaved(0);setCreationProgress(undefined);try{const before=new Set(state?.accounts.map(a=>a.id));const updated=await request<AccountsStateDto>('accounts.create',{}, {signal:controller.current.signal});setState(updated);setCreationSaved(updated.accounts.filter(a=>!before.has(a.id)).length);}finally{controller.current=null;await refresh();}}
 return <div className="stack">
  {error&&<p role="alert" className="error-banner">{error}</p>}
  {mode==='accounts'&&<div>  <section className="card"><div className="card-heading"><div><h2>Account library</h2><p><strong>{state?.accounts.length??0} total</strong> · <span className="signin-count">{state?.accounts.filter(a=>needsSignIn(accountStatus(a))).length??0} known to need sign-in</span></p></div><div className="button-row"><button className="primary" disabled={locked||!state?.available} onClick={()=>void act(()=>connect())}>Add Roblox account…</button><button disabled={locked||!state?.available||(state?.accounts.length??0)>=100} onClick={()=>void act(createAccounts)}>Create accounts…</button><button disabled={locked||!state?.available} onClick={()=>void act(async()=>{await request('accounts.credentials.open',{id:''});await refresh();})}>Unfinished signups…</button><button disabled={locked} onClick={()=>void act(refresh)}>Refresh accounts</button></div></div><div className="padded stack feature-content">
     {creationProgress&&<p role="status">Account creation: {creationProgress.status} · {creationProgress.completed} completed · {creationProgress.skipped} skipped · {creationProgress.remaining} remaining</p>}
   {working&&operationKind.current==='creation'&&<button onClick={()=>controller.current?.abort()}>Stop creation queue</button>}
   {creationSaved>0&&<div className="account-ready-callout"><div><strong>Ready to play</strong><span>{creationSaved} {creationSaved===1?'account saved':'accounts saved'}. Available in Play.</span></div><button onClick={()=>onPlay?.()}>Go to Play →</button></div>}
   {state&&!state.available&&<p role="alert">Account settings could not be read. Existing files have been preserved.</p>}
   {working&&controller.current&&operationKind.current==='signin'&&<div><button onClick={()=>controller.current?.abort()}>Cancel sign-in</button></div>}
   
   <div className="feature-form account-filters"><label>Search accounts<input value={search} maxLength={1000} onChange={e=>setSearch(e.target.value)} placeholder="Nickname, username or group"/></label><label>Status<ThemedSelect label="Filter account status" value={statusFilter} onChange={setStatusFilter} options={[{value:'all',label:'All statuses'},{value:'signin',label:'Needs sign-in'}]}/></label><label className="check"><input type="checkbox" checked={favoritesOnly} onChange={e=>setFavoritesOnly(e.target.checked)}/>Favorites only</label></div>
   <div className="account-groups" role="group" aria-label="Account groups"><button aria-pressed={!groupFilter} onClick={()=>setGroupFilter('')}>All <small>{state?.accounts.length??0}</small></button>{groups.map(name=><button key={name} aria-pressed={groupFilter===name} onClick={()=>setGroupFilter(name)}>{name} <small>{state?.accounts.filter(a=>a.group===name).length}</small></button>)}<button aria-expanded={manageGroups} disabled={locked} onClick={()=>setManageGroups(value=>!value)}>Manage groups</button></div>
   {manageGroups&&<div className="group-manager stack"><div className="feature-form"><label>Group name<input value={groupName} maxLength={40} disabled={locked} onChange={e=>setGroupName(e.target.value)} list="account-group-names"/></label><datalist id="account-group-names">{groups.map(name=><option key={name} value={name}/>)}</datalist><button disabled={locked||!managed.length||!groupName.trim()} onClick={()=>void act(async()=>{await changeGroup(managed,groupName);setGroupName('');})}>Assign selected to group</button><button disabled={locked||!managed.length} onClick={()=>void act(()=>changeGroup(managed,''))}>Ungroup selected</button></div>{groups.map(name=><div className="group-manager-row" key={name}><span><strong>{name}</strong> · {state?.accounts.filter(a=>a.group===name).length} accounts</span><div className="button-row"><button disabled={locked} onClick={()=>{setGroupFilter(name);setManaged(state?.accounts.filter(a=>a.group===name).map(a=>a.id)??[]);}}>Select group</button><button aria-label={'Rename group '+name} disabled={locked||!groupName.trim()||groupName.trim()===name} onClick={()=>void act(async()=>{const next=groupName.trim();await changeGroup(state?.accounts.filter(a=>a.group===name).map(a=>a.id)??[],next);if(groupFilter===name)setGroupFilter(next);setGroupName('');})}>Rename to entered name</button><button aria-label={'Remove group '+name} disabled={locked} onClick={()=>void act(async()=>{if(await confirm('Remove group',`Remove “${name}” and ungroup its accounts? Accounts and sign-in sessions will be kept.`)){await changeGroup(state?.accounts.filter(a=>a.group===name).map(a=>a.id)??[],'');if(groupFilter===name)setGroupFilter('');}})}>Remove group…</button></div></div>)}</div>}
   {state?.accounts.length===0&&<p>No accounts saved yet.</p>}
   {!!state?.accounts.length&&visible?.length===0&&<p>No accounts match these filters.</p>}
   <div className="account-workspace"><div className="account-list">
   {visible?.map(account=><div className={"account-row"+(account.favorite?" is-favorite":"")+(focused===account.id?" is-focused":"")} key={account.id} data-account-id={account.id} ref={node=>{if(node)accountCards.current.set(account.id,node);else accountCards.current.delete(account.id);}}><label className="selection-hit"><input type="checkbox" aria-label={'Select account '+(account.nickname||account.username)} checked={managed.includes(account.id)} disabled={locked} onChange={e=>setManaged(ids=>e.target.checked?[...ids,account.id]:ids.filter(id=>id!==account.id))}/></label><button className="account-identity" aria-label={'View '+(account.nickname||account.username)} aria-pressed={focused===account.id} onClick={()=>{setFocused(account.id);setEditing(undefined);setRenaming(undefined);setNickname('');}}><AccountAvatar account={account}/><span><strong>{account.nickname||account.username}</strong><small>@{account.username}</small></span></button><span className="account-status">{accountStatus(account)}{account.bypassOptimizations&&<small className="badge">Bypass enabled</small>}</span><span className="account-group-cell">{account.group?<span className="badge">{account.group}</span>:<small aria-label="No group">—</small>}</span><button className="favorite-toggle" aria-label={(account.favorite?'Unfavorite ':'Favorite ')+(account.nickname||account.username)} aria-pressed={account.favorite} disabled={locked} onClick={()=>void act(()=>organize(account,{favorite:!account.favorite}))}>{account.favorite?'★':'☆'}</button></div>)}
   </div><aside className="account-profile" aria-label="Account details">{profile?<div className="stack"><div className="profile-heading"><AccountAvatar account={profile}/><div>{renaming===profile.id?<form className="nickname-editor" onSubmit={event=>{event.preventDefault();if(locked||!nickname.trim())return;const id=profile.id;void act(async()=>{setState(await request<AccountsStateDto>('accounts.rename',{id,nickname:nickname.trim()}));setRenaming(undefined);});}} onKeyDown={event=>{if(event.key==='Escape'&&!locked){event.preventDefault();setRenaming(undefined);}}}><label>Local nickname<input autoFocus value={nickname} maxLength={60} disabled={locked} onChange={event=>setNickname(event.target.value)} aria-describedby="nickname-hint"/></label><div className="button-row"><button type="submit" aria-label="Save nickname" disabled={locked||!nickname.trim()}>Save</button><button type="button" aria-label="Cancel nickname" disabled={locked} onClick={()=>setRenaming(undefined)}>Cancel</button></div></form>:<h3><button className="profile-name-edit" disabled={locked} aria-label={'Edit nickname for '+profile.username} aria-describedby="nickname-hint" onClick={()=>{setNickname(profile.nickname||profile.username);setRenaming(profile.id);setEditing(undefined);}}>{profile.nickname||profile.username}</button></h3>}<p>@{profile.username}</p>{profile.badges?.map(b=><SpecialBadge key={b.id} badge={b}/>)}<small id="nickname-hint" className="muted">Click the name to edit your local nickname.</small></div></div>{accountStatus(profile)&&<p className="badge">{accountStatus(profile)}</p>}<p className="muted">Sign-in is verified when saved and when launching. Refresh reloads saved account information.</p><dl><dt>Group</dt><dd>{profile.group||'No group'}</dd></dl>{profile.notes&&<p>{profile.notes}</p>}<button disabled={locked} onClick={()=>void act(()=>connect(profile.id))}>Reconnect</button><button disabled={locked} onClick={()=>editAccount(profile.id)}>Organize</button><label className="check"><input type="checkbox" checked={profile.bypassOptimizations??false} disabled={locked} onChange={e=>{const bypass=e.target.checked;void act(async()=>setState(await request<AccountsStateDto>('accounts.optimizationBypass',{id:profile.id,bypass})));}}/>Bypass optimizations</label><small className="muted">Preserve this account’s original settings. Live controls restore immediately; any launch-time restoration needs a relaunch.</small>
   {editing===profile.id&&<div className="stack"><label>Group<input value={group} maxLength={40} list="existing-account-groups" onChange={e=>setGroup(e.target.value)} disabled={locked}/></label><datalist id="existing-account-groups">{groups.map(name=><option key={name} value={name}/>)}</datalist><label>Color (optional #RRGGBB)<input value={color} maxLength={7} onChange={e=>setColor(e.target.value)} disabled={locked}/></label><label>Notes<textarea value={notes} maxLength={1000} onChange={e=>setNotes(e.target.value)} disabled={locked}/></label><button disabled={locked} onClick={()=>void act(async()=>{await organize(profile,{group,color,notes});setEditing(undefined);})}>Save account details</button><button disabled={locked} onClick={()=>{setEditing(undefined);setNickname('');}}>Cancel edit</button></div>}
   <button className="danger-text" disabled={locked} onClick={()=>void act(async()=>{if(await confirm('Remove account','Clear this account’s local browser session? Already running games will stay open.')){setState(await request<AccountsStateDto>('accounts.remove',{id:profile.id,confirmed:true}));setSelected(ids=>ids.filter(id=>id!==profile.id));setManaged(ids=>ids.filter(id=>id!==profile.id));setFocused('');setEditing(undefined);}})}>Remove…</button><button className={profile.hasStoredCredentials?undefined:"credential-action-placeholder"} aria-hidden={!profile.hasStoredCredentials} tabIndex={profile.hasStoredCredentials?undefined:-1} disabled={locked||!profile.hasStoredCredentials} onClick={()=>void act(async()=>{await request('accounts.credentials.open',{id:profile.id});})}>View credentials…</button></div>:<div className="profile-empty"><h3>Account details</h3><p>Select a profile to view its details, reconnect or organize it.</p></div>}</aside></div>
  </div></section>
</div>}
  {mode==='play'&&<div className="stack">
  {state?.notice&&<p className="notice">{state.notice}</p>}
  <section className="card"><div className="card-heading"><div><h2 ref={chooseHeading} tabIndex={-1}>Choose accounts</h2><p>{changingAccounts?'Choose accounts below for this game. The next launch saves your selection for Play again.':'Select accounts in launch order. Favorites appear first.'}</p></div><div className="button-row"><span className="badge">{selected.length} selected</span>{changingAccounts&&<button disabled={locked} onClick={cancelChangeAccounts}>Cancel changes</button>}</div></div><div className="padded stack"><div className="account-groups" role="group" aria-label="Play account groups"><button aria-pressed={!playGroupFilter} onClick={()=>setPlayGroupFilter('')}>All <small>{orderedAccounts.length}</small></button>{groups.map(name=><button key={name} aria-pressed={playGroupFilter===name} onClick={()=>setPlayGroupFilter(name)}>{name} <small>{orderedAccounts.filter(a=>a.group===name).length}</small></button>)}</div><div className="button-row"><button disabled={locked||!playAccounts.some(a=>!selected.includes(a.id))} onClick={()=>setSelected(ids=>[...ids,...playAccounts.filter(a=>!ids.includes(a.id)).map(a=>a.id)])}>{playGroupFilter?'Select group':'Select all accounts'}</button><button disabled={locked||!playAccounts.some(a=>selected.includes(a.id))} onClick={()=>setSelected(ids=>ids.filter(id=>!playAccounts.some(a=>a.id===id)))}>{playGroupFilter?'Deselect group':'Deselect all accounts'}</button><span className="muted">Filtering keeps your launch selection and order.</span></div><div className="play-account-list">{playAccounts.map(account=><label className="check play-account" key={account.id}><input type="checkbox" aria-label={'Select account '+(account.nickname||account.username)} checked={selected.includes(account.id)} disabled={locked} onChange={e=>setSelected(ids=>e.target.checked?[...ids,account.id]:ids.filter(id=>id!==account.id))}/><span><strong>{account.nickname||account.username}{account.favorite?' ★':''}</strong><small>@{account.username} · {account.group||'No group'}</small></span></label>)}{state?.accounts.length===0&&<p>Add an account in Accounts to get started.</p>}{!!orderedAccounts.length&&!playAccounts.length&&<p>No accounts in this group.</p>}</div></div></section>
  <section className="card"><div className="card-heading"><div><h2>Launch selected accounts</h2><p>{state?.launchAvailable?'Launch accounts in the order below.':'Launching saved accounts is not implemented in this build.'}</p></div></div><div className="padded stack feature-content">
   <div className="feature-form"><label>Game URL or Place ID<input value={target} maxLength={2048} placeholder="https://www.roblox.com/games/…" onChange={e=>{targetRevision.current++;setTarget(e.target.value);setResolved(undefined);}} disabled={locked}/></label><label>Public server Job ID (optional)<input value={jobId} maxLength={64} onChange={e=>{targetRevision.current++;setJobId(e.target.value);setResolved(undefined);}} disabled={locked}/></label></div>
   <div className="button-row"><button disabled={locked||!target.trim()} onClick={()=>void act(checkTarget)}>Check game target</button>{resolved&&<span role="status">Place {resolved.placeId}{resolved.jobId?' · Selected public server':''}</span>}</div>
   {resolved?.game&&<GameCard game={resolved.game}/>}
   <label className="feature-select">Layout preset<ThemedSelect label="Account launch layout" value={arrangement|| (preset?'preset:'+preset:'')} disabled={locked} onChange={value=>{setArrangement(value.startsWith('preset:')?'':value);setPreset(value.startsWith('preset:')?value.slice(7):'');}} options={[{value:'',label:'No automatic layout'},{value:'Grid',label:'Grid'},{value:'Columns',label:'Columns'},...presets.map(p=>({value:'preset:'+p.name,label:p.name}))]}/></label>
   {mismatch&&<p role="alert">This preset needs {selectedPreset?.slots.length} accounts; {selected.length} are selected.</p>}
   {selected.map((id,i)=>{const account=state?.accounts.find(a=>a.id===id);return <div className="launch-order" key={id}><span>Slot {i+1}: {account?.nickname||account?.username}</span><div className="button-row"><button aria-label={'Move account '+(i+1)+' up'} disabled={locked||i===0} onClick={()=>move(id,-1)}>↑</button><button aria-label={'Move account '+(i+1)+' down'} disabled={locked||i===selected.length-1} onClick={()=>move(id,1)}>↓</button></div></div>;})}
   <div className="button-row"><button className="primary" disabled={locked||!state?.launchAvailable||!selected.length||!target.trim()||mismatch} onClick={()=>void act(()=>launchGame(target,jobId,true))}>Launch selected</button>{changingAccounts&&<button disabled={locked} onClick={cancelChangeAccounts}>Cancel changes</button>}{working&&controller.current&&operationKind.current==='launch'&&<button onClick={()=>controller.current?.abort()}>Cancel pending launches</button>}</div>
   {state&&!state.launchAvailable&&<p className="notice">Launch selected is disabled in this build, even after successful sign-in and game-target validation. Use Play on the Roblox website to start games. Saved accounts are not used for browser launches.</p>}
   {progress.map(row=><p role="status" key={row.accountId}>{state?.accounts.find(a=>a.id===row.accountId)?.nickname||'Slot '+row.slot}: {row.status} · {Math.floor(((row.elapsedMs??0)+(working&&['Queued','Launching','Preparing','Starting','Waiting for helper','Joining'].includes(row.status)?Math.max(0,now-(progressAt.current[row.accountId]??now)):0))/1000)}s{row.plannedServer?' · Group '+(1+[...new Set(progress.filter(p=>p.plannedServer).map(p=>p.plannedServer))].indexOf(row.plannedServer)):' · Normal matchmaking'}</p>)}
   {result&&<div role="status"><p>{result.layoutStatus}</p><p>Batch: {result.batch.status}</p>{result.batch.items.some(row=>row.status==='Unknown')&&<p className="notice">Roblox may have opened, but the selected account could not be confirmed. Check the game before retrying. No automatic layout was applied.</p>}{result.batch.items.some(row=>row.status==='NeedsSignIn')&&<p className="notice">Reconnect the affected account and complete any Roblox security challenge before trying again.</p>}{result.batch.items.map(row=><p key={row.accountId}>Slot {row.slot}: {row.status} ({row.code}){row.plannedServer?' · Assigned public server':' · Normal matchmaking'}{row.observedServer?(row.plannedServer?(row.observedServer===row.plannedServer?' · Assigned server confirmed':' · Different server observed'):' · Server observed'):' · Server not verified'}</p>)}</div>}
   <p className="muted">Automatic layouts require every selected account’s window to be verified. A failed or uncertain launch will not shift other accounts into its slot. Private-server links are not supported in this version.</p>
  </div></section>
  <section className="card"><div className="card-heading"><div><h2 ref={recentHeading} tabIndex={-1}>Recent games</h2><p>Replay your saved accounts and layout, or change accounts for the next launch.</p></div><div className="button-row"><button disabled={locked} onClick={()=>void loadRecent()}>Refresh recent games</button><button disabled={locked||!recent.length} onClick={()=>void act(async()=>setRecent(await request<GameCardDto[]>('games.clear')))}>Clear recent games</button></div></div><div className="padded game-library">
   {recentError&&<p role="alert">{recentError}</p>}
   {!recentError&&recent.length===0&&<p>No recent games yet. Games appear after a launch starts.</p>}
   {recent.map(game=>{
    const ids=game.accountIds??[];
    const missing=ids.some(id=>!state?.accounts.some(a=>a.id===id));
    const layout=presets.find(p=>p.name===game.preset);
    const layoutUnavailable=!!game.preset&&(!layout||layout.slots.length!==ids.length);
    return <div className="recent-game" key={game.placeId}><GameCard game={game}><p>Accounts: {ids.length?ids.map(id=>{const account=state?.accounts.find(a=>a.id===id);return account?.nickname||account?.username||'Removed account';}).join(', '):'Choose accounts for this older entry.'}</p>{(game.preset||game.arrangement)&&<p>Layout: {game.preset||game.arrangement}</p>}{missing&&<p className="notice">A saved account was removed. Change accounts before playing again.</p>}{layoutUnavailable&&<p className="notice">The saved layout changed or was removed. Change accounts to review the batch.</p>}</GameCard><div className="button-row recent-game-actions"><button disabled={locked||!state} aria-label={'Change accounts for '+game.name} onClick={()=>changeRecentAccounts(game)}>Change accounts…</button><button disabled={locked} aria-label={'Remove '+game.name+' from recent games'} onClick={()=>void act(async()=>setRecent(await request<GameCardDto[]>('games.remove',{placeId:game.placeId})))}>Remove</button><button className="primary" disabled={locked||!state?.launchAvailable||!ids.length||missing||layoutUnavailable} aria-label={'Play '+game.name+' again'} onClick={()=>void act(()=>launchGame(game.placeId,'',false,ids,game.preset??'',game.arrangement??''))}>Play again</button></div></div>;
   })}
  </div></section>
  </div>}
 </div>;
}

function GameCard({game,children}:{game:GameCardDto;children?:ReactNode}){
 return <article className="game-card">{game.artwork?.startsWith('data:image/')?<img src={game.artwork} alt={'Cover art for '+game.name}/>:<div className="game-art-placeholder" aria-label="Cover art unavailable">No cover art</div>}<div><h3>{game.name}</h3><small>Place {game.placeId}</small>{children}</div></article>;
}
