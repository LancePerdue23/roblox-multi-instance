import {useEffect,useId,useRef,useState} from 'react';
import {createPortal} from 'react-dom';

type Option={value:string;label:string};
/** A select-only combobox: its popup uses the same CSS tokens as the page. */
export function ThemedSelect({label,value,options,onChange,disabled=false}:{label:string;value:string;options:Option[];onChange:(value:string)=>void;disabled?:boolean}){
 const id=useId(),trigger=useRef<HTMLButtonElement>(null),list=useRef<HTMLDivElement>(null);
 const [open,setOpen]=useState(false),[active,setActive]=useState(0);
 const [position,setPosition]=useState({left:0,top:0,width:0,maxHeight:240});
 const search=useRef({text:'',time:0});const anchor=useRef({top:0,left:0});
 const selected=options.findIndex(option=>option.value===value);
 function show(last=false){if(disabled||!options.length)return;const rect=trigger.current!.getBoundingClientRect();anchor.current={top:rect.top,left:rect.left};const height=Math.min(280,options.length*44+12),below=window.innerHeight-rect.bottom-12;const down=below>=Math.min(height,160)||below>=rect.top-12;const maxHeight=Math.max(0,Math.min(height,down?below:rect.top-12));setPosition({left:Math.max(8,Math.min(rect.left,window.innerWidth-Math.min(rect.width,window.innerWidth-16)-8)),top:down?rect.bottom+6:Math.max(8,rect.top-maxHeight-6),width:Math.min(rect.width,window.innerWidth-16),maxHeight});setActive(selected>=0?selected:last?options.length-1:0);setOpen(true);}
 function choose(index:number){if(options[index])onChange(options[index].value);setOpen(false);trigger.current?.focus();}
 useEffect(()=>{if(!open)return;function outside(event:PointerEvent){if(!trigger.current?.contains(event.target as Node)&&!list.current?.contains(event.target as Node))setOpen(false);}function scroll(event:Event){if(list.current?.contains(event.target as Node))return;const rect=trigger.current?.getBoundingClientRect();if(rect&&(Math.abs(rect.top-anchor.current.top)>1||Math.abs(rect.left-anchor.current.left)>1))setOpen(false);}const close=()=>setOpen(false);document.addEventListener('pointerdown',outside,true);window.addEventListener('scroll',scroll,true);window.addEventListener('resize',close);return()=>{document.removeEventListener('pointerdown',outside,true);window.removeEventListener('scroll',scroll,true);window.removeEventListener('resize',close);};},[open]);
 useEffect(()=>{
  const popup=list.current,option=document.getElementById(`${id}-${active}`);if(!open||!popup||!option)return;
  // Scroll only the popup. scrollIntoView can also scroll the page and dismiss this menu.
  const top=option.offsetTop,bottom=top+option.offsetHeight;
  if(top<popup.scrollTop)popup.scrollTop=top;else if(bottom>popup.scrollTop+popup.clientHeight)popup.scrollTop=bottom-popup.clientHeight;
 },[open,active,id]);
 useEffect(()=>{if(disabled)setOpen(false);},[disabled]);
 return <><button ref={trigger} type="button" className="select-trigger" role="combobox" aria-label={label} aria-haspopup="listbox" aria-expanded={open} aria-controls={open?id:undefined} aria-activedescendant={open?`${id}-${active}`:undefined} disabled={disabled||!options.length} onClick={()=>open?setOpen(false):show()} onBlur={event=>{if(!list.current?.contains(event.relatedTarget as Node))setOpen(false);}} onKeyDown={event=>{
  if(['ArrowDown','ArrowUp','Home','End','Enter',' '].includes(event.key)){event.preventDefault();if(!open){show(event.key==='ArrowUp'||event.key==='End');return;}if(event.key==='Enter'||event.key===' '){choose(active);return;}setActive(index=>event.key==='Home'?0:event.key==='End'?options.length-1:Math.max(0,Math.min(options.length-1,index+(event.key==='ArrowDown'?1:-1))));}
  else if(event.key==='Escape'){event.preventDefault();setOpen(false);}else if(event.key==='Tab')setOpen(false);
  else if(event.key.length===1&&!event.ctrlKey&&!event.altKey&&!event.metaKey){event.preventDefault();const now=Date.now();search.current={text:now-search.current.time<700?search.current.text+event.key:event.key,time:now};const index=options.findIndex(option=>option.label.toLocaleLowerCase().startsWith(search.current.text.toLocaleLowerCase()));if(index>=0){if(open)setActive(index);else onChange(options[index].value);}}
 }}><span>{options[selected]?.label??'No options available'}</span><span className="select-chevron" aria-hidden="true">⌄</span></button>{open&&createPortal(<div ref={list} id={id} role="listbox" aria-label={label} className="select-popup" style={position}>{options.map((option,index)=><div key={option.value} id={`${id}-${index}`} role="option" aria-selected={option.value===value} className={'select-option'+(index===active?' highlighted':'')} onPointerMove={()=>setActive(index)} onMouseDown={event=>event.preventDefault()} onClick={()=>choose(index)}><span>{option.label}</span><span className="select-check" aria-hidden="true">{option.value===value?'✓':''}</span></div>)}</div>,document.body)}</>;
}
