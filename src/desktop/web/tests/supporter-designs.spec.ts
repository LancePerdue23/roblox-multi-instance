import {test,expect} from '@playwright/test';
import {existsSync} from 'node:fs';
import {installWorkspace} from './fixtures/workspace';

test('Classic remains the default, both designs persist and only Classic offers palettes',async({page})=>{
 await installWorkspace(page);await page.goto('/?supporter-verified');await page.getByRole('button',{name:'Support development',exact:true}).click();
 await expect(page.getByRole('button',{name:'Classic hearts',exact:true})).toHaveAttribute('aria-pressed','true');await expect(page.locator('.sidebar-supporter .heart-symbol')).toBeVisible();
 await page.getByRole('button',{name:'Ocean',exact:true}).click();await page.getByRole('button',{name:'Detailed hearts',exact:true}).click();
 await expect(page.locator('.sidebar-supporter .sp-ember')).toBeVisible();await expect(page.locator('.cosmetic-stage .sp-ember')).toBeVisible();await expect(page.getByRole('button',{name:'Ocean',exact:true})).toHaveCount(0);await expect(page.locator('.tier-gallery .sp-mark')).toHaveCount(3);
 await page.getByLabel('Enable animation').uncheck();await expect(page.locator('.sidebar-supporter .sp-heart-core')).toHaveCSS('animation-name','none');
 await page.reload();await expect(page.locator('.sidebar-supporter .sp-ember')).toBeVisible();await page.getByRole('button',{name:'Support development',exact:true}).click();await expect(page.getByRole('button',{name:'Detailed hearts',exact:true})).toHaveAttribute('aria-pressed','true');
 await page.getByRole('button',{name:'Classic hearts',exact:true}).click();await expect(page.getByRole('button',{name:'Ocean',exact:true})).toHaveAttribute('aria-pressed','true');await expect(page.locator('.sidebar-supporter .heart-badge')).toHaveCSS('--heart','#7dcddd');await expect(page.getByLabel('Enable animation')).not.toBeChecked();await expect(page.getByRole('dialog')).not.toBeVisible();
});

test('Detailed unlock respects entitlement, settles with reduced motion, and never grants a previewed tier',async({page})=>{
 await installWorkspace(page);await page.goto('/?supporter-verified');await page.getByRole('button',{name:'Support development',exact:true}).click();await page.getByRole('button',{name:'Detailed hearts',exact:true}).click();
 await page.getByRole('button',{name:'Replay Supporter unlock',exact:true}).click();let scene=page.getByRole('dialog');await expect(scene).toBeVisible();await expect(scene.locator('.sp-unlock-label')).toHaveCSS('color','rgb(241, 152, 174)');await expect(scene.locator('.sp-unlock-halo')).toHaveCSS('background-image',/radial-gradient/);await expect(scene.locator('.sp-heart-core')).toHaveCSS('animation-name','supporter-detail-heart-arrive');await scene.evaluate(el=>el.getAnimations({subtree:true}).forEach(a=>{a.pause();a.currentTime=4900;}));await scene.screenshot({path:'../../../.build/supporter-detailed-unlock.png'});await page.getByRole('button',{name:'Continue',exact:true}).click();
 for(const tier of [1,2]){await page.locator('.tier-choice').nth(tier).click();await expect(page.getByRole('button',{name:/Replay .* unlock/})).toHaveCount(0);await expect(page.locator('.sidebar-supporter .sp-ember')).toBeVisible();}
 await page.locator('.tier-choice').nth(0).click();await page.emulateMedia({reducedMotion:'reduce'});await page.getByRole('button',{name:'Replay Supporter unlock',exact:true}).click();scene=page.getByRole('dialog');await expect(scene.locator('.sp-heart-core')).toHaveCSS('animation-name','none');await expect(scene.getByRole('heading')).toHaveCSS('opacity','1');await page.getByRole('button',{name:'Continue',exact:true}).click();
 expect(await page.evaluate(()=>(window as any).testCalls.filter((x:any)=>['support.start','support.claim','support.setRole'].includes(x.command)))).toHaveLength(0);
});

test('owner dropdown renders and replays both artwork sets for all tiers',async({page})=>{
 test.skip(!existsSync('dist/local-role-switcher.js'),'Owner-local preview only');await installWorkspace(page);await page.getByRole('button',{name:'Settings',exact:true}).click();await page.getByRole('tab',{name:'Support',exact:true}).click();await page.getByRole('button',{name:'Detailed hearts',exact:true}).click();
 for(const [tier,kind] of [['0','ember'],['1','orbit'],['2','radiant']]){
  await page.getByLabel('Local role preview').selectOption(tier);await expect(page.getByRole('dialog')).toBeVisible();await expect(page.getByRole('dialog').locator('svg.sp-'+kind)).toBeVisible();await page.getByRole('dialog').evaluate(el=>el.getAnimations({subtree:true}).forEach(a=>{a.pause();a.currentTime=4900;}));await page.getByRole('dialog').screenshot({path:'../../../.build/supporter-'+kind+'-in-app.png'});await page.getByRole('button',{name:'Continue',exact:true}).click();
  await expect(page.locator('.local-role-button .sp-'+kind)).toBeVisible();await page.locator('.tier-choice').nth(Number(tier)).click();await page.getByRole('button',{name:'Replay '+['Supporter','Supporter+','Super Supporter'][Number(tier)]+' unlock',exact:true}).click();await expect(page.getByRole('dialog')).toBeVisible();await page.getByRole('button',{name:'Continue',exact:true}).click();
 }
 await page.screenshot({path:'../../../.build/supporter-detailed-in-app.png',fullPage:true});await page.getByRole('button',{name:'Classic hearts',exact:true}).click();await expect(page.locator('.local-role-button .heart-symbol')).toBeVisible();await page.getByRole('button',{name:'Detailed hearts',exact:true}).click();await expect(page.locator('.local-role-button .sp-radiant')).toBeVisible();
 await page.getByLabel('Local role preview').selectOption('-2');await expect(page.locator('.local-role-button')).toHaveCount(0);
});
