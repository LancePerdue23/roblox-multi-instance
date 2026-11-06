import {existsSync} from 'node:fs';
import {test,expect} from '@playwright/test';
import {installWorkspace} from './fixtures/workspace';
test.beforeEach(()=>test.skip(!existsSync('dist/local-supporter-preview.json'),'Preview controls are excluded from normal builds'));

test('preview shell explores tiers without touching supporter state',async({page})=>{
 await installWorkspace(page);await page.goto('/?supporter-preview');
 await expect(page.getByRole('heading',{name:'A little heart. A bigger thank-you.'})).toBeVisible();
 for(const tier of ['Supporter','Supporter+','Super Supporter']){
  await page.getByRole('button',{name:new RegExp('^♥?\\s*'+tier.replace('+','\\+')+' ')}).first().click();
  await page.getByRole('button',{name:'Replay '+tier+' unlock',exact:true}).click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await page.getByRole('button',{name:'Continue',exact:true}).click();
 }
 expect(await page.evaluate(()=>(window as any).testCalls.filter((x:any)=>x.command!=='host.ready'))).toHaveLength(0);
 await page.screenshot({path:'../../../.build/supporter-studio.png',fullPage:true});
 await page.getByRole('button',{name:'Replay Super Supporter unlock',exact:true}).click();
 await expect(page.getByRole('dialog').locator('.unlock-particles i')).toHaveCount(12);
 await expect(page.getByRole('dialog')).not.toContainText('✧');
 await page.getByRole('dialog').evaluate(node=>{for(const animation of node.getAnimations({subtree:true})){animation.pause();animation.currentTime=900;}});
 await page.screenshot({path:'../../../.build/super-supporter-reveal.png'});
 await page.getByRole('button',{name:'Continue',exact:true}).click();
 await page.emulateMedia({reducedMotion:'reduce'});
 await page.getByLabel('Enable animation').check();
 await page.getByRole('button',{name:'Replay Super Supporter unlock',exact:true}).click();
 await expect(page.getByRole('dialog').locator('.heart-symbol')).toHaveCSS('animation-name','none');
 await page.screenshot({path:'../../../.build/supporter-unlock.png'});
});


test('normal and plus unlock halos finish transparent',async({page})=>{
 await installWorkspace(page);await page.goto('/?supporter-preview');
 for(const tier of ['Supporter','Supporter+']){
  await page.getByRole('button',{name:new RegExp('^♥?\\s*'+tier.replace('+','\\+')+' ')}).first().click();
  await page.getByRole('button',{name:'Replay '+tier+' unlock',exact:true}).click();
  await page.getByRole('dialog').evaluate(node=>{for(const animation of node.getAnimations({subtree:true})){if(animation.effect?.getTiming().iterations!==Infinity)animation.finish();}});
  await expect(page.getByRole('dialog').locator('.unlock-halo')).toHaveCSS('opacity','0');
  if(tier==='Supporter')await expect(page.getByRole('dialog').locator('.heart-symbol')).toHaveCSS('filter','none');
  else await expect(page.getByRole('dialog').locator('.heart-symbol')).toHaveCSS('filter','drop-shadow(rgb(240, 145, 177) 0px 0px 5px)');
  await page.getByRole('button',{name:'Continue',exact:true}).click();
 }
});

test('supporter button animates briefly on hover and still opens support',async({page})=>{
 await installWorkspace(page);await page.goto('/?supporter-verified');
 const button=page.getByRole('button',{name:'Support development',exact:true});
 await expect(button).toHaveClass(/sidebar-supporter/);
 await button.hover();
 await expect(button.locator('.heart-symbol')).toHaveCSS('animation-name','heart-beat, button-heart-reveal-clean');
 await button.evaluate(node=>{for(const animation of node.getAnimations({subtree:true})){if(animation.effect?.getTiming().iterations!==Infinity)animation.finish();}});
 await expect(button.locator('.heart-symbol')).toHaveCSS('filter','none');
 await button.click();
 await expect(page.getByRole('heading',{name:'Support development',exact:true})).toBeVisible();
});


test('palette and animation settings reach the sidebar badge',async({page})=>{
 await installWorkspace(page);await page.goto('/?supporter-verified');
 await page.getByRole('button',{name:'Support development',exact:true}).click();
 await page.getByRole('button',{name:'Ocean',exact:true}).click();
 await expect(page.locator('.sidebar-supporter .heart-badge')).toHaveCSS('--heart','#7dcddd');
 await expect(page.locator('.sidebar-supporter')).toHaveCSS('--heart','#7dcddd');
 await page.locator('.sidebar-supporter').hover();
 await expect(page.locator('.sidebar-supporter')).toHaveCSS('border-top-color','rgb(48, 55, 68)');
 expect(await page.locator('.sidebar-supporter').evaluate(node=>getComputedStyle(node,'::before').backgroundImage)).toContain('color(srgb 0.490196 0.803922 0.866667');
 await page.getByLabel('Enable animation').uncheck();
 await expect(page.locator('.sidebar-supporter .heart-badge')).toHaveClass(/heart-still/);
 await page.reload();
 await expect(page.locator('.sidebar-supporter .heart-badge')).toHaveCSS('--heart','#7dcddd');
});

test('local build shows timestamp and source identity on the regular workspace',async({page})=>{
 await installWorkspace(page);await page.goto('/');
 const label=page.locator('.version small').filter({hasText:'Local testing'});
 await expect(label).toBeVisible();await expect(label).toHaveAttribute('title',/^Source /);
 expect(await label.textContent()).toMatch(/\d/);
});
