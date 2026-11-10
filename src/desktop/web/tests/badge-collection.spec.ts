import {test,expect,type Page} from '@playwright/test';
import {existsSync} from 'node:fs';
import {installWorkspace} from './fixtures/workspace';
const badges=[{id:'alpha-tester',title:'Alpha Tester'},{id:'bug-hunter',title:'Bug Hunter'},{id:'contributor',title:'Contributor'},{id:'founder',title:'Founder'}].map(b=>({...b,robloxUserId:'123'}));
async function grant(page:Page,items=badges){await page.evaluate(badges=>(window as any).testEmit({version:1,event:'badges.changed',data:{badges}}),items);}

test('collection cards show all earned kinds including hidden badges and select one editor without an unlock',async({page})=>{
 await installWorkspace(page);await page.evaluate(()=>localStorage.setItem('rmi.special-badge-style',JSON.stringify({badges:{founder:{visible:false}}})));
 await grant(page,[...badges,{...badges[0],robloxUserId:'456'}]);await page.getByRole('button',{name:'Settings',exact:true}).click();await page.getByRole('tab',{name:'Badges',exact:true}).click();
 const collection=page.getByRole('group',{name:'Your badge collection'});await expect(collection.getByRole('button')).toHaveCount(4);await expect(page.getByRole('combobox',{name:'Choose special badge'})).toHaveCount(0);
 await expect(collection.getByRole('button',{name:'Select Founder badge',exact:true})).toContainText('Hidden');
 for(const badge of badges){const choice=collection.getByRole('button',{name:'Select '+badge.title+' badge',exact:true});await choice.focus();await page.keyboard.press('Enter');await expect(choice).toHaveAttribute('aria-pressed','true');await expect(collection.locator('[aria-pressed=true]')).toHaveCount(1);await expect(page.locator('.recognition-workbench')).toHaveCount(1);await expect(page.getByRole('region',{name:'Customize '+badge.title,exact:true})).toBeVisible();await expect(page.getByRole('dialog')).not.toBeVisible();}
 await expect(page.getByLabel('Show this badge')).not.toBeChecked();await page.getByLabel('Show this badge').check();await expect(collection.getByRole('button',{name:'Select Founder badge',exact:true})).toContainText('Shown');
 await page.screenshot({path:'../../../.build/badge-collection-grid.png',fullPage:true});
});

test('sidebar icons name badges on hover and focus, and overflow leads to the full collection',async({page})=>{
 await installWorkspace(page);await grant(page);const sidebar=page.locator('.sidebar-recognition');
 const hunter=sidebar.getByRole('button',{name:'Customize Bug Hunter badge',exact:true});await hunter.hover();await expect(hunter.getByRole('tooltip')).toHaveText('Bug Hunter');
 await hunter.focus();await page.keyboard.press('Tab');await page.keyboard.press('Shift+Tab');await page.mouse.move(1300,20);await expect(hunter.getByRole('tooltip')).toBeVisible();
 const extra=[...badges,{id:'community-helper',title:'Community Helper',robloxUserId:'123'},{id:'launch-crew',title:'Launch Crew',robloxUserId:'123'}];await grant(page,extra);
 await expect(sidebar.getByRole('button',{name:'View 3 more badges',exact:true})).toBeVisible();await expect(sidebar.locator('.special-badge')).toHaveCount(3);
 await sidebar.getByRole('button',{name:'View 3 more badges',exact:true}).click();await expect(page.getByRole('group',{name:'Your badge collection'}).getByRole('button')).toHaveCount(6);await expect(page.getByRole('button',{name:'Select Founder badge',exact:true})).toHaveAttribute('aria-pressed','true');await expect(page.getByRole('dialog')).not.toBeVisible();
 await page.getByRole('button',{name:'Select Alpha Tester badge',exact:true}).click();await page.getByLabel('Show this badge').uncheck();await expect(sidebar.getByRole('button',{name:'View 2 more badges',exact:true})).toBeVisible();
});

for(const size of [{width:1440,height:960,scale:1},{width:900,height:600,scale:1.25},{width:1000,height:640,scale:1.5},{width:800,height:440,scale:2},{width:900,height:320,scale:2},{width:720,height:480,scale:2}]){
 test('public sidebar fits '+size.width+'x'+size.height+' at '+size.scale+'x without scrolling or clipped controls',async({browser})=>{
  test.skip(existsSync('dist/local-supporter-preview.json'),'Public layout verification');
  const context=await browser.newContext({viewport:{width:size.width,height:size.height},deviceScaleFactor:size.scale});const page=await context.newPage();
  try{
   await installWorkspace(page);await page.goto('/?supporter-verified');await grant(page,[...badges,...Array.from({length:12},(_,i)=>({id:'extra-'+i,title:'Extra '+i,robloxUserId:'123'}))]);
   await expect(page.locator('.sidebar')).toHaveClass(/sidebar-regular/);await page.getByRole('button',{name:'Settings',exact:true}).click();await page.getByRole('tab',{name:'Support',exact:true}).click();await page.getByRole('tab',{name:'Badges',exact:true}).click();
   await expect(page.locator('.sidebar')).toHaveCSS('overflow-y','hidden');
   const layout=await page.locator('.sidebar').evaluate(el=>{const sidebar=el as HTMLElement,nav=sidebar.querySelector('nav')!,footer=sidebar.querySelector('.sidebar-bottom')!,r=sidebar.getBoundingClientRect();return {scrollHeight:sidebar.scrollHeight,clientHeight:sidebar.clientHeight,scrollWidth:sidebar.scrollWidth,clientWidth:sidebar.clientWidth,top:r.top,bottom:r.bottom,navBottom:nav.getBoundingClientRect().bottom,footerTop:footer.getBoundingClientRect().top,buttons:[...nav.querySelectorAll('button')].map(b=>({top:b.getBoundingClientRect().top,bottom:b.getBoundingClientRect().bottom})),versionBottom:sidebar.querySelector('.sidebar-version')!.getBoundingClientRect().bottom};});
   expect(layout.scrollHeight).toBeLessThanOrEqual(layout.clientHeight+1);expect(layout.scrollWidth).toBeLessThanOrEqual(layout.clientWidth+1);expect(layout.top).toBeGreaterThanOrEqual(0);expect(layout.bottom).toBeLessThanOrEqual(size.height);expect(layout.navBottom).toBeLessThanOrEqual(layout.footerTop);expect(layout.versionBottom).toBeLessThanOrEqual(size.height);
   expect(layout.buttons).toHaveLength(7);for(const b of layout.buttons){expect(b.top).toBeGreaterThanOrEqual(0);expect(b.bottom).toBeLessThanOrEqual(size.height);}
   const row=page.locator('.sidebar-recognition');expect(await row.evaluate(el=>el.scrollWidth<=el.clientWidth+1)).toBe(true);
   await page.locator('.sidebar').hover();await page.mouse.wheel(0,800);expect(await page.locator('.sidebar').evaluate(el=>el.scrollTop)).toBe(0);
   await page.locator('main').evaluate(el=>{el.scrollTop=500;});expect(await page.locator('main').evaluate(el=>el.scrollTop)).toBeGreaterThan(0);expect(await page.locator('.sidebar').evaluate(el=>el.scrollTop)).toBe(0);
   if(size.width===900&&size.height===600)await page.screenshot({path:'../../../.build/badge-sidebar-minimum.png'});
  }finally{await context.close();}
 });
}
