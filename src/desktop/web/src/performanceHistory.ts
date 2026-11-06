import type {PerformanceSnapshotDto} from './backend.generated';
export const HISTORY_LIMIT=60;
export type HistoryPoint={at:string;cpu:number|null;memory:number|null};
export type PerformanceHistory=Record<string,HistoryPoint[]>;
export function appendHistory(current:PerformanceHistory,snapshot:PerformanceSnapshotDto):PerformanceHistory {
 const next:PerformanceHistory={};
 for(const row of snapshot.processes){
  if(next[row.id])continue;
  const previous=current[row.id]??[];
  next[row.id]=previous.at(-1)?.at===snapshot.observedAt?previous:[...previous,{at:snapshot.observedAt,cpu:row.cpuPercent,memory:row.workingSetBytes}].slice(-HISTORY_LIMIT);
 }
 return next;
}
export const memory=(bytes:number|null|undefined)=>bytes==null?'Unavailable':`${(bytes/1048576).toFixed(1)} MB`;
export const percent=(value:number|null|undefined,waiting=false)=>value==null?(waiting?'Waiting for sample':'Unavailable'):`${value.toFixed(1)}%`;
export const rate=(value:number|null|undefined,waiting=false)=>value==null?(waiting?'Waiting for sample':'Unavailable'):`${(value/1024).toFixed(1)} KB/s`;
