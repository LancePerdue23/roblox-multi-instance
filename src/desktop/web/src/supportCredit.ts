export const robuxThresholds=[300,700,1500] as const;
export function tierProgress(total:number,index:number){const start=index===0?0:robuxThresholds[index-1];return Math.max(0,Math.min(1,(total-start)/(robuxThresholds[index]-start)));}
export function creditTier(total:number){return total>=1500?2:total>=700?1:total>=300?0:-1;}
export function passForGap<T extends {credit:number}>(passes:T[],gap:number){const index=passes.findIndex(pass=>pass.credit>=gap);return index<0?Math.max(0,passes.length-1):index;}
export function nextPassIndex(passes:{credit:number}[],total:number,tier:number){return tier>=2?0:passForGap(passes,Math.max(0,robuxThresholds[tier+1]-total));}
