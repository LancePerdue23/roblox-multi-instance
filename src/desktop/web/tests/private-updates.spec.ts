import {test,expect} from '@playwright/test';
import {installWorkspace} from './fixtures/workspace';
test('private update controls use native credential commands without sending tokens through the page',async({page})=>{
 await installWorkspace(page);await page.getByRole('button',{name:'System',exact:true}).click();
 await page.getByRole('button',{name:'Connect GitHub update access…',exact:true}).click();
 await expect(page.getByText('GitHub update credentials saved. Check for updates to verify access.')).toBeVisible();
 await page.getByLabel('Include validation prereleases').click();
 await expect(page.getByLabel('Include validation prereleases')).toBeChecked();
 await page.getByRole('button',{name:'Disconnect GitHub update access',exact:true}).click();
 await expect(page.getByText('GitHub update checks are anonymous.')).toBeVisible();
 const calls=await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>c.command.startsWith('updates.')));
 expect(calls.find((c:any)=>c.command==='updates.connect').payload).toEqual({});
 expect(calls.find((c:any)=>c.command==='updates.prereleases').payload).toEqual({enabled:true});
 expect(await page.locator('input[type=password]').count()).toBe(0);
});
