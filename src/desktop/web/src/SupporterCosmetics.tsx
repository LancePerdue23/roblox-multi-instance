import {useSupportCreditPreview} from './SupportCreditPreview';
import {creditTier} from './supportCredit';
import {useEffect,useId,useRef,useState,type CSSProperties} from 'react';
import {connected,request} from './bridge';
import {SupporterArtwork,supporterArtKinds,supporterArtColors,supporterConcepts} from './SupporterArtwork';
export const tiers=['Supporter','Supporter+','Super Supporter'] as const;
export const tierPricesUsd=[3,7,15] as const;
export const tierPricesRobux=[300,700,1500] as const;
const styleKey='rmi.supporter-style';
export function readSupporterStyle(){try{const value=JSON.parse(localStorage.getItem(styleKey)??'{}');return {motion:typeof value.motion==='boolean'?value.motion:true};}catch{return {motion:true};}}
function useSupporterStyle(){const [style,setStyle]=useState(readSupporterStyle);useEffect(()=>{const update=()=>setStyle(readSupporterStyle());window.addEventListener('supporter-style-changed',update);window.addEventListener('storage',update);return()=>{window.removeEventListener('supporter-style-changed',update);window.removeEventListener('storage',update);};},[]);return style;}
export function useSupporterColors(tier=0){const colors=supporterArtColors[tier]??supporterArtColors[0];return {'--heart':colors.color,'--heart-end':colors.end} as CSSProperties;}
export function SupportDevelopmentLink({onOpen,local=false}:{onOpen:()=>void;local?:boolean}){
 const style=useSupporterStyle();
 return <button className={'support-link support-development'+(local?' local-role-button':'')} aria-label="Support development" onClick={onOpen}><span className={'detailed-heart support-invitation'+(style.motion?'':' heart-still')}><svg className="sp-mark sp-invitation" viewBox="0 0 96 96" aria-hidden="true"><path className="sp-invitation-heart" d="M48 78C39 70 17 55 17 36c0-19 23-24 31-8 8-16 31-11 31 8 0 19-22 34-31 42Z"/><path className="sp-invitation-facets" d="m18 36 18 15 12 27 12-27 18-15M30 20l6 31 12-8 12 8 6-31M48 28v50M36 51h24"/><path className="sp-invitation-shine" d="M25 35c0-7 8-11 14-6"/><g className="sp-invitation-spark"><path d="m79 10 2 6 6 2-6 2-2 6-2-6-6-2 6-2Z"/><circle cx="17" cy="66" r="2"/></g></svg><span>Support development</span></span></button>;
}
export function HeartBadge({tier=0,motion,emblemOnly=false}:{tier?:number;motion?:boolean;emblemOnly?:boolean}){
 const saved=useSupporterStyle();motion??=saved.motion;
 const colors=supporterArtColors[tier]??supporterArtColors[0];
 return <span role={emblemOnly?'img':undefined} aria-label={emblemOnly?tiers[tier]:undefined} className={'heart-badge detailed-heart tier-'+tier+(motion?' heart-motion':' heart-still')} style={{'--heart':colors.color,'--heart-end':colors.end} as CSSProperties}><SupporterArtwork tier={tier}/>{!emblemOnly&&<span>{tiers[tier]??tiers[0]}</span>}</span>;
}
function useLocalSupporterRole(){
 const simulation=useSupportCreditPreview();
 const read=()=>{if(!__LOCAL_SUPPORTER_PREVIEW__)return -2;const value=Number(localStorage.getItem('rmi.local-role-preview')??'-2');return [-2,-1,0,1,2].includes(value)?value:-2;};
 const [role,setRole]=useState(read);useEffect(()=>{if(!__LOCAL_SUPPORTER_PREVIEW__)return;const update=()=>setRole(read());window.addEventListener('local-role-changed',update);return()=>window.removeEventListener('local-role-changed',update);},[]);return simulation!==null?creditTier(simulation):role;
}
function SupporterUnlock({tier,motion,openKey,allowed=true,preview=false,onClose}:{tier:number;motion:boolean;openKey:number;allowed?:boolean;preview?:boolean;onClose:()=>void}){
 const dialog=useRef<HTMLDialogElement>(null),opened=useRef(0),heading=useId();
 useEffect(()=>{if(!allowed){dialog.current?.close();return;}if(openKey>0&&opened.current!==openKey){opened.current=openKey;dialog.current?.showModal();}},[openKey,allowed]);
 const close=()=>dialog.current?.close();
 return <dialog ref={dialog} className={'support-unlock supporter-detailed-unlock tier-'+tier+(motion?'':' no-motion')} aria-labelledby={heading} onClose={onClose}><div key={openKey} className={'sp-unlock-scene sp-'+supporterArtKinds[tier]}><div className="sp-unlock-halo" aria-hidden="true"/><p className="sp-unlock-label">{preview?'UNLOCK PREVIEW':'STYLE PREVIEW'} · {supporterConcepts[tier]}</p><div className="sp-unlock-art"><SupporterArtwork tier={tier}/></div><h2 id={heading} className="sp-unlock-title">{tiers[tier]}</h2><p className="sp-unlock-caption">{['A little heart goes a long way.','A little more light. A little more love.','A whole lot of heart. Thank you for being super.'][tier]}</p><button autoFocus onClick={close}>Continue</button></div></dialog>;
}
export function LocalSupporterPreview({onOpen}:{onOpen:()=>void}){
 const simulation=useSupportCreditPreview();
 const role=useLocalSupporterRole(),style=useSupporterStyle(),colors=useSupporterColors(Math.max(role,0)),button=useRef<HTMLButtonElement>(null);const [replay,setReplay]=useState(0);
 useEffect(()=>{if(!__LOCAL_SUPPORTER_PREVIEW__)return;const unlock=(event:Event)=>{const tier=(event as CustomEvent).detail?.tier;if(simulation===null&&Number.isInteger(tier)&&tier>=0&&tier<=2&&localStorage.getItem('rmi.local-role-preview')===String(tier))setReplay(n=>n+1);};window.addEventListener('preview-supporter-unlock',unlock);return()=>window.removeEventListener('preview-supporter-unlock',unlock);},[simulation]);
 if(!__LOCAL_SUPPORTER_PREVIEW__||role< -1)return null;
 if(role===-1)return <SupportDevelopmentLink local onOpen={onOpen}/>;
 return <><button ref={button} className="support-link local-role-button sidebar-supporter" aria-label="Support development" style={colors} onClick={onOpen}><HeartBadge tier={role}/></button><SupporterUnlock tier={role} motion={style.motion} openKey={replay} preview onClose={()=>button.current?.focus()}/></>;
}
export function SupporterPrices(){return <div className="support-prices"><p>One-time contribution · permanent cosmetics. USD contributions accumulate. Robux recognition is verified separately.</p><table><thead><tr><th>Tier</th><th>USD</th><th>Robux</th></tr></thead><tbody>{tiers.map((name,index)=><tr key={name}><td>{name}</td><td>${tierPricesUsd[index]}</td><td>{tierPricesRobux[index].toLocaleString('en-US')}</td></tr>)}</tbody></table></div>;}
export function SupporterAppearance(){
 const saved=useSupporterStyle();
 function save(motion:boolean){try{localStorage.setItem(styleKey,JSON.stringify({motion}));}catch{}window.dispatchEvent(new Event('supporter-style-changed'));}
 return <div className="studio-options"><h3>Make it yours</h3><label className="check"><input type="checkbox" checked={saved.motion} onChange={event=>save(event.target.checked)}/>Enable animation</label></div>;
}
export function SupporterStudio({preview=false,unlockedTier=-1,reveal=0,robuxCredit,robuxTier=-1}:{preview?:boolean;unlockedTier?:number;reveal?:number;robuxCredit?:number;robuxTier?:number}){
 preview=__LOCAL_SUPPORTER_PREVIEW__&&preview;
 const localRole=useLocalSupporterRole();
 const availableTier=preview?2:__LOCAL_SUPPORTER_PREVIEW__&&localRole!==-2?localRole:unlockedTier;
 const [tier,setTier]=useState(0),[motion,setMotion]=useState(()=>readSupporterStyle().motion),[theme,setTheme]=useState('Dark');
 const [replay,setReplay]=useState(0);const trigger=useRef<HTMLButtonElement>(null);
 useEffect(()=>{if(preview&&connected())void request('host.ready').catch(()=>{});},[preview]);
 useEffect(()=>{if(preview)document.documentElement.dataset.theme=theme;},[preview,theme]);
 useEffect(()=>{try{localStorage.setItem(styleKey,JSON.stringify({motion}));}catch{}window.dispatchEvent(new Event('supporter-style-changed'));},[motion]);
 const lastReveal=useRef(0);
 useEffect(()=>{if(reveal>0&&reveal!==lastReveal.current&&unlockedTier>=0){lastReveal.current=reveal;setTier(unlockedTier);setReplay(value=>value+1);}},[reveal,unlockedTier]);
 function unlock(){if(tier>availableTier)return;setReplay(value=>value+1);}
 const content=<>
  <div className="studio-heading"><div><span className="eyebrow">{preview?'COSMETIC PREVIEW':'SUPPORTER STYLES'}</span><h2>{preview?'A little heart. A bigger thank-you.':'Meet the supporter tiers'}</h2><p>Ember, Orbit and Radiant.</p></div>{preview&&<label>Appearance<select value={theme} onChange={e=>setTheme(e.target.value)}><option>Dark</option><option>Light</option><option>WarmNeutral</option></select></label>}</div>
  <div className="tier-gallery">{tiers.map((name,index)=><button key={name} className={'tier-choice'+(tier===index?' chosen':'')} aria-pressed={tier===index} onClick={()=>setTier(index)}><HeartBadge tier={index} motion={motion} emblemOnly/><strong>{name}</strong><small>{['A warm heart with a little spark.','A prism heart with a comet orbit.','A golden heart with unfolding wings.'][index]}</small><span className="tier-price">{robuxCredit===undefined?('$'+tierPricesUsd[index]+' USD or '+tierPricesRobux[index].toLocaleString('en-US')+' Robux'):index<=Math.max(robuxTier,unlockedTier)?'Earned':Math.max(0,tierPricesRobux[index]-robuxCredit).toLocaleString('en-US')+' more Robux'}</span><small>Permanent cosmetics</small></button>)}</div>
  <div className="studio-workbench"><div className="cosmetic-stage"><div className="cosmetic-stage-lines" aria-hidden="true"/><span className="eyebrow">YOUR BADGE</span><HeartBadge tier={tier} motion={motion} emblemOnly/><p>{tiers[tier]}</p><small>{supporterConcepts[tier]}</small></div><div className="studio-options"><h3>Make it yours</h3><label className="check"><input type="checkbox" checked={motion} onChange={e=>setMotion(e.target.checked)}/>Enable animation</label><p className="muted">Ember, Orbit and Radiant use their own artwork, colors and animation sequences. Reduced-motion preferences are always respected.</p><div className="unlock-replay-slot">{tier<=availableTier&&<button ref={trigger} onClick={unlock}>Replay {tiers[tier]} unlock</button>}</div></div></div>
  <SupporterUnlock tier={tier} motion={motion} openKey={replay} allowed={tier<=availableTier} preview={preview} onClose={()=>trigger.current?.focus()}/>
 </>;
 return preview?<div className="support-preview-shell"><header><span>Roblox Multi Instance</span><span className="badge">Preview shell · no purchases</span></header><main>{content}</main><footer>All tiers are available here for preview. Your regular app and supporter status are unchanged.</footer></div>:<section className="support-studio">{content}</section>;
}
