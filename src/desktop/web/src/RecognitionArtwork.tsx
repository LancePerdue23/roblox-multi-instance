import {BugHunterMark,BugHunterTheatre} from './BugHunter';

export function recognitionArtwork(id:string){return ['bug-hunter','contributor','founder'].includes(id)?id:'alpha-tester';}

function AlphaMark(){
 return <g className="alpha-symbol" strokeLinecap="round" strokeLinejoin="round">
  <g className="alpha-orbit" fill="none" stroke="var(--alpha-start)" strokeWidth="1.3">
   <path pathLength="1" d="M12 55C-1 29 58 4 82 26s-10 63-49 56"/>
   <circle cx="13" cy="57" r="2.5" fill="var(--alpha-end)" stroke="none"/>
  </g>
  <g className="alpha-window alpha-back">
   <rect x="20" y="17" width="46" height="40" rx="5" className="alpha-pane"/>
   <path d="M20 27h46" className="alpha-line"/><circle cx="26" cy="22" r="1" className="alpha-ink"/>
   <path d="M27 35h16m-16 6h24" className="alpha-line"/>
  </g>
  <g className="alpha-window alpha-middle">
   <rect x="12" y="40" width="45" height="39" rx="5" className="alpha-pane"/>
   <path d="M12 50h45m-37 8h11m-11 6h23m-23 6h17" className="alpha-line"/>
   <circle cx="18" cy="45" r="1" className="alpha-ink"/>
  </g>
  <g className="alpha-window alpha-front">
   <rect x="34" y="30" width="49" height="43" rx="5" className="alpha-shadow" transform="translate(2 3)"/>
   <rect x="34" y="30" width="49" height="43" rx="5" className="alpha-pane"/>
   <path d="M34 40h49" className="alpha-line"/>
   <g className="alpha-controls"><circle cx="40" cy="35" r="1.2"/><circle cx="45" cy="35" r="1.2"/><circle cx="50" cy="35" r="1.2"/></g>
   <path className="alpha-glyph" d="M64 49c-3-5-14-3-14 6 0 9 10 10 13 3l5-11m-5 5c-1 7 0 13 6 10" fill="none" strokeWidth="3"/>
   <path d="M40 66h7m26 0h4" className="alpha-line"/>
   <path className="alpha-cursor" d="M73 51v8" strokeWidth="2"/>
  </g>
  <g className="alpha-first-star" fill="var(--alpha-end)">
   <path d="m76 8 2.5 6.5L85 17l-6.5 2.5L76 26l-2.5-6.5L67 17l6.5-2.5Z"/>
   <path className="alpha-star-rays" d="m63 7 2 2M88 7l-2 2m2 18-2-2" fill="none" stroke="var(--alpha-end)" strokeWidth="1.3"/>
  </g>
 </g>;
}

function ContributorMark(){
 return <g className="contributor-symbol" strokeLinecap="round" strokeLinejoin="round">
  <g className="contributor-terminal">
   <rect x="10" y="14" width="76" height="56" rx="7" className="code-shadow" transform="translate(0 4)"/>
   <rect x="10" y="14" width="76" height="56" rx="7" className="code-panel"/>
   <path d="M10 28h76" className="code-divider"/>
   <circle cx="18" cy="21" r="1.7" className="code-dot"/><circle cx="24" cy="21" r="1.7" className="code-dot"/><circle cx="30" cy="21" r="1.7" className="code-dot"/>
   <path d="m27 37-8 7 8 7m11-14 8 7-8 7" className="code-brackets" fill="none" strokeWidth="2.7"/>
   <path d="m35 35-5 19" className="code-slash" strokeWidth="2.2"/>
   <g className="code-lines" strokeWidth="2"><path d="M55 37h19m-19 7h13m-13 7h17"/></g>
   <path className="code-cursor" d="M19 60h8" strokeWidth="2"/>
  </g>
  <g className="merge-tracks" fill="none" strokeWidth="2.5">
   <path className="merge-track merge-left" pathLength="1" d="M24 65v8q0 9 24 9"/>
   <path className="merge-track merge-right" pathLength="1" d="M72 65v8q0 9-24 9"/>
   <circle className="merge-source" cx="24" cy="65" r="3"/><circle className="merge-source" cx="72" cy="65" r="3"/>
  </g>
  <circle className="merge-packet packet-left" cx="24" cy="69" r="2.3"/>
  <circle className="merge-packet packet-right" cx="72" cy="69" r="2.3"/>
  <g className="merge-result">
   <path d="m48 68 12 14-12 12-12-12Z" className="merge-diamond"/>
   <path className="merge-check" pathLength="1" d="m42 81 4 4 7-8" fill="none" strokeWidth="2.6"/>
  </g>
  <g className="merge-sparks" stroke="var(--alpha-end)" strokeWidth="1.5"><path d="M32 82h-6m38 0h6M48 63v-5M36 91l-4 3m28-3 4 3"/></g>
 </g>;
}

function FounderMark(){
 return <g className="founder-symbol" strokeLinecap="round" strokeLinejoin="round">
  <g className="founder-steam" fill="none" stroke="#e9cf9a" strokeWidth="1.8">
   <path className="steam-left" d="M27 34c-8-7 5-10-1-17"/>
   <path className="steam-right" d="M70 33c8-7-5-10 1-17"/>
  </g>
  <g className="founder-taco">
   <path className="tortilla-back" d="M13 64c-2-45 70-47 70-1Z" fill="#ce8c37" stroke="#ffe19a" strokeWidth="1.8"/>
   <g className="founder-beans" fill="#814531" stroke="#5d3028" strokeWidth="1"><ellipse cx="29" cy="48" rx="8" ry="6"/><ellipse cx="47" cy="43" rx="9" ry="6"/><ellipse cx="66" cy="49" rx="8" ry="6"/></g>
   <path className="founder-lettuce" d="m15 55 5-13 9 3 5-12 10 6 9-8 9 10 10-1 9 16-6 13H22Z" fill="#79c857" stroke="#357b42" strokeWidth="1.5"/>
   <g className="founder-tomatoes" fill="#f7735e" stroke="#bb4a38" strokeWidth="1.2">
    <path d="m23 47 10-3 4 10-11 4Zm26-9 11 5-5 9-11-5Zm15 13 10-3 3 9-10 3Z"/>
    <path d="m28 49 3 4m19-9 4 2m14 7 3 2" stroke="#ffd6a0"/>
   </g>
   <g className="founder-cheese" stroke="#ffe494" strokeWidth="3"><path d="m20 57 16-6 11 7 14-6 15 7m-40-8 3-8m22 9 3-9"/></g>
   <path className="founder-shell" d="M12 62c0-12 14-18 36-18s36 6 36 18c0 15-15 22-36 22S12 77 12 62Z" fill="#efb954" stroke="#ffe5a7" strokeWidth="2"/>
   <path className="founder-shell-shine" d="M20 60c2-6 13-10 27-10" fill="none" stroke="#ffe7a7" strokeWidth="2.5"/>
   <g className="founder-grain" fill="#ba7b32"><ellipse cx="27" cy="67" rx="2" ry="2.6" transform="rotate(-25 27 67)"/><ellipse cx="44" cy="57" rx="1.8" ry="2.4"/><ellipse cx="62" cy="69" rx="2" ry="2.7" transform="rotate(25 62 69)"/><circle cx="39" cy="75" r="1.6"/><circle cx="72" cy="60" r="1.6"/><circle cx="49" cy="69" r="1.4"/></g>
   <path className="founder-sauce" pathLength="1" d="m23 42 9-4 10 5 9-5 12 7 9-2" fill="none" stroke="#fff2d2" strokeWidth="1.8"/>
  </g>
  <g className="founder-crown" stroke="#ffebaa" strokeWidth="1.2" fill="#f8c65e"><path d="m36 18 2 10h20l2-10-7 4-5-9-5 9Z"/><path d="M39 31h18" fill="none"/><circle cx="48" cy="24" r="1.5" fill="#a96530" stroke="none"/></g>
  <g className="founder-seasoning" fill="#d6e68c"><path d="m9 42 4-2 1 5-4 1Zm75-5 4 3-3 4-3-3ZM18 21l3 1-1 4-3-1Z"/></g>
 </g>;
}

export function RecognitionSymbol({id}:{id:string}){
 switch(recognitionArtwork(id)){
  case 'bug-hunter':return <BugHunterMark/>;
  case 'contributor':return <ContributorMark/>;
  case 'founder':return <FounderMark/>;
  default:return <AlphaMark/>;
 }
}

export function RecognitionTheatre({id}:{id:string}){
 const artwork=recognitionArtwork(id);
 if(artwork==='bug-hunter')return <BugHunterTheatre/>;
 if(artwork==='founder')return <div className="recognition-theatre founder-theatre" aria-hidden="true">
  <svg viewBox="0 0 320 320" fill="none"><g className="founder-sunburst" stroke="#ffd884" strokeWidth="1.5">{Array.from({length:12},(_,i)=><path key={i} d="M160 40v25" transform={'rotate('+i*30+' 160 160)'}/>)}</g>
   <g className="founder-confetti" fill="#9cd171"><path d="m49 91 9-4 4 10-9 4Zm214 15 8 5-5 9-8-5ZM73 240l7-5 5 8-7 4Zm180-13 8 2-2 8-8-2Z"/></g></svg>
  <div className="recognition-phases"><span className="founder-phase-one">The original recipe</span><span className="founder-phase-two">Made from scratch</span><span className="founder-phase-three">Founder</span></div>
 </div>;
 if(artwork==='contributor')return <div className="recognition-theatre contributor-theatre" aria-hidden="true">
  <svg viewBox="0 0 320 320" fill="none"><g className="contributor-streams" stroke="var(--alpha-start)" strokeWidth="1.2"><path pathLength="1" d="M0 80h42l34 34h44M320 80h-42l-34 34h-44M0 220h40l40-40h40m200 40h-40l-40-40h-40"/></g><g className="contributor-pieces" stroke="var(--alpha-end)"><path d="m36 135-14 13 14 13m248-26 14 13-14 13M70 50h22m138 0h22M70 264h22m138 0h22"/></g></svg>
  <div className="recognition-phases"><span className="contributor-phase-one">Every contribution</span><span className="contributor-phase-two">Coming together</span><span className="contributor-phase-three">Built together</span></div>
 </div>;
 return <div className="recognition-theatre alpha-theatre" aria-hidden="true">
  <svg viewBox="0 0 320 320" fill="none"><path className="alpha-constellation" pathLength="1" d="m36 214 48-95 88-72 97 83-37 117-60-200m60 200-148-128" stroke="var(--alpha-start)" strokeWidth=".8"/><g className="alpha-distant-stars" fill="var(--alpha-end)"><circle cx="36" cy="214" r="2"/><circle cx="84" cy="119" r="2"/><circle cx="172" cy="47" r="3"/><circle cx="269" cy="130" r="2"/><circle cx="232" cy="247" r="2"/></g><path className="alpha-comet" d="m12 274 190-224" stroke="var(--alpha-end)" strokeWidth="2"/></svg>
  <div className="recognition-phases"><span className="alpha-phase-one">Before the crowd</span><span className="alpha-phase-two">The first windows</span><span className="alpha-phase-three">First light</span></div>
 </div>;
}
