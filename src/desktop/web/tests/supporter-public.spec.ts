import {test,expect} from '@playwright/test';
import {existsSync,readFileSync,readdirSync} from 'node:fs';
import {installWorkspace} from './fixtures/workspace';
test('normal build excludes supporter preview entry points and role-changing UI',async({page})=>{
 test.skip(existsSync('dist/local-supporter-preview.json'),'Normal-build verification');
 const scripts=readdirSync('dist/assets').filter(file=>file.endsWith('.js')).map(file=>readFileSync('dist/assets/'+file,'utf8')).join('');
 expect(scripts).not.toContain('Local role preview');
 expect(scripts).not.toContain('builtAt');
 expect(scripts).not.toContain('Local testing');
 expect(existsSync('dist/local-role-switcher.js')).toBe(false);
 await installWorkspace(page);await page.goto('/?supporter-preview');
 await expect(page.getByRole('heading',{name:'Instances',exact:true})).toBeVisible();
 await expect(page.getByLabel('Local role preview')).toHaveCount(0);
 await page.getByRole('button',{name:'Support development',exact:true}).click();
 await expect(page.getByRole('heading',{name:'Meet the supporter tiers',exact:true})).toBeVisible();
 await expect(page.getByRole('button',{name:'Replay Supporter unlock',exact:true})).toHaveCount(0);
 expect(await page.evaluate(()=>(window as any).testCalls.filter((call:any)=>call.command==='support.setRole'))).toHaveLength(0);
});


test('badge previews stay public while replay follows verified supporter status',async({page})=>{
 await installWorkspace(page);await page.goto('/?supporter-verified');
 await page.getByRole('button',{name:'Support development',exact:true}).click();
 await expect(page.getByRole('button',{name:'Replay Supporter unlock',exact:true})).toBeEnabled();
 await page.getByRole('button',{name:'Replay Supporter unlock',exact:true}).click();
 await expect(page.getByRole('dialog')).toBeVisible();
 await page.getByRole('button',{name:'Continue',exact:true}).click();
 for(const index of [1,2]){
  await page.locator('.tier-choice').nth(index).click();
  await expect(page.locator('.cosmetic-stage .heart-badge')).toContainText(index===1?'Supporter+':'Super Supporter');
  await expect(page.getByRole('button',{name:/Replay .* unlock/})).toHaveCount(0);
  await expect(page.locator('.studio-options')).not.toContainText(/replay/i);
 }
});
