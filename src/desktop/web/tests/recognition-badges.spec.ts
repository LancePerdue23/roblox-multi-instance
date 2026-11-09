import {test,expect} from '@playwright/test';
import {existsSync} from 'node:fs';
import {installWorkspace} from './fixtures/workspace';
const badges=[{id:'bug-hunter',title:'Bug Hunter',robloxUserId:'123'},{id:'contributor',title:'Contributor',robloxUserId:'123'},{id:'founder',title:'Founder',robloxUserId:'123'}];
async function grant(page:import('@playwright/test').Page,items=badges,extra={}){await page.evaluate(data=>(window as any).testEmit({version:1,event:'badges.changed',data}),{badges:items,...extra});}

test('multiple badges deduplicate across accounts and sidebar buttons select the matching studio',async({page})=>{
 await installWorkspace(page);await grant(page,[...badges,{...badges[0],robloxUserId:'456'}]);
 await expect(page.locator('.sidebar-recognition .special-badge')).toHaveCount(3);
 for(const badge of badges){
  await page.getByRole('button',{name:'Customize '+badge.title+' badge',exact:true}).click();
  await expect(page.getByRole('button',{name:'Select '+badge.title+' badge',exact:true})).toHaveAttribute('aria-pressed','true');
  await expect(page.getByRole('group',{name:'Your badge collection'}).getByRole('button')).toHaveCount(3);
  await expect(page.locator('.recognition-stage .special-badge')).toHaveAccessibleName(badge.title);
 }
 await page.getByLabel('Show this badge').uncheck();await expect(page.locator('.sidebar-recognition .special-badge')).toHaveCount(2);
 await expect(page.getByRole('tab',{name:'Badges',exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Select Bug Hunter badge',exact:true}).click();await expect(page.getByLabel('Show this badge')).toBeChecked();
 await page.getByLabel('Enable badge animations').uncheck();await page.getByRole('button',{name:'Select Founder badge',exact:true}).click();await expect(page.getByLabel('Show this badge')).not.toBeChecked();
 await page.getByRole('button',{name:'Select Bug Hunter badge',exact:true}).click();await expect(page.getByLabel('Enable badge animations')).not.toBeChecked();
 await page.reload();await grant(page);await page.getByRole('button',{name:'Customize Bug Hunter badge',exact:true}).click();await expect(page.getByLabel('Enable badge animations')).not.toBeChecked();
 await grant(page,[]);await expect(page.getByRole('tab',{name:'Badges',exact:true})).toHaveCount(0);
});

test('newly claimed badge unlocks even when another badge is selected',async({page})=>{
 await installWorkspace(page);await grant(page,[badges[0]]);await page.getByRole('button',{name:'Customize Bug Hunter badge',exact:true}).click();
 await grant(page,badges,{message:'Recognition saved. The imported file has been deleted.',claimedBadgeId:'founder'});
 await expect(page.getByRole('dialog')).toBeVisible();await expect(page.getByRole('dialog').getByRole('heading')).toHaveText('Founder');await expect(page.getByRole('button',{name:'Select Founder badge',exact:true})).toHaveAttribute('aria-pressed','true');
 await page.getByRole('button',{name:'Continue',exact:true}).click();
 await page.getByRole('tab',{name:'Appearance',exact:true}).click();await page.getByRole('tab',{name:'Badges',exact:true}).click();await expect(page.getByRole('dialog')).not.toBeVisible();
 await page.getByRole('button',{name:'Instances',exact:true}).click();await page.getByRole('button',{name:'Settings',exact:true}).click();await page.getByRole('tab',{name:'Badges',exact:true}).click();await expect(page.getByRole('dialog')).not.toBeVisible();
 await expect(page.locator('.recognition-stage .cosmetic-stage-lines')).toHaveAttribute('aria-hidden','true');await expect(page.locator('.recognition-stage .cosmetic-stage-lines')).toHaveCSS('background-image',/radial-gradient/);
 await page.locator('.recognition-replay').click();await expect(page.getByRole('dialog')).toBeVisible();await page.getByRole('button',{name:'Continue',exact:true}).click();
 await grant(page,badges,{message:'Recognition already claimed. The imported file has been deleted.',claimedBadgeId:'contributor'});
 await expect(page.getByRole('button',{name:'Select Contributor badge',exact:true})).toHaveAttribute('aria-pressed','true');await expect(page.getByRole('dialog')).not.toBeVisible();
 await grant(page,badges,{message:'Recognition saved. The imported file has been deleted.',claimedBadgeId:'bug-hunter'});await expect(page.getByRole('dialog')).toBeVisible();await expect(page.getByRole('dialog').getByRole('heading')).toHaveText('Bug Hunter');
});

test('local dropdown previews each new badge and the complete collection',async({page})=>{
 test.skip(!existsSync('dist/local-role-switcher.js'),'Owner-local preview only');await installWorkspace(page);
 for(const [value,title] of [['4','Bug Hunter'],['5','Contributor'],['6','Founder'],['7','Founder']]){
  await page.getByLabel('Local role preview').selectOption(value);await expect(page.getByRole('dialog')).toBeVisible();await expect(page.getByRole('dialog').getByRole('heading')).toHaveText(title);await page.getByRole('button',{name:'Continue',exact:true}).click();
  await expect(page.locator('.sidebar-recognition .special-badge')).toHaveCount(value==='7'?4:1);
 }
 await page.setViewportSize({width:1000,height:720});await page.getByRole('button',{name:'Select Founder badge',exact:true}).click();await expect(page.getByRole('group',{name:'Your badge collection'}).getByRole('button')).toHaveCount(4);
 await page.screenshot({path:'../../../.build/all-special-badges.png',fullPage:true});await page.reload();await expect(page.getByLabel('Local role preview')).toHaveValue('7');expect(await page.locator('.sidebar-recognition button').count()).toBeLessThanOrEqual(4);await page.locator('.sidebar-recognition button').first().click();await expect(page.getByRole('group',{name:'Your badge collection'}).getByRole('button')).toHaveCount(4);
 await page.getByLabel('Local role preview').selectOption('-2');await expect(page.locator('.sidebar-recognition .special-badge')).toHaveCount(0);await page.getByRole('button',{name:'Settings',exact:true}).click();await expect(page.getByRole('tab',{name:'Badges',exact:true})).toHaveCount(0);
});
