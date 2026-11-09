/** Standalone beetle and targeting effects, shared by the sidebar and unlock scene. */
export function BugHunterMark(){
 return <g className="bug-symbol" strokeLinecap="round" strokeLinejoin="round">
  <g className="beetle-body">
   <g className="beetle-legs" fill="none" stroke="#52b879" strokeWidth="3">
    <path d="m33 43-12-7-5-9m17 25-14 1-7 7m23 3-11 9-2 9M63 43l12-7 5-9M63 52l14 1 7 7M61 63l11 9 2 9"/>
    <path className="beetle-antennae" d="m41 25-6-9-6-1m26 10 6-9 6-1" strokeWidth="2.5"/>
   </g>
   <ellipse cx="48" cy="56" rx="17" ry="24" fill="#133c2b" stroke="#123125" strokeWidth="2"/>
   <path d="M36 60h24m-22 7h20m-15 7h10" stroke="#6bd98c" strokeWidth="1.5" opacity=".6"/>
   <g className="beetle-wing beetle-wing-left">
    <path d="M47 40C34 37 24 44 26 58c1 13 9 22 20 23Z" fill="#36a95c" stroke="#173f2b" strokeWidth="2"/>
    <path d="M41 45c-8 0-11 6-10 14" stroke="#a4f58a" strokeWidth="3" fill="none"/>
    <path d="m36 53 2 15" stroke="#197846" strokeWidth="1.5"/>
   </g>
   <g className="beetle-wing beetle-wing-right">
    <path d="M49 40c13-3 23 4 21 18-1 13-9 22-20 23Z" fill="#79cc58" stroke="#173f2b" strokeWidth="2"/>
    <path d="M55 45c6 0 10 4 10 11" stroke="#ccf99d" strokeWidth="2.5" fill="none"/>
    <path d="m59 54-2 15" stroke="#3f9b4f" strokeWidth="1.5"/>
   </g>
   <path d="M34 39c0-9 6-15 14-15s14 6 14 15l-7 6H41Z" fill="#24764b" stroke="#173f2b" strokeWidth="2"/>
   <path d="M40 31q8-6 16 0" stroke="#94df85" strokeWidth="2" fill="none"/>
   <circle cx="40" cy="35" r="2" fill="#e2ffbb"/><circle cx="56" cy="35" r="2" fill="#e2ffbb"/>
  </g>
  <g className="hunter-reticle" fill="none" strokeWidth="1.8">
   <path d="M18 37V22h15m30 0h15v15m0 30v15H63m-30 0H18V67"/>
   <path d="M48 18v10m0 48v10M14 52h10m48 0h10" opacity=".65"/>
   <circle cx="48" cy="52" r="6" strokeWidth="1"/>
   <path d="M45 52h6m-3-3v6" strokeWidth="1"/>
  </g>
  <g className="hunter-shot" fill="none" stroke="#fff5d2">
   <path className="hunter-tracer" d="M48 2v41m0 18v33" strokeWidth="2"/>
   <path d="m48 39 3 9 10 4-10 3-3 10-3-10-10-3 10-4Z" fill="#fff5d2" strokeWidth="1"/>
  </g>
  <g className="hunter-impact" fill="none" stroke="#b9fba3" strokeWidth="1.8">
   <circle cx="48" cy="52" r="12"/>
   <path d="m28 32-5-5m45 5 5-5M28 72l-5 5m45-5 5 5M19 52h-7m65 0h7"/>
  </g>
 </g>;
}

export function BugHunterTheatre(){
 return <div className="hunter-theatre" aria-hidden="true">
  <div className="hunter-grid"/><div className="hunter-sweep"/>
  <svg className="hunter-radar" viewBox="0 0 320 320" fill="none">
   <circle cx="160" cy="160" r="124"/><circle cx="160" cy="160" r="90"/>
   <path d="M160 16v34m0 220v34M16 160h34m220 0h34M58 58l21 21m162 162 21 21M58 262l21-21M241 79l21-21"/>
  </svg>
  <div className="hunter-phases"><span className="hunter-search-label">Searching</span><span className="hunter-lock-label">Target locked</span><span className="hunter-clear-label">Bug found</span></div>
 </div>;
}
