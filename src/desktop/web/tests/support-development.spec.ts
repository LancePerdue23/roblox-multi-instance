import {test,expect} from '@playwright/test';
import {existsSync} from 'node:fs';
import {installWorkspace} from './fixtures/workspace';

test('default invitation follows badge order and switches artwork with the supporter design',async({page})=>{
 await page.setViewportSize({width:1000,height:700});await installWorkspace(page);
 if(existsSync('dist/local-role-switcher.js')){
  await page.getByLabel('Local role preview').selectOption('-1');
  await page.getByText('Add recognition badges',{exact:true}).click();await page.getByLabel('Preview Founder alongside selected role',{exact:true}).check();
 }else{
  await page.evaluate(()=>(window as any).testEmit({version:1,event:'badges.changed',data:{badges:[{id:'founder',title:'Founder',robloxUserId:'123'}]}}));
 }
 const entry=page.locator('button.support-development:visible');
 await expect(entry).toHaveCount(1);await expect(entry).toContainText('♥');
 await entry.click();await page.getByRole('button',{name:'Detailed hearts',exact:true}).click();
 await expect(entry.locator('.sp-invitation')).toBeVisible();await expect(entry.locator('.heart-badge')).toHaveCount(0);
 const icons=await page.locator('.sidebar-recognition').boundingBox(),button=await entry.boundingBox(),nav=await page.locator('.sidebar nav').boundingBox();
 expect(icons!.y+icons!.height).toBeLessThanOrEqual(button!.y);expect(button!.height).toBe(44);expect(button!.x).toBeCloseTo(nav!.x,0);expect(button!.width).toBeCloseTo(nav!.width,0);
 await entry.screenshot({path:'../../../.build/support-development-detailed.png'});
 await page.emulateMedia({reducedMotion:'reduce'});await expect(entry.locator('.sp-invitation-spark')).toHaveCSS('animation-name','none');
 await page.reload();await expect(entry.locator('.sp-invitation')).toBeVisible();await entry.click();await page.getByRole('button',{name:'Classic hearts',exact:true}).click();
 await expect(entry.locator('.sp-invitation')).toHaveCount(0);await expect(entry).toContainText('♥');await expect(page.getByRole('dialog')).not.toBeVisible();
 expect(await page.evaluate(()=>(window as any).testCalls.filter((c:any)=>['support.start','support.claim','support.setRole'].includes(c.command)))).toHaveLength(0);
});
