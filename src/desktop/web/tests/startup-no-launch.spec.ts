import {test,expect} from '@playwright/test';
import {installWorkspace} from './fixtures/workspace';

test('startup, refresh and reopening with saved accounts never request a player launch',async({page})=>{
 await installWorkspace(page);
 await page.goto('/?accounts-demo&launch-testing');
 const calls=()=>page.evaluate(()=>(window as unknown as {testCalls:{command:string}[]}).testCalls.map(x=>x.command));
 await expect.poll(calls).toContain('updates.check');
 await page.getByRole('button',{name:'↻ Refresh',exact:true}).click();
 await page.getByRole('button',{name:'Accounts',exact:true}).click();
 await expect.poll(calls).toContain('accounts.state');
 expect((await calls()).filter(x=>['launch','launch.start','accounts.launchBatch'].includes(x))).toEqual([]);
 await page.reload();
 await expect.poll(calls).toContain('updates.check');
 expect((await calls()).filter(x=>['launch','launch.start','accounts.launchBatch'].includes(x))).toEqual([]);
});
