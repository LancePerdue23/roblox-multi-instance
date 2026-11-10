import {existsSync} from 'node:fs';
import {test,expect} from '@playwright/test';
import {installWorkspace} from './fixtures/workspace';

test.beforeEach(()=>test.skip(!existsSync('dist/local-supporter-preview.json'),'Preview controls are excluded from normal builds'));

test('preview shell explores new tiers without touching supporter state',async({page})=>{
 await installWorkspace(page);await page.goto('/?supporter-preview');
 await expect(page.getByRole('heading',{name:'A little heart. A bigger thank-you.'})).toBeVisible();
 for(const [index,kind] of ['ember','orbit','radiant'].entries()){
  await page.locator('.tier-choice').nth(index).click();
  await page.getByRole('button',{name:'Replay '+['Supporter','Supporter+','Super Supporter'][index]+' unlock',exact:true}).click();
  await expect(page.getByRole('dialog').locator('.sp-'+kind+' .sp-heart-core')).toBeVisible();
  await page.getByRole('button',{name:'Continue',exact:true}).click();
 }
 expect(await page.evaluate(()=>(window as any).testCalls.filter((x:any)=>x.command!=='host.ready'))).toHaveLength(0);
});

test('new unlocks settle when reduced motion is requested',async({page})=>{
 await installWorkspace(page);await page.goto('/?supporter-preview');await page.emulateMedia({reducedMotion:'reduce'});
 for(const index of [0,1,2]){
  await page.locator('.tier-choice').nth(index).click();await page.getByRole('button',{name:/Replay .* unlock/}).click();
  await expect(page.getByRole('dialog').locator('.sp-heart-core')).toHaveCSS('animation-name','none');
  await expect(page.getByRole('dialog').getByRole('heading')).toHaveCSS('opacity','1');
  await page.getByRole('button',{name:'Continue',exact:true}).click();
 }
});

test('supporter button uses the new heart and opens support',async({page})=>{
 await installWorkspace(page);await page.goto('/?supporter-verified');const button=page.getByRole('button',{name:'Support development',exact:true});
 await expect(button.locator('.sp-ember')).toBeVisible();await button.hover();await button.click();
 await expect(page.getByRole('heading',{name:'Support development',exact:true})).toBeVisible();
 await expect(page.getByRole('button',{name:'Classic hearts',exact:true})).toHaveCount(0);
});

test('animation settings persist for the new sidebar badge',async({page})=>{
 await installWorkspace(page);await page.goto('/?supporter-verified');await page.getByRole('button',{name:'Support development',exact:true}).click();
 await page.getByLabel('Enable animation').uncheck();await expect(page.locator('.sidebar-supporter .sp-heart-core')).toHaveCSS('animation-name','none');
 await page.reload();await expect(page.locator('.sidebar-supporter .sp-ember')).toBeVisible();await expect(page.locator('.sidebar-supporter .heart-badge')).toHaveClass(/heart-still/);
});

test('local build shows timestamp and source identity on the regular workspace',async({page})=>{
 await installWorkspace(page);await page.goto('/');const label=page.locator('.version small').filter({hasText:'Local testing'});
 await expect(label).toBeVisible();await expect(label).toHaveAttribute('title',/^Source /);expect(await label.textContent()).toMatch(/\d/);
});
