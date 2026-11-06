import {describe,it,expect} from 'vitest';
import {appendHistory,HISTORY_LIMIT,memory,percent,rate} from './performanceHistory';
import type {PerformanceSnapshotDto} from './backend.generated';
const sample=(id:string,time:number)=>({observedAt:String(time),processes:[{id,cpuPercent:1,workingSetBytes:20}]} as PerformanceSnapshotDto);
describe('performance histories',()=>{
 it('bounds active process history and rejects duplicate observations',()=>{let h={};for(let i=0;i<100;i++)h=appendHistory(h,sample('life-a',i));expect(h['life-a' as keyof typeof h]).toHaveLength(HISTORY_LIMIT);expect(appendHistory(h,sample('life-a',99))).toEqual(h);});
 it('does not inherit history after PID reuse and discards exited process histories',()=>{const first=appendHistory({},sample('pid-123-start-a',1));const reused=appendHistory(first,sample('pid-123-start-b',2));expect(Object.keys(reused)).toEqual(['pid-123-start-b']);expect(reused['pid-123-start-b']).toHaveLength(1);expect(appendHistory(reused,{observedAt:'3',processes:[]} as unknown as PerformanceSnapshotDto)).toEqual({});});
 it('keeps unavailable counters distinct from measured zero',()=>{expect(memory(null)).toBe('Unavailable');expect(memory(0)).toBe('0.0 MB');expect(percent(null)).toBe('Unavailable');expect(percent(null,true)).toBe('Waiting for sample');expect(percent(0)).toBe('0.0%');expect(rate(0)).toBe('0.0 KB/s');});
});
