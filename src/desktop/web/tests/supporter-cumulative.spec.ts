import {test,expect} from '@playwright/test';
import {installWorkspace} from './fixtures/workspace';

test('verified cumulative top-up upgrades once and survives reopening without replay',async({page})=>{
 await installWorkspace(page);await page.goto('/?cumulative-support');
 await page.getByRole('button',{name:'Support development',exact:true}).click();
 await page.getByText('USD contributions and recovery',{exact:true}).click();
 await expect(page.getByText('Verified USD total:',{exact:false})).toContainText('$5.00');
 await page.getByRole('button',{name:'Use next-tier top-up ($2.00)',exact:true}).click();
 await expect(page.getByLabel('Contribution amount (USD)')).toHaveValue('2.00');
 await page.getByRole('button',{name:'Support with USD…',exact:true}).click();
 expect(await page.evaluate(()=>(window as any).testCalls.find((c:any)=>c.command==='support.start').payload.amountCents)).toBe(200);
 await page.getByRole('button',{name:'Check payment status',exact:true}).click();
 await expect(page.getByRole('dialog')).toBeVisible();await expect(page.getByRole('dialog').getByRole('heading')).toHaveText('Supporter+');
 await page.getByRole('button',{name:'Continue',exact:true}).click();
 await expect(page.locator('.sidebar-supporter')).toContainText('Supporter+');
 await expect(page.getByText('Verified USD total:',{exact:false})).toContainText('$7.00');
 await expect(page.getByRole('button',{name:'Use next-tier top-up ($8.00)',exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Instances',exact:true}).click();await page.getByRole('button',{name:'Support development',exact:true}).click();
 await expect(page.getByRole('dialog')).not.toBeVisible();
 await page.getByText('USD contributions and recovery',{exact:true}).click();
 await page.getByRole('button',{name:'Refresh contribution total',exact:true}).click();await expect(page.getByRole('dialog')).not.toBeVisible();
 await page.getByRole('button',{name:'Show recovery code',exact:true}).click();await expect(page.getByLabel('Private supporter recovery code',{exact:true})).toHaveValue('w'.repeat(43));
 await page.getByRole('button',{name:'Hide recovery code',exact:true}).click();await expect(page.getByLabel('Private supporter recovery code',{exact:true})).toHaveCount(0);
});

for(const viewport of [{width:1440,height:960},{width:1000,height:700},{width:800,height:600}])test(`detailed supporter aligns with column at ${viewport.width}x${viewport.height}`,async({page})=>{
 await page.setViewportSize(viewport);await installWorkspace(page);await page.goto('/?cumulative-support');await page.getByRole('button',{name:'Support development',exact:true}).click();
 const row=await page.locator('.sidebar-bottom>.sidebar-supporter').boundingBox();const nav=await page.locator('.sidebar nav').boundingBox();
 expect(row).toBeTruthy();expect(nav).toBeTruthy();expect(Math.abs(row!.x-nav!.x)).toBeLessThanOrEqual(1);expect(Math.abs(row!.width-nav!.width)).toBeLessThanOrEqual(1);expect(row!.height).toBe(44);
 if(await page.locator('.sidebar-regular').count())expect(await page.locator('.sidebar').evaluate(el=>el.scrollHeight<=el.clientHeight+1)).toBe(true);
 if(!process.env.CI)await page.screenshot({path:`../../../.build/supporter-column-${viewport.width}.png`});
});
