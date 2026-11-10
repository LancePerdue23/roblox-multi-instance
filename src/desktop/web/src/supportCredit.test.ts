import {describe,it,expect} from 'vitest';
import {creditTier,nextPassIndex,passForGap,tierProgress} from './supportCredit';
const passes=[50,100,200,300,400,500,700,1000,1250,1500,2000,2500,3000,3500,4000,4500,5000,7500,10000,50000,100000].map(credit=>({credit}));
describe('cumulative Robux support',()=>{
 it.each([[0,[0,0,0]],[150,[.5,0,0]],[500,[1,.5,0]],[1100,[1,1,.5]],[1500,[1,1,1]],[100000,[1,1,1]]] as const)('fills each tier interval accurately at %i credit',(total,expected)=>{
  expected.forEach((fraction,index)=>expect(tierProgress(total,index)).toBeCloseTo(fraction));
 });
 it.each([[0,-1],[299,-1],[300,0],[699,0],[700,1],[1499,1],[1500,2],[100000,2]])('maps %i credit to tier %i',(total,tier)=>expect(creditTier(total)).toBe(tier));
 it('shifts the next pass and tier markers by existing credit',()=>{
  expect(passes[nextPassIndex(passes,500,0)].credit).toBe(200);
  expect(passes[passForGap(passes,1500-500)].credit).toBe(1000);
  expect(passes[nextPassIndex(passes,650,0)].credit).toBe(50);
  expect(passes[nextPassIndex(passes,325,0)].credit).toBe(400);
  expect(nextPassIndex(passes,1500,2)).toBe(0);
 });
});
