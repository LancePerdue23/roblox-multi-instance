import {test,expect,type Locator} from '@playwright/test';
import {installWorkspace} from './fixtures/workspace';
const badges=[{id:'alpha-tester',title:'Alpha Tester'},{id:'contributor',title:'Contributor'},{id:'founder',title:'Founder'},{id:'bug-hunter',title:'Bug Hunter'}].map(b=>({...b,robloxUserId:'123'}));
async function freeze(scene:Locator,time:number){await scene.evaluate((el,time)=>el.getAnimations({subtree:true}).forEach(a=>{a.pause();a.currentTime=time;}),time);}
async function opacity(el:Locator){return el.evaluate(el=>Number(getComputedStyle(el).opacity));}
const scenes=[
 {id:'alpha-tester',title:'Alpha Tester',idle:'.alpha-first-star',idleName:'alpha-star-idle',hover:'.alpha-front',hoverName:'alpha-front-focus',unlock:'alpha-birth',duration:5000},
 {id:'contributor',title:'Contributor',idle:'.code-cursor',idleName:'code-cursor',hover:'.merge-check',hoverName:'merge-approve',unlock:'contributor-build',duration:5200},
 {id:'founder',title:'Founder',idle:'.steam-left',idleName:'founder-steam',hover:'.founder-taco',hoverName:'founder-toss',unlock:'founder-recipe',duration:5200}
];
for(const art of scenes){
 test(art.title+' has detailed idle, hover and staged unlock artwork',async({page})=>{
  await installWorkspace(page);await page.evaluate(badges=>(window as any).testEmit({version:1,event:'badges.changed',data:{badges}}),badges);await page.getByRole('button',{name:'Customize '+art.title+' badge',exact:true}).click();
  const stage=page.locator('.recognition-stage'),icon=stage.locator('.special-badge');
  await expect(stage.locator('svg')).toHaveAttribute('viewBox','0 0 96 96');await expect(page.locator('.badge-design-gallery,.badge-plate,.badge-ribbon')).toHaveCount(0);
  await page.mouse.move(1300,20);await expect(stage.locator(art.idle)).toHaveCSS('animation-name',art.idleName);
  await icon.hover();await expect(stage.locator(art.hover)).toHaveCSS('animation-name',art.hoverName);await freeze(icon,900);if(!process.env.CI)await stage.screenshot({path:'../../../.build/'+art.id+'-detail-hover.png'});
  const button=page.getByRole('button',{name:'Customize '+art.title+' badge',exact:true});await button.focus();await page.keyboard.press('Tab');await page.keyboard.press('Shift+Tab');await expect(page.locator('.sidebar-recognition .kind-'+art.id+' '+art.hover)).toHaveCSS('animation-name',art.hoverName);
  await page.locator('.recognition-replay').click();const scene=page.getByRole('dialog');await expect(scene.locator('.badge-emblem')).toHaveCSS('animation-name',art.unlock);
  await freeze(scene,art.duration*.2);
  if(art.id==='alpha-tester'){expect(await opacity(scene.locator('.alpha-back'))).toBeGreaterThan(0);expect(await opacity(scene.locator('.alpha-front'))).toBe(0);}
  if(art.id==='contributor')expect(await opacity(scene.locator('.merge-check'))).toBe(0);
  if(art.id==='founder'){expect(await opacity(scene.locator('.founder-shell'))).toBe(1);expect(await opacity(scene.locator('.founder-lettuce'))).toBe(0);expect(await opacity(scene.locator('.founder-crown'))).toBe(0);}
  if(!process.env.CI)await scene.screenshot({path:'../../../.build/'+art.id+'-detail-unlock-start.png'});
  await freeze(scene,art.duration*.5);if(!process.env.CI)await scene.screenshot({path:'../../../.build/'+art.id+'-detail-unlock-middle.png'});
  await freeze(scene,art.duration*.78);
  if(art.id==='contributor'){await expect(scene.locator('.merge-check')).toHaveCSS('stroke-dashoffset','0px');expect(await opacity(scene.locator('.merge-check'))).toBe(1);}
  if(art.id==='founder')expect(await opacity(scene.locator('.founder-crown'))).toBeGreaterThan(.9);
  if(!process.env.CI)await scene.screenshot({path:'../../../.build/'+art.id+'-detail-unlock-impact.png'});
  await freeze(scene,art.duration);await expect(scene.getByRole('heading')).toHaveText(art.title);await expect(scene.getByRole('heading')).toHaveCSS('opacity','1');
  if(!process.env.CI)await scene.screenshot({path:'../../../.build/'+art.id+'-detail-unlock-final.png'});await page.getByRole('button',{name:'Continue',exact:true}).click();
 });
 test(art.title+' settles with motion disabled and with reduced motion',async({page})=>{
  await installWorkspace(page);await page.evaluate(badges=>(window as any).testEmit({version:1,event:'badges.changed',data:{badges}}),badges);await page.getByRole('button',{name:'Customize '+art.title+' badge',exact:true}).click();
  for(const reduced of [false,true]){
   if(reduced){await page.getByLabel('Enable badge animations').check();await page.emulateMedia({reducedMotion:'reduce'});}else await page.getByLabel('Enable badge animations').uncheck();
   const stage=page.locator('.recognition-stage');await stage.locator('.special-badge').hover();await expect(stage.locator(art.idle)).toHaveCSS('animation-name','none');await expect(stage.locator(art.hover)).toHaveCSS('animation-name','none');
   expect(await stage.locator('svg').evaluate(el=>el.getAnimations({subtree:true}).length)).toBe(0);
   expect(await stage.locator('svg').evaluate(el=>{const svg=el as unknown as SVGSVGElement,b=svg.getBBox(),v=svg.viewBox.baseVal;return b.x>=v.x&&b.y>=v.y&&b.x+b.width<=v.x+v.width&&b.y+b.height<=v.y+v.height;})).toBe(true);
   await page.locator('.recognition-replay').click();const scene=page.getByRole('dialog');await expect(scene.locator('.recognition-theatre')).toBeHidden();await expect(scene.locator('.badge-emblem')).toHaveCSS('animation-name','none');await expect(scene.getByRole('heading')).toHaveCSS('opacity','1');
   expect(await scene.locator('svg.badge-emblem').evaluate(el=>el.getAnimations({subtree:true}).length)).toBe(0);
   if(art.id==='founder')await expect(scene.locator('.founder-crown')).toHaveCSS('opacity','1');
   if(art.id==='contributor')await expect(scene.locator('.merge-check')).toHaveCSS('stroke-dashoffset','0px');
   await page.getByRole('button',{name:'Continue',exact:true}).click();
  }
 });
}
