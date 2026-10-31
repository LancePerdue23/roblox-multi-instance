import {test,expect} from '@playwright/test';
import {installScenario,recordedCalls} from '../fixtures/scenario';

const nav=async(page:import('@playwright/test').Page,name:string)=>{
  await page.getByRole('button',{name,exact:true}).click();
  await expect(page.locator('main')).toHaveAttribute('aria-busy','false');
};

test('basic: first launch and all destinations',async({page})=>{
  await installScenario(page);
  for(const name of ['Layouts','Activity','System','Settings','Instances']){
    await nav(page,name);await expect(page.getByRole('heading',{name,exact:true,level:1})).toBeVisible();
  }
  expect((await recordedCalls(page)).some(c=>['launch','system.maintenance','system.integration'].includes(c.command))).toBe(false);
});
test('basic: missing helper disables launch and offers setup',async({page})=>{
  await installScenario(page,{helperInstalled:false});await expect(page.getByRole('button',{name:'Launch Roblox',exact:true})).toBeDisabled();
  await page.getByRole('button',{name:'System',exact:true}).click();await page.getByRole('tab',{name:'Health',exact:true}).click();
  await expect(page.getByRole('button',{name:'Set up helper…',exact:true})).toBeVisible();
  expect((await recordedCalls(page)).filter(c=>c.command==='launch')).toHaveLength(0);
});
test('basic: select, focus, cancel close, and restore keyboard focus',async({page})=>{
  await installScenario(page);await page.getByLabel('Select Synthetic window 1').check();
  await page.getByRole('button',{name:'Focus',exact:true}).click();
  const close=page.getByRole('button',{name:'Close window…',exact:true});await close.click();
  await expect(page.getByRole('dialog')).toBeVisible();await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).not.toBeVisible();await expect(close).toBeFocused();
  const calls=await recordedCalls(page);expect(calls.filter(c=>c.command==='instances.focus')).toHaveLength(1);expect(calls.some(c=>c.command==='instances.close')).toBe(false);
});
test('basic: appearance persists after reload',async({page})=>{
  await installScenario(page);await nav(page,'Settings');await page.getByRole('button',{name:'Light Use this scheme'}).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme','Light');await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme','Light');
});
test('basic: notification cancel and confirm have distinct effects',async({page})=>{
  await installScenario(page);await nav(page,'Settings');await page.getByRole('tab',{name:'Notifications',exact:true}).click();
  await page.getByLabel('Show launch progress').check();await page.getByRole('button',{name:'Save notifications…',exact:true}).click();await page.keyboard.press('Escape');
  expect((await recordedCalls(page)).some(c=>c.command==='settings.notifications')).toBe(false);
  await page.getByRole('button',{name:'Save notifications…',exact:true}).click();await page.getByRole('button',{name:'Save shared notifications',exact:true}).click();
  await expect.poll(async()=>(await recordedCalls(page)).filter(c=>c.command==='settings.notifications'&&c.payload.confirmed===true).length).toBe(1);
});
test('basic: offline update check recovers on retry',async({page})=>{
  await installScenario(page,{failures:{'updates.check':1}});await nav(page,'System');
  await page.getByRole('button',{name:'Check for updates',exact:true}).click();await expect(page.getByRole('alert')).toContainText('Synthetic recoverable failure');
  await page.getByRole('button',{name:'Check for updates',exact:true}).click();await expect(page.getByText('9.9.9',{exact:true})).toBeVisible();
});
test('basic: narrow layout, long content, forced colors and keyboard tabs',async({page})=>{
  await installScenario(page,{activity:20});await page.setViewportSize({width:900,height:640});
  await nav(page,'System');await page.getByRole('button',{name:'Check for updates',exact:true}).click();await expect(page.getByText('9.9.9',{exact:true})).toBeVisible();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  await nav(page,'Settings');const tab=page.getByRole('tab',{name:'Appearance',exact:true});await tab.focus();await tab.press('ArrowRight');await expect(page.getByRole('tab',{name:'Notifications',exact:true})).toBeFocused();
  await page.emulateMedia({forcedColors:'active'});await page.screenshot({path:test.info().outputPath('forced-colors.png')});
});
test('basic: unavailable history remains distinct from empty history',async({page})=>{
  await installScenario(page,{historyUnavailable:true});await nav(page,'Activity');
  await expect(page.getByRole('heading',{name:'Launch history unavailable',exact:true})).toBeVisible();
  await expect(page.getByText('No matching launch results',{exact:true})).toHaveCount(0);
  expect((await recordedCalls(page)).some(c=>c.command==='activity.clear')).toBe(false);
});
test('power: selection, order, monitor assignment and partial placement',async({page})=>{
  await installScenario(page,{instances:10});await nav(page,'Layouts');await page.getByRole('button',{name:'Select all',exact:true}).click();
  await expect(page.getByRole('button',{name:'Apply layout…',exact:true})).toBeEnabled();
  await page.getByRole('button',{name:'Move Synthetic window 1 down',exact:true}).click();
  await expect.poll(async()=>(await recordedCalls(page)).filter(c=>c.command==='layouts.preview').at(-1)?.payload.ids).toEqual(['fixture-1','fixture-0',...Array.from({length:8},(_,i)=>`fixture-${i+2}`)]);
  await page.getByRole('combobox',{name:'Monitor for Synthetic window 1',exact:true}).click();
  await page.getByRole('option',{name:'Display 2',exact:true}).click();
  await expect.poll(async()=>(await recordedCalls(page)).filter(c=>c.command==='layouts.preview').at(-1)?.payload.assignments).toEqual(['m1','m2',...Array(8).fill('m1')]);
  await page.getByRole('button',{name:'Apply layout…',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'Apply layout',exact:true}).click();
  await expect(page.getByRole('alert')).toContainText('Window changed; skipped.');expect((await recordedCalls(page)).filter(c=>c.command==='layouts.apply')).toHaveLength(1);
});
test('power: presets save, rename and delete',async({page})=>{
  await installScenario(page,{instances:2});await nav(page,'Layouts');await page.getByRole('button',{name:'Select all',exact:true}).click();
  await expect(page.getByRole('button',{name:'Save preset…',exact:true})).toBeEnabled();await page.getByRole('button',{name:'Save preset…',exact:true}).click();
  await page.getByLabel('Preset name').fill('Audit grid');await page.getByRole('dialog').getByRole('button',{name:'Save',exact:true}).click();
  await expect.poll(async()=>(await recordedCalls(page)).filter(c=>c.command==='layouts.savePreset').length).toBe(1);
  await page.getByRole('button',{name:'Rename…',exact:true}).click();await page.getByLabel('Preset name').fill('Renamed grid');await page.getByRole('dialog').getByRole('button',{name:'Save',exact:true}).click();
  await page.getByRole('button',{name:'Delete…',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'Delete preset',exact:true}).click();
  await expect.poll(async()=>(await recordedCalls(page)).filter(c=>c.command==='layouts.deletePreset').length).toBe(1);
});
test('power: activity filtering, detail and cancel history clear',async({page})=>{
  await installScenario(page,{activity:1000});await nav(page,'Activity');await expect(page.getByRole('button',{name:'View details',exact:true})).toHaveCount(1000);
  await page.getByRole('combobox',{name:'Filter launch activity'}).click();await page.getByRole('option',{name:'Needs attention',exact:true}).click();
  await expect(page.getByRole('button',{name:'View details',exact:true})).toHaveCount(500);await page.getByRole('button',{name:'View details',exact:true}).first().click();
  await expect(page.locator('tr.activity-detail-row')).toHaveCount(1);await page.getByRole('button',{name:'Clear history…',exact:true}).click();await page.keyboard.press('Escape');expect((await recordedCalls(page)).some(c=>c.command==='activity.clear')).toBe(false);
});
test('power: import and explicit corrupt-settings recovery',async({page})=>{
  await installScenario(page,{corrupt:true});await nav(page,'Settings');
  await page.getByRole('button',{name:'Reset preview settings…',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'Reset preview settings',exact:true}).click();
  await expect.poll(async()=>(await recordedCalls(page)).filter(c=>c.command==='settings.reset').length).toBe(1);
  await page.getByRole('tab',{name:'Import',exact:true}).click();await page.getByRole('button',{name:'Import preferences…',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'Import preferences',exact:true}).click();
  await expect(page.getByRole('button',{name:'Preferences already imported',exact:true})).toBeDisabled();
});
test('power: tray and shortcut conflicts remain recoverable',async({page})=>{
  await installScenario(page);await nav(page,'Settings');await page.getByRole('tab',{name:'Shortcuts & tray',exact:true}).click();await page.getByRole('button',{name:'Enable tray mode',exact:true}).click();
  await expect(page.getByRole('button',{name:'Disable tray mode',exact:true})).toBeEnabled();await page.getByRole('button',{name:'Enable global shortcuts',exact:true}).click();await expect(page.getByRole('alert')).toContainText('Synthetic shortcut conflict');await page.getByRole('button',{name:'Disable tray mode',exact:true}).click();
});
test('power: download cancellation, retry and duplicate-click protection',async({page})=>{
  await installScenario(page);await nav(page,'System');await page.getByRole('button',{name:'Check for updates',exact:true}).click();await page.getByRole('button',{name:'Download update',exact:true}).click();
  await expect(page.getByRole('button',{name:'Download update',exact:true})).toBeDisabled();await page.getByRole('button',{name:'Cancel operation',exact:true}).click();await expect(page.getByRole('alert')).toContainText('Operation canceled');
  await page.getByRole('button',{name:'Download update',exact:true}).click();await expect(page.getByRole('button',{name:'Run setup…',exact:true})).toBeEnabled();
  expect((await recordedCalls(page)).filter(c=>c.command==='updates.download')).toHaveLength(2);expect((await recordedCalls(page)).some(c=>c.command==='updates.install')).toBe(false);
  expect((await recordedCalls(page)).filter(c=>c.command==='updates.download').map(c=>c.outcome)).toEqual(['canceled','passed']);
});
test('power: maintenance and report routing require confirmation',async({page})=>{
  await installScenario(page);await nav(page,'System');await page.getByRole('tab',{name:'Health',exact:true}).click();
  await page.getByRole('button',{name:'Recover / Repair…',exact:true}).click();await page.keyboard.press('Escape');expect((await recordedCalls(page)).some(c=>c.command==='system.maintenance')).toBe(false);
  await page.getByRole('button',{name:'Test worker',exact:true}).click();
  await expect.poll(async()=>(await recordedCalls(page)).filter(c=>c.command==='system.testWorker').length).toBe(1);
  await page.getByRole('tab',{name:'Logs & reports',exact:true}).click();await page.getByRole('button',{name:'Export report…',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'Export report',exact:true}).click();
  await expect.poll(async()=>(await recordedCalls(page)).filter(c=>c.command==='report.export'&&c.payload.confirmed===true).length).toBe(1);
});
