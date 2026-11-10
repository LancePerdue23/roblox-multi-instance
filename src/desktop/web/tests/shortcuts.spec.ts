import {test,expect} from '@playwright/test';
import {installWorkspace} from './fixtures/workspace';
test('shortcut capture preserves Ctrl Alt M and Tab navigation',async({page})=>{
 await installWorkspace(page);await page.getByRole('button',{name:'Settings',exact:true}).click();await page.getByRole('tab',{name:'Shortcuts & tray',exact:true}).click();
 const field=page.getByRole('textbox',{name:'Manager shortcut'});await field.focus();await field.press('Control+Alt+m');await expect(field).toHaveValue('Ctrl + Alt + M');
 await field.press('Tab');await expect(field).not.toBeFocused();
 const calls=await page.evaluate(()=>(window as unknown as {testCalls:{command:string;payload:{enabled?:boolean}}[]}).testCalls.filter(c=>c.command==='host.shortcutCapture'));
 expect(calls.map(c=>c.payload.enabled)).toEqual([true,false,true]);
});
test('native partial, failed and disabled results update status and controls',async({page})=>{
 await installWorkspace(page);await page.getByRole('button',{name:'Settings',exact:true}).click();await page.getByRole('tab',{name:'Shortcuts & tray',exact:true}).click();
 for(const [status,registered,tray] of [['partial',1,true],['failed',0,true],['disabled',0,false]] as const){
 await page.evaluate(({status,registered,tray})=>(window as unknown as {testEmit:(e:unknown)=>void}).testEmit({version:1,event:'host.shortcutState',data:{enabled:registered>0,status,registered,total:status==='disabled'?0:2,conflicts:status==='disabled'?[]:['Manager: Windows error 1409'],tray}}),{status,registered,tray});
 await expect(page.getByText('Global shortcuts: '+status+' ('+registered+'/'+(status==='disabled'?0:2)+')',{exact:true})).toBeVisible();
 await expect(page.getByRole('button',{name:registered?'Disable global shortcuts':'Enable global shortcuts',exact:true})).toBeVisible();
 await expect(page.getByRole('button',{name:tray?'Disable tray mode':'Enable tray mode',exact:true})).toBeVisible();
 }
});
