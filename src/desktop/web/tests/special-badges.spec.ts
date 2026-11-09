import {test,expect} from '@playwright/test';
import {installWorkspace} from './fixtures/workspace';
async function grant(page:import('@playwright/test').Page,badges=[{id:'alpha-tester',title:'Alpha Tester',robloxUserId:'123'}]){await page.evaluate(badges=>(window as any).testEmit({version:1,event:'badges.changed',data:{badges}}),badges);}

test('badge tab is absent for ordinary users',async({page})=>{
 await installWorkspace(page);await page.getByRole('button',{name:'Settings',exact:true}).click();await expect(page.getByRole('tab',{name:'Badges',exact:true})).toHaveCount(0);await expect(page.locator('.special-badge')).toHaveCount(0);
});

test('refactored Alpha studio follows Import, preserves visibility and has its own motion',async({page})=>{
 await installWorkspace(page);await grant(page);await page.getByRole('button',{name:'Settings',exact:true}).click();
 const tabs=await page.getByRole('tab').allTextContents();expect(tabs.indexOf('Badges')).toBe(tabs.indexOf('Import')+1);
 await page.getByRole('tab',{name:'Support',exact:true}).click();await expect(page.locator('.recognition-workbench')).toHaveCount(0);
 await page.getByRole('tab',{name:'Badges',exact:true}).click();await expect(page.locator('.sidebar-recognition .special-badge')).toHaveAccessibleName('Alpha Tester');
 await expect(page.locator('.badge-design-gallery')).toHaveCount(0);await expect(page.locator('.recognition-stage .alpha-symbol')).toBeVisible();
 await page.getByRole('button',{name:'Aurora',exact:true}).click();await expect(page.getByRole('button',{name:'Aurora',exact:true})).toHaveAttribute('aria-pressed','true');
 await page.getByLabel('Show this badge').uncheck();await expect(page.locator('.sidebar-recognition .special-badge')).toHaveCount(0);await expect(page.locator('.recognition-stage .special-badge')).toBeVisible();await expect(page.getByRole('tab',{name:'Badges',exact:true})).toBeVisible();
 await page.getByLabel('Show this badge').check();await page.locator('.recognition-stage .special-badge').hover();await expect(page.locator('.recognition-stage .alpha-front')).toHaveCSS('animation-name','alpha-front-focus');
 await page.locator('.recognition-replay').click();await expect(page.getByRole('dialog')).toBeVisible();await expect(page.getByRole('dialog').locator('.badge-emblem')).toHaveCSS('animation-name','alpha-birth');await page.getByRole('button',{name:'Continue',exact:true}).click();
 await page.getByLabel('Enable badge animations').uncheck();await expect(page.locator('.recognition-stage .alpha-front')).toHaveCSS('animation-name','none');
 await grant(page,[]);await expect(page.getByRole('tab',{name:'Badges',exact:true})).toHaveCount(0);await expect(page.locator('.recognition-workbench')).toHaveCount(0);
});

test('only recolorable artwork has palettes and each badge keeps independent preferences',async({page})=>{
 await installWorkspace(page);await grant(page,[{id:'alpha-tester',title:'Alpha Tester',robloxUserId:'123'},{id:'contributor',title:'Contributor',robloxUserId:'123'},{id:'founder',title:'Founder',robloxUserId:'123'},{id:'bug-hunter',title:'Bug Hunter',robloxUserId:'123'}]);
 await page.getByRole('button',{name:'Customize Alpha Tester badge',exact:true}).click();await page.getByRole('button',{name:'Aurora',exact:true}).click();await page.getByLabel('Show this badge').uncheck();
 await page.getByRole('button',{name:'Select Contributor badge',exact:true}).click();await expect(page.getByLabel('Show this badge')).toBeChecked();await page.getByRole('button',{name:'Solar',exact:true}).click();
 await expect(page.locator('.recognition-stage .code-brackets')).toHaveCSS('stroke','rgb(255, 155, 184)');
 for(const id of ['founder','bug-hunter']){await page.getByRole('button',{name:'Select '+(id==='founder'?'Founder':'Bug Hunter')+' badge',exact:true}).click();await expect(page.locator('.recognition-palettes')).toHaveCount(0);await expect(page.getByText('Color palette',{exact:true})).toHaveCount(0);await expect(page.getByLabel('Enable badge animations')).toBeVisible();}
 await page.getByRole('button',{name:'Select Alpha Tester badge',exact:true}).click();await expect(page.getByLabel('Show this badge')).not.toBeChecked();await expect(page.getByRole('button',{name:'Aurora',exact:true})).toHaveAttribute('aria-pressed','true');await expect(page.locator('.recognition-stage .alpha-pane').first()).toHaveCSS('stroke','rgb(101, 226, 209)');
 await page.getByRole('button',{name:'Select Contributor badge',exact:true}).click();await expect(page.getByRole('button',{name:'Solar',exact:true})).toHaveAttribute('aria-pressed','true');
});

test('legacy shape choices migrate to the standalone Alpha and unknown signed kinds retain artwork',async({page})=>{
 await installWorkspace(page);await page.evaluate(()=>localStorage.setItem('rmi.special-badge-style',JSON.stringify({design:'fan',palette:1,motion:false,visible:false})));
 await grant(page);await page.getByRole('button',{name:'Settings',exact:true}).click();await page.getByRole('tab',{name:'Badges',exact:true}).click();
 await expect(page.locator('.recognition-stage .special-badge')).toHaveClass(/design-native/);await expect(page.getByLabel('Show this badge')).not.toBeChecked();await expect(page.getByLabel('Enable badge animations')).not.toBeChecked();await expect(page.getByRole('button',{name:'Aurora',exact:true})).toHaveAttribute('aria-pressed','true');await expect(page.locator('.badge-plate,.badge-rim,.badge-ribbon')).toHaveCount(0);
 await grant(page,[{id:'community-helper',title:'Community Helper',robloxUserId:'123'}]);await expect(page.locator('.recognition-stage .alpha-symbol')).toBeVisible();await expect(page.locator('.recognition-stage .special-badge')).toHaveAccessibleName('Community Helper');
});
