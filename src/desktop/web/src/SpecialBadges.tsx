import {useEffect,useId,useRef,useState,type CSSProperties} from 'react';
import {connected,request,subscribe} from './bridge';
import {RecognitionSymbol,RecognitionTheatre,recognitionArtwork} from './RecognitionArtwork';
export interface Recognition {id:string;title:string;robloxUserId:string}
export const badgeKinds=[
 {id:'alpha-tester',title:'Alpha Tester',description:'Here from the beginning.',unlock:'You were here before the crowd.',details:'The first windows, a guiding star. Hover to bring the constellation to life.'},
 {id:'bug-hunter',title:'Bug Hunter',description:'A sharp eye. A better app.',unlock:'Every bug you uncover makes the workspace stronger.',details:'A green beetle with a flutter of wings. Hover to acquire, lock on and fire.'},
 {id:'contributor',title:'Contributor',description:'Built better, together.',unlock:'Your work is part of what makes Multi Instance possible.',details:'Two branches. One shared result. Hover to send your contribution through the merge.'},
 {id:'founder',title:'Founder',description:'The original recipe.',unlock:'One taco. A big idea. The beginning of Multi Instance.',details:'Fresh ingredients, a golden shell and the original crown. Hover for a little extra seasoning.'}
];
const palettes=[{name:'Nebula',start:'#bba1ff',end:'#f5cf78'},{name:'Aurora',start:'#65e2d1',end:'#b6a1ff'},{name:'Solar',start:'#ffd380',end:'#ff9bb8'},{name:'Glacier',start:'#87cfff',end:'#d8c5ff'}];
export function badgeHasPalette(id:string){return id!=='bug-hunter'&&id!=='founder';}
function badgeColors(id:string,palette:number){
 if(id==='bug-hunter')return {start:'#8cdea2',end:'#c6f8b0'};
 if(id==='founder')return {start:'#f4c36f',end:'#f9e0a1'};
 return palettes[palette];
}
function colorVariables(id:string,palette:number){const colors=badgeColors(id,palette);return {'--alpha-start':colors.start,'--alpha-end':colors.end} as CSSProperties;}
export function uniqueBadges(badges:Recognition[]){return [...new Map(badges.map(b=>[b.id,b])).values()];}
const key='rmi.special-badge-style';
export function readBadgeStyle(id='alpha-tester'){
 try{const stored=JSON.parse(localStorage.getItem(key)??'{}');const value=stored.badges?.[id]??(id==='alpha-tester'?stored:{});return {palette:Number.isInteger(value.palette)&&value.palette>=0&&value.palette<4?value.palette:0,motion:value.motion!==false,visible:value.visible!==false};}
 catch{return {palette:0,motion:true,visible:true};}
}
function useBadgeStyle(id='alpha-tester'){
 const [saved,setSaved]=useState(()=>({id,value:readBadgeStyle(id)}));
 useEffect(()=>{const update=()=>setSaved({id,value:readBadgeStyle(id)});update();window.addEventListener('recognition-style-changed',update);window.addEventListener('storage',update);return()=>{window.removeEventListener('recognition-style-changed',update);window.removeEventListener('storage',update);};},[id]);
 return saved.id===id?saved.value:readBadgeStyle(id);
}
export function SpecialBadge({badge,preview=false,emblemOnly=false}:{badge:Pick<Recognition,'id'|'title'>;preview?:boolean;emblemOnly?:boolean}){
 const style=useBadgeStyle(badge.id);if(!preview&&!style.visible)return null;
 return <span role="img" aria-label={badge.title} tabIndex={preview&&!emblemOnly?0:undefined} className={'special-badge kind-'+badge.id+' artwork-'+recognitionArtwork(badge.id)+' design-native'+(style.motion?' special-motion':'')} style={colorVariables(badge.id,style.palette)}>
  <svg className="badge-emblem" viewBox="0 0 96 96" fill="none" aria-hidden="true"><RecognitionSymbol id={badge.id}/></svg>
  {!emblemOnly&&<span className="badge-lettering"><span className="badge-title">{badge.title}</span></span>}
 </span>;
}
export function SidebarBadge({badge,local=false}:{badge:Recognition;local?:boolean}){
 const style=useBadgeStyle(badge.id),tooltip=useId();
 return <button type="button" className={(local?'local-role-button ':'')+'local-special-button'} aria-label={local&&badge.id==='alpha-tester'?'Customize special badges':'Customize '+badge.title+' badge'} aria-describedby={tooltip} hidden={!style.visible} onClick={()=>window.dispatchEvent(new CustomEvent('open-special-badges',{detail:{badgeId:badge.id}}))}><SpecialBadge badge={badge} emblemOnly/><span id={tooltip} className="sidebar-badge-name" role="tooltip">{badge.title}</span></button>;
}
export function SidebarRecognition({badges}:{badges:Recognition[]}){
 const [,refresh]=useState(0),[capacity,setCapacity]=useState(4);const row=useRef<HTMLDivElement>(null);
 useEffect(()=>{const update=()=>refresh(n=>n+1);window.addEventListener('recognition-style-changed',update);window.addEventListener('storage',update);return()=>{window.removeEventListener('recognition-style-changed',update);window.removeEventListener('storage',update);};},[]);
 const visible=uniqueBadges(badges).filter(b=>readBadgeStyle(b.id).visible),shown=visible.length>capacity?visible.slice(0,capacity-1):visible,extra=visible.length-shown.length;
 useEffect(()=>{if(!row.current)return;const observer=new ResizeObserver(entries=>setCapacity(Math.max(1,Math.min(4,Math.floor((entries[0].contentRect.width+5)/41)))));observer.observe(row.current);return()=>observer.disconnect();},[visible.length]);
 if(!visible.length)return null;
 return <div ref={row} className="sidebar-recognition" aria-label="Your special badges">{shown.map(b=><SidebarBadge key={b.id} badge={b} local={b.robloxUserId==='local-preview'}/>)}{extra>0&&<button className="sidebar-badge-overflow" aria-label={'View '+extra+' more badges'} title="View your badge collection" onClick={()=>window.dispatchEvent(new CustomEvent('open-special-badges',{detail:{badgeId:visible[shown.length].id}}))}>+{extra}</button>}</div>;
}
function BadgeChoice({badge,selected,onSelect,editorId}:{badge:Recognition;selected:boolean;onSelect:()=>void;editorId:string}){
 const style=useBadgeStyle(badge.id);
 return <button type="button" className="badge-collection-choice" aria-label={'Select '+badge.title+' badge'} aria-pressed={selected} aria-controls={editorId} onClick={onSelect}>
  <SpecialBadge badge={badge} preview emblemOnly/><span className="badge-choice-title">{badge.title}</span><span className="badge-choice-status">{style.visible?'Shown':'Hidden'}</span>{selected&&<span className="badge-choice-check" aria-hidden="true">✓</span>}
 </button>;
}
export function useRecognition(){
 const [badges,setBadges]=useState<Recognition[]>([]);
 useEffect(()=>{if(connected())void request<{badges:Recognition[]}>('support.badges').then(s=>setBadges(Array.isArray(s?.badges)?s.badges:[])).catch(()=>{});return subscribe(event=>{if(event.event==='badges.changed')setBadges((event.data as {badges:Recognition[]}).badges);});},[]);
 return badges;
}
export function useLocalBadges(){
 const read=()=>{if(!__LOCAL_SUPPORTER_PREVIEW__)return [];const role=Number(localStorage.getItem('rmi.local-role-preview')??'-2');const kinds=role===7?badgeKinds:Number.isInteger(role)&&role>=3&&role<=6?[badgeKinds[role-3]]:[];let extras:unknown=[];try{extras=JSON.parse(localStorage.getItem('rmi.local-extra-badges')??'[]');}catch{}const additional=Array.isArray(extras)?badgeKinds.filter(b=>extras.includes(b.id)):[];return [...new Map([...kinds,...additional].map(b=>[b.id,b])).values()].map(b=>({...b,robloxUserId:'local-preview'}));};
 const [badges,setBadges]=useState<Recognition[]>(read);
 useEffect(()=>{if(!__LOCAL_SUPPORTER_PREVIEW__)return;const update=()=>setBadges(read());window.addEventListener('local-role-changed',update);return()=>window.removeEventListener('local-role-changed',update);},[]);
 return badges;
}
export function BadgeStudio({badges,localPreview=false,revealKey=0,requestedBadgeId,requestKey=0,onRevealConsumed}:{badges:Recognition[];localPreview?:boolean;revealKey?:number;requestedBadgeId?:string;requestKey?:number;onRevealConsumed?:()=>void}){
 const available=uniqueBadges(badges),editorId=useId();
 const [selected,setSelected]=useState(available[0]?.id??'');
 const badge=available.find(b=>b.id===selected)??available[0],id=badge?.id??'alpha-tester',value=useBadgeStyle(id);
 const [replay,setReplay]=useState(0),dialog=useRef<HTMLDialogElement>(null),trigger=useRef<HTMLButtonElement>(null),opened=useRef(0);
 const kind=badgeKinds.find(b=>b.id===id);
 useEffect(()=>{if(requestedBadgeId)setSelected(requestedBadgeId);},[requestedBadgeId,requestKey,revealKey]);
 useEffect(()=>{dialog.current?.close();},[id]);
 useEffect(()=>{if(revealKey===0)opened.current=0;if(revealKey>0&&opened.current!==revealKey&&(!requestedBadgeId||id===requestedBadgeId)){opened.current=revealKey;setReplay(r=>r+1);dialog.current?.showModal();onRevealConsumed?.();}},[revealKey,requestedBadgeId,id,onRevealConsumed]);
 function save(patch:Partial<typeof value>){try{const stored=JSON.parse(localStorage.getItem(key)??'{}');localStorage.setItem(key,JSON.stringify({...stored,badges:{...stored.badges,[id]:{...value,...patch}}}));}catch{}window.dispatchEvent(new Event('recognition-style-changed'));}
 if(!badge)return null;
 return <section className="card recognition-page">
  <div className="card-heading"><div><h2>Your badges</h2><p>{kind?.description??'Recognition you have earned. Make each one your own.'}</p></div>{localPreview&&<span className="badge">Local preview</span>}</div>
  <div className="padded">
   <div className="badge-collection" role="group" aria-label="Your badge collection">{available.map(b=><BadgeChoice key={b.id} badge={b} selected={b.id===id} onSelect={()=>setSelected(b.id)} editorId={editorId}/>)}</div>
   <div id={editorId} className="recognition-workbench" role="region" aria-label={'Customize '+badge.title}>
    <div style={colorVariables(id,value.palette)} className={'recognition-stage artwork-'+recognitionArtwork(id)+(id==='bug-hunter'?' hunter-stage':'')}>
     <div className="cosmetic-stage-lines" aria-hidden="true"/><span className="eyebrow">YOUR BADGE</span><SpecialBadge badge={badge} preview/>{!value.visible&&<small>Hidden on your accounts and sidebar</small>}
    </div>
    <div className="recognition-controls"><h3>Make it yours</h3>
     {badgeHasPalette(id)&&<><span className="muted">Color palette</span><div className="recognition-palettes">{palettes.map((p,i)=><button key={p.name} aria-pressed={value.palette===i} onClick={()=>save({palette:i})}><i style={{background:p.start}}/>{p.name}</button>)}</div></>}
     <label className="check"><input type="checkbox" checked={value.visible} onChange={e=>save({visible:e.target.checked})}/>Show this badge</label>
     <label className="check"><input type="checkbox" checked={value.motion} onChange={e=>save({motion:e.target.checked})}/>Enable badge animations</label>
     <p className="muted">{kind?.details??'The first windows, made your own.'} Your icon appears in the sidebar and on your accounts.</p>
     <button ref={trigger} className="recognition-replay" onClick={()=>{setReplay(r=>r+1);dialog.current?.showModal();}}>Replay unlock</button>
    </div>
   </div>
   <dialog className={'recognition-unlock kind-'+id+' artwork-'+recognitionArtwork(id)+' unlock-native'+(value.motion?'':' recognition-static')} style={colorVariables(id,value.palette)} ref={dialog} aria-label={badge.title+' unlock'} onClose={()=>trigger.current?.focus()}>
    <div key={replay} className="recognition-arrival">
     <RecognitionTheatre id={id}/><span className="eyebrow">{localPreview?'LOCAL PREVIEW':'RECOGNITION UNLOCKED'}</span>
     <SpecialBadge badge={badge} preview emblemOnly/><h2>{badge.title}</h2><p>{kind?.unlock??'Thank you for making a difference.'}</p>
     <button autoFocus onClick={()=>dialog.current?.close()}>Continue</button>
    </div>
   </dialog>
  </div>
 </section>;
}
