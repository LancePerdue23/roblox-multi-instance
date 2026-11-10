import {test,expect} from '@playwright/test';
import {existsSync} from 'node:fs';
import {installWorkspace} from './fixtures/workspace';
const badges=[{id:'alpha-tester',title:'Alpha Tester'},{id:'bug-hunter',title:'Bug Hunter'},{id:'contributor',title:'Contributor'},{id:'founder',title:'Founder'}].map(b=>({...b,robloxUserId:'123'}));
test('verified supporter and recognition coexist with independent appearance and visibility',async({page})=>{
 await installWorkspace(page);await page.goto('/?supporter-verified');await page.evaluate(badges=>(window as any).testEmit({version:1,event:'badges.changed',data:{badges}}),badges);
 await expect(page.locator('.sidebar-recognition .special-badge')).toHaveCount(4);await expect(page.locator('.sidebar-bottom>.sidebar-supporter')).toBeVisible();
 await page.getByRole('button',{name:'Support development',exact:true}).click();await expect(page.locator('.sidebar-bottom>.sidebar-supporter .sp-ember')).toBeVisible();await expect(page.locator('.sidebar-recognition .special-badge')).toHaveCount(4);
 const row=await page.locator('.sidebar-recognition').boundingBox(),supporter=await page.locator('.sidebar-bottom>.sidebar-supporter').boundingBox();expect(row!.y+row!.height).toBeLessThanOrEqual(supporter!.y);expect(supporter!.height).toBe(44);const nav=await page.locator('.sidebar nav').boundingBox();expect(supporter!.x).toBeCloseTo(nav!.x,0);expect(supporter!.width).toBeCloseTo(nav!.width,0);
 await page.locator('.sidebar-bottom>.sidebar-supporter').screenshot({path:'../../../.build/supporter-compact-entry.png'});
 await page.getByRole('button',{name:'Customize Founder badge',exact:true}).click();await expect(page.getByRole('tab',{name:'Badges',exact:true})).toHaveAttribute('aria-selected','true');await page.getByLabel('Show this badge').uncheck();await expect(page.locator('.sidebar-recognition .special-badge')).toHaveCount(3);await expect(page.locator('.sidebar-bottom>.sidebar-supporter .sp-ember')).toBeVisible();
});

test('owner can combine a supporter tier with arbitrary extra recognition badges without grants',async({page})=>{
 test.skip(!existsSync('dist/local-role-switcher.js'),'Owner-local controls only');await installWorkspace(page);
 await page.getByLabel('Local role preview').selectOption('2');await page.getByRole('button',{name:'Continue',exact:true}).click();await page.getByText('Add recognition badges',{exact:true}).click();
 for(const title of ['Founder','Bug Hunter'])await page.getByLabel('Preview '+title+' alongside selected role',{exact:true}).check();
 await expect(page.locator('.sidebar-recognition .special-badge')).toHaveCount(2);await expect(page.locator('.local-role-button .heart-badge.tier-2')).toBeVisible();await expect(page.getByRole('dialog')).not.toBeVisible();
 await page.locator('button.support-link.local-role-button').click();await expect(page.locator('.local-role-button .sp-radiant')).toBeVisible();await expect(page.locator('.sidebar-recognition .special-badge')).toHaveCount(2);
 const icons=await page.locator('.sidebar-recognition').boundingBox(),tier=await page.locator('button.support-link.local-role-button').boundingBox();expect(icons!.y+icons!.height).toBeLessThanOrEqual(tier!.y);expect(tier!.height).toBe(44);
 await page.getByLabel('Local role preview').selectOption('0');await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.locator('.local-role-button .sp-ember')).toBeVisible();await expect(page.locator('.sidebar-recognition .special-badge')).toHaveCount(2);
 await page.getByLabel('Local role preview').selectOption('3');await page.getByRole('button',{name:'Continue',exact:true}).click();await page.getByLabel('Preview Alpha Tester alongside selected role',{exact:true}).check();await expect(page.locator('.sidebar-recognition .special-badge')).toHaveCount(3);
 await page.getByLabel('Preview Contributor alongside selected role',{exact:true}).check();await page.getByLabel('Local role preview').selectOption('2');await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(page.locator('.sidebar-recognition .special-badge')).toHaveCount(4);
 await page.reload();await expect(page.getByLabel('Local role preview')).toHaveValue('2');await expect(page.locator('.sidebar-recognition .special-badge')).toHaveCount(4);await expect(page.locator('.local-role-button .sp-radiant')).toBeVisible();
 await page.getByRole('button',{name:'Customize Founder badge',exact:true}).click();await expect(page.getByRole('group',{name:'Your badge collection'}).getByRole('button')).toHaveCount(4);await expect(page.getByRole('dialog')).not.toBeVisible();await page.screenshot({path:'../../../.build/supporter-recognition-combo.png',fullPage:true});
 expect(await page.evaluate(()=>(window as any).testCalls.filter((x:any)=>['support.start','support.claim','support.setRole','support.importBadgeFile'].includes(x.command)))).toHaveLength(0);
});
