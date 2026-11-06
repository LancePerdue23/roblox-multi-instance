import {useEffect,useState,type ReactNode} from 'react';
import {connected,request} from './bridge';
import {ThemedSelect} from './ThemedSelect';
import type {OptimizationStateDto,PerformanceProfileDto,PerformanceProfilesDto} from './backend.generated';

export function builtInProfile(id:string):PerformanceProfileDto {
 const base:PerformanceProfileDto={id,name:'Roblox defaults',description:'Preserve your original Roblox settings.',cpuPriority:'unchanged',ecoQos:'unchanged',fpsCap:null,graphicsQuality:null,textureQuality:'unchanged',minimumMsaa:false,disableGrass:false,reducedGeometry:false,graySky:false,windowBehavior:'unchanged'};
 if(id==='balanced')return {...base,name:'Balanced',description:'Keep background accounts lighter while you play.',cpuPriority:'belowNormal',ecoQos:'off',fpsCap:30,graphicsQuality:1,textureQuality:'reduced',minimumMsaa:true};
 if(id==='many-accounts')return {...base,name:'Maximum instances',description:'Prioritize capacity for background accounts.',cpuPriority:'belowNormal',ecoQos:'on',fpsCap:10,graphicsQuality:1,textureQuality:'lowest',minimumMsaa:true,disableGrass:true,reducedGeometry:true,graySky:true,windowBehavior:'smallMinimized'};
 return base;
}
const fpsStops=[10,15,20,30,45,60,90,120,144,240];
function SettingRow({title,description,children}:{title:string;description:string;children:ReactNode}){return <div className="performance-setting-row"><div><strong>{title}</strong><p>{description}</p></div><div className="performance-setting-control">{children}</div></div>;}
function SettingSwitch({label,checked,disabled,onChange}:{label:string;checked:boolean;disabled:boolean;onChange:(value:boolean)=>void}){return <button type="button" className="performance-switch" role="switch" aria-label={label} aria-checked={checked} disabled={disabled} onClick={()=>onChange(!checked)}><span/></button>;}
const ids=['full-quality','balanced','many-accounts'];
export function PerformancePresets({profiles,saved,locked,onChange,onApply}:{profiles?:PerformanceProfilesDto;saved?:PerformanceProfilesDto;locked:boolean;onChange:(value:PerformanceProfilesDto)=>void;onApply:()=>void}) {
 const [state,setState]=useState<OptimizationStateDto>(),[error,setError]=useState('');
 useEffect(()=>{
  let disposed=false;let timer:ReturnType<typeof setTimeout>|undefined;
  async function tick(){
   if(!document.hidden&&connected())try {const value=await request<OptimizationStateDto>('performance.optimizationState');if(!disposed){setState(value);setError('');}}catch(e){if(!disposed)setError(e instanceof Error?e.message:'Optimization status unavailable.');}
   if(!disposed)timer=setTimeout(tick,2000);
  }
  void tick();return()=>{disposed=true;clearTimeout(timer);};
 },[saved]);
 const profile=profiles?.profiles.find(p=>p.id===profiles.selectedProfileId);
 const standard=profile&&builtInProfile(profile.id);
 const custom=!!profile&&((profiles?.focusBoost??true)!==true||!ids.includes(profile.id)||Object.keys(standard!).some(key=>!['id','name','description'].includes(key)&&profile[key as keyof PerformanceProfileDto]!==standard![key as keyof PerformanceProfileDto]));
 function edit(patch:Partial<PerformanceProfileDto>){if(profiles&&profile)onChange({...profiles,profiles:profiles.profiles.map(p=>p.id===profile.id?{...p,...patch}:p)});}
 function choose(id:string){if(!profiles)return;const preset=builtInProfile(id);onChange({...profiles,selectedProfileId:id,focusBoost:true,profiles:profiles.profiles.some(p=>p.id===id)?profiles.profiles.map(p=>p.id===id?preset:p):[...profiles.profiles,preset]});}
 return <section className="card performance-presets" aria-label="Performance optimizations">
  <div className="card-heading"><div><h2>Performance optimizations</h2><p>One policy for your managed accounts. Exempt your main account in its profile.</p></div><span className="badge">{saved?.enabled?'Active':'Inactive'}{custom?' · Custom':''}</span></div>
  <div className="padded stack">
   <div className="performance-preset-grid" role="group" aria-label="Optimization presets">{ids.map(id=>{const preset=builtInProfile(id);return <button key={id} className="performance-preset" disabled={locked} aria-pressed={profiles?.selectedProfileId===id&&!custom} onClick={()=>choose(id)}><strong>{preset.name}</strong><p>{preset.description}</p><ul>{id==='full-quality'?<><li>Restore app-owned live settings</li><li>Preserve your Roblox preferences</li></>:<><li>{id==='balanced'?'Lower background CPU priority':'Background efficiency mode'}</li><li>{id==='balanced'?'Keep your window placement':'Small, minimized background windows'}</li><li>{preset.fpsCap} FPS and lighter visuals requested</li></>}</ul></button>;})}</div>
   <p className="notice">FPS and graphics apply to the next account launch. Existing games keep their current FPS and graphics; live background controls apply now.</p>
   {profile&&<div className="performance-tuning">
    <div className="performance-tuning-heading"><h3>Fine-tune</h3><span className="muted">{custom?'Custom settings':profile.name}</span></div>
    <SettingRow title="FPS cap" description="Choose a common limit or enter an exact value. Applies when you next launch this account.">
     <div className="performance-fps-control"><div className="performance-fps-track"><input aria-label="Preset FPS cap" type="range" min="0" max={fpsStops.length-1} step="1" value={fpsStops.reduce((best,value,index)=>Math.abs(value-(profile.fpsCap??60))<Math.abs(fpsStops[best]-(profile.fpsCap??60))?index:best,0)} aria-valuetext={profile.fpsCap===null?'Original setting':profile.fpsCap+' FPS'} disabled={locked||profile.fpsCap===null} onChange={e=>edit({fpsCap:fpsStops[Number(e.target.value)]})}/><div className="performance-fps-stops" aria-hidden="true">{fpsStops.map(value=><span key={value}><i/>{[10,30,60,120,240].includes(value)?value:''}</span>)}</div></div><label className="performance-fps-exact"><input aria-label="Exact FPS cap" type="number" min="10" max="240" value={profile.fpsCap??''} placeholder="Original" disabled={locked||profile.fpsCap===null} onChange={e=>{const value=Number(e.target.value);if(Number.isInteger(value)&&value>=10&&value<=240)edit({fpsCap:value});}}/><span>FPS</span></label></div>
     <label className="check performance-cap-enabled"><input type="checkbox" checked={profile.fpsCap!==null} disabled={locked} onChange={e=>edit({fpsCap:e.target.checked?60:null})}/>Use FPS cap</label>
    </SettingRow>
    <details open className="performance-settings-group"><summary>Background behavior</summary>
     <SettingRow title="CPU priority" description="Give background clients less scheduling priority. This does not impose a CPU percentage limit."><ThemedSelect label="Automatic CPU priority" value={profile.cpuPriority} disabled={locked} onChange={value=>edit({cpuPriority:value})} options={[{value:'unchanged',label:'Preserve original'},{value:'belowNormal',label:'Below normal'},{value:'idle',label:'Idle'},{value:'normal',label:'Normal'}]}/></SettingRow>
     <SettingRow title="Windows efficiency mode" description="Favor power-efficient background processing where Windows supports it."><ThemedSelect label="Automatic efficiency mode" value={profile.ecoQos??'unchanged'} disabled={locked} onChange={value=>edit({ecoQos:value})} options={[{value:'unchanged',label:'Preserve original'},{value:'on',label:'On'},{value:'off',label:'Off'}]}/></SettingRow>
     <SettingRow title="Window behavior" description="Keep your placement, or start background clients small and minimized."><ThemedSelect label="Optimized window behavior" value={profile.windowBehavior??'unchanged'} disabled={locked} onChange={value=>edit({windowBehavior:value})} options={[{value:'unchanged',label:'Preserve placement'},{value:'smallMinimized',label:'Small, then minimized'}]}/></SettingRow>
     <SettingRow title="Boost focused account" description="Restore original scheduling while you play. Background tuning resumes after one second."><SettingSwitch label="Boost focused account scheduling" checked={profiles?.focusBoost??true} disabled={locked} onChange={value=>profiles&&onChange({...profiles,focusBoost:value})}/></SettingRow>
    </details>
    <details open className="performance-settings-group performance-visuals"><summary>Visual reductions <span className="badge">Next launch</span></summary><p className="performance-group-description">These requests apply when launching optimized accounts. Visual effects depend on the current Roblox client and its allowed settings.</p>
     <SettingRow title="Lowest graphics" description="Request Roblox’s lowest graphics quality for optimized accounts."><SettingSwitch label="Lowest graphics request" checked={profile.graphicsQuality===1} disabled={locked} onChange={value=>edit({graphicsQuality:value?1:null})}/></SettingRow>
     <SettingRow title="Texture quality" description="Request lower texture detail to reduce texture memory demands."><ThemedSelect label="Texture quality request" value={profile.textureQuality??'unchanged'} disabled={locked} onChange={value=>edit({textureQuality:value})} options={[{value:'unchanged',label:'Preserve original'},{value:'reduced',label:'Reduced'},{value:'lowest',label:'Lowest'}]}/></SettingRow>
     {([{key:'minimumMsaa',label:'Minimum anti-aliasing',description:'Request less edge smoothing. Edges may look more jagged.'},{key:'disableGrass',label:'Disable decorative grass',description:'Request removal of decorative grass rendering.'},{key:'reducedGeometry',label:'Reduce geometry detail',description:'Request simpler geometry detail without removing gameplay objects.'},{key:'graySky',label:'Gray sky',description:'Request a flat gray sky in place of the skybox.'}] as const).map(option=><SettingRow key={option.key} title={option.label} description={option.description}><SettingSwitch label={option.label+' request'} checked={profile[option.key]??false} disabled={locked} onChange={value=>edit({[option.key]:value})}/></SettingRow>)}
    </details>
   </div>}
   <div className="button-row"><button className="primary" disabled={locked||!profile} onClick={onApply}>Apply optimizations</button><span className="muted">Live controls apply now. Choose Roblox defaults and Apply to restore original live settings.</span></div>
   <p className="muted">Returning to normal: apply Roblox defaults for all managed accounts, or enable Bypass optimizations on one account. Original app-owned settings restore immediately where supported. Launch accounts again to change their FPS or graphics.</p>
   {error&&<p role="alert">{error}</p>}
   {state?.error&&<p role="alert">{state.error}</p>}
   {state&&<div className="stack" aria-label="Optimization results"><p role="status">{state.applied} applied now · {state.bypassed} bypassed · {state.unavailable} unavailable · {state.failed} failed · FPS/graphics on next launch</p>{state.instances.length>0&&<div className="table-scroll"><table><thead><tr><th>Client</th><th>Status</th><th>Effective controls</th><th>Details</th></tr></thead><tbody>{state.instances.map(row=><tr key={row.pid+'-'+row.startedAt}><th>PID {row.pid}</th><td>{row.state}</td><td>{row.effective}</td><td>{row.message}</td></tr>)}</tbody></table></div>}<details><summary>Launch compatibility</summary><p>{state.launchCompatibility}</p></details></div>}
  </div>
 </section>;
}
