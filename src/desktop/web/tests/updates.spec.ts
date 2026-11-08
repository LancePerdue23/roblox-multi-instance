import {test,expect} from '@playwright/test';
import {installWorkspace} from './fixtures/workspace';
test('startup offers a newer release without downloading or opening GitHub',async({page})=>{
 await installWorkspace(page);await page.goto('/?update-available');
 await expect(page.getByText('Update 1.2.0 is available',{exact:true})).toBeVisible();
 let calls=await page.evaluate(()=>(window as any).testCalls);
 expect(calls.some((c:any)=>['updates.download','updates.install','updates.openPage'].includes(c.command))).toBe(false);
 await page.getByRole('button',{name:'Review update',exact:true}).click();
 await expect(page.getByRole('heading',{name:/App and helper updates/})).toBeVisible();
 await page.getByRole('button',{name:'Download update',exact:true}).click();
 await expect(page.getByRole('button',{name:'Run setup…',exact:true})).toBeEnabled();
 await page.getByRole('button',{name:'Run setup…',exact:true}).click();
 await expect(page.getByRole('dialog')).toBeVisible();
 calls=await page.evaluate(()=>(window as any).testCalls);
 expect(calls.some((c:any)=>c.command==='updates.install')).toBe(false);
 await page.getByRole('button',{name:'Install update',exact:true}).click();
 await expect.poll(()=>page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command==='updates.install').length)).toBe(1);
 expect(await page.evaluate(()=>(window as any).testCalls.some((c:any)=>c.command==='updates.openPage'))).toBe(false);
});
test('update prompt can be deferred without starting a download',async({page})=>{
 await installWorkspace(page);await page.goto('/?update-available');
 await page.getByRole('button',{name:'Later',exact:true}).click();
 await expect(page.getByText('Update 1.2.0 is available',{exact:true})).toHaveCount(0);
 expect(await page.evaluate(()=>(window as any).testCalls.some((c:any)=>c.command==='updates.download'))).toBe(false);
});
