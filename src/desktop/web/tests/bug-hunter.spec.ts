import {test,expect,type Locator} from '@playwright/test';
import {installWorkspace} from './fixtures/workspace';
const badge={id:'bug-hunter',title:'Bug Hunter',robloxUserId:'123'};
async function freeze(scene:Locator,time:number){await scene.evaluate((el,time)=>el.getAnimations({subtree:true}).forEach(a=>{a.pause();a.currentTime=time;}),time);}
async function opacity(el:Locator){return el.evaluate(el=>Number(getComputedStyle(el).opacity));}
async function openHunter(page:import('@playwright/test').Page){
 await installWorkspace(page);
 // An old saved Ribbon choice must not restore the retired frame or motion.
 await page.evaluate(()=>localStorage.setItem('rmi.special-badge-style',JSON.stringify({badges:{'bug-hunter':{design:'fan',palette:0}}})));
 await page.evaluate(badge=>(window as any).testEmit({version:1,event:'badges.changed',data:{badges:[badge]}}),badge);
 await page.getByRole('button',{name:'Customize Bug Hunter badge',exact:true}).click();
}

test('standalone green beetle flutters its wings, focuses, locks on and fires once',async({page})=>{
 await openHunter(page);const stage=page.locator('.recognition-stage'),icon=stage.locator('.special-badge');
 await expect(page.locator('.badge-design-gallery')).toHaveCount(0);await expect(stage.locator('.badge-plate,.badge-rim,.badge-ribbon')).toHaveCount(0);
 await expect(icon).toHaveClass(/design-native/);await expect(stage.locator('.badge-emblem')).toHaveCSS('animation-name','none');
 await page.mouse.move(1300,20);await expect(stage.locator('.beetle-wing-left')).toHaveCSS('animation-name','beetle-flutter-left');await expect(stage.locator('.beetle-wing-right')).toHaveCSS('animation-name','beetle-flutter-right');
 await expect(stage.locator('.beetle-wing-left path').first()).toHaveCSS('fill','rgb(54, 169, 92)');await expect(stage.locator('.hunter-reticle')).toHaveCSS('opacity','0');
 await icon.hover();await expect(stage.locator('.hunter-reticle')).toHaveCSS('animation-name','hunter-target');
 await freeze(icon,250);expect(await opacity(stage.locator('.hunter-reticle'))).toBeGreaterThan(.8);expect(await opacity(stage.locator('.hunter-shot'))).toBe(0);
 const search=await stage.locator('.hunter-reticle').evaluate(el=>getComputedStyle(el).transform);
 await freeze(icon,900);await expect(stage.locator('.hunter-reticle')).toHaveCSS('stroke','rgb(255, 164, 123)');expect(await stage.locator('.hunter-reticle').evaluate(el=>getComputedStyle(el).transform)).not.toBe(search);
 await page.screenshot({path:'../../../.build/bug-hunter-lock.png',fullPage:true});
 await freeze(icon,1206);expect(await opacity(stage.locator('.hunter-shot'))).toBeGreaterThan(.8);expect(await opacity(stage.locator('.hunter-impact'))).toBeGreaterThan(0);
 await freeze(icon,1800);expect(await opacity(stage.locator('.hunter-shot'))).toBe(0);expect(await opacity(stage.locator('.hunter-reticle'))).toBe(0);
 await page.mouse.move(1300,20);await icon.hover();await expect(stage.locator('.hunter-reticle')).toHaveCSS('animation-name','hunter-target');
 await page.getByRole('button',{name:'Customize Bug Hunter badge',exact:true}).focus();await page.keyboard.press('Tab');await page.keyboard.press('Shift+Tab');await expect(page.locator('.sidebar-recognition .hunter-reticle')).toHaveCSS('animation-name','hunter-target');
});

test('unlock stages a larger search, target lock, shot and final beetle reveal',async({page})=>{
 await openHunter(page);await page.locator('.recognition-replay').click();const scene=page.getByRole('dialog');
 await expect(scene).toBeVisible();await expect(scene.locator('.badge-emblem')).toHaveCSS('animation-name','hunter-enter');await expect(scene.locator('.badge-emblem')).toHaveCSS('width','184px');
 await freeze(scene,900);expect(await opacity(scene.locator('.hunter-search-label'))).toBeGreaterThan(.9);expect(await opacity(scene.locator('.hunter-shot'))).toBe(0);
 await scene.screenshot({path:'../../../.build/bug-hunter-unlock-search.png'});
 await freeze(scene,2400);expect(await opacity(scene.locator('.hunter-lock-label'))).toBe(1);await expect(scene.locator('.hunter-reticle')).toHaveCSS('stroke','rgb(255, 164, 123)');
 await scene.screenshot({path:'../../../.build/bug-hunter-unlock-lock.png'});
 await freeze(scene,3216);expect(await opacity(scene.locator('.hunter-shot'))).toBeGreaterThan(.8);await scene.screenshot({path:'../../../.build/bug-hunter-unlock-shot.png'});
 await freeze(scene,4800);await expect(scene.getByRole('heading')).toHaveText('Bug Hunter');await expect(scene.getByRole('heading')).toHaveCSS('opacity','1');expect(await opacity(scene.locator('.hunter-shot'))).toBe(0);
 await scene.screenshot({path:'../../../.build/bug-hunter-unlock-reveal.png'});await page.getByRole('button',{name:'Continue',exact:true}).click();
});

test('motion toggle and reduced motion show a settled beetle without targeting or theatre',async({page})=>{
 await openHunter(page);const stage=page.locator('.recognition-stage');
 for(const reduced of [false,true]){
  if(reduced){await page.getByLabel('Enable badge animations').check();await page.emulateMedia({reducedMotion:'reduce'});}else await page.getByLabel('Enable badge animations').uncheck();
  await stage.locator('.special-badge').hover();
  for(const part of ['.beetle-wing-left','.beetle-wing-right','.hunter-reticle','.hunter-shot','.hunter-impact','.beetle-body'])await expect(stage.locator(part)).toHaveCSS('animation-name','none');
  await expect(stage.locator('.hunter-shot')).toHaveCSS('opacity','0');
  await page.locator('.recognition-replay').click();const scene=page.getByRole('dialog');await expect(scene.locator('.hunter-theatre')).toBeHidden();await expect(scene.locator('.badge-emblem')).toHaveCSS('animation-name','none');await expect(scene.getByRole('heading')).toHaveCSS('opacity','1');await expect(scene.locator('.hunter-shot')).toHaveCSS('opacity','0');await page.getByRole('button',{name:'Continue',exact:true}).click();
 }
});
