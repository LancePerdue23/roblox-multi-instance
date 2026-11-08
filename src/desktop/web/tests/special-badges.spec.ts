import {test,expect} from '@playwright/test';
import {installWorkspace} from './fixtures/workspace';
async function grant(page:import('@playwright/test').Page,badges=[{id:'alpha-tester',title:'Alpha Tester',robloxUserId:'123'}]){await page.evaluate(badges=>(window as any).testEmit({version:1,event:'badges.changed',data:{badges}}),badges);}
test('badge tab is absent for ordinary users',async({page})=>{
 await installWorkspace(page);await page.getByRole('button',{name:'Settings',exact:true}).click();await expect(page.getByRole('tab',{name:'Badges',exact:true})).toHaveCount(0);await expect(page.locator('.special-badge')).toHaveCount(0);
});
test('badge studio follows Import, controls visibility and uses its own animations',async({page})=>{
 await installWorkspace(page);await grant(page);await page.getByRole('button',{name:'Settings',exact:true}).click();
 const tabs=await page.getByRole('tab').allTextContents();expect(tabs.indexOf('Badges')).toBe(tabs.indexOf('Import')+1);
 await page.getByRole('tab',{name:'Support',exact:true}).click();await expect(page.locator('.recognition-workbench')).toHaveCount(0);
 await page.getByRole('tab',{name:'Badges',exact:true}).click();await expect(page.locator('.recognition-stage')).toBeVisible();await expect(page.locator('.sidebar-recognition .special-badge')).toHaveAccessibleName('Alpha Tester');
 await page.getByRole('button',{name:'Aurora',exact:true}).click();await expect(page.getByRole('button',{name:'Aurora',exact:true})).toHaveAttribute('aria-pressed','true');
 await page.getByLabel('Show this badge').uncheck();await expect(page.locator('.sidebar-recognition .special-badge')).toHaveCount(0);await expect(page.locator('.recognition-stage .special-badge')).toBeVisible();await expect(page.getByRole('tab',{name:'Badges',exact:true})).toBeVisible();
 await page.getByLabel('Show this badge').check();await page.locator('.recognition-stage .special-badge').hover();await expect(page.locator('.recognition-stage .badge-emblem')).toHaveCSS('animation-name','pin-hover');
 await page.locator('.recognition-replay').click();await expect(page.getByRole('dialog')).toBeVisible();await expect(page.getByRole('dialog').locator('.unlock-halo')).toHaveCount(0);await page.getByRole('button',{name:'Continue',exact:true}).click();
 await page.getByLabel('Enable badge animations').uncheck();await expect(page.locator('.recognition-stage .badge-emblem')).toHaveCSS('animation-name','none');await page.locator('.recognition-replay').click();await expect(page.getByRole('dialog').locator('.special-badge')).toHaveCSS('animation-name','none');await page.getByRole('button',{name:'Continue',exact:true}).click();
 await page.getByLabel('Enable badge animations').check();await page.emulateMedia({reducedMotion:'reduce'});await expect(page.locator('.recognition-stage .badge-emblem')).toHaveCSS('animation-name','none');await page.locator('.recognition-stage .special-badge').hover();await expect(page.locator('.recognition-stage .badge-emblem')).toHaveCSS('animation-name','none');
 await page.screenshot({path:'../../../.build/alpha-redesign.png',fullPage:true});
 await grant(page,[]);await expect(page.getByRole('tab',{name:'Badges',exact:true})).toHaveCount(0);await expect(page.locator('.recognition-workbench')).toHaveCount(0);
});
test('future special badges have independent appearance preferences',async({page})=>{
 await installWorkspace(page);await grant(page,[{id:'alpha-tester',title:'Alpha Tester',robloxUserId:'123'},{id:'contributor',title:'Contributor',robloxUserId:'123'}]);await page.getByRole('button',{name:'Settings',exact:true}).click();await page.getByRole('tab',{name:'Badges',exact:true}).click();
 await page.getByLabel('Show this badge').uncheck();await page.getByLabel('Choose special badge').selectOption('contributor');await expect(page.getByLabel('Show this badge')).toBeChecked();await page.getByRole('button',{name:'Solar',exact:true}).click();await page.getByLabel('Choose special badge').selectOption('alpha-tester');await expect(page.getByLabel('Show this badge')).not.toBeChecked();await expect(page.getByRole('button',{name:'Nebula',exact:true})).toHaveAttribute('aria-pressed','true');
});

test('two identity-based badge designs can be compared and selected',async({page})=>{
 await installWorkspace(page);await grant(page);await page.getByRole('button',{name:'Settings',exact:true}).click();await page.getByRole('tab',{name:'Badges',exact:true}).click();
 await expect(page.locator('.badge-design-choice')).toHaveCount(2);
 for(const [name,id] of [['Circle','link'],['Ribbon','fan']]){await page.getByRole('button',{name:name+' design',exact:true}).click();await expect(page.getByRole('button',{name:name+' design',exact:true})).toHaveAttribute('aria-pressed','true');await expect(page.locator('.recognition-stage .special-badge')).toHaveClass(new RegExp('design-'+id));await expect(page.locator('.recognition-stage .special-badge')).toHaveCSS('background-image','none');await expect(page.locator('.sidebar-recognition .special-badge')).toHaveClass(new RegExp('design-'+id));}
 await page.getByRole('button',{name:'Circle design',exact:true}).click();await page.screenshot({path:'../../../.build/three-badge-designs.png',fullPage:true});await page.locator('.badge-design-gallery').screenshot({path:'../../../.build/badge-identity-concepts.png'});
});


test('circle and ribbon animate distinctly, fit their artwork and respect motion settings',async({page})=>{
 await installWorkspace(page);await grant(page);await page.getByRole('button',{name:'Settings',exact:true}).click();await page.getByRole('tab',{name:'Badges',exact:true}).click();
 for(const [name,hover,unlock] of [['Circle','pin-hover','pin-arrival'],['Ribbon','ribbon-hover','ribbon-arrival']]){
  await page.getByRole('button',{name:name+' design',exact:true}).click();
  const graphic=page.locator('.recognition-stage .badge-emblem');
  await expect(page.locator('.recognition-stage .badge-era')).toHaveCount(0);
  expect(await graphic.evaluate(svg=>{const box=(svg as unknown as SVGGraphicsElement).getBBox();const view=(svg as unknown as SVGSVGElement).viewBox.baseVal;return box.x>=view.x&&box.y>=view.y&&box.x+box.width<=view.x+view.width&&box.y+box.height<=view.y+view.height;})).toBe(true);
  await graphic.hover();await expect(graphic).toHaveCSS('animation-name',hover);
  await page.locator('.recognition-replay').click();const arrival=page.getByRole('dialog').locator('.badge-emblem');await expect(arrival).toHaveCSS('animation-name',unlock);await expect(page.getByRole('dialog').getByText('Alpha Tester',{exact:true})).toHaveCount(1);const effect=page.getByRole('dialog').locator('.special-badge');const anchor=await effect.evaluate(el=>({top:parseFloat(getComputedStyle(el,'::before').top),height:el.getBoundingClientRect().height}));expect(anchor.top).toBeCloseTo(anchor.height/2-4,0);await page.getByRole('dialog').evaluate(el=>el.getAnimations({subtree:true}).forEach(a=>{a.pause();a.currentTime=750;}));await page.getByRole('dialog').screenshot({path:'../../../.build/unlock-'+unlock+'.png'});await expect(page.getByRole('dialog').locator('.window-first')).toHaveCSS('animation-name','window-assemble');
  await page.getByRole('button',{name:'Continue',exact:true}).click();
  await page.getByLabel('Enable badge animations').uncheck();await graphic.hover();await expect(graphic).toHaveCSS('animation-name','none');
  await page.locator('.recognition-replay').click();await expect(arrival).toHaveCSS('animation-name','none');await page.getByRole('button',{name:'Continue',exact:true}).click();await page.getByLabel('Enable badge animations').check();
  await page.emulateMedia({reducedMotion:'reduce'});await graphic.hover();await expect(graphic).toHaveCSS('animation-name','none');await page.locator('.recognition-replay').click();await expect(arrival).toHaveCSS('animation-name','none');await page.getByRole('button',{name:'Continue',exact:true}).click();await page.emulateMedia({reducedMotion:'no-preference'});
 }
});
