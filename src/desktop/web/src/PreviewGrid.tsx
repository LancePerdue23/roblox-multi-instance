import {useEffect,useRef,useState} from 'react';
import {request,subscribe} from './bridge';
import type {InstanceDto,PreviewStateDto} from './backend.generated';

function saved(key:string,fallback:string,allowed:string[]){try{const value=localStorage.getItem(key);return value!==null&&allowed.includes(value)?value:fallback;}catch{return fallback;}}
export function PreviewGrid({rows,selected,toggle,focus,disabled}:{rows:InstanceDto[];selected:string[];toggle:(id:string)=>void;focus:(id:string)=>void;disabled:boolean}){
 const [privacy,setPrivacy]=useState(()=>saved('preview-privacy','false',['true','false'])==='true'),[paused,setPaused]=useState(false);
 const [fit,setFit]=useState(()=>saved('preview-fit','cover',['cover','contain']));
 const [rate,setRate]=useState(()=>{try{const saved=Number(localStorage.getItem('preview-fps'));return [2,5,15].includes(saved)?saved:2;}catch{return 2;}}),[density,setDensity]=useState(()=>saved('preview-density','Comfortable',['Comfortable','Compact']));
 useEffect(()=>{try{localStorage.setItem('preview-fps',String(rate));}catch{}},[rate]);
 useEffect(()=>{try{localStorage.setItem('preview-privacy',String(privacy));localStorage.setItem('preview-fit',fit);localStorage.setItem('preview-density',density);}catch{}},[privacy,fit,density]);
 const [retry,setRetry]=useState(0),[retrying,setRetrying]=useState(false);
 const [state,setState]=useState<PreviewStateDto>({supported:true,sources:[]}),[error,setError]=useState('');
 const [visible,setVisible]=useState<string[]>([]),[documentVisible,setDocumentVisible]=useState(!document.hidden);
 const container=useRef<HTMLDivElement>(null);
 const cards=rows,key=JSON.stringify(cards.map(row=>row.id));
 const active=!privacy&&!paused&&documentVisible;
 const activeRef=useRef(active);activeRef.current=active;
 useEffect(()=>{const handler=()=>setDocumentVisible(!document.hidden);document.addEventListener('visibilitychange',handler);return()=>document.removeEventListener('visibilitychange',handler);},[]);
 useEffect(()=>{
  const inView=new Set<string>();setVisible([]);
  const observer=new IntersectionObserver(entries=>{for(const entry of entries){const id=(entry.target as HTMLElement).dataset.previewId!;entry.isIntersecting?inView.add(id):inView.delete(id);}setVisible([...inView]);});
  container.current?.querySelectorAll('[data-preview-id]').forEach(node=>observer.observe(node));
  return()=>observer.disconnect();
 },[key]);
 function retryPreview(){if(retrying)return;setRetrying(true);setRetry(value=>value+1);}
 const captureIds=JSON.stringify(cards.filter(row=>row.hasWindow&&visible.includes(row.id)).map(row=>row.id));
 useEffect(()=>subscribe(event=>{if(event.event==='previews.frames'&&activeRef.current)setState(event.data as PreviewStateDto);}),[]);
 useEffect(()=>{
  let obsolete=false,pending=false;
  setState({supported:true,sources:[]});setError('');
  const configure=async()=>{
   if(pending)return;pending=true;
   try{const next=await request<PreviewStateDto>('previews.configure',{ids:JSON.parse(captureIds),rate});if(!obsolete)setState(next);}
   catch(e){if(!obsolete)setError(e instanceof Error?e.message:'Previews unavailable.');}finally{pending=false;}
  };
  if(active)void configure().finally(()=>{if(!obsolete)setRetrying(false);});else void request('previews.stop').catch(()=>{});
  const timer=active?setInterval(()=>void configure(),3000):undefined;
  return()=>{obsolete=true;clearInterval(timer);void request('previews.stop').catch(()=>{});};
 },[active,captureIds,rate,retry]);
 return <div className="preview-panel">
  <div className="preview-controls">
   <div className="preview-control"><span>Privacy</span><div className="preview-segments" role="group" aria-label="Privacy"><button aria-label="Show previews and names" aria-pressed={!privacy} onClick={()=>setPrivacy(false)}>Visible</button><button aria-label="Hide previews and names" aria-pressed={privacy} onClick={()=>setPrivacy(true)}>Hidden</button></div></div>
   <div className="preview-control"><span>Rendering</span><div className="preview-segments" role="group" aria-label="Rendering"><button aria-label="Resume previews" aria-pressed={!paused} onClick={()=>setPaused(false)}>Running</button><button aria-label="Pause previews" aria-pressed={paused} onClick={()=>setPaused(true)}>Paused</button></div></div>
   <div className="preview-rate-control"><span>Preview rate</span><div className="preview-segments fps-segments" role="group" aria-label="Preview rate">{[2,5,15].map(value=><button key={value} aria-pressed={rate===value} aria-label={value+' FPS'} onClick={()=>setRate(value)}>{value}<small>FPS</small></button>)}</div></div>
   <div className="preview-control"><span>Preview fit</span><div className="preview-segments" role="group" aria-label="Preview fit">{[{value:'cover',label:'Fill card'},{value:'contain',label:'Whole window'}].map(option=><button key={option.value} aria-pressed={fit===option.value} onClick={()=>setFit(option.value)}>{option.label}</button>)}</div></div>
   <div className="preview-control"><span>Card size</span><div className="preview-segments" role="group" aria-label="Card size">{['Comfortable','Compact'].map(value=><button key={value} aria-pressed={density===value} onClick={()=>setDensity(value)}>{value}</button>)}</div></div>
   {active&&error&&<button disabled={retrying} onClick={retryPreview}>{retrying?'Retrying…':'Retry previews'}</button>}
  </div>
  {error&&<p role="alert">{error}</p>}
  {active&&!state.supported&&<p role="status">Window capture is unavailable on this system. Table view remains available.</p>}
  <div ref={container} className={'preview-grid '+(density==='Compact'?'compact':'')}>
   {cards.map((row,index)=>{
    const source=state.sources?.find(item=>item.id===row.id);
    const status=privacy?'Hidden':paused||!documentVisible?'Paused':!row.hasWindow?'No window':!state.supported?'Unsupported':source?.status??'Waiting';
    const label=privacy?'Client '+(index+1):row.username??row.title??'Roblox';
    return <article key={row.id} data-preview-id={row.id} className={'preview-card '+(selected.includes(row.id)?'selected':'')}>
     <div className="preview-image">{active&&source?.url?<img style={{objectFit:fit as 'cover'|'contain'}} src={source.url} alt={'Live preview of '+label}/>:<span>{status}</span>}</div>
     <div className="preview-card-body"><label className="check"><input type="checkbox" aria-label={'Select '+label} disabled={!row.hasWindow} checked={selected.includes(row.id)} onChange={()=>toggle(row.id)}/><strong title={label}>{label}</strong></label>
      {row.bypassOptimizations&&<small className="badge">Bypass enabled</small>}<small title={status==='Capacity limit'?'Capture capacity reached. Use fewer visible cards or smaller client windows, then retry previews.':status==='Unavailable'?'Capture could not start. Check the client window, then retry previews.':!privacy&&source?.updated?'Updated '+new Date(source.updated).toLocaleTimeString():undefined}>{status}</small>
      {active&&status==='Minimized'&&<button disabled={disabled} aria-label={'Resume preview for '+label} onClick={()=>focus(row.id)}>Resume preview</button>}
      {active&&row.hasWindow&&['Unavailable','Capacity limit','Closed','Stale'].includes(status)&&<button disabled={retrying} aria-label={'Retry preview for '+label} onClick={retryPreview}>Retry</button>}
      <button disabled={disabled||!row.hasWindow} aria-label={'Focus '+label} onClick={()=>focus(row.id)}>Focus</button>
     </div>
    </article>;
   })}
  </div>
 </div>;
}
