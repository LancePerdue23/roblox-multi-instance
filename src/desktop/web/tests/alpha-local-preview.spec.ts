import {existsSync} from 'node:fs';
import {test,expect} from '@playwright/test';
import {installWorkspace} from './fixtures/workspace';
test('local Alpha preview opens its dedicated studio and distinct reveal',async({page})=>{
 test.skip(!existsSync('dist/local-role-switcher.js'),'Owner-local preview only');
 await installWorkspace(page);await page.getByLabel('Local role preview').selectOption('3');
 await expect(page.getByRole('dialog')).toBeVisible();await expect(page.getByRole('dialog')).toHaveClass(/recognition-unlock/);await page.getByRole('button',{name:'Continue',exact:true}).click();
 await expect(page.getByRole('tab',{name:'Badges',exact:true})).toHaveAttribute('aria-selected','true');
 await page.getByRole('button',{name:'Nebula',exact:true}).click();await expect(page.locator('.local-special-button .special-badge')).toHaveAccessibleName('Alpha Tester');const badgeBox=await page.locator('.local-special-button .special-badge').boundingBox();const buttonBox=await page.locator('.local-special-button').boundingBox();expect(badgeBox!.height).toBeLessThanOrEqual(38);const supportBox=await page.locator('.sidebar-bottom>button.support-link').boundingBox();expect(Math.abs(badgeBox!.x-supportBox!.x)).toBeLessThan(1);
 const sidebar=page.locator('.local-special-button .special-badge');
 await expect(sidebar.locator('svg.badge-emblem')).toBeVisible();await expect(sidebar.locator('.badge-era')).toHaveCount(0);
 await expect(page.locator('.badge-design-gallery')).toHaveCount(0);await expect(sidebar.locator('.alpha-front')).toBeVisible();expect(await sidebar.locator('svg').innerHTML()).toBe(await page.locator('.recognition-stage .badge-emblem').innerHTML());
 await page.getByLabel('Show this badge').uncheck();await expect(page.getByRole('button',{name:'Customize special badges',exact:true})).toBeHidden();await expect(page.locator('.recognition-stage .special-badge')).toBeVisible();await page.getByLabel('Show this badge').check();
 await page.getByLabel('Enable badge animations').uncheck();await expect(page.locator('.local-role-button .special-badge')).not.toHaveClass(/special-motion/);await page.getByLabel('Enable badge animations').check();
 await page.screenshot({path:'../../../.build/alpha-local-preview.png',fullPage:true});await page.reload();await expect(page.getByLabel('Local role preview')).toHaveValue('3');await expect(page.locator('.local-special-button svg.badge-emblem')).toBeVisible();await page.getByRole('button',{name:'Customize special badges',exact:true}).click();await expect(page.getByRole('heading',{name:'Your badges',exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Instances',exact:true}).click();await page.getByRole('button',{name:'Customize special badges',exact:true}).click();await expect(page.getByRole('tab',{name:'Badges',exact:true})).toHaveAttribute('aria-selected','true');await expect(page.getByRole('dialog')).toHaveCount(0);
 await page.getByLabel('Local role preview').selectOption('-2');await expect(page.getByRole('tab',{name:'Badges',exact:true})).toHaveCount(0);
});


test('saved local badge never paints above controls during delayed initialization',async({page})=>{
 test.skip(!existsSync('dist/local-role-switcher.js'),'Owner-local preview only');
 await installWorkspace(page);
 await page.addInitScript(()=>localStorage.setItem('rmi.local-role-preview','3'));
 await page.route('**/local-role-switcher.js',route=>route.fulfill({status:200,contentType:'text/javascript',body:''}));
 await page.reload();
 const badge=page.getByRole('button',{name:'Customize special badges',exact:true,includeHidden:true});
 await expect(badge).toHaveCount(1);await expect(badge).toBeHidden();
 await page.unroute('**/local-role-switcher.js');await page.addScriptTag({url:'https://preview-fixture.invalid/local-role-switcher.js'});
 await expect(badge).toBeVisible();
 const controls=page.locator('.local-role-tools');
 const assertOrder=async()=>{const controlBox=await controls.boundingBox();const badgeBox=await badge.boundingBox();expect(badgeBox!.y).toBeGreaterThanOrEqual(controlBox!.y+controlBox!.height);};
 await assertOrder();
 await page.getByRole('button',{name:'Settings',exact:true}).click();await assertOrder();
 await page.getByLabel('Local role preview').selectOption('-2');await expect(badge).toHaveCount(0);
 await page.getByLabel('Local role preview').selectOption('3');await page.getByRole('button',{name:'Continue',exact:true}).click();await expect(badge).toBeVisible();await assertOrder();
});
